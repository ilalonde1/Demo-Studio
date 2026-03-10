using System.Diagnostics;
using System.IO;

namespace DemoStudio.Desktop.App.Services;

public sealed class DesktopStartupHealthService
{
    private readonly DesktopCaptureRuntime _captureRuntime;
    private readonly DesktopProcessRunner _processRunner;

    public DesktopStartupHealthService(DesktopCaptureRuntime captureRuntime, DesktopProcessRunner processRunner)
    {
        _captureRuntime = captureRuntime ?? throw new ArgumentNullException(nameof(captureRuntime));
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
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
            var startInfo = new ProcessStartInfo
            {
                FileName = _captureRuntime.FfmpegPath,
                Arguments = "-version",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            var result = await _processRunner.RunAsync(startInfo, TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
            if (result.StartFailed)
            {
                errors.Add($"FFmpeg process failed to start: {result.ErrorMessage}");
                return;
            }

            if (!result.Succeeded)
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
