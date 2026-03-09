namespace DemoStudio.Infrastructure.Execution.Windows;

using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

internal sealed class Win32WindowLocator : IWindowLocator
{
    private readonly IWin32WindowEnumerator _enumerator;
    private readonly ILogger<Win32WindowLocator> _logger;

    public Win32WindowLocator(IWin32WindowEnumerator enumerator, ILogger<Win32WindowLocator> logger)
    {
        _enumerator = enumerator;
        _logger = logger;
    }

    public Task<WindowLocatorResult> FindAsync(WindowLocatorRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var windows = _enumerator.Enumerate();
        var desktopBounds = _enumerator.GetDesktopBounds();
        if (windows.Count == 0)
        {
            return Task.FromResult(new WindowLocatorResult(false, IntPtr.Zero, string.Empty, 0, "No windows found.", null, desktopBounds));
        }

        if (request.PreferExactHandle && TryParseHandle(request.HandleHex, out var exactHandle))
        {
            var exact = windows.FirstOrDefault(x => x.Handle == exactHandle);
            if (exact is not null && IsPrimaryCandidate(exact))
            {
                return Task.FromResult(ToResult(exact, desktopBounds));
            }

            if (exact is not null)
            {
                _logger.LogWarning(
                    "Exact handle {Handle} was found but is not a primary capture candidate. Falling back to process/title matching.",
                    request.HandleHex);
            }
        }

        var filtered = windows
            .Where(IsPrimaryCandidate)
            .ToArray();

        if (!string.IsNullOrWhiteSpace(request.ProcessName))
        {
            filtered = filtered
                .Where(x => x.ProcessName.Equals(request.ProcessName, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        if (!string.IsNullOrWhiteSpace(request.TitleContains))
        {
            filtered = filtered
                .Where(x => x.Title.Contains(request.TitleContains, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        if (!string.IsNullOrWhiteSpace(request.TitleRegex))
        {
            var regex = new Regex(request.TitleRegex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            filtered = filtered.Where(x => regex.IsMatch(x.Title)).ToArray();
        }

        var preHandleFiltered = filtered;
        if (!TryParseHandle(request.HandleHex, out var handle))
        {
            handle = IntPtr.Zero;
        }

        if (handle != IntPtr.Zero)
        {
            filtered = filtered.Where(x => x.Handle == handle).ToArray();
            if (filtered.Length == 0 && preHandleFiltered.Length > 0)
            {
                _logger.LogWarning(
                    "Exact handle {Handle} not found. Falling back to title/process match for window selection.",
                    request.HandleHex);
                filtered = preHandleFiltered;
            }
        }

        if (filtered.Length == 0)
        {
            return Task.FromResult(new WindowLocatorResult(false, IntPtr.Zero, string.Empty, 0, "No matching window found.", null, desktopBounds));
        }

        // With no explicit filters, prefer the current foreground window so window capture
        // tracks the operator's active target instead of an arbitrary visible window.
        if (string.IsNullOrWhiteSpace(request.ProcessName)
            && string.IsNullOrWhiteSpace(request.TitleContains)
            && string.IsNullOrWhiteSpace(request.TitleRegex)
            && string.IsNullOrWhiteSpace(request.HandleHex))
        {
            var foreground = GetForegroundWindow();
            if (foreground != IntPtr.Zero)
            {
                var foregroundMatch = filtered.FirstOrDefault(x => x.Handle == foreground);
                if (foregroundMatch is not null)
                {
                    _logger.LogInformation(
                        "Selected foreground window for capture. Handle={Handle}, Title={Title}, ProcessId={ProcessId}.",
                        foregroundMatch.Handle,
                        foregroundMatch.Title,
                        foregroundMatch.ProcessId);
                    return Task.FromResult(ToResult(foregroundMatch, desktopBounds));
                }
            }
        }

        var best = SelectBest(filtered, request.TitleContains);
        _logger.LogInformation("Selected window for capture. Handle={Handle}, Title={Title}, ProcessId={ProcessId}.", best.Handle, best.Title, best.ProcessId);
        return Task.FromResult(ToResult(best, desktopBounds));
    }

    private static Win32WindowRecord SelectBest(IReadOnlyCollection<Win32WindowRecord> matches, string? titleContains)
    {
        if (!string.IsNullOrWhiteSpace(titleContains))
        {
            var exactTitle = matches
                .FirstOrDefault(x => x.Title.Equals(titleContains, StringComparison.OrdinalIgnoreCase));

            if (exactTitle is not null)
            {
                return exactTitle;
            }
        }

        return matches
            .OrderBy(x => x.Title.Length)
            .ThenBy(x => x.ProcessId)
            .First();
    }

    private static bool IsPrimaryCandidate(Win32WindowRecord window)
    {
        if (window.IsMinimized || window.IsCloaked || !window.Bounds.IsValid)
        {
            return false;
        }

        var hasLargeBounds = window.Bounds.Width >= 320 && window.Bounds.Height >= 180;
        if (!hasLargeBounds)
        {
            return false;
        }

        if (window.IsVisible)
        {
            return true;
        }

        // Chromium-family windows can present as non-visible while still being
        // the active top-level capture target; accept them when they look like
        // a normal content window.
        var hasMeaningfulTitle = !string.IsNullOrWhiteSpace(window.Title);
        return hasMeaningfulTitle && hasLargeBounds;
    }

    private static WindowLocatorResult ToResult(Win32WindowRecord window, WindowBounds desktopBounds)
    {
        return new WindowLocatorResult(
            true,
            window.Handle,
            window.Title,
            window.ProcessId,
            null,
            window.Bounds,
            desktopBounds);
    }

    private static bool TryParseHandle(string? value, out IntPtr handle)
    {
        handle = IntPtr.Zero;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[2..];
        }

        if (!long.TryParse(normalized, System.Globalization.NumberStyles.HexNumber, null, out var parsed))
        {
            return false;
        }

        handle = new IntPtr(parsed);
        return handle != IntPtr.Zero;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
