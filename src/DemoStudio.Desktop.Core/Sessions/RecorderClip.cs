namespace DemoStudio.Desktop.Core.Sessions;
public sealed record RecorderClip(int Sequence, DateTimeOffset StartedUtc, DateTimeOffset EndedUtc)
{
    public TimeSpan Duration => EndedUtc - StartedUtc;
}
