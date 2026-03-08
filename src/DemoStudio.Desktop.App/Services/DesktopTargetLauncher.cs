using System.Diagnostics;
using System.IO;

namespace DemoStudio.Desktop.App.Services;

public sealed class DesktopTargetLauncher
{
    private readonly DesktopProcessRunner _processRunner;

    public DesktopTargetLauncher(DesktopProcessRunner? processRunner = null)
    {
        _processRunner = processRunner ?? new DesktopProcessRunner();
    }

    public async Task<DesktopTargetLaunchResult> LaunchAsync(DesktopLaunchProfile profile, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(profile.ExecutablePath))
        {
            return DesktopTargetLaunchResult.Failure("Executable path is required.");
        }

        var executablePath = Path.GetFullPath(profile.ExecutablePath.Trim());
        if (!File.Exists(executablePath))
        {
            return DesktopTargetLaunchResult.Failure($"Executable not found: '{executablePath}'.");
        }

        var workingDirectory = string.IsNullOrWhiteSpace(profile.WorkingDirectory)
            ? Path.GetDirectoryName(executablePath)
            : Path.GetFullPath(profile.WorkingDirectory.Trim());
        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
        {
            return DesktopTargetLaunchResult.Failure($"Working directory not found: '{workingDirectory}'.");
        }

        var launchResult = _processRunner.StartDetached(new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = profile.Arguments ?? string.Empty,
            WorkingDirectory = workingDirectory,
            UseShellExecute = true
        });
        if (!launchResult.Succeeded)
        {
            return DesktopTargetLaunchResult.Failure(launchResult.ErrorMessage ?? "Failed to launch target process.");
        }

        if (profile.StartupDelaySeconds > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(profile.StartupDelaySeconds), cancellationToken);
        }

        return DesktopTargetLaunchResult.Success($"Launched '{Path.GetFileName(executablePath)}'.");
    }
}

public sealed record DesktopTargetLaunchResult(bool Succeeded, string Message)
{
    public static DesktopTargetLaunchResult Success(string message) => new(true, message);

    public static DesktopTargetLaunchResult Failure(string message) => new(false, message);
}
