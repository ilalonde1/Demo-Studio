using System.Text;
using System.Text.Json;
using System.IO;
using DemoStudio.Desktop.App.Infrastructure;
using Microsoft.Extensions.Logging;

namespace DemoStudio.Desktop.App.Services;

internal sealed class DesktopComposeTelemetryService
{
    private readonly DesktopComposeCacheManager _cacheManager;
    private readonly ILogger _logger;

    public DesktopComposeTelemetryService(DesktopComposeCacheManager cacheManager, ILogger logger)
    {
        _cacheManager = cacheManager ?? throw new ArgumentNullException(nameof(cacheManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void WriteArtifacts(
        string curatedDirectory,
        string telemetryPath,
        string cacheDirectory,
        ComposeTelemetryEnvelope latest,
        string composeOperationId)
    {
        WriteTelemetry(telemetryPath, latest, composeOperationId);
        WriteHealthSnapshot(curatedDirectory, telemetryPath, cacheDirectory, latest, composeOperationId);
    }

    private void WriteTelemetry(string outputPath, ComposeTelemetryEnvelope envelope, string composeOperationId)
    {
        try
        {
            var line = JsonSerializer.Serialize(envelope);
            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.AppendAllText(outputPath, line + Environment.NewLine, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed writing compose telemetry to {OutputPath}. ComposeOperationId={ComposeOperationId}", outputPath, composeOperationId);
        }
    }

    private void WriteHealthSnapshot(
        string curatedDirectory,
        string telemetryPath,
        string cacheDirectory,
        ComposeTelemetryEnvelope latest,
        string composeOperationId)
    {
        try
        {
            var outputPath = DesktopStoragePaths.GetComposeHealthPath(curatedDirectory);
            var runs = ReadTelemetryRuns(telemetryPath, 200);
            var cacheStats = _cacheManager.GetStats(cacheDirectory);
            var summary = BuildTelemetrySummary(runs, latest);
            var snapshot = new ComposeHealthSnapshot(
                GeneratedUtc: DateTimeOffset.UtcNow,
                LatestRun: latest,
                Summary: summary,
                Cache: cacheStats);
            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });
            File.WriteAllText(outputPath, json, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed writing compose health snapshot for {CuratedDirectory}. ComposeOperationId={ComposeOperationId}", curatedDirectory, composeOperationId);
        }
    }

    private List<ComposeTelemetryEnvelope> ReadTelemetryRuns(string telemetryPath, int maxRuns)
    {
        var result = new List<ComposeTelemetryEnvelope>();
        if (string.IsNullOrWhiteSpace(telemetryPath) || !File.Exists(telemetryPath) || maxRuns <= 0)
        {
            return result;
        }

        try
        {
            var lines = File.ReadAllLines(telemetryPath);
            for (var i = lines.Length - 1; i >= 0 && result.Count < maxRuns; i--)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var item = JsonSerializer.Deserialize<ComposeTelemetryEnvelope>(line);
                if (item is not null)
                {
                    result.Add(item);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed reading compose telemetry runs from {TelemetryPath}.", telemetryPath);
            return new List<ComposeTelemetryEnvelope>();
        }

        result.Reverse();
        return result;
    }

    private static ComposeTelemetrySummary BuildTelemetrySummary(
        IReadOnlyList<ComposeTelemetryEnvelope> runs,
        ComposeTelemetryEnvelope latest)
    {
        var runCount = runs.Count;
        var successCount = runs.Count(x => x.Succeeded);
        var successRate = runCount == 0 ? 0d : (double)successCount / runCount;
        var elapsedSamples = runs.Select(x => (double)x.ElapsedMs).OrderBy(x => x).ToArray();
        var averageMs = elapsedSamples.Length == 0 ? 0d : elapsedSamples.Average();
        var p95Ms = Percentile(elapsedSamples, 95);

        var stageEvents = runs
            .SelectMany(x => x.Stages ?? Array.Empty<ComposeTelemetryStage>())
            .Where(x => x.Attempted)
            .ToArray();
        var cacheHitCount = stageEvents.Count(x => x.CacheHit);
        var cacheHitRate = stageEvents.Length == 0 ? 0d : (double)cacheHitCount / stageEvents.Length;

        return new ComposeTelemetrySummary(
            RunCount: runCount,
            SuccessCount: successCount,
            SuccessRate: successRate,
            AverageElapsedMs: averageMs,
            P95ElapsedMs: p95Ms,
            StageEventCount: stageEvents.Length,
            CacheHitRate: cacheHitRate,
            LastFailureCode: latest.FailureCode,
            LastFailureAtUtc: string.IsNullOrWhiteSpace(latest.FailureCode) ? null : latest.TimestampUtc);
    }

    private static double Percentile(double[] sortedSamples, int percentile)
    {
        if (sortedSamples.Length == 0)
        {
            return 0d;
        }

        if (sortedSamples.Length == 1)
        {
            return sortedSamples[0];
        }

        var position = (percentile / 100d) * (sortedSamples.Length - 1);
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper)
        {
            return sortedSamples[lower];
        }

        var weight = position - lower;
        return sortedSamples[lower] + ((sortedSamples[upper] - sortedSamples[lower]) * weight);
    }
}

internal sealed record ComposeTelemetryEnvelope(
    DateTimeOffset TimestampUtc,
    string ComposeKey,
    Guid SessionId,
    string QualityPreset,
    string ExportStyle,
    int ClipCount,
    double TotalDurationSeconds,
    bool Succeeded,
    string FailureCode,
    long ElapsedMs,
    IReadOnlyList<ComposeTelemetryStage> Stages);

internal sealed record ComposeTelemetryStage(
    string Stage,
    long ElapsedMs,
    bool Attempted,
    bool Succeeded,
    string FailureCode,
    bool CacheHit);

internal sealed record ComposeHealthSnapshot(
    DateTimeOffset GeneratedUtc,
    ComposeTelemetryEnvelope LatestRun,
    ComposeTelemetrySummary Summary,
    ComposeCacheStats Cache);

internal sealed record ComposeTelemetrySummary(
    int RunCount,
    int SuccessCount,
    double SuccessRate,
    double AverageElapsedMs,
    double P95ElapsedMs,
    int StageEventCount,
    double CacheHitRate,
    string LastFailureCode,
    DateTimeOffset? LastFailureAtUtc);
