namespace DemoStudio.Automation.FlaUI.Models;

internal sealed record FlaUIRunnerRequest(
    int SchemaVersion,
    string RunId,
    string ApplicationPath,
    string? WorkingDirectory,
    string? LaunchArguments,
    int WindowFindTimeoutMs,
    IReadOnlyCollection<FlaUIRunnerStep> Steps);

internal sealed record FlaUIRunnerStep(
    string Type,
    string? TitleContains,
    string? AutomationId,
    string? Value,
    int? Ms);

internal sealed record FlaUIRunnerResponse(
    int? SchemaVersion,
    bool Succeeded,
    string? DiagnosticMessage,
    IReadOnlyCollection<string> LogLines,
    FlaUIInspectionElement? InspectionElement = null);

internal sealed record FlaUIInspectionElement(
    string? AutomationId,
    string? Name,
    string? ControlType,
    string BoundingRectangle,
    string? WindowTitle);
