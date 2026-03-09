using DemoStudio.Capture.Abstractions.Interfaces;
using DemoStudio.Domain.Entities;
using DemoStudio.Infrastructure.Execution;
using DemoStudio.Infrastructure.Execution.Windows;
using DemoStudio.Infrastructure.Options;
using DemoStudio.Infrastructure.Process;
using DemoStudio.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Text.Json;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("Desktop smoke runner supports Windows only.");
    return 2;
}

var argsMap = ParseArgs(args);
var iterations = GetInt(argsMap, "--iterations", 1, 1, 1000);
var seconds = GetInt(argsMap, "--seconds", 8, 2, 120);
var intervalMs = GetInt(argsMap, "--interval-ms", 250, 0, 30_000);
var startupRetries = GetInt(argsMap, "--startup-retries", 2, 0, 5);
var startupProbeTimeoutSeconds = GetInt(argsMap, "--startup-probe-timeout-seconds", 12, 2, 60);
var trendStartAt = GetInt(argsMap, "--trend-start", Math.Max(10, iterations / 5), 1, iterations);
var maxWorkingSetSlopeMbPerIteration = GetDouble(argsMap, "--max-working-set-slope", 0.05d, 0d, 10d);
var maxPrivateSlopeMbPerIteration = GetDouble(argsMap, "--max-private-slope", 0.03d, 0d, 10d);
var maxHandleSlopePerIteration = GetDouble(argsMap, "--max-handle-slope", 0.5d, 0d, 500d);
var captureMode = argsMap.TryGetValue("--mode", out var modeArg) && modeArg.Equals("Window", StringComparison.OrdinalIgnoreCase)
    ? "Window"
    : "Desktop";
var windowTitleContains = argsMap.TryGetValue("--window-title", out var windowTitleArg) && !string.IsNullOrWhiteSpace(windowTitleArg)
    ? windowTitleArg.Trim()
    : null;
var windowProcessName = argsMap.TryGetValue("--window-process", out var windowProcessArg) && !string.IsNullOrWhiteSpace(windowProcessArg)
    ? windowProcessArg.Trim()
    : null;
var windowHandleHex = argsMap.TryGetValue("--window-handle", out var windowHandleArg) && !string.IsNullOrWhiteSpace(windowHandleArg)
    ? windowHandleArg.Trim()
    : null;
var preferExactHandle = argsMap.TryGetValue("--prefer-exact-handle", out var preferHandleArg) && bool.TryParse(preferHandleArg, out var parsedPreferHandle)
    ? parsedPreferHandle
    : true;
var fallbackToDesktop = argsMap.TryGetValue("--fallback-to-desktop", out var fallbackArg) && bool.TryParse(fallbackArg, out var parsedFallback)
    ? parsedFallback
    : false;
var outputRoot = argsMap.TryGetValue("--output", out var outputArg) && !string.IsNullOrWhiteSpace(outputArg)
    ? Path.GetFullPath(outputArg)
    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DemoStudio", "RecorderDesktop", "smoke");
var ffmpegPath = argsMap.TryGetValue("--ffmpeg", out var ffmpegArg) && !string.IsNullOrWhiteSpace(ffmpegArg)
    ? ffmpegArg.Trim()
    : "ffmpeg";
var metricsPath = argsMap.TryGetValue("--metrics", out var metricsArg) && !string.IsNullOrWhiteSpace(metricsArg)
    ? Path.GetFullPath(metricsArg)
    : string.Empty;

Directory.CreateDirectory(outputRoot);
var soakId = Guid.NewGuid();
var startedUtc = DateTimeOffset.UtcNow;
var soakDirectory = Path.Combine(outputRoot, $"soak_{startedUtc:yyyyMMdd_HHmmss}_{soakId:N}");
Directory.CreateDirectory(soakDirectory);

var options = new FfmpegCaptureOptions
{
    Enabled = true,
    FfmpegPath = ffmpegPath,
    FrameRate = 30,
    VideoCodec = "libx264",
    Preset = "veryfast",
    Crf = 23,
    MaxDurationSeconds = Math.Max(seconds + 15, 30),
    CaptureMode = captureMode,
    WindowTitleContains = windowTitleContains,
    WindowProcessName = windowProcessName,
    WindowHandleHex = windowHandleHex,
    PreferExactHandle = preferExactHandle,
    FallbackToDesktop = fallbackToDesktop,
    CropEnabled = false,
    HighlightCursor = false,
    OutputFileExtension = ".mp4"
};

var captureService = new FfmpegVideoCaptureService(
    new ProcessLauncher(),
    new LocalFileStorage(soakDirectory),
    WindowLocatorFactory.CreateDefault(),
    Options.Create(options),
    NullLogger<FfmpegVideoCaptureService>.Instance);

var runMetrics = new List<SmokeIterationMetrics>(iterations);
var overallStopwatch = Stopwatch.StartNew();

Console.WriteLine(
    $"[soak] Starting soak: mode={captureMode}, iterations={iterations}, captureSeconds={seconds}, intervalMs={intervalMs}, " +
    $"startupRetries={startupRetries}, trendStart={trendStartAt}, output={soakDirectory}");
if (captureMode.Equals("Window", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine(
        $"[soak] Window selector: title='{windowTitleContains ?? "-"}' process='{windowProcessName ?? "-"}' " +
        $"handle='{windowHandleHex ?? "-"}' preferExactHandle={preferExactHandle} fallbackToDesktop={fallbackToDesktop}");
}

for (var iteration = 1; iteration <= iterations; iteration++)
{
    var process = Process.GetCurrentProcess();
    process.Refresh();

    var runId = Guid.NewGuid();
    var runStartedUtc = DateTimeOffset.UtcNow;
    var outputDirectory = Path.Combine(soakDirectory, $"{runStartedUtc:yyyyMMdd_HHmmss}_{iteration:000}_{runId:N}");
    Directory.CreateDirectory(outputDirectory);
    var rawVideoPath = Path.Combine(outputDirectory, $"smoke-{runId:D}.mp4");

    var startStopwatch = Stopwatch.StartNew();
    var stopStopwatch = new Stopwatch();
    var totalStopwatch = Stopwatch.StartNew();

    var status = "Passed";
    string? error = null;
    long fileSizeBytes = 0;

    Console.WriteLine($"[soak] Iteration {iteration}/{iterations}: start");

    try
    {
        var run = new DemoRun(Guid.NewGuid(), Guid.NewGuid(), "smoke.runner@demostudio.local");
        string? effectiveRawPath = rawVideoPath;
        var startupAttempts = Math.Max(1, startupRetries + 1);
        var startupSucceeded = false;

        for (var startupAttempt = 1; startupAttempt <= startupAttempts; startupAttempt++)
        {
            var start = await captureService.StartAsync(new CaptureStartRequest(run, outputDirectory, rawVideoPath));
            if (!start.Succeeded)
            {
                if (startupAttempt < startupAttempts)
                {
                    await Task.Delay(350);
                    continue;
                }

                status = "StartFailed";
                error = start.ErrorMessage ?? "Capture start failed.";
                throw new InvalidOperationException(error);
            }

            effectiveRawPath = string.IsNullOrWhiteSpace(start.RawVideoPath) ? rawVideoPath : start.RawVideoPath;
            if (await WaitForNonZeroFileAsync(effectiveRawPath, TimeSpan.FromSeconds(startupProbeTimeoutSeconds)))
            {
                startupSucceeded = true;
                break;
            }

            var startupStop = await captureService.StopAsync(new CaptureStopRequest(run, effectiveRawPath));
            if (!startupStop.Succeeded && startupAttempt >= startupAttempts)
            {
                status = "StartFailed";
                error = startupStop.ErrorMessage ?? "Capture startup stop failed.";
            }

            if (startupAttempt < startupAttempts)
            {
                await Task.Delay(350);
                continue;
            }
        }

        if (!startupSucceeded || string.IsNullOrWhiteSpace(effectiveRawPath))
        {
            if (string.IsNullOrWhiteSpace(error))
            {
                status = "HandshakeTimeout";
                error = "Output file did not start writing within timeout.";
            }

            throw new TimeoutException(error);
        }

        startStopwatch.Stop();
        await Task.Delay(TimeSpan.FromSeconds(seconds));

        stopStopwatch.Start();
        var stop = await captureService.StopAsync(new CaptureStopRequest(run, effectiveRawPath));
        stopStopwatch.Stop();
        if (!stop.Succeeded)
        {
            status = "StopFailed";
            error = stop.ErrorMessage ?? "Capture stop failed.";
            throw new InvalidOperationException(error);
        }

        if (!File.Exists(effectiveRawPath))
        {
            status = "MissingOutput";
            error = "Output file missing after stop.";
            throw new FileNotFoundException(error, effectiveRawPath);
        }

        fileSizeBytes = new FileInfo(effectiveRawPath).Length;
        if (fileSizeBytes <= 0)
        {
            status = "EmptyOutput";
            error = "Output file is empty after stop.";
            throw new InvalidOperationException(error);
        }
    }
    catch (Exception ex)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            error = ex.Message;
        }

        Console.Error.WriteLine($"[soak] Iteration {iteration} FAIL: {error}");
    }
    finally
    {
        if (startStopwatch.IsRunning)
        {
            startStopwatch.Stop();
        }

        if (stopStopwatch.IsRunning)
        {
            stopStopwatch.Stop();
        }

        totalStopwatch.Stop();
    }

    process.Refresh();
    var ffmpegCount = CountFfmpegProcesses();
    var metric = new SmokeIterationMetrics(
        Iteration: iteration,
        StartedUtc: runStartedUtc,
        Status: status,
        Error: error,
        StartHandshakeMs: startStopwatch.Elapsed.TotalMilliseconds,
        CaptureSeconds: seconds,
        StopMs: stopStopwatch.Elapsed.TotalMilliseconds,
        TotalMs: totalStopwatch.Elapsed.TotalMilliseconds,
        FileSizeBytes: fileSizeBytes,
        WorkingSetMb: process.WorkingSet64 / 1024d / 1024d,
        PrivateMb: process.PrivateMemorySize64 / 1024d / 1024d,
        HandleCount: process.HandleCount,
        FfmpegProcessCountAfter: ffmpegCount);
    runMetrics.Add(metric);

    Console.WriteLine($"[soak] Iteration {iteration}/{iterations}: {status} size={Math.Round(fileSizeBytes / 1024d / 1024d, 2)}MB ffmpegAfter={ffmpegCount}");

    if (intervalMs > 0)
    {
        await Task.Delay(intervalMs);
    }
}

overallStopwatch.Stop();

var passed = runMetrics.Count(x => string.Equals(x.Status, "Passed", StringComparison.OrdinalIgnoreCase));
var failed = runMetrics.Count - passed;
var ffmpegLeftovers = runMetrics.Where(x => x.FfmpegProcessCountAfter > 0).ToArray();

var trendSlice = runMetrics.Where(x => x.Iteration >= trendStartAt).ToArray();
var workingSetSlope = ComputeSlope(trendSlice.Select(x => (double)x.Iteration).ToArray(), trendSlice.Select(x => x.WorkingSetMb).ToArray());
var privateSlope = ComputeSlope(trendSlice.Select(x => (double)x.Iteration).ToArray(), trendSlice.Select(x => x.PrivateMb).ToArray());
var handleSlope = ComputeSlope(trendSlice.Select(x => (double)x.Iteration).ToArray(), trendSlice.Select(x => (double)x.HandleCount).ToArray());

var slopeBreaches = new List<string>();
if (workingSetSlope > maxWorkingSetSlopeMbPerIteration)
{
    slopeBreaches.Add($"Working-set slope {workingSetSlope:0.###} MB/iter > threshold {maxWorkingSetSlopeMbPerIteration:0.###}");
}

if (privateSlope > maxPrivateSlopeMbPerIteration)
{
    slopeBreaches.Add($"Private-memory slope {privateSlope:0.###} MB/iter > threshold {maxPrivateSlopeMbPerIteration:0.###}");
}

if (handleSlope > maxHandleSlopePerIteration)
{
    slopeBreaches.Add($"Handle slope {handleSlope:0.###}/iter > threshold {maxHandleSlopePerIteration:0.###}");
}

var summary = new SmokeSoakSummary(
    StartedUtc: startedUtc,
    CompletedUtc: DateTimeOffset.UtcNow,
    Iterations: iterations,
    Passed: passed,
    Failed: failed,
    TotalMs: overallStopwatch.Elapsed.TotalMilliseconds,
    MeanIterationMs: runMetrics.Count == 0 ? 0d : runMetrics.Average(x => x.TotalMs),
    MeanStartHandshakeMs: runMetrics.Count == 0 ? 0d : runMetrics.Average(x => x.StartHandshakeMs),
    MeanStopMs: runMetrics.Count == 0 ? 0d : runMetrics.Average(x => x.StopMs),
    MaxWorkingSetMb: runMetrics.Count == 0 ? 0d : runMetrics.Max(x => x.WorkingSetMb),
    MaxPrivateMb: runMetrics.Count == 0 ? 0d : runMetrics.Max(x => x.PrivateMb),
    MaxHandleCount: runMetrics.Count == 0 ? 0 : runMetrics.Max(x => x.HandleCount),
    AnyFfmpegLeftover: ffmpegLeftovers.Length > 0,
    TrendStartIteration: trendStartAt,
    WorkingSetSlopeMbPerIteration: workingSetSlope,
    PrivateSlopeMbPerIteration: privateSlope,
    HandleSlopePerIteration: handleSlope,
    SlopeThresholdBreaches: slopeBreaches,
    Results: runMetrics);

var resolvedMetricsPath = string.IsNullOrWhiteSpace(metricsPath)
    ? Path.Combine(soakDirectory, "soak-metrics.json")
    : metricsPath;
Directory.CreateDirectory(Path.GetDirectoryName(resolvedMetricsPath)!);
await File.WriteAllTextAsync(
    resolvedMetricsPath,
    JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));

Console.WriteLine($"[soak] Summary metrics={resolvedMetricsPath}");
Console.WriteLine($"[soak] COMPLETE passed={passed} failed={failed} ffmpegLeftover={summary.AnyFfmpegLeftover} wsSlope={workingSetSlope:0.###} privSlope={privateSlope:0.###} handleSlope={handleSlope:0.###}");

if (failed > 0 || summary.AnyFfmpegLeftover || slopeBreaches.Count > 0)
{
    foreach (var breach in slopeBreaches)
    {
        Console.Error.WriteLine($"[soak] TREND FAIL: {breach}");
    }

    return 20;
}

return 0;

static double ComputeSlope(double[] x, double[] y)
{
    if (x.Length == 0 || y.Length == 0 || x.Length != y.Length)
    {
        return 0d;
    }

    if (x.Length == 1)
    {
        return 0d;
    }

    var n = x.Length;
    var sumX = x.Sum();
    var sumY = y.Sum();
    var sumXY = 0d;
    var sumX2 = 0d;
    for (var i = 0; i < n; i++)
    {
        sumXY += x[i] * y[i];
        sumX2 += x[i] * x[i];
    }

    var denominator = (n * sumX2) - (sumX * sumX);
    if (Math.Abs(denominator) < 0.0000001d)
    {
        return 0d;
    }

    return ((n * sumXY) - (sumX * sumY)) / denominator;
}

static Dictionary<string, string> ParseArgs(string[] args)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < args.Length; i++)
    {
        var current = args[i];
        if (!current.StartsWith("--", StringComparison.Ordinal))
        {
            continue;
        }

        if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
        {
            result[current] = args[i + 1];
            i++;
        }
        else
        {
            result[current] = "true";
        }
    }

    return result;
}

static int GetInt(IReadOnlyDictionary<string, string> args, string key, int defaultValue, int min, int max)
{
    if (!args.TryGetValue(key, out var raw) || !int.TryParse(raw, out var parsed))
    {
        return defaultValue;
    }

    return Math.Clamp(parsed, min, max);
}

static double GetDouble(IReadOnlyDictionary<string, string> args, string key, double defaultValue, double min, double max)
{
    if (!args.TryGetValue(key, out var raw) || !double.TryParse(raw, out var parsed))
    {
        return defaultValue;
    }

    return Math.Clamp(parsed, min, max);
}

static int CountFfmpegProcesses()
{
    try
    {
        return Process.GetProcessesByName("ffmpeg").Length;
    }
    catch
    {
        return 0;
    }
}

static async Task<bool> WaitForNonZeroFileAsync(string path, TimeSpan timeout)
{
    var deadline = DateTimeOffset.UtcNow + timeout;
    while (DateTimeOffset.UtcNow <= deadline)
    {
        try
        {
            if (File.Exists(path))
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (stream.Length > 0)
                {
                    return true;
                }
            }
        }
        catch
        {
        }

        await Task.Delay(250);
    }

    return false;
}

internal sealed record SmokeIterationMetrics(
    int Iteration,
    DateTimeOffset StartedUtc,
    string Status,
    string? Error,
    double StartHandshakeMs,
    int CaptureSeconds,
    double StopMs,
    double TotalMs,
    long FileSizeBytes,
    double WorkingSetMb,
    double PrivateMb,
    int HandleCount,
    int FfmpegProcessCountAfter);

internal sealed record SmokeSoakSummary(
    DateTimeOffset StartedUtc,
    DateTimeOffset CompletedUtc,
    int Iterations,
    int Passed,
    int Failed,
    double TotalMs,
    double MeanIterationMs,
    double MeanStartHandshakeMs,
    double MeanStopMs,
    double MaxWorkingSetMb,
    double MaxPrivateMb,
    int MaxHandleCount,
    bool AnyFfmpegLeftover,
    int TrendStartIteration,
    double WorkingSetSlopeMbPerIteration,
    double PrivateSlopeMbPerIteration,
    double HandleSlopePerIteration,
    IReadOnlyList<string> SlopeThresholdBreaches,
    IReadOnlyList<SmokeIterationMetrics> Results);
