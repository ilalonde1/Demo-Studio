using DemoStudio.Infrastructure.Execution.Windows;
using System.IO;

namespace DemoStudio.Desktop.App.Services;

public sealed class DesktopCapturePreflightService
{
    private readonly IWindowLocator _windowLocator;

    public DesktopCapturePreflightService(IWindowLocator windowLocator)
    {
        _windowLocator = windowLocator ?? throw new ArgumentNullException(nameof(windowLocator));
    }

    public async Task<DesktopPreflightReport> RunAsync(
        DesktopCaptureRuntime runtime,
        CaptureTargetSettings targetSettings,
        DesktopLaunchProfile draftLaunchProfile,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        ValidateStorage(runtime.StorageRoot, errors);
        ValidateFfmpeg(runtime.FfmpegPath, errors);
        ValidateLaunchProfile(draftLaunchProfile, errors);
        await ValidateTargetAsync(targetSettings, errors, warnings, cancellationToken);

        return new DesktopPreflightReport(errors.Count == 0, errors, warnings);
    }

    private static void ValidateStorage(string storageRoot, List<string> errors)
    {
        try
        {
            Directory.CreateDirectory(storageRoot);
            var probe = Path.Combine(storageRoot, $".probe-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
        }
        catch (Exception ex)
        {
            errors.Add($"Storage root is not writable: {ex.Message}");
        }
    }

    private static void ValidateFfmpeg(string ffmpegPath, List<string> errors)
    {
        if (!FfmpegExecutableResolver.TryResolve(ffmpegPath, out _))
        {
            errors.Add($"FFmpeg executable not found: '{ffmpegPath}'. Set a valid path or add it to PATH.");
        }
    }

    private static void ValidateLaunchProfile(DesktopLaunchProfile profile, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(profile.ExecutablePath))
        {
            return;
        }

        var executable = Path.GetFullPath(profile.ExecutablePath.Trim());
        if (!File.Exists(executable))
        {
            errors.Add($"Launch executable not found: '{executable}'.");
        }

        if (!string.IsNullOrWhiteSpace(profile.WorkingDirectory))
        {
            var workingDirectory = Path.GetFullPath(profile.WorkingDirectory.Trim());
            if (!Directory.Exists(workingDirectory))
            {
                errors.Add($"Launch working directory not found: '{workingDirectory}'.");
            }
        }
    }

    private async Task ValidateTargetAsync(
        CaptureTargetSettings targetSettings,
        List<string> errors,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var mode = string.Equals(targetSettings.Mode, "Desktop", StringComparison.OrdinalIgnoreCase)
            ? "Desktop"
            : "Window";
        if (!mode.Equals("Window", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var preferExactHandle = !string.IsNullOrWhiteSpace(targetSettings.WindowHandleHex);
        var titleForLookup = preferExactHandle ? null : targetSettings.WindowTitleContains;
        var processForLookup = targetSettings.WindowProcessName;
        var locate = await _windowLocator.FindAsync(
            new WindowLocatorRequest(
                titleForLookup,
                TitleRegex: null,
                processForLookup,
                targetSettings.WindowHandleHex,
                PreferExactHandle: preferExactHandle),
            cancellationToken);

        if (locate.Found)
        {
            var stability = await ProbeWindowStabilityAsync(targetSettings, cancellationToken);
            if (!stability.IsStable)
            {
                errors.Add(stability.Message);
            }

            return;
        }

        errors.Add($"Window target not found. Reason: {locate.FailureReason ?? "Unknown"}");
    }

    private async Task<(bool IsStable, string Message)> ProbeWindowStabilityAsync(
        CaptureTargetSettings targetSettings,
        CancellationToken cancellationToken)
    {
        IntPtr? baselineHandle = null;
        var preferExactHandle = !string.IsNullOrWhiteSpace(targetSettings.WindowHandleHex);
        var titleForLookup = preferExactHandle ? null : targetSettings.WindowTitleContains;
        var processForLookup = targetSettings.WindowProcessName;
        for (var i = 0; i < 3; i++)
        {
            var result = await _windowLocator.FindAsync(
                    new WindowLocatorRequest(
                        titleForLookup,
                        TitleRegex: null,
                        processForLookup,
                        targetSettings.WindowHandleHex,
                        PreferExactHandle: preferExactHandle),
                    cancellationToken);

            if (!result.Found)
            {
                return (false, "Window target is unstable: target disappeared during stability probe.");
            }

            if (baselineHandle is null)
            {
                baselineHandle = result.Handle;
            }
            else if (baselineHandle.Value != result.Handle)
            {
                return (false, "Window target is unstable: different window handles detected during probe.");
            }

            if (i < 2)
            {
                await Task.Delay(300, cancellationToken);
            }
        }

        return (true, "Stable");
    }

}

public sealed record DesktopPreflightReport(
    bool IsReady,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    public string ToDisplayText()
    {
        if (IsReady && Warnings.Count == 0)
        {
            return "Readiness check passed.";
        }

        if (!IsReady)
        {
            return "Readiness check failed: " + string.Join(" | ", Errors);
        }

        return "Readiness check passed with warnings: " + string.Join(" | ", Warnings);
    }
}
