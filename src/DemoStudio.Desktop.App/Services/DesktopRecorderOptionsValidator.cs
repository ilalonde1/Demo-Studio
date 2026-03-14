using DemoStudio.Infrastructure.Options.Validation;
using Microsoft.Extensions.Options;

namespace DemoStudio.Desktop.App.Services;

internal sealed class DesktopRecorderOptionsValidator : IValidateOptions<DesktopRecorderOptions>
{
    private readonly FfmpegCaptureOptionsValidator _captureValidator = new();

    public ValidateOptionsResult Validate(string? name, DesktopRecorderOptions options)
    {
        if (options is null)
        {
            return ValidateOptionsResult.Fail("DesktopRecorder options are required.");
        }

        if (string.IsNullOrWhiteSpace(options.StorageRoot))
        {
            return ValidateOptionsResult.Fail("DesktopRecorder:StorageRoot is required.");
        }

        if (options.Capture is null)
        {
            return ValidateOptionsResult.Fail("DesktopRecorder:Capture is required.");
        }

        var captureValidation = _captureValidator.Validate(name, options.Capture);
        if (captureValidation.Failed)
        {
            return captureValidation;
        }

        return ValidateOptionsResult.Success;
    }
}
