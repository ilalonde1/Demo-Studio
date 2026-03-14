using System.IO;
using DemoStudio.Application.Abstractions.System;

namespace DemoStudio.Desktop.App.Services;

public sealed class DesktopStartupHealthService
{
    private readonly DesktopCaptureRuntime _captureRuntime;
    private readonly IProcessLauncher _processLauncher;

    public DesktopStartupHealthService(DesktopCaptureRuntime captureRuntime, IProcessLauncher processLauncher)
    {
        _captureRuntime = captureRuntime ?? throw new ArgumentNullException(nameof(captureRuntime));
        _processLauncher = processLauncher ?? throw new ArgumentNullException(nameof(processLauncher));
    }

    public async Task<DesktopStartupHealthReport> EvaluateAsync(CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();

        ValidateStorage(errors);
        await ValidateFfmpegAsync(errors, cancellationToken).ConfigureAwait(false);

        return new DesktopStartupHealthReport(errors.Count == 0, errors);
    }

    private void ValidateStorage(List<string> errors)
    {
        try
        {
            Directory.CreateDirectory(_captureRuntime.StorageRoot);
            var probe = Path.Combine(_captureRuntime.StorageRoot, $".startup-probe-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
        }
        catch (Exception ex)
        {
            errors.Add($"Storage root is not writable: {_captureRuntime.StorageRoot} ({ex.Message})");
        }
    }

    private async Task ValidateFfmpegAsync(List<string> errors, CancellationToken cancellationToken)
    {
        if (!_captureRuntime.IsFfmpegAvailable)
        {
            errors.Add($"FFmpeg executable is not resolvable: '{_captureRuntime.FfmpegPath}'.");
            return;
        }

        try
        {
            var result = await _processLauncher.LaunchAsync(
                new ProcessLaunchRequest(
                    _captureRuntime.FfmpegPath,
                    string.Empty,
                    _captureRuntime.StorageRoot,
                    new[] { "-version" },
                    "ffmpeg-startup-probe",
                    "startup-health"),
                cancellationToken).ConfigureAwait(false);
            if (!result.Started)
            {
                errors.Add($"FFmpeg process failed to start: {result.ErrorMessage}");
                return;
            }

            if (result.Execution is null || result.Execution.ExitCode != 0 || result.Execution.TimedOut || result.Execution.Cancelled)
            {
                errors.Add("FFmpeg failed startup probe (unable to run `-version`).");
            }
        }
        catch (Exception ex)
        {
            errors.Add($"FFmpeg startup probe failed: {ex.Message}");
        }
    }
}

public sealed record DesktopStartupHealthReport(
    bool IsReady,
    IReadOnlyList<string> Errors);
