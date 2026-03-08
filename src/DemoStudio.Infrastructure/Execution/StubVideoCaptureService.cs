namespace DemoStudio.Infrastructure.Execution;

using System.Text;
using DemoStudio.Capture.Abstractions.Interfaces;

public sealed class StubVideoCaptureService : IVideoCaptureService
{
    public async Task<CaptureSessionStartResult> StartAsync(CaptureStartRequest request, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(request.OutputDirectory);
        await File.WriteAllTextAsync(
            request.RawVideoPath,
            $"STUB_CAPTURE_START|Run={request.Run.Id}|Utc={DateTimeOffset.UtcNow:u}{Environment.NewLine}",
            Encoding.UTF8,
            cancellationToken);

        return new CaptureSessionStartResult(true, request.RawVideoPath, null);
    }

    public async Task<CaptureSessionStopResult> StopAsync(CaptureStopRequest request, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(request.RawVideoPath))
        {
            return new CaptureSessionStopResult(false, $"Raw video file '{request.RawVideoPath}' was not found.");
        }

        await File.AppendAllTextAsync(
            request.RawVideoPath,
            $"STUB_CAPTURE_STOP|Run={request.Run.Id}|Utc={DateTimeOffset.UtcNow:u}{Environment.NewLine}",
            Encoding.UTF8,
            cancellationToken);

        return new CaptureSessionStopResult(true, null);
    }
}
