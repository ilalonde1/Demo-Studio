using System.Net.Http;
using System.IO;
using DemoStudio.Application.Abstractions.System;
using DemoStudio.Application.Services;
using DemoStudio.Automation.Abstractions.Interfaces;
using DemoStudio.Automation.FlaUI.Engines;
using DemoStudio.Automation.FlaUI.Options;
using DemoStudio.Automation.FlaUI.Services;
using DemoStudio.Capture.Abstractions.Interfaces;
using DemoStudio.Desktop.App.Infrastructure;
using DemoStudio.Desktop.App.Services;
using DemoStudio.Desktop.App.ViewModels;
using DemoStudio.Desktop.Core.Sessions;
using DemoStudio.Desktop.Core.Time;
using DemoStudio.Infrastructure.Execution;
using DemoStudio.Infrastructure.Execution.Windows;
using DemoStudio.Infrastructure.Export;
using DemoStudio.Infrastructure.Options;
using DemoStudio.Infrastructure.Options.Validation;
using DemoStudio.Infrastructure.Process;
using DemoStudio.Infrastructure.Storage;
using DemoStudio.Redaction.Abstractions.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DemoStudio.Desktop.App;

internal static class DesktopCompositionRoot
{
    public static ServiceProvider Build(string? baseDirectory = null)
    {
        var resolvedBaseDirectory = string.IsNullOrWhiteSpace(baseDirectory)
            ? AppContext.BaseDirectory
            : Path.GetFullPath(baseDirectory);
        var configuration = BuildConfiguration(resolvedBaseDirectory);

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(_ => new HttpClient());

        AddOptions(services, configuration);

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<DesktopRecorderOptions>>().Value;
            return new DesktopRuntimeLogService(DesktopRecorderOptionsNormalizer.ResolveStorageRoot(options.StorageRoot));
        });
        services.AddSingleton<ILoggerProvider, DesktopRuntimeLoggerProvider>();
        services.AddLogging();

        AddApplicationAndInfrastructureServices(services);
        AddDesktopRuntimeServices(services);

        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        provider.GetRequiredService<IStartupValidator>().Validate();
        return provider;
    }

    private static IConfigurationRoot BuildConfiguration(string baseDirectory)
    {
        return new ConfigurationBuilder()
            .SetBasePath(baseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();
    }

    private static void AddOptions(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions();

        services.AddSingleton<IValidateOptions<DesktopRecorderOptions>, DesktopRecorderOptionsValidator>();
        services.AddSingleton<IValidateOptions<FfmpegCaptureOptions>, FfmpegCaptureOptionsValidator>();
        services.AddSingleton<IValidateOptions<AutomationOptions>, AutomationOptionsValidator>();
        services.AddSingleton<IValidateOptions<CaptureOptions>, CaptureOptionsValidator>();
        services.AddSingleton<IValidateOptions<DemoExecutionOptions>, DemoExecutionOptionsValidator>();
        services.AddSingleton<IValidateOptions<FlaUIRunnerOptions>, FlaUIRunnerOptionsValidator>();

        services.AddOptions<DesktopRecorderOptions>()
            .Bind(configuration.GetSection("DesktopRecorder"))
            .PostConfigure(DesktopRecorderOptionsNormalizer.Normalize)
            .ValidateOnStart();

        services.AddOptions<FfmpegCaptureOptions>()
            .Bind(configuration.GetSection("DesktopRecorder:Capture"))
            .PostConfigure(DesktopRecorderOptionsNormalizer.NormalizeCapture)
            .ValidateOnStart();

        services.AddOptions<AutomationRuntimeOptions>()
            .Bind(configuration.GetSection("AutomationRuntime"))
            .ValidateOnStart();

        services.AddOptions<AutomationOptions>()
            .Bind(configuration.GetSection(AutomationOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<CaptureOptions>()
            .Bind(configuration.GetSection(CaptureOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<DemoExecutionOptions>()
            .Bind(configuration.GetSection(DemoExecutionOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<FlaUIRunnerOptions>()
            .Bind(configuration.GetSection("Automation:FlaUIRunner"))
            .ValidateOnStart();
    }

    private static void AddApplicationAndInfrastructureServices(IServiceCollection services)
    {
        services.AddSingleton<IProcessLauncher, ProcessLauncher>();
        services.AddSingleton<IApplicationProcessInspector, ApplicationProcessInspector>();
        services.AddSingleton<IWindowLocator, DesktopWindowLocator>();
        services.AddSingleton<DesktopVideoCaptureServiceFactory>();
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<DesktopRecorderOptions>>().Value;
            return new DesktopCaptureRuntime(
                options,
                sp.GetRequiredService<IProcessLauncher>(),
                sp.GetRequiredService<DesktopVideoCaptureServiceFactory>(),
                sp.GetRequiredService<IWindowLocator>());
        });
        services.AddSingleton(sp =>
        {
            var runtime = sp.GetRequiredService<DesktopCaptureRuntime>();
            return new DesktopRuntimePaths(runtime.StorageRoot, runtime.FfmpegPath);
        });
        services.AddSingleton<IFileStorage>(sp =>
        {
            var paths = sp.GetRequiredService<DesktopRuntimePaths>();
            return new LocalFileStorage(paths.StorageRoot);
        });
        services.AddSingleton<IVideoCaptureService>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<DesktopRecorderOptions>>().Value;
            var paths = sp.GetRequiredService<DesktopRuntimePaths>();
            return sp.GetRequiredService<DesktopVideoCaptureServiceFactory>()
                .Create(options.Capture, paths.StorageRoot, sp.GetRequiredService<IWindowLocator>());
        });
        services.AddSingleton<IRedactionProcessor, StubRedactionProcessor>();
        services.AddSingleton<IAutomationRuntimeGate, AutomationRuntimeGate>();
        services.AddSingleton<IAutomationEngineResolver, AutomationEngineResolver>();
        services.AddSingleton<IDesktopAutomationEngine>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AutomationOptions>>().Value;
            if (options.DesktopEngine.Equals("FlaUI", StringComparison.OrdinalIgnoreCase))
            {
                return new FlaUIDesktopAutomationEngine(
                    sp.GetRequiredService<IProcessLauncher>(),
                    sp.GetRequiredService<IFileStorage>(),
                    sp.GetRequiredService<IOptions<FlaUIRunnerOptions>>());
            }

            return new StubDesktopAutomationEngine();
        });
        services.AddSingleton<IDesktopInspectionService>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AutomationOptions>>().Value;
            if (options.DesktopEngine.Equals("FlaUI", StringComparison.OrdinalIgnoreCase))
            {
                return new FlaUIDesktopInspectionService(
                    sp.GetRequiredService<IProcessLauncher>(),
                    sp.GetRequiredService<IFileStorage>(),
                    sp.GetRequiredService<IOptions<FlaUIRunnerOptions>>());
            }

            return new StubDesktopInspectionService();
        });
        services.AddSingleton<IDemoOutputPathProvider, DemoOutputPathProvider>();
        services.AddSingleton<ITimelineMarkerWriter, TimelineMarkerWriter>();
        services.AddSingleton<IDemoExportService, DemoExportService>();
        services.AddSingleton<IDemoRunPipeline, DemoRunPipeline>();
    }

    private static void AddDesktopRuntimeServices(IServiceCollection services)
    {
        services.AddSingleton<DesktopProcessRunner>();
        services.AddSingleton<RecorderSessionEngine>(_ => new RecorderSessionEngine(new SystemClock()));
        services.AddSingleton<DesktopCaptureMediaCoordinator>();
        services.AddSingleton<DesktopNarrationCoordinator>();
        services.AddSingleton<DesktopCaptureWatchdogCoordinator>();
        services.AddSingleton<DesktopClipCurationCoordinator>();
        services.AddSingleton<DesktopDependencyHealthService>();
        services.AddSingleton<DesktopWindowCatalogService>();
        services.AddSingleton<DesktopTargetLauncher>();
        services.AddSingleton<DesktopCapturePreflightService>();
        services.AddSingleton<DesktopWindowFocusService>();
        services.AddSingleton<DesktopPerformanceMetricsService>();
        services.AddSingleton<DesktopComposeManifestService>();
        services.AddSingleton<DesktopVideoComposeService>();
        services.AddSingleton<DesktopFfmpegOperationQueue>();
        services.AddSingleton<DesktopClipNarrationService>();
        services.AddSingleton<DesktopAiNarrationService>();
        services.AddSingleton<DesktopPublishPackageService>();
        services.AddSingleton<DesktopPresenterViewService>();
        services.AddSingleton<DesktopCrashReporter>(sp =>
        {
            var paths = sp.GetRequiredService<DesktopRuntimePaths>();
            return new DesktopCrashReporter(
                paths.StorageRoot,
                sp.GetRequiredService<ILogger<DesktopCrashReporter>>());
        });
        services.AddSingleton<DesktopLaunchProfileService>(sp =>
        {
            var paths = sp.GetRequiredService<DesktopRuntimePaths>();
            return new DesktopLaunchProfileService(
                paths.StorageRoot,
                sp.GetRequiredService<ILogger<DesktopLaunchProfileService>>());
        });
        services.AddSingleton<DesktopSessionHistoryService>(sp =>
        {
            var paths = sp.GetRequiredService<DesktopRuntimePaths>();
            return new DesktopSessionHistoryService(
                paths.StorageRoot,
                sp.GetRequiredService<ILogger<DesktopSessionHistoryService>>());
        });
        services.AddSingleton<DesktopDiagnosticsBundleService>(sp =>
        {
            var paths = sp.GetRequiredService<DesktopRuntimePaths>();
            return new DesktopDiagnosticsBundleService(
                paths.StorageRoot,
                sp.GetRequiredService<ILogger<DesktopDiagnosticsBundleService>>());
        });
        services.AddSingleton<DesktopSmokeCheckService>(sp =>
        {
            var paths = sp.GetRequiredService<DesktopRuntimePaths>();
            return new DesktopSmokeCheckService(
                paths.StorageRoot,
                paths.FfmpegPath,
                sp.GetRequiredService<IProcessLauncher>(),
                sp.GetRequiredService<DesktopVideoCaptureServiceFactory>());
        });
        services.AddSingleton<DesktopOnboardingService>(sp =>
        {
            var paths = sp.GetRequiredService<DesktopRuntimePaths>();
            return new DesktopOnboardingService(paths.StorageRoot);
        });
        services.AddSingleton<DesktopDemoTemplateService>(sp =>
        {
            var paths = sp.GetRequiredService<DesktopRuntimePaths>();
            return new DesktopDemoTemplateService(paths.StorageRoot);
        });
        services.AddSingleton<DesktopSessionRecoveryService>(sp =>
        {
            var paths = sp.GetRequiredService<DesktopRuntimePaths>();
            return new DesktopSessionRecoveryService(
                paths.StorageRoot,
                sp.GetRequiredService<ILogger<DesktopSessionRecoveryService>>());
        });
        services.AddSingleton<DesktopStartupHealthService>();
        services.AddSingleton<IDesktopRuntimeInitializationUseCase, DesktopRuntimeInitializationUseCase>();
        services.AddSingleton<IDesktopPreflightChecksUseCase, DesktopPreflightChecksUseCase>();
        services.AddSingleton<IDesktopCaptureSessionUseCase, DesktopCaptureSessionUseCase>();
        services.AddSingleton<IDesktopComposeOutputUseCase, DesktopComposeOutputUseCase>();
        services.AddSingleton<IDesktopDraftSessionUseCase, DesktopDraftSessionUseCase>();
        services.AddSingleton<IDesktopSessionLifecycleUseCase, DesktopSessionLifecycleUseCase>();
        services.AddSingleton<IDesktopTargetingUseCase, DesktopTargetingUseCase>();
        services.AddSingleton<IDesktopSessionHistoryUseCase, DesktopSessionHistoryUseCase>();
        services.AddSingleton<IDesktopShellIntegrationUseCase, DesktopShellIntegrationUseCase>();
        services.AddSingleton<IDesktopFailureDiagnosticsUseCase, DesktopFailureDiagnosticsUseCase>();
        services.AddSingleton<IDesktopPublishWorkflowUseCase, DesktopPublishWorkflowUseCase>();
        services.AddSingleton<HealthMonitorViewModel>();
        services.AddSingleton<PublishWorkflowViewModel>();
        services.AddSingleton<CaptureSessionViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();
    }
}
