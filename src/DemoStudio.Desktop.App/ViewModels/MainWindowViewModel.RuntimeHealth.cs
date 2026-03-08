using System.Diagnostics;
using System.IO;
using DemoStudio.Desktop.Core.Sessions;
using System.Windows.Threading;

namespace DemoStudio.Desktop.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private async Task UpdateRuntimeTelemetryAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        if (Interlocked.Exchange(ref _telemetryRefreshInFlight, 1) == 1)
        {
            return;
        }

        try
        {
            var dependencyTask = RefreshDependencyHealthAsync();
            var snapshot = await Task.Run(CollectTelemetrySnapshot).ConfigureAwait(false);
            await RunOnUiThreadAsync(() =>
            {
                ApplyTelemetrySnapshot(snapshot);
                UpdatePerformanceBudgetState(snapshot.WorkingSetMb, snapshot.PrivateMb, _captureWriteRateText, _composeLastRunText);
            });
            await dependencyTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await ReportBackgroundFailureAsync(
                "DS-DESK-HEALTH-001",
                "Runtime telemetry refresh failed.",
                ex,
                TimeSpan.FromSeconds(30));
        }
        finally
        {
            Interlocked.Exchange(ref _telemetryRefreshInFlight, 0);
        }
    }

    private string? ResolveTelemetryCapturePath()
    {
        if ((_snapshot.State == RecorderSessionState.Recording || _snapshot.State == RecorderSessionState.Paused)
            && !string.IsNullOrWhiteSpace(_captureRuntime.LastRawVideoPath)
            && File.Exists(_captureRuntime.LastRawVideoPath))
        {
            return _captureRuntime.LastRawVideoPath;
        }

        if (!string.IsNullOrWhiteSpace(_lastOutputPath) && _lastOutputPath != "-" && File.Exists(_lastOutputPath))
        {
            return _lastOutputPath;
        }

        return null;
    }

    private TelemetrySnapshot CollectTelemetrySnapshot()
    {
        var process = Process.GetCurrentProcess();
        process.Refresh();
        var workingSetMb = process.WorkingSet64 / 1024d / 1024d;
        var privateMb = process.PrivateMemorySize64 / 1024d / 1024d;
        var memoryWorkingSetText = $"Working Set: {workingSetMb:0.0} MB";
        var memoryPrivateText = $"Private Memory: {privateMb:0.0} MB";

        var capturePath = ResolveTelemetryCapturePath();
        if (string.IsNullOrWhiteSpace(capturePath) || !File.Exists(capturePath))
        {
            return new TelemetrySnapshot(
                workingSetMb,
                privateMb,
                memoryWorkingSetText,
                memoryPrivateText,
                "Capture Write: -",
                "Capture Size: -",
                0L,
                DateTimeOffset.MinValue);
        }

        var info = new FileInfo(capturePath);
        var now = DateTimeOffset.UtcNow;
        var bytes = Math.Max(0L, info.Length);
        var captureFileSizeText = $"Capture Size: {bytes / 1024d / 1024d:0.00} MB";
        string captureWriteRateText;
        if (_telemetryLastUtc == DateTimeOffset.MinValue)
        {
            captureWriteRateText = "Capture Write: warming up...";
        }
        else
        {
            var seconds = Math.Max(0.001d, (now - _telemetryLastUtc).TotalSeconds);
            var deltaBytes = Math.Max(0L, bytes - _telemetryLastBytes);
            captureWriteRateText = $"Capture Write: {(deltaBytes / 1024d / 1024d) / seconds:0.00} MB/s";
        }

        return new TelemetrySnapshot(
            workingSetMb,
            privateMb,
            memoryWorkingSetText,
            memoryPrivateText,
            captureWriteRateText,
            captureFileSizeText,
            bytes,
            now);
    }

    private void ApplyTelemetrySnapshot(TelemetrySnapshot snapshot)
    {
        if (!string.Equals(_memoryWorkingSetText, snapshot.MemoryWorkingSetText, StringComparison.Ordinal))
        {
            _memoryWorkingSetText = snapshot.MemoryWorkingSetText;
            OnPropertyChanged(nameof(MemoryWorkingSetText));
        }

        if (!string.Equals(_memoryPrivateText, snapshot.MemoryPrivateText, StringComparison.Ordinal))
        {
            _memoryPrivateText = snapshot.MemoryPrivateText;
            OnPropertyChanged(nameof(MemoryPrivateText));
        }

        if (!string.Equals(_captureWriteRateText, snapshot.CaptureWriteRateText, StringComparison.Ordinal))
        {
            _captureWriteRateText = snapshot.CaptureWriteRateText;
            OnPropertyChanged(nameof(CaptureWriteRateText));
        }

        if (!string.Equals(_captureFileSizeText, snapshot.CaptureFileSizeText, StringComparison.Ordinal))
        {
            _captureFileSizeText = snapshot.CaptureFileSizeText;
            OnPropertyChanged(nameof(CaptureFileSizeText));
        }

        _telemetryLastBytes = snapshot.TelemetryBytes;
        _telemetryLastUtc = snapshot.TelemetrySampleUtc;
    }

    private async Task RefreshDependencyHealthAsync(bool force = false)
    {
        if (_isDisposed)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (!force && now - _dependencyHealthLastRefreshUtc < TimeSpan.FromSeconds(30))
        {
            return;
        }

        if (_dependencyHealthRefreshInFlight)
        {
            return;
        }

        _dependencyHealthRefreshInFlight = true;
        try
        {
            var previous = _dependencyHealthSnapshot;
            var refreshed = await _dependencyHealthService.RefreshAsync(_lifecycleCancellation.Token).ConfigureAwait(false);
            _dependencyHealthLastRefreshUtc = now;
            await RunOnUiThreadAsync(() =>
            {
                _dependencyHealthSnapshot = refreshed;
                OnPropertyChanged(nameof(DependencyHealthLabel));
                OnPropertyChanged(nameof(DependencyHealthDetail));
                OnPropertyChanged(nameof(DependencyHealthBackground));
                OnPropertyChanged(nameof(DependencyHealthBorder));

                var transitionedToDegraded = previous.IsHealthy && !_dependencyHealthSnapshot.IsHealthy;
                if (transitionedToDegraded)
                {
                    _lastRuntimeMessage = _dependencyHealthSnapshot.Summary;
                    OnPropertyChanged(nameof(LastRuntimeMessage));
                }
            });
        }
        catch (Exception ex)
        {
            await ReportBackgroundFailureAsync(
                "DS-DESK-HEALTH-002",
                "Dependency health refresh failed.",
                ex,
                TimeSpan.FromSeconds(30));
        }
        finally
        {
            _dependencyHealthRefreshInFlight = false;
        }
    }

    private void UpdatePerformanceBudgetState(double workingSetMb, double privateMb, string writeRateText, string composeRunText)
    {
        var warnings = new List<string>();
        var critical = false;

        if (workingSetMb >= 1100d)
        {
            critical = true;
            warnings.Add($"Working set high ({workingSetMb:0} MB)");
        }
        else if (workingSetMb >= 800d)
        {
            warnings.Add($"Working set elevated ({workingSetMb:0} MB)");
        }

        if (privateMb >= 1400d)
        {
            critical = true;
            warnings.Add($"Private memory high ({privateMb:0} MB)");
        }
        else if (privateMb >= 1000d)
        {
            warnings.Add($"Private memory elevated ({privateMb:0} MB)");
        }

        if (TryParseWriteRate(writeRateText, out var writeRate))
        {
            if ((_snapshot.State == RecorderSessionState.Recording || _snapshot.State == RecorderSessionState.Paused) && writeRate < 0.05d)
            {
                warnings.Add($"Capture write stalled ({writeRate:0.00} MB/s)");
            }
            else if (writeRate > 60d)
            {
                critical = true;
                warnings.Add($"Capture write very high ({writeRate:0.0} MB/s)");
            }
            else if (writeRate > 35d)
            {
                warnings.Add($"Capture write elevated ({writeRate:0.0} MB/s)");
            }
        }

        if (TryParseComposeDurationSeconds(composeRunText, out var composeSeconds))
        {
            if (composeSeconds > 360d)
            {
                critical = true;
                warnings.Add($"Compose runtime very high ({composeSeconds:0}s)");
            }
            else if (composeSeconds > 180d)
            {
                warnings.Add($"Compose runtime elevated ({composeSeconds:0}s)");
            }
        }

        if (_dependencyHealthSnapshot.CheckedUtc != DateTimeOffset.MinValue)
        {
            if (!_dependencyHealthSnapshot.IsHealthy)
            {
                critical = true;
                warnings.Add($"Dependency health degraded ({_dependencyHealthSnapshot.SummaryShort})");
            }
            else if (_dependencyHealthSnapshot.Warnings.Count > 0)
            {
                warnings.Add($"Dependency warning ({_dependencyHealthSnapshot.SummaryShort})");
            }
        }

        if (critical)
        {
            _performanceHealthLabel = "Critical";
            _performanceHealthBackground = "#FDECEC";
            _performanceHealthBorder = "#E09A9A";
            _performanceHealthDetail = warnings.Count == 0
                ? "Critical threshold exceeded."
                : string.Join(" | ", warnings);
        }
        else if (warnings.Count > 0)
        {
            _performanceHealthLabel = "Watch";
            _performanceHealthBackground = "#FFF6E9";
            _performanceHealthBorder = "#E8C28A";
            _performanceHealthDetail = string.Join(" | ", warnings);
        }
        else
        {
            _performanceHealthLabel = "Healthy";
            _performanceHealthBackground = "#EAF9EE";
            _performanceHealthBorder = "#9BD3A9";
            _performanceHealthDetail = "All runtime metrics are within budget.";
        }

        OnPropertyChanged(nameof(PerformanceHealthLabel));
        OnPropertyChanged(nameof(PerformanceHealthDetail));
        OnPropertyChanged(nameof(PerformanceHealthBackground));
        OnPropertyChanged(nameof(PerformanceHealthBorder));
    }

    private static bool TryParseWriteRate(string text, out double value)
    {
        value = 0d;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var marker = "Capture Write:";
        var idx = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return false;
        }

        var tail = text[(idx + marker.Length)..].Trim();
        if (tail.StartsWith("warming", StringComparison.OrdinalIgnoreCase) || tail == "-")
        {
            return false;
        }

        var numeric = new string(tail.TakeWhile(ch => char.IsDigit(ch) || ch == '.').ToArray());
        return double.TryParse(numeric, out value);
    }

    private static bool TryParseComposeDurationSeconds(string text, out double seconds)
    {
        seconds = 0d;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var marker = "Compose Last Run:";
        var idx = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return false;
        }

        var tail = text[(idx + marker.Length)..].Trim();
        if (tail == "-" || !TimeSpan.TryParseExact(tail, @"mm\:ss", null, out var span))
        {
            return false;
        }

        seconds = span.TotalSeconds;
        return true;
    }

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
