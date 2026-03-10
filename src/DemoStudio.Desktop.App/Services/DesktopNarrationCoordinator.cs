using System.IO;
using DemoStudio.Desktop.App.Infrastructure;
using DemoStudio.Desktop.App.ViewModels;

namespace DemoStudio.Desktop.App.Services;

public sealed class DesktopNarrationCoordinator
{
    public sealed record AiNarrationOptions(
        string Provider,
        string BaseUrl,
        string Model,
        string Voice,
        string ApiKey,
        bool AutoTrimScript,
        double WordsPerSecond);

    public sealed record NarrationOperationResult(bool Succeeded, bool DraftChanged, string Message);

    private readonly DesktopCaptureRuntime _captureRuntime;
    private readonly DesktopClipNarrationService _clipNarrationService;
    private readonly DesktopAiNarrationService _aiNarrationService;
    private readonly DesktopProcessRunner _processRunner;

    public DesktopNarrationCoordinator(
        DesktopCaptureRuntime captureRuntime,
        DesktopClipNarrationService clipNarrationService,
        DesktopAiNarrationService aiNarrationService,
        DesktopProcessRunner processRunner)
    {
        _captureRuntime = captureRuntime ?? throw new ArgumentNullException(nameof(captureRuntime));
        _clipNarrationService = clipNarrationService ?? throw new ArgumentNullException(nameof(clipNarrationService));
        _aiNarrationService = aiNarrationService ?? throw new ArgumentNullException(nameof(aiNarrationService));
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    }

    public async Task<NarrationOperationResult> RecordNarrationAsync(CurrentSessionClipItem clip, Guid sessionId, string microphoneDeviceName)
    {
        var narrationRoot = DesktopStoragePaths.GetNarrationDirectory(_captureRuntime.StorageRoot, sessionId);
        Directory.CreateDirectory(narrationRoot);
        var narrationPath = Path.Combine(narrationRoot, $"clip-{clip.Sequence:000}.m4a");

        var capture = await _clipNarrationService.CaptureAsync(
            _captureRuntime.FfmpegPath,
            string.IsNullOrWhiteSpace(microphoneDeviceName) ? null : microphoneDeviceName.Trim(),
            clip.DurationSeconds,
            narrationPath);

        if (!capture.Succeeded || string.IsNullOrWhiteSpace(capture.OutputPath))
        {
            return new NarrationOperationResult(false, false, capture.Message);
        }

        clip.NarrationAudioPath = capture.OutputPath;
        clip.IncludeNarration = true;
        clip.NarrationSource = "Manual";
        if (string.IsNullOrWhiteSpace(clip.NarrationScript))
        {
            clip.NarrationScript = clip.BannerText;
        }

        return new NarrationOperationResult(true, true, $"Narration saved for {clip.Label}.");
    }

    public async Task<NarrationOperationResult> GenerateAiNarrationAsync(
        CurrentSessionClipItem clip,
        Guid sessionId,
        AiNarrationOptions options,
        CancellationToken cancellationToken = default)
    {
        var narrationRoot = DesktopStoragePaths.GetNarrationDirectory(_captureRuntime.StorageRoot, sessionId);
        Directory.CreateDirectory(narrationRoot);
        var narrationPath = Path.Combine(narrationRoot, $"clip-{clip.Sequence:000}-ai.mp3");
        var script = ResolveNarrationScript(clip);
        var (effectiveScript, wasTrimmed) = TrimScriptForDuration(script, clip.DurationSeconds, options.WordsPerSecond, options.AutoTrimScript);

        var generate = await _aiNarrationService.GenerateAsync(
            new DesktopAiNarrationRequest(
                Script: effectiveScript,
                OutputPath: narrationPath,
                Provider: options.Provider,
                BaseUrl: string.IsNullOrWhiteSpace(options.BaseUrl) ? null : options.BaseUrl,
                Model: options.Model,
                Voice: options.Voice,
                ApiKey: string.IsNullOrWhiteSpace(options.ApiKey) ? null : options.ApiKey),
            cancellationToken);
        if (!generate.Succeeded || string.IsNullOrWhiteSpace(generate.OutputPath))
        {
            return new NarrationOperationResult(false, false, generate.Message);
        }

        clip.NarrationAudioPath = generate.OutputPath;
        clip.NarrationSource = "AI";
        clip.NarrationScript = effectiveScript;
        clip.IncludeNarration = true;
        var message = wasTrimmed
            ? $"AI narration saved for {clip.Label}. Script auto-trimmed for duration."
            : $"AI narration saved for {clip.Label}.";
        return new NarrationOperationResult(true, true, message);
    }

    public NarrationOperationResult PlayNarration(CurrentSessionClipItem clip)
    {
        if (string.IsNullOrWhiteSpace(clip.NarrationAudioPath))
        {
            return new NarrationOperationResult(false, false, "Narration audio not found.");
        }

        try
        {
            var openResult = _processRunner.OpenWithShell(clip.NarrationAudioPath);
            return openResult.Succeeded
                ? new NarrationOperationResult(true, false, $"Opened narration for {clip.Label}.")
                : new NarrationOperationResult(false, false, $"Unable to play narration: {openResult.ErrorMessage}");
        }
        catch (Exception ex)
        {
            return new NarrationOperationResult(false, false, $"Unable to play narration: {ex.Message}");
        }
    }

    public NarrationOperationResult ClearNarration(CurrentSessionClipItem clip)
    {
        clip.NarrationAudioPath = null;
        clip.NarrationSource = string.Empty;
        return new NarrationOperationResult(true, true, $"Cleared narration for {clip.Label}.");
    }

    private static string ResolveNarrationScript(CurrentSessionClipItem clip)
    {
        if (!string.IsNullOrWhiteSpace(clip.NarrationScript))
        {
            return clip.NarrationScript.Trim();
        }

        if (!string.IsNullOrWhiteSpace(clip.BannerText))
        {
            return clip.BannerText.Trim();
        }

        return $"This segment demonstrates {clip.Label}.";
    }

    private static (string Script, bool WasTrimmed) TrimScriptForDuration(
        string script,
        double durationSeconds,
        double wordsPerSecond,
        bool autoTrim)
    {
        var normalized = string.IsNullOrWhiteSpace(script) ? string.Empty : script.Trim();
        if (!autoTrim || string.IsNullOrWhiteSpace(normalized))
        {
            return (normalized, false);
        }

        var effectiveDuration = Math.Max(1d, durationSeconds);
        var effectiveRate = Math.Clamp(wordsPerSecond, 1.2d, 4.5d);
        var maxWords = Math.Max(4, (int)Math.Floor(effectiveDuration * effectiveRate));
        var words = normalized
            .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= maxWords)
        {
            return (normalized, false);
        }

        var trimmed = string.Join(" ", words.Take(maxWords)).Trim();
        if (!trimmed.EndsWith(".", StringComparison.Ordinal) &&
            !trimmed.EndsWith("!", StringComparison.Ordinal) &&
            !trimmed.EndsWith("?", StringComparison.Ordinal))
        {
            trimmed += "...";
        }

        return (trimmed, true);
    }
}
