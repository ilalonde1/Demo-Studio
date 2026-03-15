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
            using var startFlowCts = CancellationTokenSource.CreateLinkedTokenSource(_lifecycleCancellation.Token);
            OnPropertyChanged(nameof(CanStopSession));
            RaiseCommandState();
            var result = await _captureSessionUseCase.StartAsync(
                new DesktopCaptureStartRequest(
                    _snapshot,
                    IsStageMode,
                    IsWindowMode,
                    PresenterViewEnabled,
                    async () =>
                    {
                        CurrentSessionClips.Clear();
                        SelectedCurrentSessionClip = null;
                        IsClipCurationExpanded = false;
                        _curation.ActiveClipLabel = null;
                        _lastFinalizedSessionId = Guid.Empty;
                        _lastFailureCode = null;
                        _lastDiagnosticsPath = null;
                        await Task.CompletedTask;
                    },
                    EnsureStageWorkspaceReadyAsync,
                    EnsureWindowTargetLockedFromSelection,
                    new DesktopPreflightChecksRequest(IsStageMode, EnsureStageWorkspaceReadyAsync, BuildAndRunPreflightAsync),
                    status =>
                    {
                        PreflightStatus = status;
                        OnPropertyChanged(nameof(PreflightStatus));
                    },
                    BuildTargetSettings,
                    (targetSettings, cancellationToken) => _captureSession.TryActivateTargetAsync(
                        IsWindowMode,
                        PresenterViewEnabled,
                        targetSettings,
                        message =>
                        {
                            _lastRuntimeMessage = message;
                            OnPropertyChanged(nameof(LastRuntimeMessage));
                        },
                        cancellationToken),
                    RunStartCountdownAsync,
                    _captureSession,
                    targetSettings =>
                    {
                        StartTargetWatchdogIfNeeded(targetSettings);
                        return Task.CompletedTask;
                    },
                    PersistFinalizedSessionToHistoryAsync,
                    RefreshSessionHistoryAsync,
                    ClearDraftStateAsync),
                _lifecycleCancellation.Token);

            _snapshot = result.Snapshot;
            if (!string.IsNullOrWhiteSpace(result.LastOutputPath))
            {
                _lastOutputPath = result.LastOutputPath!;
            }

            if (result.ShouldRaiseWorkflowState)
            {
                _curation.ActiveClipLabel = NormalizeClipLabel(_snapshot.ClipCount + 1);
                BeginLiveClipTracking(_snapshot);
                RaiseWorkflowAndClipState();
            }

            if (!string.IsNullOrWhiteSpace(result.RuntimeMessage))
            {
                _lastRuntimeMessage = result.RuntimeMessage;
                OnPropertyChanged(nameof(LastRuntimeMessage));
            }
            else if (_captureSession.IsStartCancellationRequested)
            {
                _lastRuntimeMessage = "Recording start cancelled.";
                OnPropertyChanged(nameof(LastRuntimeMessage));
            }
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
            var result = await _captureSessionUseCase.StopAsync(
                new DesktopCaptureStopRequest(
                    _captureSession,
                    StopTargetWatchdogAsync,
                    EndLiveClipTracking,
                    CaptureCompletedClipMetadata,
                    PersistFinalizedSessionToHistoryAsync,
                    GenerateMissingClipThumbnailsAsync,
                    () => IsClipCurationExpanded = true,
                    ClearDraftStateAsync,
                    RefreshSessionHistoryAsync,
                    _snapshot,
                    () => _snapshot.SessionId,
                    path =>
                    {
                        _lastOutputPath = path ?? _lastOutputPath;
                        OnPropertyChanged(nameof(LastOutputPath));
                    }));
            _snapshot = result.Snapshot;
            _lastRuntimeMessage = result.RuntimeMessage;
            if (_snapshot.State == RecorderSessionState.Completed && _snapshot.ClipCount > 0)
            {
                _onboarding.Dismiss();
                _lastRuntimeMessage = CanCreatePublishPackage
                    ? "Recording complete. You can now export your tutorial."
                    : "Recording complete. Review the captured clips, then generate your tutorial.";
            }
            if (result.ShouldRaiseWorkflowState)
            {
                RaiseWorkflowAndClipState();
            }
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
