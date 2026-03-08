using System.Runtime.InteropServices;
using System.Windows;

namespace DemoStudio.Desktop.App.Services;

public sealed class DesktopPresenterViewService
{
    public bool TryMoveTargetToSecondary(string? windowHandleHex)
    {
        if (!TryParseHandle(windowHandleHex, out var handle))
        {
            return false;
        }

        var primaryWidth = SystemParameters.PrimaryScreenWidth;
        var virtualWidth = SystemParameters.VirtualScreenWidth;
        if (virtualWidth <= primaryWidth + 20)
        {
            return false;
        }

        if (!GetWindowRect(handle, out var rect))
        {
            return false;
        }

        var width = Math.Max(640, rect.Right - rect.Left);
        var height = Math.Max(420, rect.Bottom - rect.Top);
        var targetX = (int)Math.Round(primaryWidth + 24);
        var targetY = 60;
        return SetWindowPos(handle, IntPtr.Zero, targetX, targetY, width, height, SwpNoZOrder | SwpShowWindow);
    }

    private static bool TryParseHandle(string? handleHex, out IntPtr handle)
    {
        handle = IntPtr.Zero;
        if (string.IsNullOrWhiteSpace(handleHex))
        {
            return false;
        }

        var raw = handleHex.Trim();
        if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            raw = raw[2..];
        }

        if (!long.TryParse(raw, System.Globalization.NumberStyles.HexNumber, null, out var parsed))
        {
            return false;
        }

        handle = new IntPtr(parsed);
        return handle != IntPtr.Zero;
    }

    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpShowWindow = 0x0040;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
