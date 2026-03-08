namespace DemoStudio.Automation.FlaUI.Options;

using Microsoft.Extensions.Options;

public sealed class FlaUIRunnerOptionsValidator : IValidateOptions<FlaUIRunnerOptions>
{
    public ValidateOptionsResult Validate(string? name, FlaUIRunnerOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.RunnerExePath))
        {
            return ValidateOptionsResult.Fail("Automation:FlaUIRunner:RunnerExePath is required.");
        }

        if (options.TimeoutMs < 1000)
        {
            return ValidateOptionsResult.Fail("Automation:FlaUIRunner:TimeoutMs must be >= 1000.");
        }

        if (options.WindowFindTimeoutMs < 1000)
        {
            return ValidateOptionsResult.Fail("Automation:FlaUIRunner:WindowFindTimeoutMs must be >= 1000.");
        }

        return ValidateOptionsResult.Success;
    }
}

