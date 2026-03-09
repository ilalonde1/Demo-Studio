using DemoStudio.Infrastructure.Execution.Windows;

namespace DemoStudio.Desktop.App.Services;

public sealed class DesktopCaptureWatchdogCoordinator : IDisposable
{
    private readonly IWindowLocator _windowLocator;
    private CancellationTokenSource? _cancellation;
    private Task? _watchdogTask;
    private bool _stopTriggered;

    public DesktopCaptureWatchdogCoordinator()
        : this(new DesktopWindowLocator())
    {
    }

    public DesktopCaptureWatchdogCoordinator(IWindowLocator windowLocator)
    {
        _windowLocator = windowLocator ?? throw new ArgumentNullException(nameof(windowLocator));
    }

    public void Start(
        Func<CaptureTargetSettings> targetSettingsProvider,
        Func<string, Task> reportMessageAsync,
        Func<IntPtr, Task> applyReacquiredHandleAsync,
        Func<string, Task> stopCaptureAsync)
    {
        if (targetSettingsProvider is null || reportMessageAsync is null || applyReacquiredHandleAsync is null || stopCaptureAsync is null)
        {
            return;
        }

        _stopTriggered = false;
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;
        _watchdogTask = Task.Run(
            () => RunAsync(targetSettingsProvider, reportMessageAsync, applyReacquiredHandleAsync, stopCaptureAsync, token),
            token);
    }

    public async Task StopAsync()
    {
        _cancellation?.Cancel();
        if (_watchdogTask is not null)
        {
            var currentTaskId = Task.CurrentId;
            if (!currentTaskId.HasValue || _watchdogTask.Id != currentTaskId.Value)
            {
                try
                {
                    await _watchdogTask;
                }
                catch (OperationCanceledException)
                {
                }
            }
        }

        _watchdogTask = null;
        _cancellation?.Dispose();
        _cancellation = null;
    }

    public void Dispose()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
    }

    private async Task RunAsync(
        Func<CaptureTargetSettings> targetSettingsProvider,
        Func<string, Task> reportMessageAsync,
        Func<IntPtr, Task> applyReacquiredHandleAsync,
        Func<string, Task> stopCaptureAsync,
        CancellationToken cancellationToken)
    {
        var consecutiveMisses = 0;
        const int warningThreshold = 3;
        const int stopThreshold = 8;
        var warningShown = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var current = targetSettingsProvider();
                var preferExactHandle = !string.IsNullOrWhiteSpace(current.WindowHandleHex);
                var titleForLookup = preferExactHandle ? null : current.WindowTitleContains;
                var processForLookup = current.WindowProcessName;
                var locate = await _windowLocator.FindAsync(
                    new WindowLocatorRequest(
                        titleForLookup,
                        TitleRegex: null,
                        processForLookup,
                        current.WindowHandleHex,
                        PreferExactHandle: preferExactHandle),
                    cancellationToken);

                if (locate.Found)
                {
                    consecutiveMisses = 0;
                    warningShown = false;
                }
                else if (!string.IsNullOrWhiteSpace(current.WindowHandleHex))
                {
                    var reacquire = await _windowLocator.FindAsync(
                        new WindowLocatorRequest(
                            TitleContains: null,
                            TitleRegex: null,
                            ProcessName: current.WindowProcessName,
                            HandleHex: null,
                            PreferExactHandle: false),
                        cancellationToken);
                    if (reacquire.Found)
                    {
                        await applyReacquiredHandleAsync(reacquire.Handle);
                        await reportMessageAsync("Watchdog auto-reacquired target window handle.");
                        consecutiveMisses = 0;
                        warningShown = false;
                    }
                    else
                    {
                        consecutiveMisses++;
                    }
                }
                else
                {
                    consecutiveMisses++;
                }

                if (consecutiveMisses >= warningThreshold && !warningShown)
                {
                    warningShown = true;
                    await reportMessageAsync("Watchdog warning: target window is temporarily unavailable. Trying to recover.");
                }

                if (consecutiveMisses >= stopThreshold)
                {
                    if (_stopTriggered)
                    {
                        return;
                    }

                    _stopTriggered = true;
                    await stopCaptureAsync("Watchdog stopped recording: target window became unavailable.");
                    return;
                }

                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                await reportMessageAsync($"Watchdog warning: {ex.Message}");
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
    }
}
