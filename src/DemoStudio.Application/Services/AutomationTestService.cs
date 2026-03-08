namespace DemoStudio.Application.Services;

using DemoStudio.Application.Abstractions.Persistence;
using DemoStudio.Application.Abstractions.System;
using DemoStudio.Automation.Abstractions.Interfaces;
using DemoStudio.Domain.Entities;
using DemoStudio.Domain.Enums;

public sealed class AutomationTestService : IAutomationTestService
{
    private readonly IAutomationRuntimeGate _runtimeGate;
    private readonly IAutomationEngineResolver _engineResolver;
    private readonly IDemoProjectRepository _projectRepository;
    private readonly IApplicationProcessInspector _applicationProcessInspector;

    public AutomationTestService(
        IAutomationRuntimeGate runtimeGate,
        IAutomationEngineResolver engineResolver,
        IDemoProjectRepository projectRepository,
        IApplicationProcessInspector applicationProcessInspector)
    {
        _runtimeGate = runtimeGate;
        _engineResolver = engineResolver;
        _projectRepository = projectRepository;
        _applicationProcessInspector = applicationProcessInspector;
    }

    public async Task<StepTestResult> TestStepAsync(DemoFlow flow, FlowStep step, CancellationToken ct = default)
    {
        if (flow is null)
        {
            throw new ArgumentNullException(nameof(flow));
        }

        if (step is null)
        {
            throw new ArgumentNullException(nameof(step));
        }

        if (step.DemoFlowId != flow.Id)
        {
            throw new InvalidOperationException("Step does not belong to the specified flow.");
        }

        if (!_runtimeGate.Enabled)
        {
            return new StepTestResult(false, "Automation runtime disabled.", Array.Empty<string>());
        }

        var target = await _projectRepository.GetPrimaryTargetAsync(flow.DemoProjectId, ct)
            ?? throw new InvalidOperationException($"Project '{flow.DemoProjectId}' has no application target configured.");

        try
        {
            var engine = _engineResolver.Resolve(target.ApplicationType);
            var steps = await BuildStepSubsetAsync(flow, step, target, ct);

            var run = new DemoRun(flow.DemoProjectId, flow.Id, "step-test@demostudio.local");
            var result = await engine.ExecuteAsync(
                new AutomationExecutionRequest(run, target, flow, steps, ExecutionMode.StepTest),
                ct);

            return new StepTestResult(result.Succeeded, result.DiagnosticMessage, result.LogLines);
        }
        catch (OperationCanceledException)
        {
            return new StepTestResult(false, "Step test was cancelled.", Array.Empty<string>());
        }
        catch (Exception ex)
        {
            return new StepTestResult(false, ex.Message, Array.Empty<string>());
        }
    }

    private async Task<IReadOnlyCollection<FlowStep>> BuildStepSubsetAsync(
        DemoFlow flow,
        FlowStep step,
        ApplicationTarget target,
        CancellationToken ct)
    {
        if (target.ApplicationType != ApplicationType.Desktop)
        {
            return new[] { step };
        }

        var isRunning = await _applicationProcessInspector.IsRunningAsync(target.TargetReference, ct);
        var steps = new List<FlowStep>();

        if (!isRunning && !IsLaunchStep(step))
        {
            steps.Add(new FlowStep(flow.Id, 1, FlowStepType.Custom, "Launch", null, 30));
            steps.Add(new FlowStep(flow.Id, 2, FlowStepType.Custom, "WaitForWindow", BuildWaitForWindowPayload(target), 30));
            steps.Add(new FlowStep(flow.Id, 3, step.StepType, step.ActionKey, step.PayloadJson, step.TimeoutSeconds));
            steps.Add(new FlowStep(flow.Id, 4, FlowStepType.Custom, "CloseApplication", null, 15));
            return steps;
        }

        steps.Add(new FlowStep(flow.Id, 1, step.StepType, step.ActionKey, step.PayloadJson, step.TimeoutSeconds));

        if (!isRunning && !IsCloseStep(step))
        {
            steps.Add(new FlowStep(flow.Id, 2, FlowStepType.Custom, "CloseApplication", null, 15));
        }

        return steps;
    }

    private static bool IsLaunchStep(FlowStep step)
    {
        return step.ActionKey.Equals("Launch", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCloseStep(FlowStep step)
    {
        return step.ActionKey.Equals("CloseApplication", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildWaitForWindowPayload(ApplicationTarget target)
    {
        var titleContains = string.IsNullOrWhiteSpace(target.Name)
            ? ExtractReferenceName(target.TargetReference)
            : target.Name;

        return System.Text.Json.JsonSerializer.Serialize(new { titleContains });
    }

    private static string ExtractReferenceName(string value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return "Application";
        }

        var slashIndex = Math.Max(trimmed.LastIndexOf('/'), trimmed.LastIndexOf('\\'));
        var fileName = slashIndex >= 0 && slashIndex < trimmed.Length - 1
            ? trimmed[(slashIndex + 1)..]
            : trimmed;
        var dotIndex = fileName.LastIndexOf('.');
        return dotIndex > 0 ? fileName[..dotIndex] : fileName;
    }
}
