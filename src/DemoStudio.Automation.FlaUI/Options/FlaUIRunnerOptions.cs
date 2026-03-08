namespace DemoStudio.Automation.FlaUI.Options;

public sealed class FlaUIRunnerOptions
{
    public string RunnerExePath { get; set; } = "DemoStudio.Automation.FlaUIRunner.exe";

    public int TimeoutMs { get; set; } = 60000;

    public int WindowFindTimeoutMs { get; set; } = 20000;
}

