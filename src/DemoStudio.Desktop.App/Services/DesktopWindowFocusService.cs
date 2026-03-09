using System.Runtime.InteropServices;

namespace DemoStudio.Desktop.App.Services;

public sealed class DesktopWindowFocusService
{
    private readonly DesktopWindowLocator _windowLocator = new();

    public async Task<(bool Succeeded, string Message)> TryActivateAsync(
        CaptureTargetSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(settings.Mode, "Window", StringComparison.OrdinalIgnoreCase))
        {
            return (true, "Desktop mode selected; no target focus needed.");
        }

        var preferExactHandle = !string.IsNullOrWhiteSpace(settings.WindowHandleHex);
        var titleForLookup = preferExactHandle ? null : settings.WindowTitleContains;
        var processForLookup = settings.WindowProcessName;
        var locate = await _windowLocator.FindAsync(
            new DemoStudio.Infrastructure.Execution.Windows.WindowLocatorRequest(
                titleForLookup,
                TitleRegex: null,
                processForLookup,
                settings.WindowHandleHex,
                PreferExactHandle: preferExactHandle),
            cancellationToken);

        if (!locate.Found || locate.Handle == IntPtr.Zero)
        {
            return (false, $"Target window not found. {locate.FailureReason}".Trim());
        }

        ShowWindow(locate.Handle, 9); // SW_RESTORE
        var focused = SetForegroundWindow(locate.Handle);
        if (!focused)
        {
            return (false, "Target window was found but could not be focused.");
        }

        return (true, $"Focused target: {locate.Title}");
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
