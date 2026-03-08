using System.Diagnostics;
using DemoStudio.Desktop.App.Services;

namespace DemoStudio.Desktop.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private async Task RecordNarrationForSelectedClipAsync()
    {
        var clip = SelectedCurrentSessionClip;
        if (!CanRecordNarration || clip is null)
        {
            return;
        }

        SetBusy(true);
        try
        {
            var sessionId = _lastFinalizedSessionId != Guid.Empty ? _lastFinalizedSessionId : _snapshot.SessionId;
            _lastRuntimeMessage = $"Recording narration for {clip.Label} ({clip.DurationDisplay})...";
            OnPropertyChanged(nameof(LastRuntimeMessage));

            var result = await _narrationCoordinator.RecordNarrationAsync(clip, sessionId, MicrophoneDeviceName);
            _lastRuntimeMessage = result.Message;
            OnPropertyChanged(nameof(LastRuntimeMessage));
            if (result.DraftChanged)
            {
                await SaveDraftStateAsync();
            }
        }
        catch (Exception ex)
        {
            SetRuntimeFailure("DS-DESK-NARR-001", "Narration capture failed.", ex);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task GenerateAiNarrationForSelectedClipAsync()
    {
        if (_snapshot.State == DemoStudio.Desktop.Core.Sessions.RecorderSessionState.Recording)
        {
            _lastRuntimeMessage = "Pause or stop recording before generating AI narration.";
            OnPropertyChanged(nameof(LastRuntimeMessage));
            return;
        }

        var clip = SelectedCurrentSessionClip ?? CurrentSessionClips.OrderBy(x => x.Order).FirstOrDefault();
        if (clip is null || clip.DurationSeconds <= 0.15d)
        {
            _lastRuntimeMessage = "Select a clip with non-zero duration before generating AI narration.";
            OnPropertyChanged(nameof(LastRuntimeMessage));
            return;
        }

        if (SelectedCurrentSessionClip is null)
        {
            SelectedCurrentSessionClip = clip;
        }

        if (string.IsNullOrWhiteSpace(AiNarrationApiKey) && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY")))
        {
            _lastRuntimeMessage = "AI narration is not configured. Set API key in AI Voice Settings or OPENAI_API_KEY.";
            OnPropertyChanged(nameof(LastRuntimeMessage));
            return;
        }

        SetBusy(true);
        try
        {
            var sessionId = _lastFinalizedSessionId != Guid.Empty ? _lastFinalizedSessionId : _snapshot.SessionId;

            _lastRuntimeMessage = $"Generating AI narration for {clip.Label}...";
            OnPropertyChanged(nameof(LastRuntimeMessage));

            var result = await _narrationCoordinator.GenerateAiNarrationAsync(
                clip,
                sessionId,
                new DesktopNarrationCoordinator.AiNarrationOptions(
                    AiNarrationProvider,
                    AiNarrationBaseUrl,
                    AiNarrationModel,
                    AiNarrationVoice,
                    AiNarrationApiKey,
                    AiAutoTrimScript,
                    AiWordsPerSecond),
                _lifecycleCancellation.Token);
            _lastRuntimeMessage = result.Message;
            OnPropertyChanged(nameof(LastRuntimeMessage));
            if (result.DraftChanged)
            {
                await SaveDraftStateAsync();
            }
        }
        catch (Exception ex)
        {
            SetRuntimeFailure("DS-DESK-NARR-002", "AI narration failed.", ex);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void PlayNarrationForSelectedClip()
    {
        var clip = SelectedCurrentSessionClip;
        if (!CanPlayNarration || clip is null || string.IsNullOrWhiteSpace(clip.NarrationAudioPath))
        {
            return;
        }

        var result = _narrationCoordinator.PlayNarration(clip);
        _lastRuntimeMessage = result.Message;
        OnPropertyChanged(nameof(LastRuntimeMessage));
    }

    private void ClearNarrationForSelectedClip()
    {
        var clip = SelectedCurrentSessionClip;
        if (!CanClearNarration || clip is null)
        {
            return;
        }

        var result = _narrationCoordinator.ClearNarration(clip);
        _lastRuntimeMessage = result.Message;
        OnPropertyChanged(nameof(LastRuntimeMessage));
        if (result.DraftChanged)
        {
            _ = SaveDraftStateAsync();
        }
        RaiseCommandState();
    }

    private void MoveSelectedClipUp()
    {
        if (!CanMoveSelectedClipUp || SelectedCurrentSessionClip is null)
        {
            return;
        }

        if (!_clipCurationCoordinator.TryMoveSelectedUp(CurrentSessionClips, SelectedCurrentSessionClip))
        {
            return;
        }

        OnPropertyChanged(nameof(CurrentSessionClips));
        OnPropertyChanged(nameof(CanMoveSelectedClipUp));
        OnPropertyChanged(nameof(CanMoveSelectedClipDown));
    }

    private void MoveSelectedClipDown()
    {
        if (!CanMoveSelectedClipDown || SelectedCurrentSessionClip is null)
        {
            return;
        }

        if (!_clipCurationCoordinator.TryMoveSelectedDown(CurrentSessionClips, SelectedCurrentSessionClip))
        {
            return;
        }

        OnPropertyChanged(nameof(CurrentSessionClips));
        OnPropertyChanged(nameof(CanMoveSelectedClipUp));
        OnPropertyChanged(nameof(CanMoveSelectedClipDown));
    }

    public void ReorderClip(int fromIndex, int toIndex)
    {
        var selected = _clipCurationCoordinator.TryReorder(CurrentSessionClips, fromIndex, toIndex);
        if (selected is null)
        {
            return;
        }

        SelectedCurrentSessionClip = selected;
        OnPropertyChanged(nameof(CurrentSessionClips));
        OnPropertyChanged(nameof(CanMoveSelectedClipUp));
        OnPropertyChanged(nameof(CanMoveSelectedClipDown));
    }

    private async Task ComposeVideoAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        if (!CanComposeManifest)
        {
            return;
        }

        SetBusy(true);
        try
        {
            var manifest = new DesktopComposeManifest(
                SessionId: _lastFinalizedSessionId != Guid.Empty ? _lastFinalizedSessionId : _snapshot.SessionId,
                RawVideoPath: _lastOutputPath,
                GeneratedUtc: DateTimeOffset.UtcNow,
                Clips: CurrentSessionClips
                    .OrderBy(x => x.Order)
                    .Select(x => new DesktopComposeClip(
                        x.Order,
                        x.Sequence,
                        x.Label,
                        string.IsNullOrWhiteSpace(x.BannerText) ? null : x.BannerText.Trim(),
                        x.StartSeconds,
                        x.DurationSeconds,
                        x.DurationDisplay,
                        x.IncludeNarration,
                        x.NarrationAudioPath))
                    .ToArray(),
                QualityPreset: SelectedComposeQualityPreset,
                ExportStyle: SelectedExportStyle);

            var manifestResult = _composeManifestService.WriteManifest(manifest);
            if (!manifestResult.Succeeded)
            {
                ComposeStatus = manifestResult.Message;
                _lastRuntimeMessage = manifestResult.Message;
                OnPropertyChanged(nameof(ComposeStatus));
                OnPropertyChanged(nameof(LastRuntimeMessage));
                return;
            }

            var queuedCompose = await _ffmpegOperationQueue.EnqueueAsync(
                "Build Final Video",
                ct => _videoComposeService.ComposeAsync(manifest, _captureRuntime.FfmpegPath, ct),
                _lifecycleCancellation.Token);
            if (!queuedCompose.Accepted || queuedCompose.Value is null)
            {
                ComposeStatus = string.IsNullOrWhiteSpace(queuedCompose.Message)
                    ? "Compose skipped: render queue is full."
                    : queuedCompose.Message;
                _lastRuntimeMessage = ComposeStatus;
                OnPropertyChanged(nameof(ComposeStatus));
                OnPropertyChanged(nameof(LastRuntimeMessage));
                return;
            }

            var composeResult = queuedCompose.Value;
            ComposeStatus = composeResult.Message;
            _lastRuntimeMessage = composeResult.Message;
            if (queuedCompose.QueueDelay > TimeSpan.FromMilliseconds(200))
            {
                _lastRuntimeMessage = $"{composeResult.Message} (queued {queuedCompose.QueueDelay.TotalSeconds:0.0}s)";
            }
            if (composeResult.Succeeded && !string.IsNullOrWhiteSpace(composeResult.OutputPath))
            {
                _lastOutputPath = composeResult.OutputPath;
                OnPropertyChanged(nameof(LastOutputPath));
            }

            OnPropertyChanged(nameof(ComposeStatus));
            OnPropertyChanged(nameof(LastRuntimeMessage));
        }
        catch (Exception ex)
        {
            ComposeStatus = $"Compose failed: {ex.Message}";
            _lastRuntimeMessage = ComposeStatus;
            OnPropertyChanged(nameof(ComposeStatus));
            OnPropertyChanged(nameof(LastRuntimeMessage));
        }
        finally
        {
            SetBusy(false);
            RecordOperationMetric("ComposeVideo", stopwatch.Elapsed);
        }
    }
}
