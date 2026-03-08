namespace DemoStudio.Infrastructure.Options;

public sealed class AutomationOptions
{
    public const string SectionName = "Automation";

    public string DesktopEngine { get; set; } = "Stub";
}
