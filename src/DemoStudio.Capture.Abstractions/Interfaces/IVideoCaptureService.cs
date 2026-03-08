namespace DemoStudio.Capture.Abstractions.Interfaces;

using DemoStudio.Domain.Entities;

public interface IVideoCaptureService
{
    Task<CaptureSessionStartResult> StartAsync(CaptureStartRequest request, CancellationToken cancellationToken = default);

    Task<CaptureSessionStopResult> StopAsync(CaptureStopRequest request, CancellationToken cancellationToken = default);
}

public sealed record CaptureStartRequest(DemoRun Run, string OutputDirectory, string RawVideoPath);

public sealed record CaptureSessionStartResult(bool Succeeded, string? RawVideoPath, string? ErrorMessage);

public sealed record CaptureStopRequest(DemoRun Run, string RawVideoPath);

public sealed record CaptureSessionStopResult(bool Succeeded, string? ErrorMessage);
