namespace DemoStudio.Infrastructure.Execution.Windows;

using System.Runtime.InteropServices;
using System.Text;

internal sealed class Win32WindowEnumerator : IWin32WindowEnumerator
{
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;

    public IReadOnlyCollection<Win32WindowRecord> Enumerate()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Array.Empty<Win32WindowRecord>();
        }

        var windows = new List<Win32WindowRecord>();

        EnumWindows((hwnd, _) =>
        {
            if (hwnd == IntPtr.Zero)
            {
                return true;
            }

            var isVisible = IsWindowVisible(hwnd);
            var isMinimized = IsIconic(hwnd);
            var isCloaked = IsCloaked(hwnd);

            var title = GetWindowTitle(hwnd);
            GetWindowThreadProcessId(hwnd, out var pid);
            var processName = GetProcessName(pid);
            var bounds = GetBounds(hwnd);

            windows.Add(new Win32WindowRecord(hwnd, title, isVisible, isMinimized, isCloaked, (int)pid, processName, bounds));
            return true;
        }, IntPtr.Zero);

        return windows;
    }

    public WindowBounds GetDesktopBounds()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new WindowBounds(0, 0, 0, 0);
        }

        var x = GetSystemMetrics(SmXVirtualScreen);
        var y = GetSystemMetrics(SmYVirtualScreen);
        var width = GetSystemMetrics(SmCxVirtualScreen);
        var height = GetSystemMetrics(SmCyVirtualScreen);
        return new WindowBounds(x, y, width, height);
    }

    internal static WindowBounds GetBounds(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return default;
        }

        RECT rect;
        if (DwmGetWindowAttributeRect(hwnd, 9, out rect, Marshal.SizeOf<RECT>()) == 0)
        {
            var width = rect.Right - rect.Left;
            var height = rect.Bottom - rect.Top;
            return new WindowBounds(rect.Left, rect.Top, width, height);
        }

        if (!GetWindowRect(hwnd, out rect))
        {
            return default;
        }

        return new WindowBounds(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    private static string GetProcessName(uint processId)
    {
        try
        {
            var process = System.Diagnostics.Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
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
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        const int dwmwaCloaked = 14;
        int cloaked;
        if (DwmGetWindowAttributeInt(hwnd, dwmwaCloaked, out cloaked, sizeof(int)) != 0)
        {
            return false;
        }

        return cloaked != 0;
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

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
