using DemoStudio.Infrastructure.Execution.Windows;
using System.Runtime.InteropServices;
using System.Text;

namespace DemoStudio.Desktop.App.Services;

public sealed class DesktopWindowCatalogService
{
    public IReadOnlyList<DesktopWindowCandidate> ListCapturableWindows()
    {
        var windows = new List<DesktopWindowCandidate>();
        EnumWindows((hwnd, lParam) =>
        {
            if (hwnd == IntPtr.Zero)
            {
                return true;
            }

            var isVisible = IsWindowVisible(hwnd);
            var isMinimized = IsIconic(hwnd);
            var isCloaked = IsCloaked(hwnd);

            GetWindowThreadProcessId(hwnd, out var processId);
            var processName = GetProcessName(processId);
            var title = GetWindowTitle(hwnd).Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                if (!IsBrowserProcess(processName))
                {
                    return true;
                }

                title = BuildFallbackTitle(processName, (int)processId, hwnd);
            }

            var bounds = GetBounds(hwnd);
            if (!bounds.IsValid)
            {
                return true;
            }

            var hasLargeBounds = bounds.Width >= 320 && bounds.Height >= 180;
            var include = !isMinimized
                && !isCloaked
                && hasLargeBounds
                && (isVisible || IsBrowserProcess(processName));
            if (!include)
            {
                return true;
            }

            windows.Add(new DesktopWindowCandidate(
                HandleHex: $"0x{hwnd.ToInt64():X}",
                Title: title,
                ProcessName: processName,
                ProcessId: (int)processId,
                Bounds: bounds));

            return true;
        }, IntPtr.Zero);

        var ordered = windows
            .OrderBy(x => x.SortPriority)
            .ThenBy(x => x.AppLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // Default to a cleaner list for operators. If over-filtered, fall back to full list.
        var filtered = ordered.Where(x => !IsNoiseWindow(x)).ToArray();
        return filtered.Length > 0 ? filtered : ordered;
    }

    private static bool IsNoiseWindow(DesktopWindowCandidate window)
    {
        if (window is null)
        {
            return true;
        }

        var process = window.ProcessName;
        if (string.IsNullOrWhiteSpace(process))
        {
            return true;
        }

        // Never suppress mainstream browser targets from the picker.
        if (IsBrowserProcess(process))
        {
            return false;
        }

        if (process.Equals("TextInputHost", StringComparison.OrdinalIgnoreCase) ||
            process.Equals("SearchHost", StringComparison.OrdinalIgnoreCase) ||
            process.Equals("ShellExperienceHost", StringComparison.OrdinalIgnoreCase) ||
            process.Equals("StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase) ||
            process.Equals("LockApp", StringComparison.OrdinalIgnoreCase) ||
            process.Equals("Widgets", StringComparison.OrdinalIgnoreCase) ||
            process.Equals("RuntimeBroker", StringComparison.OrdinalIgnoreCase) ||
            process.Contains("DemoStudio", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var title = window.Title?.Trim() ?? string.Empty;
        if (title.StartsWith("Recorder HUD", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (title.Contains("Recorder Studio", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("DemoStudio Recorder", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (title.Equals("Program Manager", StringComparison.OrdinalIgnoreCase) ||
            title.Equals("Windows Input Experience", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (process.Equals("SystemSettings", StringComparison.OrdinalIgnoreCase) ||
            process.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase) &&
            (title.Equals("Settings", StringComparison.OrdinalIgnoreCase) ||
             title.Equals("Films & TV", StringComparison.OrdinalIgnoreCase) ||
             title.Equals("Video.UI - Films & TV", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    private static bool IsBrowserProcess(string processName)
    {
        return processName.Equals("msedge", StringComparison.OrdinalIgnoreCase)
            || processName.Equals("chrome", StringComparison.OrdinalIgnoreCase)
            || processName.Equals("firefox", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildFallbackTitle(string processName, int processId, IntPtr handle)
    {
        var app = string.IsNullOrWhiteSpace(processName) ? "Window" : processName;
        return $"{app} [{processId}] 0x{handle.ToInt64():X}";
    }

    private static string GetWindowTitle(IntPtr hwnd)
    {
        var length = GetWindowTextLength(hwnd);
        var capacity = Math.Max(length + 1, 1024);
        var buffer = new StringBuilder(capacity);
        _ = GetWindowText(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private static bool IsCloaked(IntPtr hwnd)
    {
        const int dwmwaCloaked = 14;
        int cloaked;
        if (DwmGetWindowAttributeInt(hwnd, dwmwaCloaked, out cloaked, sizeof(int)) != 0)
        {
            return false;
        }

        return cloaked != 0;
    }

    private static string GetProcessName(uint processId)
    {
        try
        {
            return System.Diagnostics.Process.GetProcessById((int)processId).ProcessName;
        }
        catch
        {
            return "Unknown";
        }
    }

    private static WindowBounds GetBounds(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return default;
        }

        RECT rect;
        if (DwmGetWindowAttributeRect(hwnd, 9, out rect, Marshal.SizeOf<RECT>()) == 0)
        {
            return new WindowBounds(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }

        if (!GetWindowRect(hwnd, out rect))
        {
            return default;
        }

        return new WindowBounds(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    private static extern int DwmGetWindowAttributeRect(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    private static extern int DwmGetWindowAttributeInt(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}

public sealed record DesktopWindowCandidate(
    string HandleHex,
    string Title,
    string ProcessName,
    int ProcessId,
    WindowBounds Bounds)
{
    public int SortPriority => ProcessName switch
    {
        "msedge" => 0,
        "chrome" => 0,
        "firefox" => 0,
        "devenv" => 1,
        "code" => 1,
        "explorer" => 2,
        _ => 3
    };

    public string AppLabel => ProcessName switch
    {
        "msedge" => "Microsoft Edge",
        "chrome" => "Google Chrome",
        "firefox" => "Firefox",
        "devenv" => "Visual Studio",
        "code" => "VS Code",
        "explorer" => "File Explorer",
        "outlook" => "Outlook",
        "pwsh" => "PowerShell",
        "powershell" => "PowerShell",
        "ApplicationFrameHost" => "Windows App",
        "SystemSettings" => "Settings",
        "TextInputHost" => "Windows Input",
        _ => ProcessName
    };

    public string FriendlyTitle
    {
        get
        {
            var cleaned = Title.Trim();
            cleaned = StripSuffix(cleaned, " - Microsoft Edge");
            cleaned = StripSuffix(cleaned, " - Google Chrome");
            cleaned = StripSuffix(cleaned, " - Mozilla Firefox");
            cleaned = StripSuffix(cleaned, " - Microsoft Visual Studio");
            cleaned = StripSuffix(cleaned, " - Visual Studio Code");
            cleaned = cleaned.Replace(" (Running)", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
            return cleaned;
        }
    }

    public string DisplayName => string.Equals(FriendlyTitle, AppLabel, StringComparison.OrdinalIgnoreCase)
        ? AppLabel
        : ProcessName.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase)
            ? $"{FriendlyTitle} (Windows App)"
            : $"{AppLabel} - {FriendlyTitle}";

    public string TechnicalSummary => $"{ProcessName} [{ProcessId}] | {Title} | {HandleHex}";

    private static string StripSuffix(string value, string suffix)
    {
        return value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? value[..^suffix.Length].Trim()
            : value;
    }
}
