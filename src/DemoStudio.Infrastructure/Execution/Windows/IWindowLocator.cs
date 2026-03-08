namespace DemoStudio.Infrastructure.Execution.Windows;

public sealed record WindowLocatorRequest(
    string? TitleContains,
    string? TitleRegex,
    string? ProcessName,
    string? HandleHex,
    bool PreferExactHandle);

public sealed record WindowLocatorResult(
    bool Found,
    IntPtr Handle,
    string Title,
    int ProcessId,
    string? FailureReason,
    WindowBounds? Bounds = null,
    WindowBounds? DesktopBounds = null);

public readonly record struct WindowBounds(int X, int Y, int Width, int Height)
{
    public bool IsValid => Width > 0 && Height > 0;
}

public interface IWindowLocator
{
    Task<WindowLocatorResult> FindAsync(WindowLocatorRequest request, CancellationToken cancellationToken = default);
}

internal sealed record Win32WindowRecord(
    IntPtr Handle,
    string Title,
    bool IsVisible,
    bool IsMinimized,
    int ProcessId,
    string ProcessName,
    WindowBounds Bounds);

internal interface IWin32WindowEnumerator
{
    IReadOnlyCollection<Win32WindowRecord> Enumerate();

    WindowBounds GetDesktopBounds();
}
