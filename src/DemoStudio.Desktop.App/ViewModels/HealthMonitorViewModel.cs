using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using DemoStudio.Desktop.App.Services;
using DemoStudio.Desktop.Core.Sessions;
using Microsoft.Extensions.Logging;

namespace DemoStudio.Desktop.App.ViewModels;

public sealed class HealthMonitorViewModel : INotifyPropertyChanged
{
    private readonly DesktopDependencyHealthService _dependencyHealthService;
    private readonly DesktopPerformanceMetricsService _performanceMetricsService;
    private readonly DesktopSmokeCheckService _smokeCheckService;
    private readonly DesktopCaptureRuntime _captureRuntime;
    private readonly ILogger<HealthMonitorViewModel> _logger;
    private string _performanceSummary = "Perf: no samples yet.";
    private string _memoryWorkingSetText = "Working Set: -";
    private string _memoryPrivateText = "Private Memory: -";
    private string _captureWriteRateText = "Capture Write: -";
    private string _captureFileSizeText = "Capture Size: -";
    private string _composeLastRunText = "Compose Last Run: -";
    private long _telemetryLastBytes;
    private DateTimeOffset _telemetryLastUtc = DateTimeOffset.MinValue;
    private string _performanceHealthLabel = "Healthy";
    private string _performanceHealthDetail = "All runtime metrics are within budget.";
    private string _performanceHealthBackground = "#EAF9EE";
    private string _performanceHealthBorder = "#9BD3A9";
    private DesktopDependencyHealthSnapshot _dependencyHealthSnapshot;
    private DateTimeOffset _dependencyHealthLastRefreshUtc = DateTimeOffset.MinValue;
    private bool _dependencyHealthRefreshInFlight;
    private int _telemetryRefreshInFlight;

    public HealthMonitorViewModel(
        DesktopDependencyHealthService dependencyHealthService,
        DesktopPerformanceMetricsService performanceMetricsService,
        DesktopSmokeCheckService smokeCheckService,
        DesktopCaptureRuntime captureRuntime,
        ILogger<HealthMonitorViewModel> logger)
    {
        _dependencyHealthService = dependencyHealthService ?? throw new ArgumentNullException(nameof(dependencyHealthService));
        _performanceMetricsService = performanceMetricsService ?? throw new ArgumentNullException(nameof(performanceMetricsService));
        _smokeCheckService = smokeCheckService ?? throw new ArgumentNullException(nameof(smokeCheckService));
        _captureRuntime = captureRuntime ?? throw new ArgumentNullException(nameof(captureRuntime));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dependencyHealthSnapshot = _dependencyHealthService.Current;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string PerformanceSummary => _performanceSummary;

    public string MemoryWorkingSetText => _memoryWorkingSetText;

    public string MemoryPrivateText => _memoryPrivateText;

    public string CaptureWriteRateText => _captureWriteRateText;

    public string CaptureFileSizeText => _captureFileSizeText;

    public string ComposeLastRunText => _composeLastRunText;

    public string PerformanceHealthLabel => _performanceHealthLabel;

    public string PerformanceHealthDetail => _performanceHealthDetail;

    public string PerformanceHealthBackground => _performanceHealthBackground;

    public string PerformanceHealthBorder => _performanceHealthBorder;

    public string DependencyHealthLabel => _dependencyHealthSnapshot.IsHealthy ? "Dependencies: Healthy" : "Dependencies: Degraded";

    public string DependencyHealthDetail => _dependencyHealthSnapshot.Summary;

    public string DependencyHealthBackground => _dependencyHealthSnapshot.IsHealthy ? "#EAF9EE" : "#FDECEC";

    public string DependencyHealthBorder => _dependencyHealthSnapshot.IsHealthy ? "#9BD3A9" : "#E09A9A";

    public void RecordOperationMetric(string operationName, TimeSpan elapsed)
    {
        _performanceSummary = _performanceMetricsService.Record(operationName, elapsed);
        if (string.Equals(operationName, "ComposeVideo", StringComparison.OrdinalIgnoreCase))
        {
            _composeLastRunText = $"Compose Last Run: {elapsed:mm\\:ss}";
            OnPropertyChanged(nameof(ComposeLastRunText));
        }

        OnPropertyChanged(nameof(PerformanceSummary));
    }

    public async Task UpdateRuntimeTelemetryAsync(
        RecorderSessionState sessionState,
        string? lastOutputPath,
        CancellationToken cancellationToken,
        Func<bool> isDisposed,
        Func<Action, Task> runOnUiThreadAsync,
        Func<string, string, Exception, TimeSpan, Task> reportBackgroundFailureAsync,
        Action<string> setRuntimeMessage)
    {
        if (isDisposed())
        {
            return;
        }

        if (Interlocked.Exchange(ref _telemetryRefreshInFlight, 1) == 1)
        {
            return;
        }

        try
        {
            var dependencyTask = RefreshDependencyHealthAsync(
                false,
                cancellationToken,
                isDisposed,
                runOnUiThreadAsync,
                reportBackgroundFailureAsync,
                setRuntimeMessage);
            var snapshot = await Task.Run(() => CollectTelemetrySnapshot(sessionState, lastOutputPath), cancellationToken).ConfigureAwait(false);
            await runOnUiThreadAsync(() =>
            {
                ApplyTelemetrySnapshot(snapshot);
                UpdatePerformanceBudgetState(sessionState, snapshot.WorkingSetMb, snapshot.PrivateMb, _captureWriteRateText, _composeLastRunText);
            });
            await dependencyTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Runtime telemetry refresh failed.");
            await reportBackgroundFailureAsync(
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

    public async Task RefreshDependencyHealthAsync(
        bool force,
        CancellationToken cancellationToken,
        Func<bool> isDisposed,
        Func<Action, Task> runOnUiThreadAsync,
        Func<string, string, Exception, TimeSpan, Task> reportBackgroundFailureAsync,
        Action<string> setRuntimeMessage)
    {
        if (isDisposed())
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
            var refreshed = await _dependencyHealthService.RefreshAsync(cancellationToken).ConfigureAwait(false);
            _dependencyHealthLastRefreshUtc = now;
            await runOnUiThreadAsync(() =>
            {
                _dependencyHealthSnapshot = refreshed;
                OnPropertyChanged(nameof(DependencyHealthLabel));
                OnPropertyChanged(nameof(DependencyHealthDetail));
                OnPropertyChanged(nameof(DependencyHealthBackground));
                OnPropertyChanged(nameof(DependencyHealthBorder));

                var transitionedToDegraded = previous.IsHealthy && !_dependencyHealthSnapshot.IsHealthy;
                if (transitionedToDegraded)
                {
                    setRuntimeMessage(_dependencyHealthSnapshot.Summary);
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dependency health refresh failed.");
            await reportBackgroundFailureAsync(
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

    public async Task RunSmokeCheckAsync(
        Action<bool> setBusy,
        Action<string> setSmokeCheckStatus,
        Action<string> setLastRuntimeMessage,
        Action<string, TimeSpan> recordOperationMetric,
        Func<string, string, string?, string> buildFailureDisplay)
    {
        ArgumentNullException.ThrowIfNull(setBusy);
        ArgumentNullException.ThrowIfNull(setSmokeCheckStatus);
        ArgumentNullException.ThrowIfNull(setLastRuntimeMessage);
        ArgumentNullException.ThrowIfNull(recordOperationMetric);
        ArgumentNullException.ThrowIfNull(buildFailureDisplay);

        var stopwatch = Stopwatch.StartNew();
        setBusy(true);
        try
        {
            var running = "Running smoke check...";
            setSmokeCheckStatus(running);
            var result = await _smokeCheckService.RunAsync(3).ConfigureAwait(false);
            var status = result.Succeeded
                ? $"{result.Message} Output: {result.OutputPath}"
                : $"Smoke FAIL: {result.Message}";
            setSmokeCheckStatus(status);
            setLastRuntimeMessage(status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Smoke check failed.");
            var status = buildFailureDisplay("DS-DESK-SMOKE-001", "Smoke check failed.", ex.Message);
            setSmokeCheckStatus(status);
            setLastRuntimeMessage(status);
        }
        finally
        {
            setBusy(false);
            recordOperationMetric("SmokeCheck", stopwatch.Elapsed);
        }
    }

    private string? ResolveTelemetryCapturePath(RecorderSessionState sessionState, string? lastOutputPath)
    {
        if ((sessionState == RecorderSessionState.Recording || sessionState == RecorderSessionState.Paused)
            && !string.IsNullOrWhiteSpace(_captureRuntime.LastRawVideoPath)
            && File.Exists(_captureRuntime.LastRawVideoPath))
        {
            return _captureRuntime.LastRawVideoPath;
        }

        if (!string.IsNullOrWhiteSpace(lastOutputPath) && lastOutputPath != "-" && File.Exists(lastOutputPath))
        {
            return lastOutputPath;
        }

        return null;
    }

    private TelemetrySnapshot CollectTelemetrySnapshot(RecorderSessionState sessionState, string? lastOutputPath)
    {
        var process = Process.GetCurrentProcess();
        process.Refresh();
        var workingSetMb = process.WorkingSet64 / 1024d / 1024d;
        var privateMb = process.PrivateMemorySize64 / 1024d / 1024d;
        var memoryWorkingSetText = $"Working Set: {workingSetMb:0.0} MB";
        var memoryPrivateText = $"Private Memory: {privateMb:0.0} MB";

        var capturePath = ResolveTelemetryCapturePath(sessionState, lastOutputPath);
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

    private void UpdatePerformanceBudgetState(RecorderSessionState sessionState, double workingSetMb, double privateMb, string writeRateText, string composeRunText)
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
            if ((sessionState == RecorderSessionState.Recording || sessionState == RecorderSessionState.Paused) && writeRate < 0.05d)
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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
