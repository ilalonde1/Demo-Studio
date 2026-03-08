namespace DemoStudio.Application.Abstractions.System;

using DemoStudio.Domain.Enums;

public interface IDemoExportService
{
    Task<DemoExportResult> ExportAsync(DemoExportRequest request, CancellationToken cancellationToken = default);
}

public sealed record DemoExportRequest(
    Guid RunId,
    string ProjectName,
    ApplicationType ApplicationType,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    IReadOnlyCollection<string> StepsExecuted,
    string OutputDirectory,
    string RawVideoPath,
    string? RedactedVideoPath,
    string TimelinePath);

public sealed record DemoExportResult(
    bool Succeeded,
    string ExportDirectory,
    string? ManifestPath,
    string? SummaryPath,
    IReadOnlyCollection<string> Diagnostics);

