namespace DemoStudio.Infrastructure.Options.Validation;

using DemoStudio.Infrastructure.Options;
using Microsoft.Extensions.Options;

public sealed class CaptureOptionsValidator : IValidateOptions<CaptureOptions>
{
    public ValidateOptionsResult Validate(string? name, CaptureOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Provider))
        {
            return ValidateOptionsResult.Fail("Capture:Provider is required.");
        }

        if (!options.Provider.Equals("Stub", StringComparison.OrdinalIgnoreCase)
            && !options.Provider.Equals("Ffmpeg", StringComparison.OrdinalIgnoreCase))
        {
            return ValidateOptionsResult.Fail("Capture:Provider must be either 'Stub' or 'Ffmpeg'.");
        }

        if (options.Provider.Equals("Ffmpeg", StringComparison.OrdinalIgnoreCase) && !options.Ffmpeg.Enabled)
        {
            return ValidateOptionsResult.Fail("Capture:Ffmpeg:Enabled must be true when Capture:Provider is 'Ffmpeg'.");
        }

        return ValidateOptionsResult.Success;
    }
}
