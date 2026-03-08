namespace DemoStudio.Infrastructure.Execution;

using System.Text.Json;
using DemoStudio.Automation.Abstractions.Interfaces;
using DemoStudio.Domain.Enums;

public sealed class StubDesktopAutomationEngine : IDesktopAutomationEngine
{
    public async Task<AutomationExecutionResult> ExecuteAsync(AutomationExecutionRequest request, CancellationToken cancellationToken = default)
    {
        var lines = new List<string>();
        try
        {
            foreach (var step in request.Steps.OrderBy(x => x.Sequence))
            {
                var delayMs = 50 + ((step.Sequence * 37) % 101);
                await Task.Delay(delayMs, cancellationToken);

                var payloadForLog = SanitizePayloadForLog(step);
                var message = $"DESKTOP step#{step.Sequence} type={step.StepType} action={step.ActionKey} payload={payloadForLog} timeout={step.TimeoutSeconds}";
                lines.Add(message);

                if (ContainsFailMarker(step))
                {
                    throw new InvalidOperationException($"FAIL_STEP marker detected at flow step sequence {step.Sequence}.");
                }
            }

            return new AutomationExecutionResult(true, lines, null);
        }
        catch (OperationCanceledException)
        {
            return new AutomationExecutionResult(false, lines, "Automation execution was cancelled.");
        }
        catch (Exception ex)
        {
            lines.Add($"DESKTOP automation failed: {ex.Message}");
            return new AutomationExecutionResult(false, lines, ex.Message);
        }
    }

    private static bool ContainsFailMarker(Domain.Entities.FlowStep step)
    {
        return step.ActionKey.Contains("FAIL_STEP", StringComparison.OrdinalIgnoreCase)
            || (step.PayloadJson?.Contains("FAIL_STEP", StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private static string SanitizePayloadForLog(Domain.Entities.FlowStep step)
    {
        if (step.StepType == FlowStepType.InputText)
        {
            return "(masked)";
        }

        if (string.IsNullOrWhiteSpace(step.PayloadJson))
        {
            return "null";
        }

        try
        {
            using var doc = JsonDocument.Parse(step.PayloadJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("value", out _))
            {
                return "(masked)";
            }
        }
        catch
        {
        }

        return step.PayloadJson;
    }
}
