using System;
using System.Globalization;
using System.IO;
using System.Diagnostics;
using System.Text;
using DemoStudio.Desktop.App.Infrastructure;
using DemoStudio.Application.Abstractions.System;
using Microsoft.Extensions.Logging;

namespace DemoStudio.Desktop.App.Services;

public sealed class DesktopVideoComposeService
{
    private static readonly TimeSpan IntroOutroTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan MinSegmentTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MaxSegmentTimeout = TimeSpan.FromMinutes(4);
    private static readonly TimeSpan MinFinalTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan MaxFinalTimeout = TimeSpan.FromMinutes(8);
    private readonly IProcessLauncher _processLauncher;
    private readonly ILogger<DesktopVideoComposeService> _logger;
    private readonly DesktopComposeCacheManager _cacheManager;
    private readonly DesktopComposeStageExecutor _stageExecutor;
    private readonly DesktopComposeTelemetryService _telemetryService;

    public DesktopVideoComposeService(IProcessLauncher processLauncher, ILogger<DesktopVideoComposeService> logger)
    {
        _processLauncher = processLauncher ?? throw new ArgumentNullException(nameof(processLauncher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cacheManager = new DesktopComposeCacheManager(_logger);
        _stageExecutor = new DesktopComposeStageExecutor(_processLauncher, _logger);
        _telemetryService = new DesktopComposeTelemetryService(_cacheManager, _logger);
    }

    public async Task<DesktopVideoComposeResult> ComposeAsync(
        DesktopComposeManifest manifest,
        string ffmpegPath,
        CancellationToken cancellationToken = default)
    {
        var composeOperationId = Guid.NewGuid().ToString("N");
        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["ComposeOperationId"] = composeOperationId,
            ["CaptureSessionId"] = manifest.SessionId,
            ["RawVideoPath"] = manifest.RawVideoPath
        });

        _logger.LogInformation("Compose workflow started for session {CaptureSessionId}.", manifest.SessionId);
        if (string.IsNullOrWhiteSpace(manifest.RawVideoPath))
        {
            return DesktopVideoComposeResult.Failure("Compose failed: raw video path is missing.");
        }

        if (!File.Exists(manifest.RawVideoPath))
        {
            return DesktopVideoComposeResult.Failure($"Compose failed: raw video file not found '{manifest.RawVideoPath}'.");
        }

        if (string.IsNullOrWhiteSpace(ffmpegPath))
        {
            return DesktopVideoComposeResult.Failure("Compose failed: FFmpeg path is missing.");
        }

        var clips = manifest.Clips
            .Where(x => x.DurationSeconds > 0.05d)
            .OrderBy(x => x.Order)
            .ToArray();
        if (clips.Length == 0)
        {
            return DesktopVideoComposeResult.Failure("Compose failed: no valid clips were selected.");
        }

        var rawDirectory = Path.GetDirectoryName(manifest.RawVideoPath);
        if (string.IsNullOrWhiteSpace(rawDirectory))
        {
            return DesktopVideoComposeResult.Failure("Compose failed: raw video directory is invalid.");
        }

        var curatedDirectory = DesktopStoragePaths.GetCuratedDirectory(manifest.RawVideoPath);
        Directory.CreateDirectory(curatedDirectory);
        var cacheDirectory = DesktopStoragePaths.GetComposeCacheDirectory(curatedDirectory);
        Directory.CreateDirectory(cacheDirectory);
        _cacheManager.Prune(cacheDirectory, DateTimeOffset.UtcNow, composeOperationId);
        var telemetryPath = DesktopStoragePaths.GetComposeTelemetryPath(curatedDirectory);
        var telemetryStages = new List<ComposeTelemetryStage>();
        var composeStopwatch = Stopwatch.StartNew();

        var runDirectory = Path.Combine(cacheDirectory, "tmp", DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(runDirectory);

        var quality = ComposeQualityProfile.Normalize(manifest.QualityPreset);
        var style = ComposeStyleProfile.Normalize(manifest.ExportStyle);
        var includeAudioTrack = clips.Any(x => x.IncludeNarration || HasNarrationFile(x));
        var totalDurationSeconds = clips.Sum(x => Math.Max(0.05d, x.DurationSeconds));
        var rawSignature = _cacheManager.BuildFileSignature(manifest.RawVideoPath);
        if (string.IsNullOrWhiteSpace(rawSignature))
        {
            return DesktopVideoComposeResult.Failure("Compose failed: raw video signature could not be read.");
        }

        var segmentPaths = new List<string>();
        var segmentKeys = new List<string>();

        var introKey = style.IntroSeconds > 0
            ? _cacheManager.HashToken($"intro|{style.Name}|{quality.Name}|{style.IntroSeconds}|{includeAudioTrack}")
            : null;

        var outroKey = style.OutroSeconds > 0
            ? _cacheManager.HashToken($"outro|{style.Name}|{quality.Name}|{style.OutroSeconds}|{includeAudioTrack}")
            : null;

        foreach (var clip in clips)
        {
            var narrationSignature = clip.IncludeNarration && HasNarrationFile(clip)
                ? _cacheManager.BuildFileSignature(clip.NarrationAudioPath!)
                : "none";
            var key = _cacheManager.HashToken(
                $"clip|{rawSignature}|{quality.Name}|{style.Name}|{clip.StartSeconds:0.###}|{clip.DurationSeconds:0.###}|{clip.BannerText}|{clip.Label}|{clip.IncludeNarration}|{narrationSignature}|{includeAudioTrack}");
            segmentKeys.Add(key);
        }

        var composeKeyBuilder = new StringBuilder()
            .Append("compose|").Append(rawSignature)
            .Append('|').Append(quality.Name)
            .Append('|').Append(style.Name)
            .Append('|').Append(includeAudioTrack);
        if (!string.IsNullOrWhiteSpace(introKey))
        {
            composeKeyBuilder.Append("|i:").Append(introKey);
        }

        for (var i = 0; i < segmentKeys.Count; i++)
        {
            composeKeyBuilder.Append("|c:").Append(segmentKeys[i]);
        }

        if (!string.IsNullOrWhiteSpace(outroKey))
        {
            composeKeyBuilder.Append("|o:").Append(outroKey);
        }

        var composeKey = _cacheManager.HashToken(composeKeyBuilder.ToString());
        var finalCachePath = Path.Combine(cacheDirectory, $"final-{composeKey}.mp4");
        var latestPath = Path.Combine(curatedDirectory, "final-latest.mp4");
        AddTelemetry(telemetryStages, "final-cache-lookup", 0, true, string.Empty, _cacheManager.IsUsableFile(finalCachePath), true);
        if (_cacheManager.IsUsableFile(finalCachePath))
        {
            File.Copy(finalCachePath, latestPath, overwrite: true);
            _cacheManager.DeleteDirectory(runDirectory, composeOperationId);
            return CompleteResult(
                DesktopVideoComposeResult.Success(latestPath, $"{quality.Name} (cache)"),
                string.Empty);
        }

        if (!Directory.Exists(cacheDirectory))
        {
            Directory.CreateDirectory(cacheDirectory);
        }

        if (!Directory.Exists(runDirectory))
        {
            Directory.CreateDirectory(runDirectory);
        }

        var composeFailed = false;
        try
        {
            if (!string.IsNullOrWhiteSpace(introKey))
            {
                var introPath = Path.Combine(cacheDirectory, $"intro-{introKey}.mp4");
                if (!_cacheManager.IsUsableFile(introPath))
                {
                    var introArgs = BuildSlateArgs(introPath, quality, style, "DemoStudio", "Portfolio Demo", style.IntroSeconds, includeAudioTrack);
                    var intro = await _stageExecutor.RunAsync(
                        ffmpegPath,
                        introArgs,
                        runDirectory,
                        "intro slate",
                        IntroOutroTimeout,
                        composeOperationId,
                        cancellationToken);
                    if (!intro.Succeeded || !_cacheManager.IsUsableFile(introPath))
                    {
                        composeFailed = true;
                        AddTelemetry(telemetryStages, "intro-slate", intro.ElapsedMs, true, intro.FailureCode, false, false);
                        return CompleteResult(
                            DesktopVideoComposeResult.Failure($"[{intro.FailureCode}] Compose failed creating intro slate. {TrimError(intro.Message)}"),
                            intro.FailureCode);
                    }

                    AddTelemetry(telemetryStages, "intro-slate", intro.ElapsedMs, true, string.Empty, true, false);
                }
                else
                {
                    AddTelemetry(telemetryStages, "intro-slate", 0, true, string.Empty, true, true);
                }

                segmentPaths.Add(introPath);
            }

            for (var i = 0; i < clips.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var clip = clips[i];
                var segmentKey = segmentKeys[i];
                var segmentPath = Path.Combine(cacheDirectory, $"clip-{segmentKey}.mp4");
                if (!_cacheManager.IsUsableFile(segmentPath))
                {
                    var segmentArgs = BuildSegmentArgs(manifest.RawVideoPath, segmentPath, clip, quality, style, includeAudioTrack);
                    var segmentTimeout = ComputeSegmentTimeout(clip.DurationSeconds);
                    var segment = await _stageExecutor.RunAsync(
                        ffmpegPath,
                        segmentArgs,
                        runDirectory,
                        $"segment {i + 1}",
                        segmentTimeout,
                        composeOperationId,
                        cancellationToken);
                    if (!segment.Succeeded)
                    {
                        composeFailed = true;
                        AddTelemetry(telemetryStages, $"segment-{i + 1:000}", segment.ElapsedMs, true, segment.FailureCode, false, false);
                        return CompleteResult(
                            DesktopVideoComposeResult.Failure($"[{segment.FailureCode}] Compose failed creating segment {i + 1}. {TrimError(segment.Message)}"),
                            segment.FailureCode);
                    }

                    if (!_cacheManager.IsUsableFile(segmentPath))
                    {
                        composeFailed = true;
                        AddTelemetry(telemetryStages, $"segment-{i + 1:000}", segment.ElapsedMs, true, "DS-COMP-OUTFILE", false, false);
                        return CompleteResult(
                            DesktopVideoComposeResult.Failure("[DS-COMP-OUTFILE] Compose failed creating segment output: file was not generated."),
                            "DS-COMP-OUTFILE");
                    }

                    AddTelemetry(telemetryStages, $"segment-{i + 1:000}", segment.ElapsedMs, true, string.Empty, true, false);
                }
                else
                {
                    AddTelemetry(telemetryStages, $"segment-{i + 1:000}", 0, true, string.Empty, true, true);
                }

                segmentPaths.Add(segmentPath);
            }

            if (!string.IsNullOrWhiteSpace(outroKey))
            {
                var outroPath = Path.Combine(cacheDirectory, $"outro-{outroKey}.mp4");
                if (!_cacheManager.IsUsableFile(outroPath))
                {
                    var outroArgs = BuildSlateArgs(outroPath, quality, style, "Thanks for watching", "Built with DemoStudio", style.OutroSeconds, includeAudioTrack);
                    var outro = await _stageExecutor.RunAsync(
                        ffmpegPath,
                        outroArgs,
                        runDirectory,
                        "outro slate",
                        IntroOutroTimeout,
                        composeOperationId,
                        cancellationToken);
                    if (!outro.Succeeded || !_cacheManager.IsUsableFile(outroPath))
                    {
                        composeFailed = true;
                        AddTelemetry(telemetryStages, "outro-slate", outro.ElapsedMs, true, outro.FailureCode, false, false);
                        return CompleteResult(
                            DesktopVideoComposeResult.Failure($"[{outro.FailureCode}] Compose failed creating outro slate. {TrimError(outro.Message)}"),
                            outro.FailureCode);
                    }

                    AddTelemetry(telemetryStages, "outro-slate", outro.ElapsedMs, true, string.Empty, true, false);
                }
                else
                {
                    AddTelemetry(telemetryStages, "outro-slate", 0, true, string.Empty, true, true);
                }

                segmentPaths.Add(outroPath);
            }

            var concatListPath = Path.Combine(runDirectory, "concat-list.txt");
            var concatBuilder = new StringBuilder();
            foreach (var segmentPath in segmentPaths)
            {
                concatBuilder.Append("file '")
                    .Append(segmentPath.Replace("'", "''"))
                    .AppendLine("'");
            }
            File.WriteAllText(concatListPath, concatBuilder.ToString(), Encoding.UTF8);

            var finalPath = Path.Combine(runDirectory, "final.mp4");
            var concatArgs = includeAudioTrack
                ? new List<string>
                {
                    "-y",
                    "-f",
                    "concat",
                    "-safe",
                    "0",
                    "-i",
                    concatListPath,
                    "-c:v",
                    "libx264",
                    "-preset",
                    quality.Preset,
                    "-crf",
                    quality.Crf.ToString(CultureInfo.InvariantCulture),
                    "-c:a",
                    "aac",
                    "-b:a",
                    "160k",
                    "-pix_fmt",
                    "yuv420p",
                    finalPath
                }
                : new List<string>
                {
                    "-y",
                    "-f",
                    "concat",
                    "-safe",
                    "0",
                    "-i",
                    concatListPath,
                    "-an",
                    "-c:v",
                    "libx264",
                    "-preset",
                    quality.Preset,
                    "-crf",
                    quality.Crf.ToString(CultureInfo.InvariantCulture),
                    "-pix_fmt",
                    "yuv420p",
                    finalPath
                };
            var compose = await _stageExecutor.RunAsync(
                ffmpegPath,
                concatArgs,
                runDirectory,
                "final render",
                ComputeFinalTimeout(totalDurationSeconds),
                composeOperationId,
                cancellationToken);
            if (!compose.Succeeded || !_cacheManager.IsUsableFile(finalPath))
            {
                composeFailed = true;
                AddTelemetry(telemetryStages, "final-render", compose.ElapsedMs, true, compose.FailureCode, false, false);
                return CompleteResult(
                    DesktopVideoComposeResult.Failure($"[{compose.FailureCode}] Compose failed during final render. {TrimError(compose.Message)}"),
                    compose.FailureCode);
            }

            AddTelemetry(telemetryStages, "final-render", compose.ElapsedMs, true, string.Empty, true, false);

            File.Copy(finalPath, finalCachePath, overwrite: true);
            File.Copy(finalPath, latestPath, overwrite: true);
            return CompleteResult(
                DesktopVideoComposeResult.Success(latestPath, quality.Name),
                string.Empty);
        }
        finally
        {
            if (!composeFailed)
            {
                _cacheManager.DeleteDirectory(runDirectory, composeOperationId);
            }
        }

        DesktopVideoComposeResult CompleteResult(DesktopVideoComposeResult result, string failureCode)
        {
            var envelope = new ComposeTelemetryEnvelope(
                TimestampUtc: DateTimeOffset.UtcNow,
                ComposeKey: composeKey,
                SessionId: manifest.SessionId,
                QualityPreset: quality.Name,
                ExportStyle: style.Name,
                ClipCount: clips.Length,
                TotalDurationSeconds: totalDurationSeconds,
                Succeeded: result.Succeeded,
                FailureCode: failureCode,
                ElapsedMs: composeStopwatch.ElapsedMilliseconds,
                Stages: telemetryStages);
            _telemetryService.WriteArtifacts(curatedDirectory, telemetryPath, cacheDirectory, envelope, composeOperationId);
            if (result.Succeeded)
            {
                _logger.LogInformation("Compose workflow completed successfully. OutputPath={OutputPath}", result.OutputPath);
            }
            else
            {
                _logger.LogWarning("Compose workflow completed with failure code {FailureCode}. Message={Message}", failureCode, result.Message);
            }

            return result;
        }
    }

    private static IReadOnlyList<string> BuildSegmentArgs(
        string rawPath,
        string outputPath,
        DesktopComposeClip clip,
        ComposeQualityProfile quality,
        ComposeStyleProfile style,
        bool includeAudioTrack)
    {
        var start = clip.StartSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        var duration = clip.DurationSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        var banner = string.IsNullOrWhiteSpace(clip.BannerText) ? clip.Label : clip.BannerText!;
        var vf = BuildVideoFilter(banner, style);
        if (includeAudioTrack)
        {
            if (clip.IncludeNarration && HasNarrationFile(clip))
            {
                return new[]
                {
                    "-y",
                    "-ss",
                    start,
                    "-t",
                    duration,
                    "-i",
                    rawPath,
                    "-stream_loop",
                    "-1",
                    "-i",
                    clip.NarrationAudioPath!,
                    "-vf",
                    vf,
                    "-map",
                    "0:v:0",
                    "-map",
                    "1:a:0",
                    "-t",
                    duration,
                    "-c:v",
                    "libx264",
                    "-preset",
                    quality.Preset,
                    "-crf",
                    quality.Crf.ToString(CultureInfo.InvariantCulture),
                    "-c:a",
                    "aac",
                    "-b:a",
                    "160k",
                    "-pix_fmt",
                    "yuv420p",
                    outputPath
                };
            }

            var args = new List<string>
            {
                "-y",
                "-ss",
                start,
                "-t",
                duration,
                "-i",
                rawPath,
                "-vf",
                vf
            };
            if (!clip.IncludeNarration)
            {
                args.Add("-af");
                args.Add("volume=0");
            }

            args.AddRange(new[]
            {
                "-c:v",
                "libx264",
                "-preset",
                quality.Preset,
                "-crf",
                quality.Crf.ToString(CultureInfo.InvariantCulture),
                "-map",
                "0:v:0",
                "-map",
                "0:a:0?",
                "-c:a",
                "aac",
                "-b:a",
                "160k",
                "-pix_fmt",
                "yuv420p",
                outputPath
            });
            return args;
        }

        return new[]
        {
            "-y",
            "-ss",
            start,
            "-t",
            duration,
            "-i",
            rawPath,
            "-an",
            "-vf",
            vf,
            "-c:v",
            "libx264",
            "-preset",
            quality.Preset,
            "-crf",
            quality.Crf.ToString(CultureInfo.InvariantCulture),
            "-pix_fmt",
            "yuv420p",
            outputPath
        };
    }

    private static IReadOnlyList<string> BuildSlateArgs(
        string outputPath,
        ComposeQualityProfile quality,
        ComposeStyleProfile style,
        string title,
        string subtitle,
        int durationSeconds,
        bool includeAudioTrack)
    {
        var safeTitle = EscapeDrawText(title);
        var safeSubtitle = EscapeDrawText(subtitle);
        var font = style.FontPath;
        var drawTitle = $"drawtext=fontfile='{font}':text='{safeTitle}':fontcolor={style.FontColor}:fontsize={style.TitleSize}:x=(w-text_w)/2:y=(h/2)-70";
        var drawSub = $"drawtext=fontfile='{font}':text='{safeSubtitle}':fontcolor={style.SubColor}:fontsize={style.SubtitleSize}:x=(w-text_w)/2:y=(h/2)+10";
        var vf = $"{drawTitle},{drawSub},scale=trunc(iw/2)*2:trunc(ih/2)*2";
        var args = new List<string>
        {
            "-y",
            "-f",
            "lavfi",
            "-t",
            durationSeconds.ToString(CultureInfo.InvariantCulture),
            "-i",
            $"color=c={style.SlateColor}:s=1920x1080"
        };
        if (includeAudioTrack)
        {
            args.AddRange(new[]
            {
                "-f",
                "lavfi",
                "-t",
                durationSeconds.ToString(CultureInfo.InvariantCulture),
                "-i",
                "anullsrc=channel_layout=mono:sample_rate=44100",
                "-map",
                "0:v:0",
                "-map",
                "1:a:0",
                "-c:a",
                "aac",
                "-b:a",
                "160k"
            });
        }
        else
        {
            args.Add("-an");
        }

        args.AddRange(new[]
        {
            "-vf",
            vf,
            "-c:v",
            "libx264",
            "-preset",
            quality.Preset,
            "-crf",
            quality.Crf.ToString(CultureInfo.InvariantCulture),
            "-pix_fmt",
            "yuv420p",
            outputPath
        });
        return args;
    }

    private static string BuildVideoFilter(string? bannerText, ComposeStyleProfile style)
    {
        const string evenScale = "scale=trunc(iw/2)*2:trunc(ih/2)*2";
        if (string.IsNullOrWhiteSpace(bannerText))
        {
            return evenScale;
        }

        var safe = EscapeDrawText(bannerText.Trim());
        var font = style.FontPath;
        var drawText =
            $"drawtext=fontfile='{font}':text='{safe}':fontcolor={style.FontColor}:fontsize={style.LowerThirdSize}:box=1:boxcolor={style.LowerThirdBoxColor}:boxborderw=10:x=(w-text_w)/2:y=h-(text_h*2)";
        return $"{drawText},{evenScale}";
    }

    private static string EscapeDrawText(string text)
    {
        return text
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace(":", "\\:", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal)
            .Replace(",", "\\,", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal);
    }

    private static bool HasNarrationFile(DesktopComposeClip clip)
        => !string.IsNullOrWhiteSpace(clip.NarrationAudioPath) && File.Exists(clip.NarrationAudioPath);

    private static string TrimError(string? stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
        {
            return string.Empty;
        }

        var lines = stderr
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        if (lines.Length == 0)
        {
            return string.Empty;
        }

        static bool IsActionable(string line)
            => line.Contains("error", StringComparison.OrdinalIgnoreCase)
               || line.Contains("invalid", StringComparison.OrdinalIgnoreCase)
               || line.Contains("not found", StringComparison.OrdinalIgnoreCase)
               || line.Contains("failed", StringComparison.OrdinalIgnoreCase)
               || line.Contains("cannot", StringComparison.OrdinalIgnoreCase);

        var actionable = lines.LastOrDefault(IsActionable) ?? lines.Last();
        var compact = actionable.Replace(Environment.NewLine, " ", StringComparison.Ordinal).Trim();
        return compact.Length <= 260 ? compact : compact[..260];
    }

    private static TimeSpan ComputeSegmentTimeout(double durationSeconds)
    {
        var computed = TimeSpan.FromSeconds(Math.Max(10, durationSeconds * 6));
        if (computed < MinSegmentTimeout)
        {
            return MinSegmentTimeout;
        }

        return computed > MaxSegmentTimeout ? MaxSegmentTimeout : computed;
    }

    private static TimeSpan ComputeFinalTimeout(double totalDurationSeconds)
    {
        var computed = TimeSpan.FromSeconds(Math.Max(30, totalDurationSeconds * 4));
        if (computed < MinFinalTimeout)
        {
            return MinFinalTimeout;
        }

        return computed > MaxFinalTimeout ? MaxFinalTimeout : computed;
    }

    private static void AddTelemetry(
        ICollection<ComposeTelemetryStage> stages,
        string stage,
        long elapsedMs,
        bool attempted,
        string failureCode,
        bool succeeded,
        bool cacheHit)
    {
        stages.Add(new ComposeTelemetryStage(
            Stage: stage,
            ElapsedMs: elapsedMs,
            Attempted: attempted,
            Succeeded: succeeded,
            FailureCode: failureCode,
            CacheHit: cacheHit));
    }
}

public sealed record DesktopVideoComposeResult(bool Succeeded, string Message, string? OutputPath)
{
    public static DesktopVideoComposeResult Success(string outputPath, string preset)
        => new(true, $"Compose succeeded ({preset}): {outputPath}", outputPath);

    public static DesktopVideoComposeResult Failure(string message)
        => new(false, message, null);
}
