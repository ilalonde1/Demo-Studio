using DemoStudio.Desktop.App.Services;
using DemoStudio.Desktop.App.ViewModels;
using DemoStudio.Desktop.Core.Sessions;
using DemoStudio.Desktop.Core.Time;
using Microsoft.Extensions.DependencyInjection;

namespace DemoStudio.Desktop.App;

internal static class DesktopCompositionRoot
{
    public static ServiceProvider Build()
    {
        var services = new ServiceCollection();

        var options = DesktopRecorderOptionsLoader.Load(AppContext.BaseDirectory);
        services.AddSingleton(options);
        services.AddSingleton<DesktopProcessRunner>();
        services.AddSingleton<RecorderSessionEngine>(_ => new RecorderSessionEngine(new SystemClock()));
        services.AddSingleton<DesktopCaptureRuntime>();
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
            var runtime = sp.GetRequiredService<DesktopCaptureRuntime>();
            return new DesktopRuntimeLogService(runtime.StorageRoot);
        });
        services.AddSingleton(sp =>
        {
            var runtime = sp.GetRequiredService<DesktopCaptureRuntime>();
            return new DesktopCrashReporter(runtime.StorageRoot);
        });
        services.AddSingleton(sp =>
        {
            var runtime = sp.GetRequiredService<DesktopCaptureRuntime>();
            return new DesktopLaunchProfileService(runtime.StorageRoot);
        });
        services.AddSingleton(sp =>
        {
            var runtime = sp.GetRequiredService<DesktopCaptureRuntime>();
            return new DesktopSessionHistoryService(runtime.StorageRoot);
        });
        services.AddSingleton(sp =>
        {
            var runtime = sp.GetRequiredService<DesktopCaptureRuntime>();
            return new DesktopDiagnosticsBundleService(runtime.StorageRoot);
        });
        services.AddSingleton(sp =>
        {
            var runtime = sp.GetRequiredService<DesktopCaptureRuntime>();
            return new DesktopSmokeCheckService(runtime.StorageRoot, runtime.FfmpegPath);
        });
        services.AddSingleton(sp =>
        {
            var runtime = sp.GetRequiredService<DesktopCaptureRuntime>();
            return new DesktopOnboardingService(runtime.StorageRoot);
        });
        services.AddSingleton(sp =>
        {
            var runtime = sp.GetRequiredService<DesktopCaptureRuntime>();
            return new DesktopDemoTemplateService(runtime.StorageRoot);
        });
        services.AddSingleton(sp =>
        {
            var runtime = sp.GetRequiredService<DesktopCaptureRuntime>();
            return new DesktopSessionRecoveryService(runtime.StorageRoot);
        });
        services.AddSingleton<DesktopStartupHealthService>();

        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider(validateScopes: true);
    }
}
