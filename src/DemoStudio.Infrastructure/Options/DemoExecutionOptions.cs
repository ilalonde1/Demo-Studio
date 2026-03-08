namespace DemoStudio.Infrastructure.Options;

public sealed class DemoExecutionOptions
{
    public const string SectionName = "DemoExecution";

    public bool EnableBackgroundPolling { get; set; } = false;

    public int PollingIntervalSeconds { get; set; } = 10;

    public int MaxRunDurationMinutes { get; set; } = 20;

    public string OutputRoot { get; set; } = "./App_Data/DemoRuns";
}
