namespace DemoStudio.Application.Services;

using DemoStudio.Application.Abstractions.Persistence;

public interface IDemoRunPipeline
{
    Task<DemoRunPipelineResult> ExecuteAsync(DemoRunExecutionContext context, CancellationToken cancellationToken = default);
}

public sealed record DemoRunPipelineResult(
    bool Succeeded,
    string OutputDirectory,
    string? RawVideoPath,
    string? RedactedVideoPath,
    string? LogPath,
    string? FailureReason);
