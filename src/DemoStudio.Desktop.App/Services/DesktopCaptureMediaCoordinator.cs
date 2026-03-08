using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using DemoStudio.Desktop.App.ViewModels;

namespace DemoStudio.Desktop.App.Services;

public sealed class DesktopCaptureMediaCoordinator
{
    public sealed record ClipPreviewOpenResult(bool Succeeded, string Message);

    private readonly DesktopCaptureRuntime _captureRuntime;
    private readonly DesktopProcessRunner _processRunner;
    private readonly SemaphoreSlim _thumbnailGenerationSemaphore = new(1, 1);
    private readonly ConcurrentDictionary<string, byte> _thumbnailGenerationInFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _thumbnailGenerationAttempts = new(StringComparer.OrdinalIgnoreCase);
    private bool _ffmpegLaunchDisabled;

    public DesktopCaptureMediaCoordinator(DesktopCaptureRuntime captureRuntime, DesktopProcessRunner? processRunner = null)
    {
        _captureRuntime = captureRuntime ?? throw new ArgumentNullException(nameof(captureRuntime));
        _processRunner = processRunner ?? new DesktopProcessRunner();
        _ffmpegLaunchDisabled = !_captureRuntime.IsFfmpegAvailable;
    }

    public bool CanRunFfmpeg => !_ffmpegLaunchDisabled && _captureRuntime.IsFfmpegAvailable && !string.IsNullOrWhiteSpace(_captureRuntime.FfmpegPath);

    public async Task GenerateMissingClipThumbnailsAsync(
        IEnumerable<CurrentSessionClipItem> clips,
        string? rawVideoPath,
        Guid sessionId,
        Action<CurrentSessionClipItem, string> assignThumbnailPath)
    {
        if (clips is null)
        {
            return;
        }

        foreach (var clip in clips.Where(x => !x.HasThumbnail))
        {
            await TryGenerateClipThumbnailAsync(clip, rawVideoPath, sessionId, assignThumbnailPath);
        }
    }

    public Task TryGenerateClipThumbnailAsync(
        CurrentSessionClipItem clip,
        string? rawVideoPath,
        Guid sessionId,
        Action<CurrentSessionClipItem, string> assignThumbnailPath)
    {
        if (clip is null || assignThumbnailPath is null || !CanRunFfmpeg)
        {
            return Task.CompletedTask;
        }

        if (clip.HasThumbnail || string.IsNullOrWhiteSpace(rawVideoPath) || rawVideoPath == "-" || !File.Exists(rawVideoPath))
        {
            return Task.CompletedTask;
        }

        var ensuredRawVideoPath = rawVideoPath!;
        return TryGenerateCoreAsync(clip, ensuredRawVideoPath, sessionId, assignThumbnailPath);
    }

    public async Task<ClipPreviewOpenResult> OpenClipPreviewAsync(
        CurrentSessionClipItem? clip,
        string? rawVideoPath,
        Guid sessionId)
    {
        if (clip is null)
        {
            return new ClipPreviewOpenResult(false, "Clip preview unavailable: no clip selected.");
        }

        if (string.IsNullOrWhiteSpace(rawVideoPath) || rawVideoPath == "-" || !File.Exists(rawVideoPath))
        {
            return new ClipPreviewOpenResult(false, "Clip preview unavailable: raw video not found.");
        }

        if (!CanRunFfmpeg)
        {
            return TryOpenMedia(rawVideoPath, $"Opened source video for {clip.Label}.");
        }

        try
        {
            var sessionKey = sessionId == Guid.Empty ? "session" : sessionId.ToString("N");
            var previewRoot = Path.Combine(_captureRuntime.StorageRoot, "previews", sessionKey);
            Directory.CreateDirectory(previewRoot);

            var previewPath = Path.Combine(previewRoot, $"clip-{clip.Sequence:000}.mp4");
            if (File.Exists(previewPath))
            {
                var previewInfo = new FileInfo(previewPath);
                var sourceInfo = new FileInfo(rawVideoPath);
                if (previewInfo.Length > 1024 && previewInfo.LastWriteTimeUtc >= sourceInfo.LastWriteTimeUtc.AddSeconds(-1))
                {
                    return TryOpenMedia(previewPath, $"Opened cached preview for {clip.Label}.");
                }
            }

            var ffmpegPath = _captureRuntime.FfmpegPath;
            if (string.IsNullOrWhiteSpace(ffmpegPath))
            {
                return TryOpenMedia(rawVideoPath, $"Opened source video for {clip.Label}.");
            }

            var startSeconds = Math.Max(0d, clip.StartSeconds);
            var clipDuration = Math.Max(1d, clip.DurationSeconds);
            var startValue = startSeconds.ToString("0.###", CultureInfo.InvariantCulture);
            var durationValue = clipDuration.ToString("0.###", CultureInfo.InvariantCulture);
            var sourceValue = rawVideoPath.Replace("\"", "\\\"", StringComparison.Ordinal);
            var outputValue = previewPath.Replace("\"", "\\\"", StringComparison.Ordinal);
            var arguments =
                $"-y -ss {startValue} -i \"{sourceValue}\" -t {durationValue} -vf \"scale=960:-2\" -c:v libx264 -preset veryfast -crf 30 -pix_fmt yuv420p -movflags +faststart -an \"{outputValue}\"";

            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            var result = await _processRunner.RunAsync(startInfo, TimeSpan.FromSeconds(20));
            if (result.StartFailed)
            {
                _ffmpegLaunchDisabled = true;
            }

            if (result.Succeeded && File.Exists(previewPath) && new FileInfo(previewPath).Length > 1024)
            {
                return TryOpenMedia(previewPath, $"Opened preview for {clip.Label}.");
            }
        }
        catch
        {
            // Best effort path; fallback to source open.
        }

        return TryOpenMedia(rawVideoPath, $"Opened source video for {clip.Label}.");
    }

    private async Task TryGenerateCoreAsync(
        CurrentSessionClipItem clip,
        string rawVideoPath,
        Guid sessionId,
        Action<CurrentSessionClipItem, string> assignThumbnailPath)
    {
        var ffmpegPath = _captureRuntime.FfmpegPath;
        if (string.IsNullOrWhiteSpace(ffmpegPath))
        {
            return;
        }

        var sessionKey = sessionId == Guid.Empty ? "session" : sessionId.ToString("N");
        var thumbnailToken = $"{sessionKey}:{clip.Sequence}";
        var attempt = _thumbnailGenerationAttempts.AddOrUpdate(thumbnailToken, 1, (_, existing) => existing + 1);
        if (attempt > 2)
        {
            return;
        }

        if (!_thumbnailGenerationInFlight.TryAdd(thumbnailToken, 0))
        {
            return;
        }

        var semaphoreHeld = false;
        try
        {
            var thumbnailsRoot = Path.Combine(_captureRuntime.StorageRoot, "thumbnails", sessionKey);
            Directory.CreateDirectory(thumbnailsRoot);
            var thumbnailPath = Path.Combine(thumbnailsRoot, $"clip-{clip.Sequence:000}.jpg");
            if (File.Exists(thumbnailPath))
            {
                var info = new FileInfo(thumbnailPath);
                if (info.Length > 0)
                {
                    assignThumbnailPath(clip, thumbnailPath);
                    return;
                }
            }

            var seekSeconds = Math.Max(0d, clip.StartSeconds + Math.Min(0.5d, Math.Max(0d, clip.DurationSeconds / 3d)));
            var seekValue = seekSeconds.ToString("0.###", CultureInfo.InvariantCulture);
            await _thumbnailGenerationSemaphore.WaitAsync();
            semaphoreHeld = true;

            var escapedInput = rawVideoPath.Replace("\"", "\\\"", StringComparison.Ordinal);
            var escapedOutput = thumbnailPath.Replace("\"", "\\\"", StringComparison.Ordinal);
            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = $"-y -ss {seekValue} -i \"{escapedInput}\" -frames:v 1 -q:v 5 \"{escapedOutput}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            var processResult = await _processRunner.RunAsync(startInfo, TimeSpan.FromSeconds(12));
            if (processResult.StartFailed)
            {
                _ffmpegLaunchDisabled = true;
                return;
            }

            if (semaphoreHeld)
            {
                _thumbnailGenerationSemaphore.Release();
                semaphoreHeld = false;
            }

            if (!processResult.Succeeded || !File.Exists(thumbnailPath))
            {
                var retryInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = $"-y -i \"{escapedInput}\" -ss {seekValue} -frames:v 1 -q:v 5 \"{escapedOutput}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                var retryResult = await _processRunner.RunAsync(retryInfo, TimeSpan.FromSeconds(12));
                if (retryResult.StartFailed)
                {
                    _ffmpegLaunchDisabled = true;
                    return;
                }

                if (!retryResult.Succeeded || !File.Exists(thumbnailPath))
                {
                    return;
                }
            }

            assignThumbnailPath(clip, thumbnailPath);
            _thumbnailGenerationAttempts.TryRemove(thumbnailToken, out _);
        }
        catch
        {
            // Best effort only.
        }
        finally
        {
            if (semaphoreHeld)
            {
                try
                {
                    _thumbnailGenerationSemaphore.Release();
                }
                catch
                {
                }
            }

            _thumbnailGenerationInFlight.TryRemove(thumbnailToken, out _);
        }
    }

    private ClipPreviewOpenResult TryOpenMedia(string path, string successMessage)
    {
        var openResult = _processRunner.OpenWithShell(path);
        if (openResult.Succeeded)
        {
            return new ClipPreviewOpenResult(true, successMessage);
        }

        return new ClipPreviewOpenResult(false, $"Unable to open media: {openResult.ErrorMessage}");
    }
}
