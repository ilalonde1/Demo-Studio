using DemoStudio.Application.Abstractions.System;
using DemoStudio.Capture.Abstractions.Interfaces;
using DemoStudio.Domain.Entities;
using DemoStudio.Infrastructure.Execution;
using DemoStudio.Infrastructure.Execution.Windows;
using DemoStudio.Infrastructure.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DemoStudio.Desktop.App.Tests;

public sealed class FfmpegVideoCaptureServiceRegressionTests
{
    [Fact]
    public async Task StartAsync_ClampsWindowBoundsToDesktop_BeforeBuildingGdigrabOffsets()
    {
        var launcher = new FakeProcessLauncher(
            new FakeProcessHandle(new ProcessExecutionResult(0, string.Empty, string.Empty, TimedOut: false, Cancelled: false)));
        var storage = new FakeFileStorage();
        var locator = new FakeWindowLocator(new WindowLocatorResult(
            Found: true,
            Handle: new IntPtr(0x3F0566),
            Title: "Edge",
            ProcessId: 1234,
            FailureReason: null,
            Bounds: new WindowBounds(-1921, -1, 1922, 1041),
            DesktopBounds: new WindowBounds(-1920, 0, 3840, 1080)));
        var options = Options.Create(new FfmpegCaptureOptions
        {
            Enabled = true,
            FfmpegPath = "ffmpeg",
            CaptureMode = "Window",
            WindowHandleHex = "0x3F0566",
            PreferExactHandle = true,
            OutputFileExtension = ".mp4",
            CaptureMicrophone = false
        });

        var service = new FfmpegVideoCaptureService(launcher, storage, locator, options, NullLogger<FfmpegVideoCaptureService>.Instance);
        var run = new DemoRun(Guid.NewGuid(), Guid.NewGuid(), "tester@demostudio.local");
        var outputDir = Path.Combine(Path.GetTempPath(), "demostudio-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDir);
        var rawPath = Path.Combine(outputDir, "capture.mp4");

        try
        {
            var start = await service.StartAsync(new CaptureStartRequest(run, outputDir, rawPath));

            Assert.True(start.Succeeded);
            Assert.NotNull(launcher.LastStartRequest);
            var args = launcher.LastStartRequest!.Arguments;
            Assert.Contains("-offset_x -1920", args, StringComparison.Ordinal);
            Assert.Contains("-offset_y 0", args, StringComparison.Ordinal);
            Assert.Contains("-video_size 1921x1040", args, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(outputDir))
            {
                Directory.Delete(outputDir, true);
            }
        }
    }

    [Fact]
    public async Task StopAsync_ReturnsFfmpegDetail_WhenOutputFileMissing()
    {
        var handle = new FakeProcessHandle(new ProcessExecutionResult(
            ExitCode: 1,
            StdOut: string.Empty,
            StdErr: "gdigrab: invalid capture area",
            TimedOut: false,
            Cancelled: false));
        var launcher = new FakeProcessLauncher(handle);
        var storage = new FakeFileStorage();
        var locator = new FakeWindowLocator(new WindowLocatorResult(
            Found: true,
            Handle: new IntPtr(0x3F0566),
            Title: "Edge",
            ProcessId: 1234,
            FailureReason: null,
            Bounds: new WindowBounds(0, 0, 1280, 720),
            DesktopBounds: new WindowBounds(0, 0, 1920, 1080)));
        var options = Options.Create(new FfmpegCaptureOptions
        {
            Enabled = true,
            FfmpegPath = "ffmpeg",
            CaptureMode = "Window",
            WindowHandleHex = "0x3F0566",
            PreferExactHandle = true,
            OutputFileExtension = ".mp4",
            CaptureMicrophone = false
        });

        var service = new FfmpegVideoCaptureService(launcher, storage, locator, options, NullLogger<FfmpegVideoCaptureService>.Instance);
        var run = new DemoRun(Guid.NewGuid(), Guid.NewGuid(), "tester@demostudio.local");
        var outputDir = Path.Combine(Path.GetTempPath(), "demostudio-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDir);
        var rawPath = Path.Combine(outputDir, "capture.mp4");

        try
        {
            var start = await service.StartAsync(new CaptureStartRequest(run, outputDir, rawPath));
            Assert.True(start.Succeeded);

            var stop = await service.StopAsync(new CaptureStopRequest(run, rawPath));

            Assert.False(stop.Succeeded);
            Assert.NotNull(stop.ErrorMessage);
            Assert.Contains("output video file was not found", stop.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ffmpeg:", stop.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(outputDir))
            {
                Directory.Delete(outputDir, true);
            }
        }
    }

    private sealed class FakeProcessLauncher : IProcessLauncher
    {
        private readonly IProcessHandle _handle;

        public FakeProcessLauncher(IProcessHandle handle)
        {
            _handle = handle;
        }

        public ProcessStartRequest? LastStartRequest { get; private set; }

        public Task<IProcessHandle> StartProcessAsync(ProcessStartRequest request, CancellationToken cancellationToken = default)
        {
            LastStartRequest = request;
            return Task.FromResult(_handle);
        }

        public Task<ProcessLaunchResult> LaunchAsync(ProcessLaunchRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ProcessLaunchResult(true, 1, null, null));
        }
    }

    private sealed class FakeProcessHandle : IProcessHandle
    {
        private readonly ProcessExecutionResult _execution;

        public FakeProcessHandle(ProcessExecutionResult execution)
        {
            _execution = execution;
        }

        public int? ProcessId => 12345;

        public Task<ProcessExecutionResult> WaitAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_execution);

        public Task StopAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeFileStorage : IFileStorage
    {
        public Task<string> SaveAsync(string relativePath, Stream content, CancellationToken cancellationToken = default)
            => Task.FromResult(relativePath);

        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);
    }

    private sealed class FakeWindowLocator : IWindowLocator
    {
        private readonly WindowLocatorResult _result;

        public FakeWindowLocator(WindowLocatorResult result)
        {
            _result = result;
        }

        public Task<WindowLocatorResult> FindAsync(WindowLocatorRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(_result);
    }
}
