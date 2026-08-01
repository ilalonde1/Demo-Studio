using System.IO;
using System.Text.RegularExpressions;
using DemoStudio.Infrastructure.Options;

namespace DemoStudio.Desktop.App.Services;

internal static class DesktopRecorderOptionsNormalizer
{
    public static void Normalize(DesktopRecorderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.Capture ??= BuildDefaultCaptureOptions();
        options.StorageRoot = string.IsNullOrWhiteSpace(options.StorageRoot)
            ? BuildDefaultStorageRoot()
            : options.StorageRoot.Trim();

        NormalizeCapture(options.Capture);
    }

    public static void NormalizeCapture(FfmpegCaptureOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.FfmpegPath = string.IsNullOrWhiteSpace(options.FfmpegPath)
            ? "ffmpeg"
            : options.FfmpegPath.Trim();
        options.VideoCodec = string.IsNullOrWhiteSpace(options.VideoCodec)
            ? "libx264"
            : options.VideoCodec.Trim();
        options.Preset = string.IsNullOrWhiteSpace(options.Preset) ? "veryfast" : options.Preset.Trim().ToLowerInvariant();
        options.OutputFileExtension = string.IsNullOrWhiteSpace(options.OutputFileExtension) ? ".mp4" : options.OutputFileExtension.Trim();
        options.CaptureMode = string.IsNullOrWhiteSpace(options.CaptureMode) ? "Window" : options.CaptureMode.Trim();
        options.WindowTitleContains = NormalizeOptional(options.WindowTitleContains);
        options.WindowTitleRegex = NormalizeOptionalRegex(options.WindowTitleRegex);
        options.WindowProcessName = NormalizeOptional(options.WindowProcessName);
        options.WindowHandleHex = NormalizeOptional(options.WindowHandleHex);
        options.MicrophoneDeviceName = NormalizeOptional(options.MicrophoneDeviceName);
    }

    public static string ResolveStorageRoot(string? configuredStorageRoot)
    {
        var candidate = string.IsNullOrWhiteSpace(configuredStorageRoot)
            ? BuildDefaultStorageRoot()
            : configuredStorageRoot.Trim();

        try
        {
            var resolved = Path.GetFullPath(candidate);
            Directory.CreateDirectory(resolved);
            return resolved;
        }
        catch
        {
            var fallback = Path.Combine(Path.GetTempPath(), "DemoStudio", "RecorderDesktop");
            Directory.CreateDirectory(fallback);
            return fallback;
        }
    }

    private static FfmpegCaptureOptions BuildDefaultCaptureOptions()
    {
        return new FfmpegCaptureOptions
        {
            Enabled = true,
            FfmpegPath = "ffmpeg",
            FrameRate = 30,
            VideoCodec = "libx264",
            Preset = "veryfast",
            Crf = 23,
            MaxDurationSeconds = 1800,
            OutputFileExtension = ".mp4",
            CaptureMode = "Window",
            FallbackToDesktop = false,
            CropEnabled = true,
            CropPaddingPixels = 48,
            HighlightCursor = false
        };
    }

    private static string BuildDefaultStorageRoot()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "DemoStudio", "RecorderDesktop");
    }

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string? NormalizeOptionalRegex(string? value)
    {
        var normalized = NormalizeOptional(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        try
        {
            _ = new Regex(normalized, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            return normalized;
        }
        catch
        {
            return null;
        }
    }
}
