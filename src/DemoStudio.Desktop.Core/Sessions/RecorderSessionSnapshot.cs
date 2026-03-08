namespace DemoStudio.Desktop.Core.Sessions;
public sealed record RecorderSessionSnapshot(
    Guid SessionId,
    RecorderSessionState State,
    DateTimeOffset StartedUtc,
    DateTimeOffset? ActiveClipStartedUtc,
    IReadOnlyList<RecorderClip> Clips,
    string? FailureReason)
{
    public int ClipCount => Clips.Count;
    public TimeSpan CapturedDuration => Clips.Aggregate(TimeSpan.Zero, static (sum, clip) => sum + clip.Duration);
}
