using DemoStudio.Desktop.App.Services;
using DemoStudio.Desktop.App.ViewModels;
using DemoStudio.Desktop.App.Infrastructure;
using DemoStudio.Desktop.Core.Sessions;
using DemoStudio.Desktop.Core.Time;
using DemoStudio.Application.Abstractions.System;
using DemoStudio.Infrastructure.Options;

namespace DemoStudio.Desktop.App.Tests.Helpers;

/// <summary>
/// Builds a minimally configured MainWindowViewModel for unit tests.
/// All services are real instances using temp paths.
/// If the constructor signature changes, this builder will fail to compile — update it first.
/// </summary>
internal static class MainWindowViewModelTestBuilder
{
    public static MainWindowViewModel CreateMinimal(string? tempRoot = null)
    {
        var root = tempRoot ?? DesktopStoragePaths.GetDefaultRecorderRoot();
        Directory.CreateDirectory(root);

        var options = new DesktopRecorderOptions
        {
            StorageRoot = root,
            Capture = new FfmpegCaptureOptions { FfmpegPath = "ffmpeg" }
        };

        var sessionEngine = new RecorderSessionEngine(new SystemClock());
        var processRunner = new DesktopProcessRunner();
        var captureRuntime = new DesktopCaptureRuntime(options);

        // All constructor parameters are required. If the constructor signature
        // changes, this builder will fail to compile  update it before adding
        // new service dependencies.
        return new MainWindowViewModel(
            sessionEngine: sessionEngine,
            captureRuntime: captureRuntime,
            windowCatalogService: new DesktopWindowCatalogService(),
            launchProfileService: new DesktopLaunchProfileService(root),
            targetLauncher: new DesktopTargetLauncher(),
            preflightService: new DesktopCapturePreflightService(),
            windowFocusService: new DesktopWindowFocusService(),
            sessionHistoryService: new DesktopSessionHistoryService(root),
            diagnosticsBundleService: new DesktopDiagnosticsBundleService(root),
            performanceMetricsService: new DesktopPerformanceMetricsService(),
            smokeCheckService: new DesktopSmokeCheckService(root, "ffmpeg"),
            composeManifestService: new DesktopComposeManifestService(),
            videoComposeService: new DesktopVideoComposeService(new NoOpProcessLauncher()),
            publishPackageService: new DesktopPublishPackageService(new NoOpProcessLauncher()),
            onboardingService: new DesktopOnboardingService(root),
            demoTemplateService: new DesktopDemoTemplateService(root),
            sessionRecoveryService: new DesktopSessionRecoveryService(root),
            presenterViewService: new DesktopPresenterViewService(),
            processRunner: processRunner,
            captureMediaCoordinator: new DesktopCaptureMediaCoordinator(captureRuntime, processRunner),
            narrationCoordinator: new DesktopNarrationCoordinator(captureRuntime,
                new DesktopClipNarrationService(),
                new DesktopAiNarrationService(),
                processRunner),
            captureWatchdogCoordinator: new DesktopCaptureWatchdogCoordinator(),
            clipCurationCoordinator: new DesktopClipCurationCoordinator(),
            dependencyHealthService: new DesktopDependencyHealthService(captureRuntime, processRunner),
            ffmpegOperationQueue: new DesktopFfmpegOperationQueue());
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
