namespace DemoStudio.Application.Abstractions.System;

using DemoStudio.Domain.Entities;

public interface IDemoOutputPathProvider
{
    Task<DemoRunOutputPaths> CreatePathsAsync(DemoProject project, DemoRun run, CancellationToken cancellationToken = default);
}

public sealed record DemoRunOutputPaths(
    string OutputDirectory,
    string RawVideoPath,
    string RedactedVideoPath,
    string LogPath);
