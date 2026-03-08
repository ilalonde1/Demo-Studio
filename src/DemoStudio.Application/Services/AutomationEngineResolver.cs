namespace DemoStudio.Application.Services;

using DemoStudio.Automation.Abstractions.Interfaces;
using DemoStudio.Domain.Enums;

public sealed class AutomationEngineResolver : IAutomationEngineResolver
{
    private readonly IDesktopAutomationEngine _desktopAutomationEngine;

    public AutomationEngineResolver(IDesktopAutomationEngine desktopAutomationEngine)
    {
        _desktopAutomationEngine = desktopAutomationEngine;
    }

    public IAutomationEngine Resolve(ApplicationType applicationType)
    {
        return applicationType switch
        {
            ApplicationType.Desktop => _desktopAutomationEngine,
            _ => throw new InvalidOperationException($"Unsupported application type '{applicationType}'.")
        };
    }
}
