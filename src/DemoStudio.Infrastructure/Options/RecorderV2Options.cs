namespace DemoStudio.Infrastructure.Options;

public sealed class RecorderV2Options
{
    public const string SectionName = "RecorderV2";

    public bool Enabled { get; init; } = false;

    public int ForegroundPollIntervalMs { get; init; } = 500;

    public int HeartbeatIntervalSeconds { get; init; } = 10;
}
