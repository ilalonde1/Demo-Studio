using System.Net;
using System.Net.Http;
using DemoStudio.Desktop.App.Services;
using DemoStudio.Desktop.App.ViewModels;
using DemoStudio.Desktop.App.Infrastructure;
using DemoStudio.Desktop.Core.Sessions;
using DemoStudio.Desktop.Core.Time;
using DemoStudio.Application.Abstractions.System;
using DemoStudio.Infrastructure.Execution;
using DemoStudio.Infrastructure.Options;
using DemoStudio.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;

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
        var processRunner = new DesktopProcessRunner(NullLogger<DesktopProcessRunner>.Instance);
        var processLauncher = new NoOpProcessLauncher();
        var windowLocator = new DesktopWindowLocator();
        var captureFactory = new DesktopVideoCaptureServiceFactory(
            processLauncher,
            NullLogger<FfmpegVideoCaptureService>.Instance);
        var captureRuntime = new DesktopCaptureRuntime(options, processLauncher, captureFactory, windowLocator);
        var launchProfileService = new DesktopLaunchProfileService(root, NullLogger<DesktopLaunchProfileService>.Instance);
        var targetLauncher = new DesktopTargetLauncher(processLauncher, NullLogger<DesktopTargetLauncher>.Instance);
        var preflightService = new DesktopCapturePreflightService(windowLocator);
        var windowFocusService = new DesktopWindowFocusService(windowLocator);
        var sessionHistoryService = new DesktopSessionHistoryService(root, NullLogger<DesktopSessionHistoryService>.Instance);
        var diagnosticsBundleService = new DesktopDiagnosticsBundleService(root, NullLogger<DesktopDiagnosticsBundleService>.Instance);
        var timelineMarkerWriter = new TimelineMarkerWriter(new LocalFileStorage(root));
        var ffmpegOperationQueue = new DesktopFfmpegOperationQueue();
        var targetingUseCase = new DesktopTargetingUseCase(
            new DesktopWindowCatalogService(),
            launchProfileService,
            targetLauncher,
            preflightService,
            windowFocusService);
        var sessionHistoryUseCase = new DesktopSessionHistoryUseCase(sessionHistoryService, NullLogger<DesktopSessionHistoryUseCase>.Instance);
        var shellIntegrationUseCase = new DesktopShellIntegrationUseCase(processRunner);
        var failureDiagnosticsUseCase = new DesktopFailureDiagnosticsUseCase(diagnosticsBundleService);
        var preflightChecksUseCase = new DesktopPreflightChecksUseCase();
        var captureSessionUseCase = new DesktopCaptureSessionUseCase(preflightChecksUseCase, NullLogger<DesktopCaptureSessionUseCase>.Instance);
        var composeOutputUseCase = new DesktopComposeOutputUseCase(
            new DesktopComposeManifestService(),
            new DesktopVideoComposeService(new NoOpProcessLauncher(), NullLogger<DesktopVideoComposeService>.Instance),
            ffmpegOperationQueue,
            captureRuntime,
            NullLogger<DesktopComposeOutputUseCase>.Instance);
        var draftSessionUseCase = new DesktopDraftSessionUseCase(
            new DesktopSessionRecoveryService(root, NullLogger<DesktopSessionRecoveryService>.Instance),
            NullLogger<DesktopDraftSessionUseCase>.Instance);
        var sessionLifecycleUseCase = new DesktopSessionLifecycleUseCase();
        var captureSessionViewModel = new CaptureSessionViewModel(
            sessionEngine,
            captureRuntime,
            windowFocusService,
            new DesktopPresenterViewService(),
            new DesktopClipCurationCoordinator());
        var publishWorkflowViewModel = new PublishWorkflowViewModel(
            new DesktopPublishWorkflowUseCase(
                new DesktopPublishPackageService(new NoOpProcessLauncher()),
                ffmpegOperationQueue,
                captureRuntime,
                NullLogger<DesktopPublishWorkflowUseCase>.Instance),
            shellIntegrationUseCase,
            NullLogger<PublishWorkflowViewModel>.Instance);
        var healthMonitorViewModel = new HealthMonitorViewModel(
            new DesktopDependencyHealthService(captureRuntime, processLauncher),
            new DesktopPerformanceMetricsService(),
            new DesktopSmokeCheckService(root, "ffmpeg", processLauncher, captureFactory),
            captureRuntime,
            NullLogger<HealthMonitorViewModel>.Instance);

        // All constructor parameters are required. If the constructor signature
        // changes, this builder will fail to compile  update it before adding
        // new service dependencies.
        return new MainWindowViewModel(
            sessionEngine: sessionEngine,
            captureRuntime: captureRuntime,
            onboardingService: new DesktopOnboardingService(root),
            demoTemplateService: new DesktopDemoTemplateService(root),
            presenterViewService: new DesktopPresenterViewService(),
            captureMediaCoordinator: new DesktopCaptureMediaCoordinator(captureRuntime, processLauncher, processRunner),
            narrationCoordinator: new DesktopNarrationCoordinator(captureRuntime,
                new DesktopClipNarrationService(new NoOpProcessLauncher()),
                new DesktopAiNarrationService(new HttpClient(new NoOpHttpMessageHandler())),
                processRunner),
            captureWatchdogCoordinator: new DesktopCaptureWatchdogCoordinator(windowLocator, NullLogger<DesktopCaptureWatchdogCoordinator>.Instance),
            clipCurationCoordinator: new DesktopClipCurationCoordinator(),
            ffmpegOperationQueue: ffmpegOperationQueue,
            runtimeInitializationUseCase: new DesktopRuntimeInitializationUseCase(NullLogger<DesktopRuntimeInitializationUseCase>.Instance),
            preflightChecksUseCase: preflightChecksUseCase,
            captureSessionUseCase: new DesktopCaptureSessionUseCase(preflightChecksUseCase, NullLogger<DesktopCaptureSessionUseCase>.Instance),
            composeOutputUseCase: composeOutputUseCase,
            draftSessionUseCase: draftSessionUseCase,
            sessionLifecycleUseCase: sessionLifecycleUseCase,
            targetingUseCase: targetingUseCase,
            sessionHistoryUseCase: sessionHistoryUseCase,
            shellIntegrationUseCase: shellIntegrationUseCase,
            failureDiagnosticsUseCase: failureDiagnosticsUseCase,
            timelineMarkerWriter: timelineMarkerWriter,
            logger: NullLogger<MainWindowViewModel>.Instance,
            healthMonitor: healthMonitorViewModel,
            publishWorkflow: publishWorkflowViewModel,
            captureSession: captureSessionViewModel);
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

    private sealed class NoOpHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Array.Empty<byte>())
            });
        }
    }
}
