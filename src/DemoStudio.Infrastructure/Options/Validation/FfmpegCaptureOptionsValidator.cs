namespace DemoStudio.Infrastructure.Options.Validation;

using DemoStudio.Infrastructure.Options;
using Microsoft.Extensions.Options;

public sealed class FfmpegCaptureOptionsValidator : IValidateOptions<FfmpegCaptureOptions>
{
    public ValidateOptionsResult Validate(string? name, FfmpegCaptureOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        if (string.IsNullOrWhiteSpace(options.FfmpegPath))
        {
            return ValidateOptionsResult.Fail("Capture:Ffmpeg:FfmpegPath is required when FFmpeg capture is enabled.");
        }

        if (options.FrameRate <= 0)
        {
            return ValidateOptionsResult.Fail("Capture:Ffmpeg:FrameRate must be > 0.");
        }

        if (string.IsNullOrWhiteSpace(options.VideoCodec))
        {
            return ValidateOptionsResult.Fail("Capture:Ffmpeg:VideoCodec is required.");
        }

        if (string.IsNullOrWhiteSpace(options.Preset))
        {
            return ValidateOptionsResult.Fail("Capture:Ffmpeg:Preset is required.");
        }

        if (options.Crf is < 0 or > 51)
        {
            return ValidateOptionsResult.Fail("Capture:Ffmpeg:Crf must be between 0 and 51.");
        }

        if (options.MaxDurationSeconds <= 0)
        {
            return ValidateOptionsResult.Fail("Capture:Ffmpeg:MaxDurationSeconds must be > 0.");
        }

        if (options.CropPaddingPixels < 0)
        {
            return ValidateOptionsResult.Fail("Capture:Ffmpeg:CropPaddingPixels must be >= 0.");
        }

        if (string.IsNullOrWhiteSpace(options.OutputFileExtension) || !options.OutputFileExtension.StartsWith('.'))
        {
            return ValidateOptionsResult.Fail("Capture:Ffmpeg:OutputFileExtension must start with '.'.");
        }

        if (!options.CaptureMode.Equals("Desktop", StringComparison.OrdinalIgnoreCase)
            && !options.CaptureMode.Equals("Window", StringComparison.OrdinalIgnoreCase))
        {
            return ValidateOptionsResult.Fail("Capture:Ffmpeg:CaptureMode must be either 'Desktop' or 'Window'.");
        }

        if (options.CaptureMode.Equals("Window", StringComparison.OrdinalIgnoreCase))
        {
            var hasHandle = !string.IsNullOrWhiteSpace(options.WindowHandleHex);

            if (hasHandle)
            {
                var value = options.WindowHandleHex!.Trim();
                if (!System.Text.RegularExpressions.Regex.IsMatch(value, "^(0x)?[0-9a-fA-F]+$"))
                {
                    return ValidateOptionsResult.Fail("Capture:Ffmpeg:WindowHandleHex must be hex format like 0x001A03F2 or 001A03F2.");
                }
            }
        }

        return ValidateOptionsResult.Success;
    }
}
