using DemoStudio.Capture.Abstractions.Interfaces;
using DemoStudio.Application.Abstractions.System;
using DemoStudio.Domain.Entities;
using DemoStudio.Infrastructure.Execution;
using DemoStudio.Infrastructure.Execution.Windows;
using DemoStudio.Infrastructure.Options;
using DemoStudio.Infrastructure.Process;
using System.IO;

namespace DemoStudio.Desktop.App.Services;

public sealed class DesktopSmokeCheckService
{
    private readonly DesktopVideoCaptureServiceFactory _captureServiceFactory;
    private readonly string _storageRoot;
    private readonly string _ffmpegPath;

    public DesktopSmokeCheckService(
        string storageRoot,
        string ffmpegPath,
        IProcessLauncher processLauncher,
        DesktopVideoCaptureServiceFactory captureServiceFactory)
    {
        ArgumentNullException.ThrowIfNull(processLauncher);
        _captureServiceFactory = captureServiceFactory ?? throw new ArgumentNullException(nameof(captureServiceFactory));
        _storageRoot = storageRoot;
        _ffmpegPath = ffmpegPath;
    }

    public async Task<DesktopSmokeCheckResult> RunAsync(int seconds = 3, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return DesktopSmokeCheckResult.Failure("Smoke check supports Windows only.");
        }

        seconds = Math.Clamp(seconds, 2, 30);
        var smokeRoot = Path.Combine(_storageRoot, "smoke");
        Directory.CreateDirectory(smokeRoot);

        var runId = Guid.NewGuid();
        var startedUtc = DateTimeOffset.UtcNow;
        var outputDirectory = Path.Combine(smokeRoot, $"{startedUtc:yyyyMMdd_HHmmss}_{runId:N}");
        Directory.CreateDirectory(outputDirectory);
        var rawVideoPath = Path.Combine(outputDirectory, $"smoke-{runId:D}.mp4");

        var captureOptions = new FfmpegCaptureOptions
        {
            Enabled = true,
            FfmpegPath = _ffmpegPath,
            FrameRate = 30,
            VideoCodec = "libx264",
            Preset = "veryfast",
            Crf = 23,
            MaxDurationSeconds = Math.Max(seconds + 15, 30),
            CaptureMode = "Desktop",
            FallbackToDesktop = true,
            CropEnabled = false,
            HighlightCursor = false,
            OutputFileExtension = ".mp4"
        };

        var captureService = _captureServiceFactory.Create(captureOptions, smokeRoot, new NullWindowLocator());

        var run = new DemoRun(Guid.NewGuid(), Guid.NewGuid(), "desktop.smoke@demostudio.local");
        var start = await captureService.StartAsync(new CaptureStartRequest(run, outputDirectory, rawVideoPath), cancellationToken);
        if (!start.Succeeded)
        {
            return DesktopSmokeCheckResult.Failure(start.ErrorMessage ?? "Smoke check failed starting capture.");
        }

        var effectivePath = string.IsNullOrWhiteSpace(start.RawVideoPath) ? rawVideoPath : start.RawVideoPath;
        if (!await WaitForNonZeroFileAsync(effectivePath, TimeSpan.FromSeconds(8), cancellationToken))
        {
            await captureService.StopAsync(new CaptureStopRequest(run, effectivePath), cancellationToken);
            return DesktopSmokeCheckResult.Failure("Smoke check failed: output file did not start writing.");
        }

        await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken);
        var stop = await captureService.StopAsync(new CaptureStopRequest(run, effectivePath), cancellationToken);
        if (!stop.Succeeded)
        {
            return DesktopSmokeCheckResult.Failure(stop.ErrorMessage ?? "Smoke check failed stopping capture.");
        }

        if (!File.Exists(effectivePath))
        {
            return DesktopSmokeCheckResult.Failure("Smoke check failed: output file missing.");
        }

        var fileSize = new FileInfo(effectivePath).Length;
        if (fileSize <= 0)
        {
            return DesktopSmokeCheckResult.Failure("Smoke check failed: output file is empty.");
        }

        return DesktopSmokeCheckResult.Success(effectivePath, fileSize);
    }

    private static async Task<bool> WaitForNonZeroFileAsync(string path, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow <= deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (File.Exists(path))
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    if (stream.Length > 0)
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }

            await Task.Delay(250, cancellationToken);
        }

        return false;
    }

    private sealed class NullWindowLocator : IWindowLocator
    {
        public Task<WindowLocatorResult> FindAsync(WindowLocatorRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new WindowLocatorResult(
                Found: false,
                Handle: IntPtr.Zero,
                Title: string.Empty,
                ProcessId: 0,
                FailureReason: "Smoke check runs in desktop capture mode.",
                Bounds: null,
                DesktopBounds: null));
        }
    }
}

public sealed record DesktopSmokeCheckResult(bool Succeeded, string Message, string? OutputPath, long FileSizeBytes)
{
    public static DesktopSmokeCheckResult Success(string outputPath, long fileSizeBytes)
        => new(true, $"Smoke PASS ({Math.Round(fileSizeBytes / 1024d / 1024d, 2)} MB)", outputPath, fileSizeBytes);

    public static DesktopSmokeCheckResult Failure(string message)
        => new(false, message, null, 0);
}
