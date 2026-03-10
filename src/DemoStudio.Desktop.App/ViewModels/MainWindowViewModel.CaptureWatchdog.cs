using DemoStudio.Desktop.App.Services;
using DemoStudio.Desktop.Core.Sessions;

namespace DemoStudio.Desktop.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private void StartTargetWatchdogIfNeeded(CaptureTargetSettings settings)
    {
        if (!string.Equals(settings.Mode, "Window", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _captureWatchdogCoordinator.Start(
            BuildTargetSettings,
            PublishWatchdogMessageAsync,
            StopFromWatchdogAsync);
    }

    private Task StopTargetWatchdogAsync()
    {
        return _captureWatchdogCoordinator.StopAsync();
    }

    private Task PublishWatchdogMessageAsync(string message)
    {
        var app = System.Windows.Application.Current;
        if (app?.Dispatcher is null)
        {
            _lastRuntimeMessage = message;
            OnPropertyChanged(nameof(LastRuntimeMessage));
            return Task.CompletedTask;
        }

        var op = app.Dispatcher.InvokeAsync(() =>
        {
            _lastRuntimeMessage = message;
            OnPropertyChanged(nameof(LastRuntimeMessage));
        });
        return op.Task;
    }

    private Task StopFromWatchdogAsync(string reason)
    {
        var app = System.Windows.Application.Current;
        if (app?.Dispatcher is null)
        {
            return HandleWatchdogStopAsync(reason);
        }

        var stopOp = app.Dispatcher.InvokeAsync(() => HandleWatchdogStopAsync(reason));
        return stopOp.Task.Unwrap();
    }

    private async Task HandleWatchdogStopAsync(string reason)
    {
        if (_snapshot.State is RecorderSessionState.Completed or RecorderSessionState.Failed or RecorderSessionState.Armed)
        {
            return;
        }

        SetBusy(true);
        try
        {
            await StopTargetWatchdogAsync();
            var stopResult = await _captureRuntime.StopAsync();
            EndLiveClipTracking();
            var detail = reason + (stopResult.Succeeded ? string.Empty : $" {stopResult.ErrorMessage}");
            _snapshot = _sessionEngine.StopFailed(BuildFailureReason("DS-DESK-WATCH-001", detail, null));
            await PersistFinalizedSessionToHistoryAsync(_snapshot, stopResult.RawVideoPath);
            IsClipCurationExpanded = true;
            await ClearDraftStateAsync();
            _snapshot = _sessionEngine.Reset();
            _lastRuntimeMessage = reason + " Open Clip Editor to inspect captured clips.";
            await RefreshSessionHistoryAsync();
            RaiseWorkflowAndClipState();
        }
        catch (Exception ex)
        {
            _snapshot = _sessionEngine.StopFailed(BuildFailureReason("DS-DESK-WATCH-002", $"Watchdog stop failed: {ex.Message}", ex));
            _lastRuntimeMessage = _snapshot.FailureReason ?? "Watchdog stop failed.";
            RaiseWorkflowAndClipState();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private CaptureTargetSettings BuildTargetSettings()
    {
        var mode = IsStageMode ? "Window" : CaptureMode;
        return new CaptureTargetSettings(
            Mode: mode,
            WindowTitleContains: string.IsNullOrWhiteSpace(WindowTitleContains) ? null : WindowTitleContains,
            WindowProcessName: string.IsNullOrWhiteSpace(WindowProcessName) ? null : WindowProcessName,
            WindowHandleHex: string.IsNullOrWhiteSpace(WindowHandleHex) ? null : WindowHandleHex,
            FallbackToDesktop: FallbackToDesktop);
    }

    private string BuildFailureReason(string failureCode, string reason, Exception? exception)
    {
        var cleanReason = string.IsNullOrWhiteSpace(reason) ? "Unknown failure." : reason.Trim();
        _lastFailureCode = failureCode;
        _lastDiagnosticsPath = _diagnosticsBundleService.TryWriteFailureBundle(
            _snapshot.SessionId,
            _captureRuntime.LastRawVideoPath,
            failureCode,
            cleanReason,
            exception,
            BuildTargetSettings(),
            _captureRuntime.FfmpegPath,
            LaunchExecutablePath,
            LaunchArguments,
            LaunchWorkingDirectory);

        var diagnosticsNote = string.IsNullOrWhiteSpace(_lastDiagnosticsPath) ? null : $"Diagnostics: {_lastDiagnosticsPath}";
        return BuildFailureDisplay(failureCode, cleanReason, diagnosticsNote);
    }
}
