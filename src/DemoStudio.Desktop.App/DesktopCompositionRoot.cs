using System.Net.Http;
using DemoStudio.Application.Abstractions.System;
using DemoStudio.Desktop.App.Services;
using DemoStudio.Desktop.App.ViewModels;
using DemoStudio.Desktop.App.Infrastructure;
using DemoStudio.Desktop.Core.Sessions;
using DemoStudio.Desktop.Core.Time;
using DemoStudio.Infrastructure.Execution.Windows;
using DemoStudio.Infrastructure.Process;
using Microsoft.Extensions.DependencyInjection;

namespace DemoStudio.Desktop.App;

internal static class DesktopCompositionRoot
{
    public static ServiceProvider Build()
    {
        var services = new ServiceCollection();

        var options = DesktopRecorderOptionsLoader.Load(AppContext.BaseDirectory);
        services.AddSingleton(options);
        services.AddSingleton(_ => new HttpClient());
        services.AddSingleton<DesktopProcessRunner>();
        services.AddSingleton<IProcessLauncher, ProcessLauncher>();
        services.AddSingleton<IWindowLocator, DesktopWindowLocator>();
        services.AddSingleton<RecorderSessionEngine>(_ => new RecorderSessionEngine(new SystemClock()));
        services.AddSingleton<DesktopCaptureRuntime>();
        services.AddSingleton(sp =>
        {
            var runtime = sp.GetRequiredService<DesktopCaptureRuntime>();
            return new DesktopRuntimePaths(runtime.StorageRoot, runtime.FfmpegPath);
        });
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

        services.AddSingleton(sp =>
        {
            var paths = sp.GetRequiredService<DesktopRuntimePaths>();
            return new DesktopRuntimeLogService(paths.StorageRoot);
        });
        services.AddSingleton(sp =>
        {
            var paths = sp.GetRequiredService<DesktopRuntimePaths>();
            return new DesktopCrashReporter(paths.StorageRoot);
        });
        services.AddSingleton(sp =>
        {
            var paths = sp.GetRequiredService<DesktopRuntimePaths>();
            return new DesktopLaunchProfileService(paths.StorageRoot);
        });
        services.AddSingleton(sp =>
        {
            var paths = sp.GetRequiredService<DesktopRuntimePaths>();
            return new DesktopSessionHistoryService(paths.StorageRoot);
        });
        services.AddSingleton(sp =>
        {
            var paths = sp.GetRequiredService<DesktopRuntimePaths>();
            return new DesktopDiagnosticsBundleService(paths.StorageRoot);
        });
        services.AddSingleton(sp =>
        {
            var paths = sp.GetRequiredService<DesktopRuntimePaths>();
            var processLauncher = sp.GetRequiredService<IProcessLauncher>();
            return new DesktopSmokeCheckService(paths.StorageRoot, paths.FfmpegPath, processLauncher);
        });
        services.AddSingleton(sp =>
        {
            var paths = sp.GetRequiredService<DesktopRuntimePaths>();
            return new DesktopOnboardingService(paths.StorageRoot);
        });
        services.AddSingleton(sp =>
        {
            var paths = sp.GetRequiredService<DesktopRuntimePaths>();
            return new DesktopDemoTemplateService(paths.StorageRoot);
        });
        services.AddSingleton(sp =>
        {
            var paths = sp.GetRequiredService<DesktopRuntimePaths>();
            return new DesktopSessionRecoveryService(paths.StorageRoot);
        });
        services.AddSingleton<DesktopStartupHealthService>();

        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider(validateScopes: true);
    }
}
