using System.IO;
using DemoStudio.Application.Abstractions.System;

namespace DemoStudio.Desktop.App.Services;

public sealed class DesktopDependencyHealthService
{
    private readonly DesktopCaptureRuntime _captureRuntime;
    private readonly IProcessLauncher _processLauncher;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private DesktopDependencyHealthSnapshot _current = DesktopDependencyHealthSnapshot.Uninitialized();
    private DateTimeOffset _ffmpegProbeMutedUntilUtc = DateTimeOffset.MinValue;

    public DesktopDependencyHealthService(DesktopCaptureRuntime captureRuntime, IProcessLauncher processLauncher)
    {
        _captureRuntime = captureRuntime ?? throw new ArgumentNullException(nameof(captureRuntime));
        _processLauncher = processLauncher ?? throw new ArgumentNullException(nameof(processLauncher));
    }

    public DesktopDependencyHealthSnapshot Current => _current;

    public async Task<DesktopDependencyHealthSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!await _refreshGate.WaitAsync(0, cancellationToken))
        {
            return _current;
        }

        try
        {
            var errors = new List<string>();
            var warnings = new List<string>();

            ValidateStorage(errors);
            await ValidateFfmpegAsync(errors, cancellationToken);
            ValidateMicrophone(warnings);

            _current = new DesktopDependencyHealthSnapshot(
                CheckedUtc: DateTimeOffset.UtcNow,
                IsHealthy: errors.Count == 0,
                Errors: errors,
                Warnings: warnings);
            return _current;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private void ValidateStorage(List<string> errors)
    {
        try
        {
            Directory.CreateDirectory(_captureRuntime.StorageRoot);
            var probe = Path.Combine(_captureRuntime.StorageRoot, $".dep-probe-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
        }
        catch (Exception ex)
        {
            errors.Add($"Storage write probe failed ({ex.Message}).");
        }
    }

    private async Task ValidateFfmpegAsync(List<string> errors, CancellationToken cancellationToken)
    {
        if (!_captureRuntime.IsFfmpegAvailable)
        {
            errors.Add($"FFmpeg not resolvable ('{_captureRuntime.FfmpegPath}').");
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (now <= _ffmpegProbeMutedUntilUtc)
        {
            errors.Add("FFmpeg probe temporarily muted after recent start failure.");
            return;
        }

        var probe = await _processLauncher.LaunchAsync(
            new ProcessLaunchRequest(
                _captureRuntime.FfmpegPath,
                string.Empty,
                _captureRuntime.StorageRoot,
                new[] { "-version" },
                "ffmpeg-dependency-probe",
                "dependency-health"),
            cancellationToken);
        var execution = probe.Execution;
        if (!probe.Started || execution is null || execution.ExitCode != 0 || execution.TimedOut || execution.Cancelled)
        {
            var reason = probe.ErrorMessage ?? "start failure";
            if (probe.Started && execution is not null)
            {
                reason = execution.TimedOut
                    ? "timed out"
                    : execution.Cancelled
                        ? "cancelled"
                        : $"exit code {execution.ExitCode}";
            }
            if (!probe.Started)
            {
                _ffmpegProbeMutedUntilUtc = now.AddSeconds(30);
            }
            errors.Add($"FFmpeg probe failed ({reason}).");
        }
    }

    private void ValidateMicrophone(List<string> warnings)
    {
        if (_captureRuntime.CaptureMicrophoneEnabled && string.IsNullOrWhiteSpace(_captureRuntime.MicrophoneDeviceName))
        {
            warnings.Add("Microphone capture is enabled without an explicit input device.");
        }
    }
}

public sealed record DesktopDependencyHealthSnapshot(
    DateTimeOffset CheckedUtc,
    bool IsHealthy,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    public static DesktopDependencyHealthSnapshot Uninitialized()
        => new(DateTimeOffset.MinValue, true, Array.Empty<string>(), Array.Empty<string>());

    public string Summary
    {
        get
        {
            if (CheckedUtc == DateTimeOffset.MinValue)
            {
                return "Dependencies: not checked yet.";
            }

            if (!IsHealthy)
            {
                return $"Dependencies degraded: {string.Join(" | ", Errors)}";
            }

            if (Warnings.Count > 0)
            {
                return $"Dependencies healthy with warnings: {string.Join(" | ", Warnings)}";
            }

            return "Dependencies healthy.";
        }
    }

    public string SummaryShort
    {
        get
        {
            if (!IsHealthy && Errors.Count > 0)
            {
                return Errors[0];
            }

            if (Warnings.Count > 0)
            {
                return Warnings[0];
            }

            return "Healthy";
        }
    }
}
