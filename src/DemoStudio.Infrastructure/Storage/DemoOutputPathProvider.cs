namespace DemoStudio.Infrastructure.Storage;

using DemoStudio.Application.Abstractions.System;
using DemoStudio.Domain.Entities;
using DemoStudio.Infrastructure.Options;
using Microsoft.Extensions.Options;

public sealed class DemoOutputPathProvider : IDemoOutputPathProvider
{
    private readonly DemoExecutionOptions _options;

    public DemoOutputPathProvider(IOptions<DemoExecutionOptions> options)
    {
        _options = options.Value;
    }

    public Task<DemoRunOutputPaths> CreatePathsAsync(DemoProject project, DemoRun run, CancellationToken cancellationToken = default)
    {
        var baseRoot = string.IsNullOrWhiteSpace(_options.OutputRoot) ? "./App_Data/DemoRuns" : _options.OutputRoot;
        var sanitizedProject = SanitizePathSegment(project.Code.Value);
        var runSegment = $"{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}_{run.Id:N}";

        var outputDirectory = Path.GetFullPath(Path.Combine(baseRoot, sanitizedProject, runSegment));
        Directory.CreateDirectory(outputDirectory);

        var rawVideoPath = Path.Combine(outputDirectory, $"demo-{run.Id:D}.mp4");
        var redactedVideoPath = Path.Combine(outputDirectory, $"demo-{run.Id:D}-redacted.mp4");
        var logPath = Path.Combine(outputDirectory, "run.log");

        return Task.FromResult(new DemoRunOutputPaths(outputDirectory, rawVideoPath, redactedVideoPath, logPath));
    }

    private static string SanitizePathSegment(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var buffer = value.Trim().ToCharArray();
        for (var i = 0; i < buffer.Length; i++)
        {
            if (invalidChars.Contains(buffer[i]))
            {
                buffer[i] = '_';
            }
        }

        var sanitized = new string(buffer).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "UNKNOWN_PROJECT" : sanitized;
    }
}
