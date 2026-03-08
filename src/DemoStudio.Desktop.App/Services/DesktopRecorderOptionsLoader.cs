using System.Text.Json;
using DemoStudio.Infrastructure.Options;
using System.IO;
using System.Text.RegularExpressions;

namespace DemoStudio.Desktop.App.Services;

public static class DesktopRecorderOptionsLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static DesktopRecorderOptions Load(string baseDirectory)
    {
        var options = new DesktopRecorderOptions
        {
            Capture = BuildDefaultCaptureOptions()
        };

        var settingsPath = Path.Combine(baseDirectory, "appsettings.json");
        if (!File.Exists(settingsPath))
        {
            options.StorageRoot = BuildDefaultStorageRoot();
            return options;
        }

        try
        {
            var raw = File.ReadAllText(settingsPath);
            using var doc = JsonDocument.Parse(raw);

            if (doc.RootElement.TryGetProperty("DesktopRecorder", out var desktopRecorder))
            {
                if (desktopRecorder.TryGetProperty("StorageRoot", out var storageRootElement))
                {
                    options.StorageRoot = storageRootElement.GetString();
                }

                if (desktopRecorder.TryGetProperty("Capture", out var captureElement))
                {
                    var parsed = JsonSerializer.Deserialize<FfmpegCaptureOptions>(captureElement.GetRawText(), JsonOptions);
                    if (parsed is not null)
                    {
                        options.Capture = parsed;
                    }
                }
            }
        }
        catch
        {
            // Falls back to defaults for reliability.
        }

        if (string.IsNullOrWhiteSpace(options.StorageRoot))
        {
            options.StorageRoot = BuildDefaultStorageRoot();
        }

        Normalize(options);
        return options;
    }

    private static void Normalize(DesktopRecorderOptions options)
    {
        if (options.Capture is null)
        {
            options.Capture = BuildDefaultCaptureOptions();
            return;
        }

        options.StorageRoot = string.IsNullOrWhiteSpace(options.StorageRoot)
            ? BuildDefaultStorageRoot()
            : options.StorageRoot.Trim();

        options.Capture.FfmpegPath = string.IsNullOrWhiteSpace(options.Capture.FfmpegPath)
            ? "ffmpeg"
            : options.Capture.FfmpegPath.Trim();
        options.Capture.FrameRate = Clamp(options.Capture.FrameRate, 10, 60, 30);
        options.Capture.Crf = Clamp(options.Capture.Crf, 0, 51, 23);
        options.Capture.MaxDurationSeconds = Clamp(options.Capture.MaxDurationSeconds, 30, 7200, 1800);
        options.Capture.CropPaddingPixels = Clamp(options.Capture.CropPaddingPixels, 0, 200, 8);

        options.Capture.VideoCodec = string.IsNullOrWhiteSpace(options.Capture.VideoCodec)
            ? "libx264"
            : options.Capture.VideoCodec.Trim();
        options.Capture.Preset = NormalizePreset(options.Capture.Preset);
        options.Capture.OutputFileExtension = NormalizeOutputExtension(options.Capture.OutputFileExtension);
        options.Capture.CaptureMode = NormalizeCaptureMode(options.Capture.CaptureMode);
        options.Capture.WindowTitleContains = NormalizeOptional(options.Capture.WindowTitleContains);
        options.Capture.WindowTitleRegex = NormalizeOptionalRegex(options.Capture.WindowTitleRegex);
        options.Capture.WindowProcessName = NormalizeOptional(options.Capture.WindowProcessName);
        options.Capture.WindowHandleHex = NormalizeOptional(options.Capture.WindowHandleHex);
        options.Capture.MicrophoneDeviceName = NormalizeOptional(options.Capture.MicrophoneDeviceName);
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
            CropEnabled = false,
            CropPaddingPixels = 8,
            HighlightCursor = false
        };
    }

    private static string BuildDefaultStorageRoot()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "DemoStudio", "RecorderDesktop");
    }

    private static int Clamp(int value, int min, int max, int fallback)
    {
        if (value < min || value > max)
        {
            return fallback;
        }

        return value;
    }

    private static string NormalizePreset(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "veryfast";
        }

        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "ultrafast" or "superfast" or "veryfast" or "faster" or "fast" or "medium" or "slow" or "slower" or "veryslow" => normalized,
            _ => "veryfast"
        };
    }

    private static string NormalizeOutputExtension(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ".mp4";
        }

        var trimmed = value.Trim();
        if (!trimmed.StartsWith(".", StringComparison.Ordinal))
        {
            trimmed = "." + trimmed;
        }

        trimmed = trimmed.ToLowerInvariant();
        return trimmed is ".mp4" or ".mkv" or ".mov" ? trimmed : ".mp4";
    }

    private static string NormalizeCaptureMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Window";
        }

        var normalized = value.Trim();
        return normalized.Equals("Desktop", StringComparison.OrdinalIgnoreCase) ? "Desktop" : "Window";
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
