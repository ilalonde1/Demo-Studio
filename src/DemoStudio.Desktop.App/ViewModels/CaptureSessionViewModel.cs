using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using DemoStudio.Desktop.App.Services;
using DemoStudio.Desktop.Core.Sessions;

namespace DemoStudio.Desktop.App.ViewModels;

public sealed class CaptureSessionViewModel : INotifyPropertyChanged
{
    private readonly RecorderSessionEngine _sessionEngine;
    private readonly DesktopCaptureRuntime _captureRuntime;
    private readonly DesktopWindowFocusService _windowFocusService;
    private readonly DesktopPresenterViewService _presenterViewService;
    private readonly DesktopClipCurationCoordinator _clipCurationCoordinator;
    private bool _isStartClipInFlight;
    private bool _isStartCancellationRequested;
    private CancellationTokenSource? _startClipCancellation;

    public CaptureSessionViewModel(
        RecorderSessionEngine sessionEngine,
        DesktopCaptureRuntime captureRuntime,
        DesktopWindowFocusService windowFocusService,
        DesktopPresenterViewService presenterViewService,
        DesktopClipCurationCoordinator clipCurationCoordinator)
    {
        _sessionEngine = sessionEngine ?? throw new ArgumentNullException(nameof(sessionEngine));
        _captureRuntime = captureRuntime ?? throw new ArgumentNullException(nameof(captureRuntime));
        _windowFocusService = windowFocusService ?? throw new ArgumentNullException(nameof(windowFocusService));
        _presenterViewService = presenterViewService ?? throw new ArgumentNullException(nameof(presenterViewService));
        _clipCurationCoordinator = clipCurationCoordinator ?? throw new ArgumentNullException(nameof(clipCurationCoordinator));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsStartClipInFlight => _isStartClipInFlight;

    public bool IsStartCancellationRequested => _isStartCancellationRequested;

    public CancellationTokenSource BeginStartFlow(CancellationToken lifecycleCancellationToken)
    {
        _isStartCancellationRequested = false;
        _startClipCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifecycleCancellationToken);
        _isStartClipInFlight = true;
        OnPropertyChanged(nameof(IsStartClipInFlight));
        OnPropertyChanged(nameof(IsStartCancellationRequested));
        return _startClipCancellation;
    }

    public void CancelPendingStart()
    {
        _isStartCancellationRequested = true;
        _startClipCancellation?.Cancel();
        OnPropertyChanged(nameof(IsStartCancellationRequested));
    }

    public void CompleteStartFlow()
    {
        _isStartClipInFlight = false;
        _isStartCancellationRequested = false;
        _startClipCancellation = null;
        OnPropertyChanged(nameof(IsStartClipInFlight));
        OnPropertyChanged(nameof(IsStartCancellationRequested));
    }

    public RecorderSessionSnapshot Snapshot() => _sessionEngine.Snapshot();

    public RecorderSessionSnapshot StartOrResumeClip() => _sessionEngine.StartOrResumeClip();

    public RecorderSessionSnapshot PauseClip() => _sessionEngine.PauseClip();

    public RecorderSessionSnapshot StopCompleted() => _sessionEngine.StopCompleted();

    public RecorderSessionSnapshot StopFailed(string reason) => _sessionEngine.StopFailed(reason);

    public RecorderSessionSnapshot Reset() => _sessionEngine.Reset();

    public Task<CaptureRuntimeResult> EnsureCaptureStartedAsync(CaptureTargetSettings targetSettings, CancellationToken cancellationToken)
        => _captureRuntime.EnsureStartedAsync(targetSettings, cancellationToken);

    public Task<CaptureRuntimeResult> StopCaptureAsync(CancellationToken cancellationToken = default)
        => _captureRuntime.StopAsync(cancellationToken);

    public async Task<bool> TryActivateTargetAsync(
        bool isWindowMode,
        bool presenterViewEnabled,
        CaptureTargetSettings targetSettings,
        Action<string> setLastRuntimeMessage,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(targetSettings.Mode, "Window", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (isWindowMode && presenterViewEnabled && _presenterViewService.TryMoveTargetToSecondary(targetSettings.WindowHandleHex))
        {
            setLastRuntimeMessage("Presenter View: moved target to secondary monitor.");
            await Task.Delay(200, cancellationToken);
        }

        var focus = await _windowFocusService.TryActivateAsync(targetSettings);
        setLastRuntimeMessage(focus.Message);
        if (!focus.Succeeded)
        {
            return false;
        }

        await Task.Delay(250, cancellationToken);
        return true;
    }

    public async Task<bool> RunStartCountdownAsync(int seconds, CancellationToken cancellationToken, Action<string> setLastRuntimeMessage)
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

            setLastRuntimeMessage($"Recording starts in {remaining}...");
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

    public string NormalizeClipLabel(int sequence, string? nextClipLabel)
    {
        var candidate = nextClipLabel?.Trim();
        return string.IsNullOrWhiteSpace(candidate) ? $"Clip {sequence}" : candidate;
    }

    public void BeginLiveClipTracking(
        RecorderSessionSnapshot snapshot,
        ClipCurationViewModel curation,
        ObservableCollection<CurrentSessionClipItem> currentSessionClips,
        bool captureNarration,
        DispatcherTimer liveClipTimer,
        Action notifyCurrentSessionClipsChanged)
    {
        if (snapshot.State != RecorderSessionState.Recording || snapshot.Clips.Count == 0)
        {
            return;
        }

        var activeClip = snapshot.Clips[^1];
        var label = string.IsNullOrWhiteSpace(curation.ActiveClipLabel) ? $"Clip {activeClip.Sequence}" : curation.ActiveClipLabel!;
        var existing = currentSessionClips.FirstOrDefault(x => x.Sequence == activeClip.Sequence);
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
                Order = currentSessionClips.Count + 1,
                IncludeNarration = captureNarration
            };
            currentSessionClips.Add(existing);
        }
        else if (string.IsNullOrWhiteSpace(existing.Label))
        {
            existing.Label = label;
        }

        curation.ActiveClipItem = existing;
        curation.ActiveClipStartedUtc = activeClip.StartedUtc;
        if (!liveClipTimer.IsEnabled)
        {
            liveClipTimer.Start();
        }

        notifyCurrentSessionClipsChanged();
    }

    public void UpdateLiveClipPreview(RecorderSessionSnapshot snapshot, ClipCurationViewModel curation)
    {
        if (snapshot.State != RecorderSessionState.Recording || curation.ActiveClipItem is null)
        {
            return;
        }

        var elapsed = DateTimeOffset.UtcNow - curation.ActiveClipStartedUtc;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        curation.ActiveClipItem.DurationSeconds = elapsed.TotalSeconds;
        curation.ActiveClipItem.DurationDisplay = elapsed.ToString(@"mm\:ss");
        curation.ActiveClipItem.TimelineWidth = Math.Clamp(120d + (curation.ActiveClipItem.DurationSeconds * 8d), 120d, 420d);
    }

    public void EndLiveClipTracking(ClipCurationViewModel curation, DispatcherTimer liveClipTimer)
    {
        curation.ActiveClipItem = null;
        if (liveClipTimer.IsEnabled)
        {
            liveClipTimer.Stop();
        }
    }

    public void CaptureCompletedClipMetadata(
        RecorderSessionSnapshot snapshot,
        ClipCurationViewModel curation,
        ObservableCollection<CurrentSessionClipItem> currentSessionClips,
        bool captureNarration,
        Func<CurrentSessionClipItem, Task> tryGenerateClipThumbnailAsync,
        Func<CurrentSessionClipItem?> getSelectedCurrentSessionClip,
        Action<CurrentSessionClipItem?> setSelectedCurrentSessionClip,
        Action notifyCollectionStateChanged)
    {
        var changed = false;
        foreach (var clip in snapshot.Clips)
        {
            var existing = currentSessionClips.FirstOrDefault(x => x.Sequence == clip.Sequence);
            if (existing is not null)
            {
                existing.StartSeconds = (clip.StartedUtc - snapshot.StartedUtc).TotalSeconds;
                existing.DurationSeconds = clip.Duration.TotalSeconds;
                existing.DurationDisplay = clip.Duration.ToString(@"mm\:ss");
                existing.TimelineWidth = Math.Clamp(120d + (existing.DurationSeconds * 8d), 120d, 420d);
                _ = tryGenerateClipThumbnailAsync(existing);
                if (curation.ActiveClipItem is not null && curation.ActiveClipItem.Sequence == existing.Sequence)
                {
                    curation.ActiveClipLabel = null;
                }

                continue;
            }

            var label = curation.ActiveClipLabel;
            if (string.IsNullOrWhiteSpace(label))
            {
                label = $"Clip {clip.Sequence}";
            }

            currentSessionClips.Add(new CurrentSessionClipItem(
                clip.Sequence,
                label,
                clip.Duration.ToString(@"mm\:ss"),
                string.Empty,
                (clip.StartedUtc - snapshot.StartedUtc).TotalSeconds,
                clip.Duration.TotalSeconds));
            currentSessionClips[^1].IncludeNarration = captureNarration;
            _ = tryGenerateClipThumbnailAsync(currentSessionClips[^1]);
            curation.ActiveClipLabel = null;
            changed = true;
        }

        if (changed)
        {
            ReindexClipOrders(currentSessionClips);
            if (getSelectedCurrentSessionClip() is null && currentSessionClips.Count > 0)
            {
                setSelectedCurrentSessionClip(currentSessionClips[^1]);
            }
        }

        notifyCollectionStateChanged();
    }

    public void ReindexClipOrders(ObservableCollection<CurrentSessionClipItem> currentSessionClips)
    {
        _clipCurationCoordinator.ReindexClipOrders(currentSessionClips);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
