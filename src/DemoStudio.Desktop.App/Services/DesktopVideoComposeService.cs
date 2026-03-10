using System;
using System.Globalization;
using System.IO;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using DemoStudio.Application.Abstractions.System;
using DemoStudio.Desktop.App.Infrastructure;

namespace DemoStudio.Desktop.App.Services;

public sealed class DesktopVideoComposeService
{
    private static readonly TimeSpan IntroOutroTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan MinSegmentTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MaxSegmentTimeout = TimeSpan.FromMinutes(4);
    private static readonly TimeSpan MinFinalTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan MaxFinalTimeout = TimeSpan.FromMinutes(8);
    private static readonly TimeSpan CacheMaxAge = TimeSpan.FromDays(14);
    private static readonly TimeSpan CachePruneInterval = TimeSpan.FromMinutes(30);
    private const long CacheMaxBytes = 2L * 1024 * 1024 * 1024; // 2 GB
    private const long CacheTrimTargetBytes = (long)(CacheMaxBytes * 0.85); // trim below 85%
    private readonly IProcessLauncher _processLauncher;

    public DesktopVideoComposeService(IProcessLauncher processLauncher)
    {
        _processLauncher = processLauncher ?? throw new ArgumentNullException(nameof(processLauncher));
    }

    public async Task<DesktopVideoComposeResult> ComposeAsync(
        DesktopComposeManifest manifest,
        string ffmpegPath,
        CancellationToken cancellationToken = default)
    {
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
        TryPruneComposeCache(cacheDirectory, DateTimeOffset.UtcNow);
        var telemetryPath = DesktopStoragePaths.GetComposeTelemetryPath(curatedDirectory);
        var telemetryStages = new List<ComposeTelemetryStage>();
        var composeStopwatch = Stopwatch.StartNew();

        var runDirectory = Path.Combine(cacheDirectory, "tmp", DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(runDirectory);

        var quality = ComposeQualityProfile.Normalize(manifest.QualityPreset);
        var style = ComposeStyleProfile.Normalize(manifest.ExportStyle);
        var includeAudioTrack = clips.Any(x => x.IncludeNarration || HasNarrationFile(x));
        var totalDurationSeconds = clips.Sum(x => Math.Max(0.05d, x.DurationSeconds));
        var rawSignature = BuildFileSignature(manifest.RawVideoPath);
        if (string.IsNullOrWhiteSpace(rawSignature))
        {
            return DesktopVideoComposeResult.Failure("Compose failed: raw video signature could not be read.");
        }

        var segmentPaths = new List<string>();
        var segmentKeys = new List<string>();

        var introKey = style.IntroSeconds > 0
            ? HashToken($"intro|{style.Name}|{quality.Name}|{style.IntroSeconds}|{includeAudioTrack}")
            : null;

        var outroKey = style.OutroSeconds > 0
            ? HashToken($"outro|{style.Name}|{quality.Name}|{style.OutroSeconds}|{includeAudioTrack}")
            : null;

        foreach (var clip in clips)
        {
            var narrationSignature = clip.IncludeNarration && HasNarrationFile(clip)
                ? BuildFileSignature(clip.NarrationAudioPath!)
                : "none";
            var key = HashToken(
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

        var composeKey = HashToken(composeKeyBuilder.ToString());
        var finalCachePath = Path.Combine(cacheDirectory, $"final-{composeKey}.mp4");
        var latestPath = Path.Combine(curatedDirectory, "final-latest.mp4");
        AddTelemetry(telemetryStages, "final-cache-lookup", 0, true, string.Empty, IsUsableFile(finalCachePath), true);
        if (IsUsableFile(finalCachePath))
        {
            File.Copy(finalCachePath, latestPath, overwrite: true);
            TryDeleteDirectory(runDirectory);
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
                if (!IsUsableFile(introPath))
                {
                    var introArgs = BuildSlateArgs(introPath, quality, style, "DemoStudio", "Portfolio Demo", style.IntroSeconds, includeAudioTrack);
                    var intro = await RunFfmpegStageAsync(
                        ffmpegPath,
                        introArgs,
                        runDirectory,
                        "intro slate",
                        IntroOutroTimeout,
                        cancellationToken);
                    if (!intro.Succeeded || !IsUsableFile(introPath))
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
                if (!IsUsableFile(segmentPath))
                {
                    var segmentArgs = BuildSegmentArgs(manifest.RawVideoPath, segmentPath, clip, quality, style, includeAudioTrack);
                    var segmentTimeout = ComputeSegmentTimeout(clip.DurationSeconds);
                    var segment = await RunFfmpegStageAsync(
                        ffmpegPath,
                        segmentArgs,
                        runDirectory,
                        $"segment {i + 1}",
                        segmentTimeout,
                        cancellationToken);
                    if (!segment.Succeeded)
                    {
                        composeFailed = true;
                        AddTelemetry(telemetryStages, $"segment-{i + 1:000}", segment.ElapsedMs, true, segment.FailureCode, false, false);
                        return CompleteResult(
                            DesktopVideoComposeResult.Failure($"[{segment.FailureCode}] Compose failed creating segment {i + 1}. {TrimError(segment.Message)}"),
                            segment.FailureCode);
                    }

                    if (!IsUsableFile(segmentPath))
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
                if (!IsUsableFile(outroPath))
                {
                    var outroArgs = BuildSlateArgs(outroPath, quality, style, "Thanks for watching", "Built with DemoStudio", style.OutroSeconds, includeAudioTrack);
                    var outro = await RunFfmpegStageAsync(
                        ffmpegPath,
                        outroArgs,
                        runDirectory,
                        "outro slate",
                        IntroOutroTimeout,
                        cancellationToken);
                    if (!outro.Succeeded || !IsUsableFile(outroPath))
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
                ? $"-y -f concat -safe 0 -i {Quote(concatListPath)} -c:v libx264 -preset {quality.Preset} -crf {quality.Crf.ToString(CultureInfo.InvariantCulture)} -c:a aac -b:a 160k -pix_fmt yuv420p {Quote(finalPath)}"
                : $"-y -f concat -safe 0 -i {Quote(concatListPath)} -an -c:v libx264 -preset {quality.Preset} -crf {quality.Crf.ToString(CultureInfo.InvariantCulture)} -pix_fmt yuv420p {Quote(finalPath)}";
            var compose = await RunFfmpegStageAsync(
                ffmpegPath,
                concatArgs,
                runDirectory,
                "final render",
                ComputeFinalTimeout(totalDurationSeconds),
                cancellationToken);
            if (!compose.Succeeded || !IsUsableFile(finalPath))
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
                TryDeleteDirectory(runDirectory);
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
            TryWriteTelemetry(
                telemetryPath,
                envelope);
            TryWriteHealthSnapshot(curatedDirectory, telemetryPath, cacheDirectory, envelope);
            return result;
        }
    }

    private static string BuildSegmentArgs(
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
                return
                    $"-y -ss {start} -t {duration} -i {Quote(rawPath)} -stream_loop -1 -i {Quote(clip.NarrationAudioPath!)} -vf {Quote(vf)} -map 0:v:0 -map 1:a:0 -t {duration} -c:v libx264 -preset {quality.Preset} -crf {quality.Crf.ToString(CultureInfo.InvariantCulture)} -c:a aac -b:a 160k -pix_fmt yuv420p {Quote(outputPath)}";
            }

            var audioFilter = clip.IncludeNarration ? null : "volume=0";
            var afArg = string.IsNullOrWhiteSpace(audioFilter) ? string.Empty : $" -af {Quote(audioFilter)}";
            return
                $"-y -ss {start} -t {duration} -i {Quote(rawPath)} -vf {Quote(vf)}{afArg} -c:v libx264 -preset {quality.Preset} -crf {quality.Crf.ToString(CultureInfo.InvariantCulture)} -map 0:v:0 -map 0:a:0? -c:a aac -b:a 160k -pix_fmt yuv420p {Quote(outputPath)}";
        }

        return
            $"-y -ss {start} -t {duration} -i {Quote(rawPath)} -an -vf {Quote(vf)} -c:v libx264 -preset {quality.Preset} -crf {quality.Crf.ToString(CultureInfo.InvariantCulture)} -pix_fmt yuv420p {Quote(outputPath)}";
    }

    private static string BuildSlateArgs(
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
        var audio = includeAudioTrack
            ? "-f lavfi -t " + durationSeconds.ToString(CultureInfo.InvariantCulture) + " -i anullsrc=channel_layout=mono:sample_rate=44100 -map 0:v:0 -map 1:a:0 -c:a aac -b:a 160k"
            : "-an";
        return $"-y -f lavfi -t {durationSeconds.ToString(CultureInfo.InvariantCulture)} -i color=c={style.SlateColor}:s=1920x1080 {audio} -vf {Quote(vf)} -c:v libx264 -preset {quality.Preset} -crf {quality.Crf.ToString(CultureInfo.InvariantCulture)} -pix_fmt yuv420p {Quote(outputPath)}";
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

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private static bool HasNarrationFile(DesktopComposeClip clip)
        => !string.IsNullOrWhiteSpace(clip.NarrationAudioPath) && File.Exists(clip.NarrationAudioPath);

    private static bool IsUsableFile(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var info = new FileInfo(path);
            return info.Length > 0;
        }
        catch (Exception)
        {
            // Best-effort operation. Failure here is intentionally swallowed because the
            // caller observes the safe fallback behavior instead.
            return false;
        }
    }

    private static string BuildFileSignature(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return string.Empty;
            }

            return $"{path.ToLowerInvariant()}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        }
        catch (Exception)
        {
            // Best-effort operation. Failure here is intentionally swallowed because the
            // caller observes the safe fallback behavior instead.
            return string.Empty;
        }
    }

    private static string HashToken(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void TryDeleteDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch (Exception)
        {
            // Best-effort operation. Failure here is intentionally swallowed because the
            // caller observes the safe fallback behavior instead.
        }
    }

    private static void TryPruneComposeCache(string cacheDirectory, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(cacheDirectory) || !Directory.Exists(cacheDirectory))
        {
            return;
        }

        try
        {
            var stampPath = Path.Combine(cacheDirectory, ".prune.stamp");
            if (File.Exists(stampPath))
            {
                var stampAge = nowUtc - File.GetLastWriteTimeUtc(stampPath);
                if (stampAge < CachePruneInterval)
                {
                    return;
                }
            }

            var expirationUtc = nowUtc - CacheMaxAge;
            var root = new DirectoryInfo(cacheDirectory);
            var files = root
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Where(file => !file.FullName.Contains($"{Path.DirectorySeparatorChar}tmp{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .Where(file => !string.Equals(file.Name, ".prune.stamp", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var file in files)
            {
                if (file.LastWriteTimeUtc < expirationUtc.UtcDateTime)
                {
                    TryDeleteFile(file.FullName);
                }
            }

            files = root
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Where(file => !file.FullName.Contains($"{Path.DirectorySeparatorChar}tmp{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .Where(file => !string.Equals(file.Name, ".prune.stamp", StringComparison.OrdinalIgnoreCase))
                .OrderBy(file => file.LastWriteTimeUtc)
                .ToList();

            long totalBytes = 0;
            foreach (var file in files)
            {
                totalBytes += SafeLength(file);
            }

            if (totalBytes > CacheMaxBytes)
            {
                foreach (var file in files)
                {
                    var length = SafeLength(file);
                    TryDeleteFile(file.FullName);
                    totalBytes -= length;
                    if (totalBytes <= CacheTrimTargetBytes)
                    {
                        break;
                    }
                }
            }

            TryDeleteEmptyDirectories(cacheDirectory);
            File.WriteAllText(stampPath, nowUtc.ToString("O", CultureInfo.InvariantCulture), Encoding.UTF8);
            File.SetLastWriteTimeUtc(stampPath, nowUtc.UtcDateTime);
        }
        catch (Exception)
        {
            // Best-effort operation. Failure here is intentionally swallowed because the
            // caller observes the safe fallback behavior instead.
        }
    }

    private static long SafeLength(FileInfo file)
    {
        try
        {
            return file.Exists ? file.Length : 0;
        }
        catch (Exception)
        {
            // Best-effort operation. Failure here is intentionally swallowed because the
            // caller observes the safe fallback behavior instead.
            return 0;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // Best-effort operation. Failure here is intentionally swallowed because the
            // caller observes the safe fallback behavior instead.
        }
    }

    private static void TryDeleteEmptyDirectories(string rootPath)
    {
        try
        {
            var dirs = Directory
                .EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories)
                .OrderByDescending(x => x.Length)
                .ToArray();
            foreach (var dir in dirs)
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(dir).Any())
                    {
                        Directory.Delete(dir, false);
                    }
                }
                catch (Exception)
                {
                    // Best-effort operation. Failure here is intentionally swallowed because the
                    // caller observes the safe fallback behavior instead.
                }
            }
        }
        catch (Exception)
        {
            // Best-effort operation. Failure here is intentionally swallowed because the
            // caller observes the safe fallback behavior instead.
        }
    }

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

    private async Task<ComposeStageOutcome> RunFfmpegStageAsync(
        string ffmpegPath,
        string arguments,
        string workingDirectory,
        string stageName,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await using var handle = await _processLauncher.StartProcessAsync(
                new ProcessStartRequest(ffmpegPath, arguments, workingDirectory, timeout),
                cancellationToken);
            var execution = await handle.WaitAsync(cancellationToken);
            if (execution.Cancelled)
            {
                return ComposeStageOutcome.Failure("DS-COMP-CANCEL", $"{stageName} cancelled.", stopwatch.ElapsedMilliseconds);
            }

            if (execution.TimedOut)
            {
                return ComposeStageOutcome.Failure("DS-COMP-TIMEOUT", $"{stageName} timed out after {timeout.TotalSeconds:0}s.", stopwatch.ElapsedMilliseconds);
            }

            if (execution.ExitCode != 0)
            {
                return ComposeStageOutcome.Failure("DS-COMP-FFMPEG", execution.StdErr, stopwatch.ElapsedMilliseconds);
            }

            return ComposeStageOutcome.Success(stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ComposeStageOutcome.Failure("DS-COMP-CANCEL", $"{stageName} cancelled.", stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            return ComposeStageOutcome.Failure("DS-COMP-START", $"{stageName} failed to start: {ex.Message}", stopwatch.ElapsedMilliseconds);
        }
    }

    private sealed record ComposeStageOutcome(bool Succeeded, string FailureCode, string Message, long ElapsedMs)
    {
        public static ComposeStageOutcome Success(long elapsedMs) => new(true, string.Empty, string.Empty, elapsedMs);

        public static ComposeStageOutcome Failure(string code, string message, long elapsedMs)
            => new(false, code, message ?? string.Empty, elapsedMs);
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

    private static void TryWriteTelemetry(string outputPath, ComposeTelemetryEnvelope envelope)
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
        catch (Exception)
        {
            // Best-effort operation. Failure here is intentionally swallowed because the
            // caller observes the safe fallback behavior instead.
        }
    }

    private static void TryWriteHealthSnapshot(
        string curatedDirectory,
        string telemetryPath,
        string cacheDirectory,
        ComposeTelemetryEnvelope latest)
    {
        try
        {
            var outputPath = DesktopStoragePaths.GetComposeHealthPath(curatedDirectory);
            var runs = ReadTelemetryRuns(telemetryPath, 200);
            var cacheStats = GetCacheStats(cacheDirectory);
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
        catch (Exception)
        {
            // Best-effort operation. Failure here is intentionally swallowed because the
            // caller observes the safe fallback behavior instead.
        }
    }

    private static List<ComposeTelemetryEnvelope> ReadTelemetryRuns(string telemetryPath, int maxRuns)
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
        catch (Exception)
        {
            // Best-effort operation. Failure here is intentionally swallowed because the
            // caller observes the safe fallback behavior instead.
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

    private static ComposeCacheStats GetCacheStats(string cacheDirectory)
    {
        if (string.IsNullOrWhiteSpace(cacheDirectory) || !Directory.Exists(cacheDirectory))
        {
            return new ComposeCacheStats(0, 0, null, null);
        }

        try
        {
            var files = Directory
                .EnumerateFiles(cacheDirectory, "*", SearchOption.AllDirectories)
                .Where(x => !x.Contains($"{Path.DirectorySeparatorChar}tmp{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .Where(x => !x.EndsWith(".prune.stamp", StringComparison.OrdinalIgnoreCase))
                .Select(path => new FileInfo(path))
                .ToArray();
            if (files.Length == 0)
            {
                return new ComposeCacheStats(0, 0, null, null);
            }

            var totalBytes = files.Sum(file => SafeLength(file));
            var oldest = files.Min(file => file.LastWriteTimeUtc);
            var newest = files.Max(file => file.LastWriteTimeUtc);
            return new ComposeCacheStats(
                FileCount: files.Length,
                TotalBytes: totalBytes,
                OldestWriteUtc: oldest,
                NewestWriteUtc: newest);
        }
        catch (Exception)
        {
            // Best-effort operation. Failure here is intentionally swallowed because the
            // caller observes the safe fallback behavior instead.
            return new ComposeCacheStats(0, 0, null, null);
        }
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

internal sealed record ComposeCacheStats(
    int FileCount,
    long TotalBytes,
    DateTimeOffset? OldestWriteUtc,
    DateTimeOffset? NewestWriteUtc);

public sealed record DesktopVideoComposeResult(bool Succeeded, string Message, string? OutputPath)
{
    public static DesktopVideoComposeResult Success(string outputPath, string preset)
        => new(true, $"Compose succeeded ({preset}): {outputPath}", outputPath);

    public static DesktopVideoComposeResult Failure(string message)
        => new(false, message, null);
}

internal sealed record ComposeQualityProfile(string Name, string Preset, double Crf)
{
    public static ComposeQualityProfile Normalize(string? presetName)
    {
        return (presetName ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "fast" => new ComposeQualityProfile("Fast", "ultrafast", 29d),
            "portfolio" => new ComposeQualityProfile("Portfolio", "slow", 18d),
            _ => new ComposeQualityProfile("Balanced", "veryfast", 23d)
        };
    }
}

internal sealed record ComposeStyleProfile(
    string Name,
    string FontPath,
    string FontColor,
    string SubColor,
    string LowerThirdBoxColor,
    string SlateColor,
    int TitleSize,
    int SubtitleSize,
    int LowerThirdSize,
    int IntroSeconds,
    int OutroSeconds)
{
    public static ComposeStyleProfile Normalize(string? style)
    {
        var font = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Fonts),
            "arial.ttf");
        return (style ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "tutorial" => new ComposeStyleProfile("Tutorial", font, "white", "white@0.82", "black@0.6", "0x1E293B", 64, 34, 30, 2, 2),
            "social reel" => new ComposeStyleProfile("Social Reel", font, "white", "white@0.9", "0x111111@0.75", "0x111827", 72, 36, 40, 1, 1),
            _ => new ComposeStyleProfile("Portfolio Clean", font, "white", "white@0.82", "black@0.45", "0x0F172A", 60, 32, 30, 2, 2)
        };
    }
}
