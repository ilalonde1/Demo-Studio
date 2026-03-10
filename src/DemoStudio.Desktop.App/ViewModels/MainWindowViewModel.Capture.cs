using System.Diagnostics;
using System.Windows;
using System.Globalization;
using System.IO;
using DemoStudio.Desktop.App.Services;
using DemoStudio.Desktop.Core.Sessions;

namespace DemoStudio.Desktop.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private async Task StartClipAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        if (!CanStartClip)
        {
            return;
        }

        SetBusy(true);
        try
        {
            using var startFlowCts = _captureSession.BeginStartFlow(_lifecycleCancellation.Token);
            OnPropertyChanged(nameof(CanStopSession));
            RaiseCommandState();

            if (_snapshot.State == RecorderSessionState.Armed)
            {
                CurrentSessionClips.Clear();
                SelectedCurrentSessionClip = null;
                IsClipCurationExpanded = false;
                _curation.ActiveClipLabel = null;
                _lastFinalizedSessionId = Guid.Empty;
                _lastFailureCode = null;
                _lastDiagnosticsPath = null;

                if (IsStageMode)
                {
                    if (!await EnsureStageWorkspaceReadyAsync(bringToFront: true))
                    {
                        _lastRuntimeMessage = "Stage workspace is unavailable. Open Stage Workspace and retry.";
                        OnPropertyChanged(nameof(LastRuntimeMessage));
                        return;
                    }
                }
                else
                {
                    EnsureWindowTargetLockedFromSelection();
                }

                var preflight = await BuildAndRunPreflightAsync();
                PreflightStatus = preflight.ToDisplayText();
                OnPropertyChanged(nameof(PreflightStatus));
                if (!preflight.IsReady)
                {
                    _lastRuntimeMessage = PreflightStatus;
                    OnPropertyChanged(nameof(LastRuntimeMessage));
                    return;
                }

                var targetSettings = BuildTargetSettings();
                if (string.Equals(targetSettings.Mode, "Window", StringComparison.OrdinalIgnoreCase))
                {
                    if (!await _captureSession.TryActivateTargetAsync(
                            IsWindowMode,
                            PresenterViewEnabled,
                            targetSettings,
                            message =>
                            {
                                _lastRuntimeMessage = message;
                                OnPropertyChanged(nameof(LastRuntimeMessage));
                            },
                            startFlowCts.Token))
                    {
                        return;
                    }
                }

                if (!await RunStartCountdownAsync(3, startFlowCts.Token))
                {
                    return;
                }

                var captureStart = await _captureSession.EnsureCaptureStartedAsync(targetSettings, startFlowCts.Token);
                if (!captureStart.Succeeded)
                {
                    EndLiveClipTracking();
                    var failedReason = BuildFailureReason(
                        "DS-DESK-START-001",
                        captureStart.ErrorMessage ?? "Failed to start capture.",
                        null);
                    _snapshot = _captureSession.StopFailed(failedReason);
                    _lastRuntimeMessage = _snapshot.FailureReason ?? "Failed to start capture.";
                    await PersistFinalizedSessionToHistoryAsync(_snapshot, captureStart.RawVideoPath);
                    await RefreshSessionHistoryAsync();
                    await ClearDraftStateAsync();
                    _snapshot = _captureSession.Reset();
                    _lastRuntimeMessage += " Session reset. Fix target/runtime issue and retry.";
                    RaiseWorkflowAndClipState();
                    return;
                }

                if (!string.IsNullOrWhiteSpace(captureStart.RawVideoPath))
                {
                    _lastOutputPath = captureStart.RawVideoPath;
                }

                _lastRuntimeMessage = $"Capture started in {CaptureMode} mode.";
                StartTargetWatchdogIfNeeded(targetSettings);
            }

            _curation.ActiveClipLabel = NormalizeClipLabel(_snapshot.ClipCount + 1);
            _snapshot = _captureSession.StartOrResumeClip();
            BeginLiveClipTracking(_snapshot);
            RaiseWorkflowAndClipState();
        }
        catch (OperationCanceledException) when (_lifecycleCancellation.IsCancellationRequested)
        {
            // Shutdown/dispose cancellation: exit without mutating session state to failed.
            return;
        }
        catch (OperationCanceledException)
        {
            if (_captureSession.IsStartCancellationRequested)
            {
                _lastRuntimeMessage = "Recording start cancelled.";
                OnPropertyChanged(nameof(LastRuntimeMessage));
                return;
            }

            EndLiveClipTracking();
            _snapshot = _captureSession.StopFailed(
                BuildFailureReason(
                    "DS-DESK-START-003",
                    "Capture startup was interrupted before completion.",
                    null));
            _lastRuntimeMessage = _snapshot.FailureReason ?? "Capture startup interrupted.";
            await PersistFinalizedSessionToHistoryAsync(_snapshot, null);
            await RefreshSessionHistoryAsync();
            await ClearDraftStateAsync();
            _snapshot = _captureSession.Reset();
            _lastRuntimeMessage += " Session reset. Retry after confirming target window is active.";
            RaiseWorkflowAndClipState();
        }
        catch (Exception ex)
        {
            EndLiveClipTracking();
            _snapshot = _captureSession.StopFailed(BuildFailureReason("DS-DESK-START-002", $"Start clip failed: {ex.Message}", ex));
            _lastRuntimeMessage = _snapshot.FailureReason ?? "Start clip failed.";
            await PersistFinalizedSessionToHistoryAsync(_snapshot, null);
            await RefreshSessionHistoryAsync();
            await ClearDraftStateAsync();
            _snapshot = _captureSession.Reset();
            _lastRuntimeMessage += " Session reset. Resolve error details and retry.";
            RaiseWorkflowAndClipState();
        }
        finally
        {
            _captureSession.CompleteStartFlow();
            OnPropertyChanged(nameof(CanStopSession));
            SetBusy(false);
            RecordOperationMetric("StartClip", stopwatch.Elapsed);
        }
    }

    private void PauseClip()
    {
        if (!CanPauseClip)
        {
            return;
        }

        _snapshot = _captureSession.PauseClip();
        CaptureCompletedClipMetadata(_snapshot);
        EndLiveClipTracking();
        PromptForNextClipLabel();
        _lastRuntimeMessage = string.IsNullOrWhiteSpace(NextClipLabel)
            ? "Clip paused. Resume creates a new clip segment."
            : $"Clip paused. Next clip title set: {NextClipLabel}.";
        RaiseWorkflowAndClipState();
    }

    private void PromptForNextClipLabel()
    {
        try
        {
            var app = System.Windows.Application.Current;
            if (app is null)
            {
                return;
            }

            Window? owner = null;
            app.Dispatcher.Invoke(() =>
            {
                owner = app.Windows.OfType<Window>().FirstOrDefault(x => x.IsActive) ?? app.MainWindow;
            });

            var prompt = new ClipLabelPromptWindow(NextClipLabel);
            if (owner is not null)
            {
                prompt.Owner = owner;
            }

            var accepted = prompt.ShowDialog() == true;
            NextClipLabel = accepted ? prompt.LabelValue : string.Empty;
        }
        catch
        {
            // Prompt is a convenience path; recording flow should keep going if UI prompt fails.
        }
    }

    private async Task StopSessionAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        if (!CanStopSession)
        {
            return;
        }

        if (_captureSession.IsStartClipInFlight)
        {
            _captureSession.CancelPendingStart();
            _lastRuntimeMessage = "Cancelling recording start...";
            OnPropertyChanged(nameof(LastRuntimeMessage));
            return;
        }

        SetBusy(true);
        try
        {
            await StopTargetWatchdogAsync();
            var stopResult = await _captureSession.StopCaptureAsync();
            EndLiveClipTracking();
            if (!stopResult.Succeeded)
            {
                _snapshot = _captureSession.StopFailed(
                    BuildFailureReason("DS-DESK-STOP-001", stopResult.ErrorMessage ?? "Capture stop failed.", null));
                CaptureCompletedClipMetadata(_snapshot);
                _lastRuntimeMessage = _snapshot.FailureReason ?? "Capture stop failed.";
                await PersistFinalizedSessionToHistoryAsync(_snapshot, stopResult.RawVideoPath);
                IsClipCurationExpanded = true;
                await ClearDraftStateAsync();
                _snapshot = _captureSession.Reset();
                _lastRuntimeMessage += " Open Clip Editor to review clips and build final video.";
                await RefreshSessionHistoryAsync();
                RaiseWorkflowAndClipState();
                return;
            }

            _snapshot = _captureSession.StopCompleted();
            CaptureCompletedClipMetadata(_snapshot);
            if (!string.IsNullOrWhiteSpace(stopResult.RawVideoPath))
            {
                _lastOutputPath = stopResult.RawVideoPath;
            }

            await PersistFinalizedSessionToHistoryAsync(_snapshot, stopResult.RawVideoPath);
            _ = GenerateMissingClipThumbnailsAsync(stopResult.RawVideoPath, _snapshot.SessionId);
            IsClipCurationExpanded = true;
            await ClearDraftStateAsync();
            _snapshot = _captureSession.Reset();
            _lastRuntimeMessage = "Capture process stopped. Open Clip Editor to reorder clips and build your final video.";
            await RefreshSessionHistoryAsync();
            RaiseWorkflowAndClipState();
        }
        catch (Exception ex)
        {
            _snapshot = _captureSession.StopFailed(BuildFailureReason("DS-DESK-STOP-002", $"Stop session failed: {ex.Message}", ex));
            EndLiveClipTracking();
            _lastRuntimeMessage = _snapshot.FailureReason ?? "Stop session failed.";
            RaiseWorkflowAndClipState();
        }
        finally
        {
            SetBusy(false);
            RecordOperationMetric("StopSession", stopwatch.Elapsed);
        }
    }

    private async Task<bool> RunStartCountdownAsync(int seconds, CancellationToken cancellationToken)
    {
        return await _captureSession.RunStartCountdownAsync(
            seconds,
            cancellationToken,
            message =>
            {
                _lastRuntimeMessage = message;
                OnPropertyChanged(nameof(LastRuntimeMessage));
            });
    }

    private string NormalizeClipLabel(int sequence)
    {
        return _captureSession.NormalizeClipLabel(sequence, NextClipLabel);
    }

    private void BeginLiveClipTracking(RecorderSessionSnapshot snapshot)
    {
        _captureSession.BeginLiveClipTracking(
            snapshot,
            _curation,
            CurrentSessionClips,
            CaptureNarration,
            _liveClipTimer,
            () => OnPropertyChanged(nameof(CurrentSessionClips)));
    }

    private void UpdateLiveClipPreview()
    {
        _captureSession.UpdateLiveClipPreview(_snapshot, _curation);
    }

    private void EndLiveClipTracking()
    {
        _captureSession.EndLiveClipTracking(_curation, _liveClipTimer);
    }

    private void CaptureCompletedClipMetadata(RecorderSessionSnapshot snapshot)
    {
        _captureSession.CaptureCompletedClipMetadata(
            snapshot,
            _curation,
            CurrentSessionClips,
            CaptureNarration,
            TryGenerateClipThumbnailAsync,
            () => SelectedCurrentSessionClip,
            clip => SelectedCurrentSessionClip = clip,
            () =>
            {
                OnPropertyChanged(nameof(CurrentSessionClips));
                OnPropertyChanged(nameof(CanMoveSelectedClipUp));
                OnPropertyChanged(nameof(CanMoveSelectedClipDown));
            });
    }

    private void ReindexClipOrders()
    {
        _captureSession.ReindexClipOrders(CurrentSessionClips);
    }

    private async Task ExecutePrimaryWorkflowAsync()
    {
        if (!CanExecutePrimaryWorkflow)
        {
            return;
        }

        if (_snapshot.State == RecorderSessionState.Recording)
        {
            PauseClip();
            return;
        }

        if (_snapshot.State is RecorderSessionState.Armed or RecorderSessionState.Paused)
        {
            await StartClipAsync();
        }
    }
}
