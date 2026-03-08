namespace DemoStudio.Infrastructure.Options.Validation;

using Microsoft.Extensions.Options;

public sealed class DemoExecutionOptionsValidator : IValidateOptions<DemoExecutionOptions>
{
    public ValidateOptionsResult Validate(string? name, DemoExecutionOptions options)
    {
        if (options.PollingIntervalSeconds < 1)
        {
            return ValidateOptionsResult.Fail("DemoExecution:PollingIntervalSeconds must be >= 1.");
        }

        if (string.IsNullOrWhiteSpace(options.OutputRoot))
        {
            return ValidateOptionsResult.Fail("DemoExecution:OutputRoot is required.");
        }

        if (options.MaxRunDurationMinutes < 1 || options.MaxRunDurationMinutes > 240)
        {
            return ValidateOptionsResult.Fail("DemoExecution:MaxRunDurationMinutes must be between 1 and 240.");
        }

        return ValidateOptionsResult.Success;
    }
}
