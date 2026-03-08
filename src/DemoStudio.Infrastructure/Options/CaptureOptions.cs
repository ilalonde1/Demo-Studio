namespace DemoStudio.Infrastructure.Options;

public sealed class CaptureOptions
{
    public const string SectionName = "Capture";

    public string Provider { get; set; } = "Stub";

    public FfmpegCaptureOptions Ffmpeg { get; set; } = new();
}
