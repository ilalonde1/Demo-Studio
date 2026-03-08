namespace DemoStudio.Infrastructure.Options;

public sealed class FfmpegCaptureOptions
{
    public bool Enabled { get; set; } = false;

    public string FfmpegPath { get; set; } = "ffmpeg";

    public int FrameRate { get; set; } = 30;

    public string VideoCodec { get; set; } = "libx264";

    public string Preset { get; set; } = "veryfast";

    public int Crf { get; set; } = 23;

    public int MaxDurationSeconds { get; set; } = 180;

    public string OutputFileExtension { get; set; } = ".mp4";

    public string CaptureMode { get; set; } = "Desktop";

    public string? WindowTitleContains { get; set; }

    public string? WindowTitleRegex { get; set; }

    public string? WindowProcessName { get; set; }

    public string? WindowHandleHex { get; set; }

    public bool PreferExactHandle { get; set; } = false;

    public bool FallbackToDesktop { get; set; } = false;

    public bool CropEnabled { get; set; } = false;

    public int CropPaddingPixels { get; set; } = 8;

    public bool HighlightCursor { get; set; } = false;

    public bool CaptureMicrophone { get; set; } = false;

    public string? MicrophoneDeviceName { get; set; }
}
