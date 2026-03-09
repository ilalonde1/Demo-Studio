using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Interop;
using DemoStudio.Desktop.App.ViewModels;

namespace DemoStudio.Desktop.App;

public partial class RecorderHudWindow : Window
{
    private readonly DispatcherTimer _snapTimer;
    private HwndSource? _hwndSource;
    private bool _manualPositionOverride;
    private bool _isApplyingSnapPosition;
    private const int HotkeyIdStartResume = 1001;
    private const int HotkeyIdPause = 1002;
    private const int HotkeyIdStop = 1003;
    private const int WmHotkey = 0x0312;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint Vk1 = 0x31;
    private const uint Vk2 = 0x32;
    private const uint Vk3 = 0x33;

    public RecorderHudWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        LocationChanged += OnLocationChanged;
        _snapTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(900)
        };
        _snapTimer.Tick += (_, _) => SnapToTargetIfAvailable();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SnapToTargetIfAvailable();
        _snapTimer.Start();
        RegisterGlobalHotkeys();
    }

    protected override void OnClosed(EventArgs e)
    {
        UnregisterGlobalHotkeys();
        _snapTimer.Stop();
        base.OnClosed(e);
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        try
        {
            DragMove();
            _manualPositionOverride = true;
        }
        catch
        {
        }
    }

    private void OnLocationChanged(object? sender, EventArgs e)
    {
        if (_isApplyingSnapPosition)
        {
            return;
        }

        // Any user move (including title-bar drag) should pin the HUD in place.
        _manualPositionOverride = true;
    }

    private void SnapToTargetIfAvailable()
    {
        if (_manualPositionOverride)
        {
            return;
        }

        if (DataContext is not MainWindowViewModel vm)
        {
            SnapToWorkArea();
            return;
        }

        if (vm.PresenterViewEnabled)
        {
            SnapToWorkArea();
            return;
        }

        if ((!string.Equals(vm.CaptureMode, "Window", StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(vm.CaptureMode, "Stage", StringComparison.OrdinalIgnoreCase)) ||
            !TryParseHandle(vm.WindowHandleHex, out var handle) ||
            !GetWindowRect(handle, out var rect))
        {
            SnapToWorkArea();
            return;
        }

        var targetTop = rect.Top + 12;
        var targetLeft = rect.Right - Width - 12;
        var workArea = SystemParameters.WorkArea;
        ApplySnapPosition(
            Math.Max(workArea.Left + 8, Math.Min(targetLeft, workArea.Right - Width - 8)),
            Math.Max(workArea.Top + 8, Math.Min(targetTop, workArea.Bottom - Height - 8)));
    }

    private void SnapToWorkArea()
    {
        var workArea = SystemParameters.WorkArea;
        ApplySnapPosition(workArea.Right - Width - 16, workArea.Bottom - Height - 16);
    }

    private void ApplySnapPosition(double left, double top)
    {
        try
        {
            _isApplyingSnapPosition = true;
            Left = left;
            Top = top;
        }
        finally
        {
            _isApplyingSnapPosition = false;
        }
    }

    private void RegisterGlobalHotkeys()
    {
        var helper = new WindowInteropHelper(this);
        if (helper.Handle == IntPtr.Zero)
        {
            return;
        }

        _hwndSource = HwndSource.FromHwnd(helper.Handle);
        _hwndSource?.AddHook(WndProc);
        _ = RegisterHotKey(helper.Handle, HotkeyIdStartResume, ModControl | ModShift, Vk1);
        _ = RegisterHotKey(helper.Handle, HotkeyIdPause, ModControl | ModShift, Vk2);
        _ = RegisterHotKey(helper.Handle, HotkeyIdStop, ModControl | ModShift, Vk3);
    }

    private void UnregisterGlobalHotkeys()
    {
        var helper = new WindowInteropHelper(this);
        if (helper.Handle != IntPtr.Zero)
        {
            _ = UnregisterHotKey(helper.Handle, HotkeyIdStartResume);
            _ = UnregisterHotKey(helper.Handle, HotkeyIdPause);
            _ = UnregisterHotKey(helper.Handle, HotkeyIdStop);
        }

        if (_hwndSource is not null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmHotkey)
        {
            return IntPtr.Zero;
        }

        handled = true;
        if (DataContext is not MainWindowViewModel vm)
        {
            return IntPtr.Zero;
        }

        var hotkeyId = wParam.ToInt32();
        switch (hotkeyId)
        {
            case HotkeyIdStartResume:
                if (vm.PrimaryWorkflowCommand.CanExecute(null))
                {
                    vm.PrimaryWorkflowCommand.Execute(null);
                }
                break;
            case HotkeyIdPause:
                if (vm.PauseClipCommand.CanExecute(null))
                {
                    vm.PauseClipCommand.Execute(null);
                }
                break;
            case HotkeyIdStop:
                if (vm.StopSessionCommand.CanExecute(null))
                {
                    vm.StopSessionCommand.Execute(null);
                }
                break;
        }

        return IntPtr.Zero;
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

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
