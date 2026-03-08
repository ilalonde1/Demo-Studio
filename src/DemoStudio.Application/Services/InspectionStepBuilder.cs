namespace DemoStudio.Application.Services;

using System.Text.Json;
using DemoStudio.Domain.Enums;

public static class InspectionStepBuilder
{
    public static InspectionStepDraft BuildClickStep(InspectedElementMetadata element, int sequence)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        if (sequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), "Sequence must be greater than zero.");
        }

        var actionKey = !string.IsNullOrWhiteSpace(element.AutomationId)
            ? element.AutomationId.Trim()
            : (!string.IsNullOrWhiteSpace(element.Name) ? element.Name.Trim().ToUpperInvariant() : "CLICK_ELEMENT");

        var payload = JsonSerializer.Serialize(new
        {
            automationId = element.AutomationId,
            name = element.Name,
            controlType = element.ControlType,
            windowTitle = element.WindowTitle,
            boundingRectangle = element.BoundingRectangle
        });

        return new InspectionStepDraft(sequence, FlowStepType.Click, actionKey, payload, 10);
    }
}

public sealed record InspectionStepDraft(
    int Sequence,
    FlowStepType StepType,
    string ActionKey,
    string? PayloadJson,
    int TimeoutSeconds);
