using DemoStudio.Desktop.Core.Sessions;
using DemoStudio.Desktop.Core.Time;

namespace DemoStudio.Desktop.Core.Tests;

public sealed class RecorderSessionEngineTests
{
    [Fact]
    public void StartPauseStop_ProducesSingleClipAndCompletedState()
    {
        var clock = new FakeClock(DateTimeOffset.Parse("2026-03-06T08:00:00Z"));
        var sut = new RecorderSessionEngine(clock);

        sut.StartOrResumeClip();
        clock.Advance(TimeSpan.FromSeconds(5));
        sut.PauseClip();
        var result = sut.StopCompleted();

        Assert.Equal(RecorderSessionState.Completed, result.State);
        Assert.Equal(1, result.ClipCount);
        Assert.Equal(TimeSpan.FromSeconds(5), result.CapturedDuration);
    }

    [Fact]
    public void Pause_WithoutRecording_Throws()
    {
        var sut = new RecorderSessionEngine(new FakeClock(DateTimeOffset.Parse("2026-03-06T08:00:00Z")));

        Assert.Throws<InvalidOperationException>(() => sut.PauseClip());
    }

    [Fact]
    public void StopFailed_FromRecording_ClosesActiveClipAndStoresReason()
    {
        var clock = new FakeClock(DateTimeOffset.Parse("2026-03-06T08:00:00Z"));
        var sut = new RecorderSessionEngine(clock);

        sut.StartOrResumeClip();
        clock.Advance(TimeSpan.FromSeconds(3));

        var result = sut.StopFailed("capture device unavailable");

        Assert.Equal(RecorderSessionState.Failed, result.State);
        Assert.Equal("capture device unavailable", result.FailureReason);
        Assert.Single(result.Clips);
        Assert.Equal(TimeSpan.FromSeconds(3), result.CapturedDuration);
    }

    private sealed class FakeClock : IClock
    {
        public FakeClock(DateTimeOffset now)
        {
            UtcNow = now;
        }

        public DateTimeOffset UtcNow { get; private set; }

        public void Advance(TimeSpan by)
        {
            UtcNow = UtcNow.Add(by);
        }
    }
}
