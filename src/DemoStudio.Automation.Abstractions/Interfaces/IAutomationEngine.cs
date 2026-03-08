namespace DemoStudio.Automation.Abstractions.Interfaces;

using DemoStudio.Domain.Entities;
using DemoStudio.Domain.Enums;

public interface IAutomationEngine
{
    Task<AutomationExecutionResult> ExecuteAsync(AutomationExecutionRequest request, CancellationToken cancellationToken = default);
}

public interface IDesktopAutomationEngine : IAutomationEngine
{
}

public sealed record AutomationExecutionRequest(
    DemoRun Run,
    ApplicationTarget Target,
    DemoFlow Flow,
    IReadOnlyCollection<FlowStep> Steps,
    ExecutionMode Mode = ExecutionMode.FullRun);

public sealed record AutomationExecutionResult(
    bool Succeeded,
    IReadOnlyCollection<string> LogLines,
    string? DiagnosticMessage);
