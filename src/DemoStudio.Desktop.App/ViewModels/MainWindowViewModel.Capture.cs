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
            _startCancellationRequested = false;
            using var startFlowCts = CancellationTokenSource.CreateLinkedTokenSource(_lifecycleCancellation.Token);
            _startClipCancellation = startFlowCts;
            _startClipInFlight = true;
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
                    if (IsWindowMode && PresenterViewEnabled && _presenterViewService.TryMoveTargetToSecondary(targetSettings.WindowHandleHex))
                    {
                        _lastRuntimeMessage = "Presenter View: moved target to secondary monitor.";
                        OnPropertyChanged(nameof(LastRuntimeMessage));
                        await Task.Delay(200, startFlowCts.Token);
                    }

                    var focus = await _windowFocusService.TryActivateAsync(targetSettings);
                    _lastRuntimeMessage = focus.Message;
                    OnPropertyChanged(nameof(LastRuntimeMessage));
                    if (!focus.Succeeded)
                    {
                        return;
                    }

                    await Task.Delay(250, startFlowCts.Token);
                }

                if (!await RunStartCountdownAsync(3, startFlowCts.Token))
                {
                    return;
                }

                var captureStart = await _captureRuntime.EnsureStartedAsync(targetSettings, startFlowCts.Token);
                if (!captureStart.Succeeded)
                {
                    EndLiveClipTracking();
                    var failedReason = BuildFailureReason(
                        "DS-DESK-START-001",
                        captureStart.ErrorMessage ?? "Failed to start capture.",
                        null);
                    _snapshot = _sessionEngine.StopFailed(failedReason);
                    _lastRuntimeMessage = _snapshot.FailureReason ?? "Failed to start capture.";
                    await PersistFinalizedSessionToHistoryAsync(_snapshot, captureStart.RawVideoPath);
                    await RefreshSessionHistoryAsync();
                    await ClearDraftStateAsync();
                    _snapshot = _sessionEngine.Reset();
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
            _snapshot = _sessionEngine.StartOrResumeClip();
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
            if (_startCancellationRequested)
            {
                _lastRuntimeMessage = "Recording start cancelled.";
                OnPropertyChanged(nameof(LastRuntimeMessage));
                return;
            }

            EndLiveClipTracking();
            _snapshot = _sessionEngine.StopFailed(
                BuildFailureReason(
                    "DS-DESK-START-003",
                    "Capture startup was interrupted before completion.",
                    null));
            _lastRuntimeMessage = _snapshot.FailureReason ?? "Capture startup interrupted.";
            await PersistFinalizedSessionToHistoryAsync(_snapshot, null);
            await RefreshSessionHistoryAsync();
            await ClearDraftStateAsync();
            _snapshot = _sessionEngine.Reset();
            _lastRuntimeMessage += " Session reset. Retry after confirming target window is active.";
            RaiseWorkflowAndClipState();
        }
        catch (Exception ex)
        {
            EndLiveClipTracking();
            _snapshot = _sessionEngine.StopFailed(BuildFailureReason("DS-DESK-START-002", $"Start clip failed: {ex.Message}", ex));
            _lastRuntimeMessage = _snapshot.FailureReason ?? "Start clip failed.";
            await PersistFinalizedSessionToHistoryAsync(_snapshot, null);
            await RefreshSessionHistoryAsync();
            await ClearDraftStateAsync();
            _snapshot = _sessionEngine.Reset();
            _lastRuntimeMessage += " Session reset. Resolve error details and retry.";
            RaiseWorkflowAndClipState();
        }
        finally
        {
            _startClipInFlight = false;
            _startCancellationRequested = false;
            _startClipCancellation = null;
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

        _snapshot = _sessionEngine.PauseClip();
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

        if (_startClipInFlight)
        {
            _startCancellationRequested = true;
            _startClipCancellation?.Cancel();
            _lastRuntimeMessage = "Cancelling recording start...";
            OnPropertyChanged(nameof(LastRuntimeMessage));
            return;
        }

        SetBusy(true);
        try
        {
            await StopTargetWatchdogAsync();
            var stopResult = await _captureRuntime.StopAsync();
            EndLiveClipTracking();
            if (!stopResult.Succeeded)
            {
                _snapshot = _sessionEngine.StopFailed(
                    BuildFailureReason("DS-DESK-STOP-001", stopResult.ErrorMessage ?? "Capture stop failed.", null));
                CaptureCompletedClipMetadata(_snapshot);
                _lastRuntimeMessage = _snapshot.FailureReason ?? "Capture stop failed.";
                await PersistFinalizedSessionToHistoryAsync(_snapshot, stopResult.RawVideoPath);
                IsClipCurationExpanded = true;
                await ClearDraftStateAsync();
                _snapshot = _sessionEngine.Reset();
                _lastRuntimeMessage += " Open Clip Editor to review clips and build final video.";
                await RefreshSessionHistoryAsync();
                RaiseWorkflowAndClipState();
                return;
            }

            _snapshot = _sessionEngine.StopCompleted();
            CaptureCompletedClipMetadata(_snapshot);
            if (!string.IsNullOrWhiteSpace(stopResult.RawVideoPath))
            {
                _lastOutputPath = stopResult.RawVideoPath;
            }

            await PersistFinalizedSessionToHistoryAsync(_snapshot, stopResult.RawVideoPath);
            _ = GenerateMissingClipThumbnailsAsync(stopResult.RawVideoPath, _snapshot.SessionId);
            IsClipCurationExpanded = true;
            await ClearDraftStateAsync();
            _snapshot = _sessionEngine.Reset();
            _lastRuntimeMessage = "Capture process stopped. Open Clip Editor to reorder clips and build your final video.";
            await RefreshSessionHistoryAsync();
            RaiseWorkflowAndClipState();
        }
        catch (Exception ex)
        {
            _snapshot = _sessionEngine.StopFailed(BuildFailureReason("DS-DESK-STOP-002", $"Stop session failed: {ex.Message}", ex));
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

    private async Task RunSmokeCheckAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        if (!CanRunSmokeCheck)
        {
            return;
        }

        SetBusy(true);
        try
        {
            _smokeCheckStatus = "Running smoke check...";
            OnPropertyChanged(nameof(SmokeCheckStatus));
            var result = await _smokeCheckService.RunAsync(3);
            _smokeCheckStatus = result.Succeeded
                ? $"{result.Message} Output: {result.OutputPath}"
                : $"Smoke FAIL: {result.Message}";
            _lastRuntimeMessage = _smokeCheckStatus;
            OnPropertyChanged(nameof(SmokeCheckStatus));
            OnPropertyChanged(nameof(LastRuntimeMessage));
        }
        catch (Exception ex)
        {
            _smokeCheckStatus = BuildFailureDisplay("DS-DESK-SMOKE-001", "Smoke check failed.", ex.Message);
            _lastRuntimeMessage = _smokeCheckStatus;
            OnPropertyChanged(nameof(SmokeCheckStatus));
            OnPropertyChanged(nameof(LastRuntimeMessage));
        }
        finally
        {
            SetBusy(false);
            RecordOperationMetric("SmokeCheck", stopwatch.Elapsed);
        }
    }

    private async Task<bool> RunStartCountdownAsync(int seconds, CancellationToken cancellationToken)
    {
        if (seconds <= 0)
        {
            return true;
        }

        for (var remaining = seconds; remaining >= 1; remaining--)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            _lastRuntimeMessage = $"Recording starts in {remaining}...";
            OnPropertyChanged(nameof(LastRuntimeMessage));
            try
            {
                await Task.Delay(1000, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return false;
            }
        }

        return true;
    }

    private string NormalizeClipLabel(int sequence)
    {
        var candidate = NextClipLabel?.Trim();
        return string.IsNullOrWhiteSpace(candidate) ? $"Clip {sequence}" : candidate;
    }

    private void BeginLiveClipTracking(RecorderSessionSnapshot snapshot)
    {
        if (snapshot.State != RecorderSessionState.Recording || snapshot.Clips.Count == 0)
        {
            return;
        }

        var activeClip = snapshot.Clips[^1];
        var label = string.IsNullOrWhiteSpace(_curation.ActiveClipLabel) ? $"Clip {activeClip.Sequence}" : _curation.ActiveClipLabel!;
        var existing = CurrentSessionClips.FirstOrDefault(x => x.Sequence == activeClip.Sequence);
        if (existing is null)
        {
            existing = new CurrentSessionClipItem(
                activeClip.Sequence,
                label,
                "00:00",
                string.Empty,
                Math.Max(0d, (activeClip.StartedUtc - snapshot.StartedUtc).TotalSeconds),
                0d)
            {
                Order = CurrentSessionClips.Count + 1,
                IncludeNarration = CaptureNarration
            };
            CurrentSessionClips.Add(existing);
        }
        else if (string.IsNullOrWhiteSpace(existing.Label))
        {
            existing.Label = label;
        }

        _curation.ActiveClipItem = existing;
        _curation.ActiveClipStartedUtc = activeClip.StartedUtc;
        if (!_liveClipTimer.IsEnabled)
        {
            _liveClipTimer.Start();
        }

        OnPropertyChanged(nameof(CurrentSessionClips));
    }

    private void UpdateLiveClipPreview()
    {
        if (_snapshot.State != RecorderSessionState.Recording || _curation.ActiveClipItem is null)
        {
            return;
        }

        var elapsed = DateTimeOffset.UtcNow - _curation.ActiveClipStartedUtc;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        _curation.ActiveClipItem.DurationSeconds = elapsed.TotalSeconds;
        _curation.ActiveClipItem.DurationDisplay = elapsed.ToString(@"mm\:ss");
        _curation.ActiveClipItem.TimelineWidth = Math.Clamp(120d + (_curation.ActiveClipItem.DurationSeconds * 8d), 120d, 420d);
    }

    private void EndLiveClipTracking()
    {
        _curation.ActiveClipItem = null;
        if (_liveClipTimer.IsEnabled)
        {
            _liveClipTimer.Stop();
        }
    }

    private void CaptureCompletedClipMetadata(RecorderSessionSnapshot snapshot)
    {
        var changed = false;
        foreach (var clip in snapshot.Clips)
        {
            var existing = CurrentSessionClips.FirstOrDefault(x => x.Sequence == clip.Sequence);
            if (existing is not null)
            {
                existing.StartSeconds = (clip.StartedUtc - snapshot.StartedUtc).TotalSeconds;
                existing.DurationSeconds = clip.Duration.TotalSeconds;
                existing.DurationDisplay = clip.Duration.ToString(@"mm\:ss");
                existing.TimelineWidth = Math.Clamp(120d + (existing.DurationSeconds * 8d), 120d, 420d);
                _ = TryGenerateClipThumbnailAsync(existing);
                if (_curation.ActiveClipItem is not null && _curation.ActiveClipItem.Sequence == existing.Sequence)
                {
                    _curation.ActiveClipLabel = null;
                }
                continue;
            }

            var label = _curation.ActiveClipLabel;
            if (string.IsNullOrWhiteSpace(label))
            {
                label = $"Clip {clip.Sequence}";
            }

            CurrentSessionClips.Add(new CurrentSessionClipItem(
                clip.Sequence,
                label,
                clip.Duration.ToString(@"mm\:ss"),
                string.Empty,
                (clip.StartedUtc - snapshot.StartedUtc).TotalSeconds,
                clip.Duration.TotalSeconds));
            CurrentSessionClips[^1].IncludeNarration = CaptureNarration;
            _ = TryGenerateClipThumbnailAsync(CurrentSessionClips[^1]);
            _curation.ActiveClipLabel = null;
            changed = true;
        }

        if (changed)
        {
            ReindexClipOrders();
            if (SelectedCurrentSessionClip is null && CurrentSessionClips.Count > 0)
            {
                SelectedCurrentSessionClip = CurrentSessionClips[^1];
            }
        }

        OnPropertyChanged(nameof(CurrentSessionClips));
        OnPropertyChanged(nameof(CanMoveSelectedClipUp));
        OnPropertyChanged(nameof(CanMoveSelectedClipDown));
    }

    private void ReindexClipOrders()
    {
        _clipCurationCoordinator.ReindexClipOrders(CurrentSessionClips);
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
