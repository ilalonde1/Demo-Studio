using System.IO;

namespace DemoStudio.Desktop.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private async Task GenerateMissingClipThumbnailsAsync(string? rawVideoPath, Guid sessionId)
    {
        if (CurrentSessionClips.Count == 0)
        {
            return;
        }

        await _captureMediaCoordinator.GenerateMissingClipThumbnailsAsync(
            CurrentSessionClips.Where(x => !x.HasThumbnail),
            rawVideoPath,
            sessionId,
            AssignThumbnailPath);
    }

    private async Task BackfillClipEditorThumbnailsAsync()
    {
        if (CurrentSessionClips.Count == 0)
        {
            return;
        }

        if (!_captureMediaCoordinator.CanRunFfmpeg)
        {
            _lastRuntimeMessage = "Clip thumbnails unavailable: FFmpeg is not available.";
            OnPropertyChanged(nameof(LastRuntimeMessage));
            return;
        }

        if (Interlocked.Exchange(ref _clipThumbnailBackfillInFlight, 1) == 1)
        {
            return;
        }

        try
        {
            var missingBefore = CurrentSessionClips.Count(x => !x.HasThumbnail);
            if (missingBefore == 0)
            {
                return;
            }

            var (rawVideoPath, sessionId) = ResolveThumbnailSource();
            await GenerateMissingClipThumbnailsAsync(rawVideoPath, sessionId);

            var missingAfter = CurrentSessionClips.Count(x => !x.HasThumbnail);
            if (missingAfter == 0)
            {
                _lastRuntimeMessage = $"Clip thumbnails ready ({CurrentSessionClips.Count}).";
                OnPropertyChanged(nameof(LastRuntimeMessage));
            }
            else if (missingAfter < missingBefore)
            {
                _lastRuntimeMessage = $"Generated {missingBefore - missingAfter} clip thumbnail(s).";
                OnPropertyChanged(nameof(LastRuntimeMessage));
            }
        }
        catch
        {
        }
        finally
        {
            Interlocked.Exchange(ref _clipThumbnailBackfillInFlight, 0);
        }
    }

    private (string? RawVideoPath, Guid SessionId) ResolveThumbnailSource()
    {
        var sessionId = _lastFinalizedSessionId != Guid.Empty ? _lastFinalizedSessionId : _snapshot.SessionId;

        var historyRawPath = SessionHistory
            .FirstOrDefault(x =>
                x.SessionId == sessionId &&
                !string.IsNullOrWhiteSpace(x.RawVideoPath) &&
                File.Exists(x.RawVideoPath))
            ?.RawVideoPath;
        if (!string.IsNullOrWhiteSpace(historyRawPath))
        {
            return (historyRawPath, sessionId);
        }

        if (!string.IsNullOrWhiteSpace(_captureRuntime.LastRawVideoPath) && File.Exists(_captureRuntime.LastRawVideoPath))
        {
            return (_captureRuntime.LastRawVideoPath, sessionId);
        }

        if (!string.IsNullOrWhiteSpace(_lastOutputPath) && _lastOutputPath != "-" && File.Exists(_lastOutputPath))
        {
            return (_lastOutputPath, sessionId);
        }

        return (null, sessionId);
    }

    private Task TryGenerateClipThumbnailAsync(CurrentSessionClipItem clip)
        => TryGenerateClipThumbnailAsync(clip, _lastOutputPath, _snapshot.SessionId);

    private async Task TryGenerateClipThumbnailAsync(CurrentSessionClipItem clip, string? rawVideoPath, Guid sessionId)
    {
        await _captureMediaCoordinator.TryGenerateClipThumbnailAsync(clip, rawVideoPath, sessionId, AssignThumbnailPath);
    }

    private async Task PlayClipPreviewAsync(CurrentSessionClipItem? clip)
    {
        if (clip is null || !CanPlayClipPreview)
        {
            return;
        }

        var sessionId = _lastFinalizedSessionId != Guid.Empty ? _lastFinalizedSessionId : _snapshot.SessionId;
        var openResult = await _captureMediaCoordinator.OpenClipPreviewAsync(clip, _lastOutputPath, sessionId);
        _lastRuntimeMessage = openResult.Message;
        OnPropertyChanged(nameof(LastRuntimeMessage));
    }

    private static void AssignThumbnailPath(CurrentSessionClipItem clip, string thumbnailPath)
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => clip.ThumbnailPath = thumbnailPath);
    }
}
