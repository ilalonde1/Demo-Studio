namespace DemoStudio.Application.Abstractions.System;

public interface ITimelineMarkerWriter
{
    Task WriteStageMarkerAsync(string outputDirectory, string stage, DateTimeOffset timestampUtc, CancellationToken cancellationToken = default);
}

