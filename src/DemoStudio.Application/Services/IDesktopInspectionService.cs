namespace DemoStudio.Application.Services;

using DemoStudio.Domain.Entities;

public interface IDesktopInspectionService
{
    Task<ElementInspectionResult> InspectAsync(ApplicationTarget target, CancellationToken cancellationToken = default);
}

public sealed record ElementInspectionResult(
    bool Succeeded,
    string? DiagnosticMessage,
    InspectedElementMetadata? Element,
    IReadOnlyCollection<string> LogLines);

public sealed record InspectedElementMetadata(
    string? AutomationId,
    string? Name,
    string? ControlType,
    string BoundingRectangle,
    string? WindowTitle);
