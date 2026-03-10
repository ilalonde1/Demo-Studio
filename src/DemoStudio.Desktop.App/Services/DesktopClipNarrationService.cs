using System.Globalization;
using System.IO;
using DemoStudio.Application.Abstractions.System;

namespace DemoStudio.Desktop.App.Services;

public sealed class DesktopClipNarrationService
{
    private readonly IProcessLauncher _processLauncher;

    public DesktopClipNarrationService(IProcessLauncher processLauncher)
    {
        _processLauncher = processLauncher ?? throw new ArgumentNullException(nameof(processLauncher));
    }

    public async Task<DesktopClipNarrationResult> CaptureAsync(
        string ffmpegPath,
        string? microphoneDeviceName,
        double durationSeconds,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ffmpegPath))
        {
            return DesktopClipNarrationResult.Failure("Narration capture failed: FFmpeg path is missing.");
        }

        if (durationSeconds <= 0.1d)
        {
            return DesktopClipNarrationResult.Failure("Narration capture failed: clip duration is too short.");
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return DesktopClipNarrationResult.Failure("Narration capture failed: output path is missing.");
        }

        var directory = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return DesktopClipNarrationResult.Failure("Narration capture failed: output directory is invalid.");
        }

        Directory.CreateDirectory(directory);
        try
        {
            File.Delete(outputPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        var durationValue = Math.Max(0.5d, durationSeconds).ToString("0.###", CultureInfo.InvariantCulture);
        var devicesToTry = BuildDeviceCandidates(microphoneDeviceName);
        string? lastError = null;

        foreach (var deviceName in devicesToTry)
        {
            var safeDevice = deviceName.Replace("\"", "\\\"", StringComparison.Ordinal);
            var safeOutput = outputPath.Replace("\"", "\\\"", StringComparison.Ordinal);
            var args =
                $"-y -f dshow -i audio=\"{safeDevice}\" -t {durationValue} -ac 1 -ar 44100 -c:a aac -b:a 128k \"{safeOutput}\"";
            var run = await _processLauncher.LaunchAsync(
                new ProcessLaunchRequest(ffmpegPath, args, directory),
                cancellationToken);

            if (run.Started && run.Execution is not null && run.Execution.ExitCode == 0 && File.Exists(outputPath))
            {
                return DesktopClipNarrationResult.Success(outputPath);
            }

            lastError = run.Execution?.StdErr ?? run.ErrorMessage;
        }

        return DesktopClipNarrationResult.Failure(
            $"Narration capture failed. {TrimError(lastError)}");
    }

    private static IReadOnlyList<string> BuildDeviceCandidates(string? microphoneDeviceName)
    {
        var result = new List<string>();
        if (!string.IsNullOrWhiteSpace(microphoneDeviceName))
        {
            result.Add(microphoneDeviceName.Trim());
        }

        if (!result.Contains("default", StringComparer.OrdinalIgnoreCase))
        {
            result.Add("default");
        }

        return result;
    }

    private static string TrimError(string? stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
        {
            return "Check microphone permissions and device name.";
        }

        var lines = stderr
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        if (lines.Length == 0)
        {
            return "Check microphone permissions and device name.";
        }

        var actionable = lines.LastOrDefault(x =>
                x.Contains("error", StringComparison.OrdinalIgnoreCase)
                || x.Contains("failed", StringComparison.OrdinalIgnoreCase)
                || x.Contains("cannot", StringComparison.OrdinalIgnoreCase)
                || x.Contains("not found", StringComparison.OrdinalIgnoreCase))
            ?? lines.Last();

        return actionable.Length <= 220 ? actionable : actionable[..220];
    }
}

public sealed record DesktopClipNarrationResult(bool Succeeded, string Message, string? OutputPath)
{
    public static DesktopClipNarrationResult Success(string outputPath)
        => new(true, $"Narration captured: {outputPath}", outputPath);

    public static DesktopClipNarrationResult Failure(string message)
        => new(false, message, null);
}
