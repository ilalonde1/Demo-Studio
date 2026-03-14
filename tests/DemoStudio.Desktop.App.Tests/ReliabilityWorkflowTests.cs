using DemoStudio.Desktop.App.Services;
using DemoStudio.Desktop.App.ViewModels;
using DemoStudio.Application.Abstractions.System;
using DemoStudio.Infrastructure.Execution;
using DemoStudio.Infrastructure.Options;
using Microsoft.Extensions.Logging.Abstractions;

namespace DemoStudio.Desktop.App.Tests;

public sealed class ReliabilityWorkflowTests
{
    [Fact]
    public async Task ProcessRunner_ReturnsStartFailed_WhenExecutableMissing()
    {
        var runner = new DesktopProcessRunner();
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "definitely-not-a-real-executable-12345.exe",
            UseShellExecute = false
        };

        var result = await runner.RunAsync(startInfo, TimeSpan.FromSeconds(1));

        Assert.False(result.Succeeded);
        Assert.True(result.StartFailed);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    [Fact]
    public async Task ProcessRunner_TimesOut_AndMarksResult()
    {
        var runner = new DesktopProcessRunner();
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c ping 127.0.0.1 -n 6 >nul",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        var result = await runner.RunAsync(startInfo, TimeSpan.FromMilliseconds(250));

        Assert.False(result.Succeeded);
        Assert.True(result.TimedOut);
    }

    [Fact]
    public async Task DependencyHealthService_ReportsDegraded_WhenFfmpegIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "demostudio-app-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var options = new DesktopRecorderOptions
            {
                StorageRoot = root,
                Capture = new FfmpegCaptureOptions
                {
                    FfmpegPath = "missing-ffmpeg-bin-for-test"
                }
            };
            var processLauncher = new NoOpProcessLauncher();
            var captureFactory = new DesktopVideoCaptureServiceFactory(
                processLauncher,
                NullLogger<FfmpegVideoCaptureService>.Instance);
            var runtime = new DesktopCaptureRuntime(options, processLauncher, captureFactory, new DesktopWindowLocator());
            var service = new DesktopDependencyHealthService(runtime, processLauncher);

            var snapshot = await service.RefreshAsync();

            Assert.False(snapshot.IsHealthy);
            Assert.Contains(snapshot.Errors, x => x.Contains("FFmpeg", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public async Task RelayCommand_AsyncMode_PreventsConcurrentExecution()
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var callCount = 0;
        var command = new RelayCommand(async _ =>
        {
            Interlocked.Increment(ref callCount);
            await gate.Task;
        });

        Assert.True(command.CanExecute(null));
        command.Execute(null);
        await Task.Delay(50);
        command.Execute(null);
        await Task.Delay(50);

        Assert.Equal(1, Volatile.Read(ref callCount));
        Assert.False(command.CanExecute(null));

        gate.SetResult(true);
        await Task.Delay(50);
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public void FailureEnvelope_IncludesCodeSummaryDetailAndFixHint()
    {
        var envelope = new DesktopFailureEnvelope(
            Code: "DS-DESK-TEST-001",
            Summary: "Synthetic failure.",
            Detail: "detail text",
            FixHint: "Try again");

        var text = envelope.ToDisplayText();

        Assert.Contains("[DS-DESK-TEST-001]", text, StringComparison.Ordinal);
        Assert.Contains("Synthetic failure.", text, StringComparison.Ordinal);
        Assert.Contains("detail text", text, StringComparison.Ordinal);
        Assert.Contains("Try again", text, StringComparison.Ordinal);
    }

    private sealed class NoOpProcessLauncher : IProcessLauncher
    {
        public Task<IProcessHandle> StartProcessAsync(ProcessStartRequest request, CancellationToken cancellationToken = default)
        {
            IProcessHandle handle = new NoOpProcessHandle();
            return Task.FromResult(handle);
        }

        public Task<ProcessLaunchResult> LaunchAsync(ProcessLaunchRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ProcessLaunchResult(
                Started: false,
                ProcessId: null,
                Execution: new ProcessExecutionResult(
                    ExitCode: 0,
                    StdOut: string.Empty,
                    StdErr: string.Empty,
                    TimedOut: false,
                    Cancelled: false),
                ErrorMessage: null));
        }
    }

    private sealed class NoOpProcessHandle : IProcessHandle
    {
        public int? ProcessId => null;

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        public Task<ProcessExecutionResult> WaitAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ProcessExecutionResult(
                ExitCode: 0,
                StdOut: string.Empty,
                StdErr: string.Empty,
                TimedOut: false,
                Cancelled: false));
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
