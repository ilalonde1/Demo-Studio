using DemoStudio.Desktop.Core.Time;
namespace DemoStudio.Desktop.Core.Sessions;
public sealed class RecorderSessionEngine
{
    private readonly IClock _clock;
    private readonly object _sync = new();
    private readonly List<RecorderClip> _clips = new();
    private Guid _sessionId = Guid.NewGuid();
    private RecorderSessionState _state = RecorderSessionState.Armed;
    private DateTimeOffset _startedUtc;
    private DateTimeOffset? _activeClipStartedUtc;
    private string? _failureReason;
    public RecorderSessionEngine(IClock clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _startedUtc = _clock.UtcNow;
    }
    public RecorderSessionSnapshot Snapshot()
    {
        lock (_sync)
        {
            return new RecorderSessionSnapshot(
                _sessionId,
                _state,
                _startedUtc,
                _activeClipStartedUtc,
                _clips.ToArray(),
                _failureReason);
        }
    }
    public RecorderSessionSnapshot StartOrResumeClip()
    {
        lock (_sync)
        {
            EnsureStateIsOneOf(RecorderSessionState.Armed, RecorderSessionState.Paused);
            _activeClipStartedUtc = _clock.UtcNow;
            _state = RecorderSessionState.Recording;
            return SnapshotUnsafe();
        }
    }
    public RecorderSessionSnapshot PauseClip()
    {
        lock (_sync)
        {
            EnsureStateIsOneOf(RecorderSessionState.Recording);
            CloseActiveClip();
            _state = RecorderSessionState.Paused;
            return SnapshotUnsafe();
        }
    }
    public RecorderSessionSnapshot StopCompleted()
    {
        lock (_sync)
        {
            EnsureStateIsOneOf(RecorderSessionState.Armed, RecorderSessionState.Recording, RecorderSessionState.Paused);
            if (_state == RecorderSessionState.Recording)
            {
                CloseActiveClip();
            }
            _state = RecorderSessionState.Completed;
            return SnapshotUnsafe();
        }
    }
    public RecorderSessionSnapshot StopFailed(string reason)
    {
        lock (_sync)
        {
            if (_state == RecorderSessionState.Completed || _state == RecorderSessionState.Failed)
            {
                return SnapshotUnsafe();
            }
            if (_state == RecorderSessionState.Recording)
            {
                CloseActiveClip();
            }
            _failureReason = string.IsNullOrWhiteSpace(reason) ? "Unknown failure." : reason.Trim();
            _state = RecorderSessionState.Failed;
            return SnapshotUnsafe();
        }
    }

    public RecorderSessionSnapshot Reset()
    {
        lock (_sync)
        {
            if (_state == RecorderSessionState.Recording)
            {
                throw new InvalidOperationException("Cannot reset while recording. Pause or stop first.");
            }

            _sessionId = Guid.NewGuid();
            _clips.Clear();
            _failureReason = null;
            _activeClipStartedUtc = null;
            _startedUtc = _clock.UtcNow;
            _state = RecorderSessionState.Armed;
            return SnapshotUnsafe();
        }
    }
    private void CloseActiveClip()
    {
        if (_activeClipStartedUtc is null)
        {
            throw new InvalidOperationException("No active clip exists.");
        }
        var clip = new RecorderClip(
            Sequence: _clips.Count + 1,
            StartedUtc: _activeClipStartedUtc.Value,
            EndedUtc: _clock.UtcNow);
        _clips.Add(clip);
        _activeClipStartedUtc = null;
    }
    private void EnsureStateIsOneOf(params RecorderSessionState[] allowedStates)
    {
        if (!allowedStates.Contains(_state))
        {
            var allowed = string.Join(", ", allowedStates.Select(static state => state.ToString()));
            throw new InvalidOperationException($"Session state '{_state}' does not allow this operation. Allowed: {allowed}.");
        }
    }
    private RecorderSessionSnapshot SnapshotUnsafe()
    {
        return new RecorderSessionSnapshot(
            _sessionId,
            _state,
            _startedUtc,
            _activeClipStartedUtc,
            _clips.ToArray(),
            _failureReason);
    }
}
