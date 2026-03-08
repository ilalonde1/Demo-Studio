namespace DemoStudio.Infrastructure.Execution;

using DemoStudio.Application.Services;
using DemoStudio.Infrastructure.Options;
using Microsoft.Extensions.Options;

public sealed class AutomationRuntimeGate : IAutomationRuntimeGate
{
    private readonly IOptionsMonitor<AutomationRuntimeOptions> _options;

    public AutomationRuntimeGate(IOptionsMonitor<AutomationRuntimeOptions> options)
    {
        _options = options;
    }

    public bool Enabled => _options.CurrentValue.Enabled;
}
