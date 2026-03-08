namespace DemoStudio.Infrastructure.Options.Validation;

using DemoStudio.Infrastructure.Options;
using Microsoft.Extensions.Options;

public sealed class AutomationOptionsValidator : IValidateOptions<AutomationOptions>
{
    public ValidateOptionsResult Validate(string? name, AutomationOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.DesktopEngine))
        {
            return ValidateOptionsResult.Fail("Automation:DesktopEngine is required.");
        }

        if (!options.DesktopEngine.Equals("Stub", StringComparison.OrdinalIgnoreCase)
            && !options.DesktopEngine.Equals("FlaUI", StringComparison.OrdinalIgnoreCase))
        {
            return ValidateOptionsResult.Fail("Automation:DesktopEngine must be either 'Stub' or 'FlaUI'.");
        }

        return ValidateOptionsResult.Success;
    }
}
