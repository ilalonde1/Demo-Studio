using System.Windows;
using System.Threading.Tasks;
using DemoStudio.Desktop.App.Services;
using DemoStudio.Desktop.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
namespace DemoStudio.Desktop.App;
public partial class App : System.Windows.Application
{
    private ServiceProvider? _serviceProvider;
    private MainWindow? _mainWindow;
    private RecorderHudWindow? _hudWindow;
    private DesktopCrashReporter? _crashReporter;
    private DesktopRuntimeLogService? _runtimeLog;
    private Task? _shutdownTask;

    internal DesktopRuntimeLogService? RuntimeLog => _runtimeLog;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            _serviceProvider = DesktopCompositionRoot.Build();
            _runtimeLog = _serviceProvider.GetRequiredService<DesktopRuntimeLogService>();
            _runtimeLog.Info("Desktop app startup initiated.", "AppStartup");
            _crashReporter = _serviceProvider.GetRequiredService<DesktopCrashReporter>();
            RegisterGlobalExceptionHandlers();

            var startupHealth = await _serviceProvider.GetRequiredService<DesktopStartupHealthService>().EvaluateAsync();
            if (!startupHealth.IsReady)
            {
                var details = string.Join(Environment.NewLine, startupHealth.Errors);
                _runtimeLog.Error($"Startup health gate failed.{Environment.NewLine}{details}", null, "AppStartup");
                MessageBox.Show(
                    $"DemoStudio cannot start due to critical environment issues:{Environment.NewLine}{Environment.NewLine}{details}",
                    "Startup Blocked",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(-1);
                return;
            }

            var viewModel = _serviceProvider.GetRequiredService<MainWindowViewModel>();
            _mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            _mainWindow.DataContext = viewModel;

            var initialization = await viewModel.InitializeAsync();
            if (!initialization.Succeeded)
            {
                var details = string.Join(Environment.NewLine, initialization.Failures);
                _runtimeLog.Error($"Startup initialization completed with failures.{Environment.NewLine}{details}", null, "AppStartup");
                MessageBox.Show(
                    $"DemoStudio started with limited functionality due to initialization issues:{Environment.NewLine}{Environment.NewLine}{details}",
                    "Startup Warning",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            MainWindow = _mainWindow;
            _mainWindow.Loaded += (_, _) => EnsureHudWindow(viewModel);
            _mainWindow.Closing += (_, _) => viewModel.BeginShutdown();

            _mainWindow.Show();
            _runtimeLog.Info("Main window shown.", "AppStartup");
        }
        catch (Exception ex)
        {
            _runtimeLog?.Error("Fatal startup failure.", ex, "AppStartup");
            _crashReporter?.TryWrite("App.OnStartup", ex);
            MessageBox.Show(
                $"DemoStudio failed to start: {ex.Message}",
                "Startup Failure",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            _runtimeLog?.Error("Unhandled dispatcher exception.", args.Exception, "UnhandledException");
            var crashPath = _crashReporter?.TryWrite("DispatcherUnhandledException", args.Exception);
            var detail = string.IsNullOrWhiteSpace(crashPath) ? string.Empty : $"{Environment.NewLine}Diagnostics: {crashPath}";
            MessageBox.Show(
                $"A critical error occurred and the app needs to close.{detail}",
                "DemoStudio Recorder Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = false;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            _runtimeLog?.Error("Unhandled app-domain exception.", ex, "UnhandledException");
            _crashReporter?.TryWrite("AppDomain.CurrentDomain.UnhandledException", ex);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _runtimeLog?.Error("Unobserved task exception.", args.Exception, "UnhandledException");
            _crashReporter?.TryWrite("TaskScheduler.UnobservedTaskException", args.Exception);
            args.SetObserved();
        };
    }

    private void EnsureHudWindow(MainWindowViewModel viewModel)
    {
        if (_hudWindow is not null || _mainWindow is null)
        {
            return;
        }

        _hudWindow = new RecorderHudWindow
        {
            DataContext = viewModel,
            Owner = _mainWindow
        };
        _hudWindow.Show();
        _runtimeLog?.Info("Recorder control panel shown.", "AppStartup");
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _runtimeLog?.Info($"Desktop app exiting with code {e.ApplicationExitCode}.", "AppShutdown");

        try
        {
            _shutdownTask ??= ShutdownRuntimeAsync();
            await _shutdownTask;
        }
        finally
        {
            _serviceProvider?.Dispose();
            base.OnExit(e);
        }
    }

    private async Task ShutdownRuntimeAsync()
    {
        if (_hudWindow is not null)
        {
            _hudWindow.Close();
            _hudWindow = null;
        }

        if (_mainWindow?.DataContext is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
            return;
        }

        if (_mainWindow?.DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
