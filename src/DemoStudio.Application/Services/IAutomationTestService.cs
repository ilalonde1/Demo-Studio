namespace DemoStudio.Application.Services;

using DemoStudio.Domain.Entities;

public interface IAutomationTestService
{
    Task<StepTestResult> TestStepAsync(
        DemoFlow flow,
        FlowStep step,
        CancellationToken ct = default);
}

public sealed record StepTestResult(
    bool Succeeded,
    string? DiagnosticMessage,
    IReadOnlyCollection<string> LogLines);
