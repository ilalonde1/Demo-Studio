namespace DemoStudio.Desktop.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private Task UpdateRuntimeTelemetryAsync() =>
        _healthMonitor.UpdateRuntimeTelemetryAsync(
            _snapshot.State,
            _lastOutputPath,
            _lifecycleCancellation.Token,
            () => _isDisposed,
            RunOnUiThreadAsync,
            ReportBackgroundFailureAsync,
            summary =>
            {
                _lastRuntimeMessage = summary;
                OnPropertyChanged(nameof(LastRuntimeMessage));
            });

    private Task RefreshDependencyHealthAsync(bool force = false) =>
        _healthMonitor.RefreshDependencyHealthAsync(
            force,
            _lifecycleCancellation.Token,
            () => _isDisposed,
            RunOnUiThreadAsync,
            ReportBackgroundFailureAsync,
            summary =>
            {
                _lastRuntimeMessage = summary;
                OnPropertyChanged(nameof(LastRuntimeMessage));
            });

    private Task RunSmokeCheckAsync() =>
        _healthMonitor.RunSmokeCheckAsync(
            SetBusy,
            status =>
            {
                _smokeCheckStatus = status;
                OnPropertyChanged(nameof(SmokeCheckStatus));
            },
            message =>
            {
                _lastRuntimeMessage = message;
                OnPropertyChanged(nameof(LastRuntimeMessage));
            },
            RecordOperationMetric,
            BuildFailureDisplay);

    private void OnTelemetryTimerTick(object? sender, EventArgs e)
    {
        if (_isDisposed)
        {
            return;
        }

        _ = RunTelemetryTimerTickAsync();
    }

    private async Task RunTelemetryTimerTickAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        try
        {
            await UpdateRuntimeTelemetryAsync();
        }
        catch (Exception ex)
        {
            await ReportBackgroundFailureAsync(
                "DS-DESK-HEALTH-003",
                "Telemetry timer refresh failed.",
                ex,
                TimeSpan.FromSeconds(30));
        }
    }
}
