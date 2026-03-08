namespace DemoStudio.Infrastructure.Export;

using System.Globalization;
using System.Text;
using System.Text.Json;
using DemoStudio.Application.Abstractions.System;
using DemoStudio.Infrastructure.Options;
using DemoStudio.Infrastructure.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public sealed class DemoExportService : IDemoExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly IProcessLauncher _processLauncher;
    private readonly string _ffmpegPath;
    private readonly ILogger<DemoExportService> _logger;

    public DemoExportService(
        IProcessLauncher processLauncher,
        IOptions<FfmpegCaptureOptions> ffmpegOptions,
        ILogger<DemoExportService> logger)
    {
        _processLauncher = processLauncher;
        _ffmpegPath = ffmpegOptions.Value.FfmpegPath;
        _logger = logger;
    }

    public async Task<DemoExportResult> ExportAsync(DemoExportRequest request, CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<string>();

        try
        {
            if (string.IsNullOrWhiteSpace(request.OutputDirectory))
            {
                return new DemoExportResult(false, string.Empty, null, null, new[] { "Output directory is missing." });
            }

            var exportDirectory = Path.Combine(request.OutputDirectory, "export");
            var screenshotsDirectory = Path.Combine(exportDirectory, "screenshots");
            Directory.CreateDirectory(exportDirectory);
            Directory.CreateDirectory(screenshotsDirectory);

            var exportRawVideoPath = Path.Combine(exportDirectory, "demo.mp4");
            File.Copy(request.RawVideoPath, exportRawVideoPath, true);
            diagnostics.Add("Raw video copied to export bundle.");

            string? exportRedactedPath = null;
            if (!string.IsNullOrWhiteSpace(request.RedactedVideoPath) && File.Exists(request.RedactedVideoPath))
            {
                exportRedactedPath = Path.Combine(exportDirectory, "demo-redacted.mp4");
                File.Copy(request.RedactedVideoPath, exportRedactedPath, true);
                diagnostics.Add("Redacted video copied to export bundle.");
            }

            var timelineMarkers = LoadTimeline(request.TimelinePath);
            var exportTimelinePath = Path.Combine(exportDirectory, "timeline.json");
            if (File.Exists(request.TimelinePath))
            {
                File.Copy(request.TimelinePath, exportTimelinePath, true);
            }
            else
            {
                await File.WriteAllTextAsync(exportTimelinePath, "[]", Encoding.UTF8, cancellationToken);
            }

            var durationSeconds = CalculateDurationSeconds(request, timelineMarkers);
            var manifest = new DemoExportManifest(
                request.RunId,
                request.ProjectName,
                request.ApplicationType.ToString(),
                durationSeconds,
                DateTimeOffset.UtcNow,
                request.StepsExecuted,
                "demo.mp4",
                exportRedactedPath is null ? null : "demo-redacted.mp4");

            var manifestPath = Path.Combine(exportDirectory, "demo.json");
            var manifestJson = JsonSerializer.Serialize(manifest, JsonOptions);
            await File.WriteAllTextAsync(manifestPath, manifestJson, Encoding.UTF8, cancellationToken);
            diagnostics.Add("Manifest generated.");

            var summaryPath = Path.Combine(exportDirectory, "summary.md");
            var summary = BuildSummaryMarkdown(manifest, timelineMarkers);
            await File.WriteAllTextAsync(summaryPath, summary, Encoding.UTF8, cancellationToken);
            diagnostics.Add("Summary generated.");

            await ExtractScreenshotsAsync(exportRawVideoPath, screenshotsDirectory, timelineMarkers, diagnostics, cancellationToken);

            return new DemoExportResult(true, exportDirectory, manifestPath, summaryPath, diagnostics);
        }
        catch (Exception ex)
        {
            diagnostics.Add($"Export packaging failed: {ex.Message}");
            _logger.LogWarning(ex, "Demo export packaging failed for run {RunId}.", request.RunId);
            return new DemoExportResult(false, Path.Combine(request.OutputDirectory, "export"), null, null, diagnostics);
        }
    }

    internal static string BuildSummaryMarkdown(DemoExportManifest manifest, IReadOnlyCollection<TimelineEntry> timelineMarkers)
    {
        var timelineStages = timelineMarkers.Count == 0
            ? new[] { "CaptureStart", "Automation", "CaptureStop" }
            : timelineMarkers.Select(x => x.Stage);

        var builder = new StringBuilder();
        builder.AppendLine("# Demo Summary");
        builder.AppendLine();
        builder.AppendLine($"Project: {manifest.ProjectName}");
        builder.AppendLine($"Application Type: {manifest.ApplicationType}");
        builder.AppendLine($"Steps Executed: {manifest.StepsExecuted.Count}");
        builder.AppendLine($"Duration: {manifest.DurationSeconds}s");
        builder.AppendLine();
        builder.AppendLine("## Timeline");
        foreach (var stage in timelineStages)
        {
            builder.AppendLine($"- {stage}");
        }

        return builder.ToString();
    }

    private async Task ExtractScreenshotsAsync(
        string exportVideoPath,
        string screenshotsDirectory,
        IReadOnlyCollection<TimelineEntry> timelineMarkers,
        List<string> diagnostics,
        CancellationToken cancellationToken)
    {
        if (timelineMarkers.Count == 0 || string.IsNullOrWhiteSpace(_ffmpegPath))
        {
            return;
        }

        var captureStart = timelineMarkers
            .Where(x => x.Stage.Equals("CaptureStart", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.TimestampUtc)
            .DefaultIfEmpty(timelineMarkers.Min(x => x.TimestampUtc))
            .First();

        var orderedMarkers = timelineMarkers.OrderBy(x => x.TimestampUtc).ToArray();
        for (var i = 0; i < orderedMarkers.Length; i++)
        {
            var marker = orderedMarkers[i];
            var seconds = (marker.TimestampUtc - captureStart).TotalSeconds;
            if (seconds < 0)
            {
                seconds = 0;
            }

            var screenshotPath = Path.Combine(screenshotsDirectory, $"screenshot-{i + 1:D2}.png");
            var arguments =
                $"-y -i {Quote(exportVideoPath)} -ss {seconds.ToString("0.###", CultureInfo.InvariantCulture)} -frames:v 1 {Quote(screenshotPath)}";

            var result = await _processLauncher.LaunchAsync(
                new ProcessLaunchRequest(_ffmpegPath, arguments, screenshotsDirectory),
                cancellationToken);

            if (!result.Started || result.Execution?.ExitCode != 0)
            {
                diagnostics.Add($"Screenshot extraction failed at marker '{marker.Stage}'.");
            }
            else
            {
                diagnostics.Add($"Screenshot extracted for stage '{marker.Stage}'.");
            }
        }
    }

    private static IReadOnlyCollection<TimelineEntry> LoadTimeline(string timelinePath)
    {
        return TimelineFileReader.LoadFromPath(timelinePath);
    }

    private static int CalculateDurationSeconds(DemoExportRequest request, IReadOnlyCollection<TimelineEntry> timelineMarkers)
    {
        if (request.StartedAtUtc.HasValue && request.CompletedAtUtc.HasValue)
        {
            var duration = request.CompletedAtUtc.Value - request.StartedAtUtc.Value;
            return Math.Max(0, (int)Math.Round(duration.TotalSeconds, MidpointRounding.AwayFromZero));
        }

        var captureStart = timelineMarkers
            .Where(x => x.Stage.Equals("CaptureStart", StringComparison.OrdinalIgnoreCase))
            .Select(x => (DateTimeOffset?)x.TimestampUtc)
            .FirstOrDefault();
        var captureStop = timelineMarkers
            .Where(x => x.Stage.Equals("CaptureStop", StringComparison.OrdinalIgnoreCase))
            .Select(x => (DateTimeOffset?)x.TimestampUtc)
            .FirstOrDefault();

        if (captureStart.HasValue && captureStop.HasValue)
        {
            var duration = captureStop.Value - captureStart.Value;
            return Math.Max(0, (int)Math.Round(duration.TotalSeconds, MidpointRounding.AwayFromZero));
        }

        return 0;
    }

    private static string Quote(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "\"\"";
        }

        return $"\"{value.Replace("\"", "\\\"")}\"";
    }

    internal sealed record DemoExportManifest(
        Guid RunId,
        string ProjectName,
        string ApplicationType,
        int DurationSeconds,
        DateTimeOffset GeneratedUtc,
        IReadOnlyCollection<string> StepsExecuted,
        string Video,
        string? RedactedVideo);

}
