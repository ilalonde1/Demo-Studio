using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using DemoStudio.Desktop.App;

namespace DemoStudio.Desktop.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private const string DefaultStageAddress = "https://www.bing.com";
    private const string StageWorkspaceWindowTitle = "DemoStudio Stage Workspace";
    private StageWorkspaceWindow? _stageWorkspaceWindow;
    private string _stageStartupUrl = DefaultStageAddress;

    public bool IsStageMode => string.Equals(CaptureMode, "Stage", StringComparison.OrdinalIgnoreCase);
    public bool IsWindowMode => string.Equals(CaptureMode, "Window", StringComparison.OrdinalIgnoreCase);
    public bool CanOpenStageWorkspace => CanEditTargetSettings && IsStageMode;

    public string StageStartupUrl
    {
        get => _stageStartupUrl;
        set
        {
            var next = string.IsNullOrWhiteSpace(value) ? DefaultStageAddress : value.Trim();
            if (string.Equals(_stageStartupUrl, next, StringComparison.Ordinal))
            {
                return;
            }

            _stageStartupUrl = next;
            if (_stageWorkspaceWindow is not null)
            {
                _stageWorkspaceWindow.CurrentAddress = _stageStartupUrl;
            }

            OnPropertyChanged();
        }
    }

    private void OnCaptureModeChanged()
    {
        OnPropertyChanged(nameof(IsStageMode));
        OnPropertyChanged(nameof(IsWindowMode));
        OnPropertyChanged(nameof(CanOpenStageWorkspace));
        OnPropertyChanged(nameof(CanUseSelectedWindow));
        OnPropertyChanged(nameof(CanFocusTarget));
        OnPropertyChanged(nameof(CanStartClip));
        RaiseCommandState();

        if (IsStageMode)
        {
            _ = EnsureStageWorkspaceReadyAsync(bringToFront: false);
        }
        else if (IsStageWorkspaceTarget())
        {
            ClearStageWindowTargetFields("Select a target window, Stage Workspace, or desktop capture to begin.");
            RefreshWindowCandidates();
        }
    }

    private async Task OpenStageWorkspaceAsync()
    {
        if (!CanOpenStageWorkspace)
        {
            return;
        }

        if (!await EnsureStageWorkspaceReadyAsync(bringToFront: true))
        {
            _lastRuntimeMessage = "Stage workspace could not be opened. Retry and ensure desktop UI is available.";
            OnPropertyChanged(nameof(LastRuntimeMessage));
        }
    }

    private async Task<bool> EnsureStageWorkspaceReadyAsync(bool bringToFront)
    {
        var app = System.Windows.Application.Current;
        if (app?.Dispatcher is null)
        {
            return false;
        }

        return await app.Dispatcher.InvokeAsync(() =>
        {
            _stageWorkspaceWindow ??= CreateStageWorkspaceWindow();
            if (!_stageWorkspaceWindow.IsVisible)
            {
                _stageWorkspaceWindow.Show();
            }

            _stageWorkspaceWindow.CurrentAddress = StageStartupUrl;
            if (bringToFront)
            {
                if (_stageWorkspaceWindow.WindowState == WindowState.Minimized)
                {
                    _stageWorkspaceWindow.WindowState = WindowState.Normal;
                }

                _stageWorkspaceWindow.Activate();
            }

            var handle = new WindowInteropHelper(_stageWorkspaceWindow).Handle;
            if (handle == IntPtr.Zero)
            {
                _stageWorkspaceWindow.UpdateLayout();
                handle = new WindowInteropHelper(_stageWorkspaceWindow).Handle;
            }

            if (handle == IntPtr.Zero)
            {
                return false;
            }

            SyncStageWindowTargetFields(handle);
            return true;
        }).Task;
    }

    private StageWorkspaceWindow CreateStageWorkspaceWindow()
    {
        var window = new StageWorkspaceWindow
        {
            CurrentAddress = StageStartupUrl
        };
        window.Closed += OnStageWorkspaceClosed;
        return window;
    }

    private void OnStageWorkspaceClosed(object? sender, EventArgs e)
    {
        if (sender is StageWorkspaceWindow closed)
        {
            closed.Closed -= OnStageWorkspaceClosed;
        }

        _stageWorkspaceWindow = null;
        if (IsStageMode)
        {
            ClearStageWindowTargetFields("Stage workspace closed. Open Stage Workspace before recording.");
        }
    }

    private void SyncStageWindowTargetFields(IntPtr handle)
    {
        var handleHex = $"0x{handle.ToInt64():X}";
        WindowHandleHex = handleHex;
        WindowProcessName = Process.GetCurrentProcess().ProcessName;
        WindowTitleContains = _stageWorkspaceWindow?.Title ?? StageWorkspaceWindowTitle;
        SelectedWindowCandidate = null;
        WindowSelectionStatus = $"Stage workspace locked ({handleHex}).";
        OnPropertyChanged(nameof(WindowSelectionStatus));
    }

    private bool IsStageWorkspaceTarget()
    {
        return string.Equals(WindowTitleContains, StageWorkspaceWindowTitle, StringComparison.OrdinalIgnoreCase)
            || string.Equals(WindowProcessName, Process.GetCurrentProcess().ProcessName, StringComparison.OrdinalIgnoreCase);
    }

    private void ClearStageWindowTargetFields(string status)
    {
        WindowHandleHex = string.Empty;
        WindowProcessName = string.Empty;
        WindowTitleContains = string.Empty;
        SelectedWindowCandidate = null;
        WindowSelectionStatus = status;
        OnPropertyChanged(nameof(WindowSelectionStatus));
        RaiseTargetingAndLaunchState();
    }

    private Task CloseStageWorkspaceAsync()
    {
        var app = System.Windows.Application.Current;
        if (app?.Dispatcher is null)
        {
            _stageWorkspaceWindow = null;
            return Task.CompletedTask;
        }

        return app.Dispatcher.InvokeAsync(() =>
        {
            if (_stageWorkspaceWindow is not null)
            {
                var window = _stageWorkspaceWindow;
                _stageWorkspaceWindow = null;
                window.Closed -= OnStageWorkspaceClosed;
                window.Close();
            }
        }).Task;
    }
}
