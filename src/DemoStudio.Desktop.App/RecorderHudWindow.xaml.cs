using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Interop;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using DemoStudio.Desktop.App.ViewModels;

namespace DemoStudio.Desktop.App;

public partial class RecorderHudWindow : Window, INotifyPropertyChanged
{
    private readonly DispatcherTimer _snapTimer;
    private HwndSource? _hwndSource;
    private bool _manualPositionOverride;
    private bool _isApplyingSnapPosition;
    private bool _isCompactMode = true;
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
        ToggleCompactModeCommand = new RelayCommand(_ => ToggleCompactMode());
        Loaded += OnLoaded;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        LocationChanged += OnLocationChanged;
        _snapTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(900)
        };
        _snapTimer.Tick += (_, _) => SnapToTargetIfAvailable();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand ToggleCompactModeCommand { get; }

    public bool IsCompactMode
    {
        get => _isCompactMode;
        private set
        {
            if (_isCompactMode == value)
            {
                return;
            }

            _isCompactMode = value;
            ApplyModeSize();
            OnPropertyChanged();
            OnPropertyChanged(nameof(ToggleModeButtonText));
            SnapToTargetIfAvailable();
        }
    }

    public string ToggleModeButtonText => IsCompactMode ? "Expand" : "Compact";

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyModeSize();
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

    public void PrepareForRecordingSession()
    {
        _manualPositionOverride = false;
        SnapToTargetIfAvailable();
    }

    public void RefreshHudPosition()
    {
        SnapToTargetIfAvailable();
    }

    private void ToggleCompactMode()
    {
        IsCompactMode = !IsCompactMode;
    }

    private void ApplyModeSize()
    {
        if (IsCompactMode)
        {
            Width = 340;
            Height = 110;
            return;
        }

        Width = 560;
        Height = 205;
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

        var workArea = SystemParameters.WorkArea;
        var targetRect = new Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        var position = FindBestHudPosition(targetRect, workArea);
        ApplySnapPosition(position.Left, position.Top);
    }

    private void SnapToWorkArea()
    {
        var workArea = SystemParameters.WorkArea;
        var cursorRect = GetCursorSafetyRect();
        var bottomRight = ClampToWorkArea(workArea.Right - Width - 16, workArea.Bottom - Height - 16, workArea);
        if (bottomRight.IntersectsWith(cursorRect))
        {
            var topRight = ClampToWorkArea(workArea.Right - Width - 16, workArea.Top + 16, workArea);
            ApplySnapPosition(topRight.Left, topRight.Top);
            return;
        }

        ApplySnapPosition(bottomRight.Left, bottomRight.Top);
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

    private Rect FindBestHudPosition(Rect targetRect, Rect workArea)
    {
        var candidates = new[]
        {
            ClampToWorkArea(targetRect.Right - Width - 12, targetRect.Top + 12, workArea),
            ClampToWorkArea(targetRect.Right - Width - 12, targetRect.Bottom - Height - 12, workArea),
            ClampToWorkArea(targetRect.Left + 12, targetRect.Top + 12, workArea),
            ClampToWorkArea(targetRect.Left + 12, targetRect.Bottom - Height - 12, workArea),
            ClampToWorkArea(workArea.Right - Width - 16, workArea.Bottom - Height - 16, workArea)
        };

        var cursorRect = GetCursorSafetyRect();
        var centerRect = new Rect(
            targetRect.Left + (targetRect.Width * 0.25d),
            targetRect.Top + (targetRect.Height * 0.25d),
            Math.Max(64d, targetRect.Width * 0.5d),
            Math.Max(64d, targetRect.Height * 0.5d));

        foreach (var candidate in candidates)
        {
            if (!candidate.IntersectsWith(cursorRect) && !candidate.IntersectsWith(centerRect))
            {
                return candidate;
            }
        }

        foreach (var candidate in candidates)
        {
            if (!candidate.IntersectsWith(cursorRect))
            {
                return candidate;
            }
        }

        return candidates[0];
    }

    private Rect ClampToWorkArea(double left, double top, Rect workArea)
    {
        var clampedLeft = Math.Max(workArea.Left + 8, Math.Min(left, workArea.Right - Width - 8));
        var clampedTop = Math.Max(workArea.Top + 8, Math.Min(top, workArea.Bottom - Height - 8));
        return new Rect(clampedLeft, clampedTop, Width, Height);
    }

    private static Rect GetCursorSafetyRect()
    {
        if (!GetCursorPos(out var point))
        {
            return Rect.Empty;
        }

        return new Rect(point.X - 48, point.Y - 48, 96, 96);
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
                if (vm.StartClipCommand.CanExecute(null))
                {
                    vm.StartClipCommand.Execute(null);
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

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

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

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
