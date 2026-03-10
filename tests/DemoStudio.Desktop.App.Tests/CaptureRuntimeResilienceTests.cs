using DemoStudio.Desktop.App.Services;
using DemoStudio.Application.Abstractions.System;

namespace DemoStudio.Desktop.App.Tests;

public sealed class CaptureRuntimeResilienceTests
{
    [Fact]
    public void Constructor_FallsBackToTempStorage_WhenConfiguredStorageRootIsInvalid()
    {
        var options = new DesktopRecorderOptions
        {
            StorageRoot = "bad\0path"
        };

        var runtime = new DesktopCaptureRuntime(options, new NoOpProcessLauncher());

        Assert.False(string.IsNullOrWhiteSpace(runtime.StorageRoot));
        Assert.True(Directory.Exists(runtime.StorageRoot));
        Assert.Contains(Path.GetTempPath(), runtime.StorageRoot, StringComparison.OrdinalIgnoreCase);
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
