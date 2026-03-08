using DemoStudio.Infrastructure.Options;

namespace DemoStudio.Desktop.App.Services;

public sealed class DesktopRecorderOptions
{
    public string? StorageRoot { get; set; }

    public FfmpegCaptureOptions Capture { get; set; } = new();
}
