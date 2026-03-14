using DemoStudio.Application.Abstractions.System;
using DemoStudio.Desktop.App.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace DemoStudio.Desktop.App.Tests;

public sealed class ProcessExecutionSafetyTests
{
    [Fact]
    public async Task TargetLauncher_UsesProcessLauncherForExecutableLaunch()
    {
        var root = Path.Combine(Path.GetTempPath(), "demostudio-app-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var executablePath = Path.Combine(root, "demo-target.exe");
        await File.WriteAllTextAsync(executablePath, "stub");

        try
        {
            var launcher = new CapturingProcessLauncher();
            var service = new DesktopTargetLauncher(launcher, NullLogger<DesktopTargetLauncher>.Instance);

            var result = await service.LaunchAsync(new DesktopLaunchProfile(
                Name: "Demo",
                ExecutablePath: executablePath,
                Arguments: "--flag value",
                WorkingDirectory: root,
                StartupDelaySeconds: 0,
                ExpectedWindowTitleContains: null,
                ExpectedProcessName: null));

            Assert.True(result.Succeeded);
            Assert.NotNull(launcher.LastStartRequest);
            Assert.Equal(executablePath, launcher.LastStartRequest!.FileName);
            Assert.Equal("--flag value", launcher.LastStartRequest.Arguments);
            Assert.Null(launcher.LastStartRequest.ArgumentList);
            Assert.Equal("target-launch", launcher.LastStartRequest.OperationName);
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
    public async Task TargetLauncher_RejectsDirectoryPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "demostudio-app-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var launcher = new CapturingProcessLauncher();
            var service = new DesktopTargetLauncher(launcher, NullLogger<DesktopTargetLauncher>.Instance);

            var result = await service.LaunchAsync(new DesktopLaunchProfile(
                Name: "Demo",
                ExecutablePath: root,
                Arguments: null,
                WorkingDirectory: root,
                StartupDelaySeconds: 0,
                ExpectedWindowTitleContains: null,
                ExpectedProcessName: null));

            Assert.False(result.Succeeded);
            Assert.Contains("directory", result.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Null(launcher.LastStartRequest);
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
    public async Task ClipNarration_UsesStructuredArguments()
    {
        var root = Path.Combine(Path.GetTempPath(), "demostudio-app-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var outputPath = Path.Combine(root, "narration.m4a");

        try
        {
            var launcher = new CapturingProcessLauncher(onLaunch: request =>
            {
                File.WriteAllText(outputPath, "audio");
                return new ProcessLaunchResult(
                    true,
                    123,
                    new ProcessExecutionResult(0, string.Empty, string.Empty, false, false),
                    null);
            });
            var service = new DesktopClipNarrationService(launcher);

            var result = await service.CaptureAsync("ffmpeg.exe", "USB Mic", 2.0d, outputPath);

            Assert.True(result.Succeeded);
            Assert.NotNull(launcher.LastLaunchRequest);
            Assert.NotNull(launcher.LastLaunchRequest!.ArgumentList);
            Assert.Contains("-f", launcher.LastLaunchRequest.ArgumentList!);
            Assert.Contains("dshow", launcher.LastLaunchRequest.ArgumentList!);
            Assert.Contains(outputPath, launcher.LastLaunchRequest.ArgumentList!);
            Assert.Equal("ffmpeg-narration-capture", launcher.LastLaunchRequest.OperationName);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    private sealed class CapturingProcessLauncher : IProcessLauncher
    {
        private readonly Func<ProcessLaunchRequest, ProcessLaunchResult>? _onLaunch;

        public CapturingProcessLauncher(Func<ProcessLaunchRequest, ProcessLaunchResult>? onLaunch = null)
        {
            _onLaunch = onLaunch;
        }

        public ProcessStartRequest? LastStartRequest { get; private set; }

        public ProcessLaunchRequest? LastLaunchRequest { get; private set; }

        public Task<IProcessHandle> StartProcessAsync(ProcessStartRequest request, CancellationToken cancellationToken = default)
        {
            LastStartRequest = request;
            IProcessHandle handle = new NoOpProcessHandle();
            return Task.FromResult(handle);
        }

        public Task<ProcessLaunchResult> LaunchAsync(ProcessLaunchRequest request, CancellationToken cancellationToken = default)
        {
            LastLaunchRequest = request;
            var result = _onLaunch?.Invoke(request)
                ?? new ProcessLaunchResult(true, 1, new ProcessExecutionResult(0, string.Empty, string.Empty, false, false), null);
            return Task.FromResult(result);
        }
    }

    private sealed class NoOpProcessHandle : IProcessHandle
    {
        public int? ProcessId => 1;

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<ProcessExecutionResult> WaitAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ProcessExecutionResult(0, string.Empty, string.Empty, false, false));
        }
    }
}
