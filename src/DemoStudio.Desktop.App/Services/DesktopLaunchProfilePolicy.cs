using System.IO;

namespace DemoStudio.Desktop.App.Services;

internal static class DesktopLaunchProfilePolicy
{
    public static DesktopLaunchProfileValidationResult ValidateForPersistence(DesktopLaunchProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            return DesktopLaunchProfileValidationResult.Invalid("Profile name is required.");
        }

        if (string.IsNullOrWhiteSpace(profile.ExecutablePath))
        {
            return DesktopLaunchProfileValidationResult.Invalid("Launch executable path is required.");
        }

        if (!TryNormalizeExecutablePath(profile.ExecutablePath, requireExists: false, out var executablePath, out var error))
        {
            return DesktopLaunchProfileValidationResult.Invalid(error!);
        }

        if (!TryNormalizeWorkingDirectory(profile.WorkingDirectory, executablePath, requireExists: false, out var workingDirectory, out error))
        {
            return DesktopLaunchProfileValidationResult.Invalid(error!);
        }

        return DesktopLaunchProfileValidationResult.Valid(
            profile with
            {
                Name = profile.Name.Trim(),
                ExecutablePath = executablePath!,
                Arguments = NormalizeOptionalValue(profile.Arguments),
                WorkingDirectory = workingDirectory,
                ExpectedWindowTitleContains = NormalizeOptionalValue(profile.ExpectedWindowTitleContains),
                ExpectedProcessName = NormalizeOptionalValue(profile.ExpectedProcessName)
            });
    }

    public static DesktopLaunchProfileValidationResult ValidateForExecution(DesktopLaunchProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.ExecutablePath))
        {
            return DesktopLaunchProfileValidationResult.Invalid("Executable path is required.");
        }

        if (!TryNormalizeExecutablePath(profile.ExecutablePath, requireExists: true, out var executablePath, out var error))
        {
            return DesktopLaunchProfileValidationResult.Invalid(error!);
        }

        if (!TryNormalizeWorkingDirectory(profile.WorkingDirectory, executablePath, requireExists: true, out var workingDirectory, out error))
        {
            return DesktopLaunchProfileValidationResult.Invalid(error!);
        }

        return DesktopLaunchProfileValidationResult.Valid(
            profile with
            {
                ExecutablePath = executablePath!,
                Arguments = NormalizeOptionalValue(profile.Arguments),
                WorkingDirectory = workingDirectory,
                ExpectedWindowTitleContains = NormalizeOptionalValue(profile.ExpectedWindowTitleContains),
                ExpectedProcessName = NormalizeOptionalValue(profile.ExpectedProcessName)
            });
    }

    public static DesktopLaunchProfileValidationResult ValidateOptionalForPreflight(DesktopLaunchProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.ExecutablePath))
        {
            return DesktopLaunchProfileValidationResult.Valid(profile with
            {
                Name = string.IsNullOrWhiteSpace(profile.Name) ? "Unnamed" : profile.Name.Trim(),
                Arguments = NormalizeOptionalValue(profile.Arguments),
                WorkingDirectory = NormalizeOptionalValue(profile.WorkingDirectory),
                ExpectedWindowTitleContains = NormalizeOptionalValue(profile.ExpectedWindowTitleContains),
                ExpectedProcessName = NormalizeOptionalValue(profile.ExpectedProcessName)
            });
        }

        return ValidateForExecution(profile);
    }

    private static bool TryNormalizeExecutablePath(
        string rawExecutablePath,
        bool requireExists,
        out string? executablePath,
        out string? error)
    {
        executablePath = null;
        error = null;

        try
        {
            executablePath = Path.GetFullPath(rawExecutablePath.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = $"Executable path is invalid: {ex.Message}";
            return false;
        }

        if (Directory.Exists(executablePath))
        {
            error = $"Executable path points to a directory: '{executablePath}'.";
            return false;
        }

        if (requireExists && !File.Exists(executablePath))
        {
            error = $"Executable not found: '{executablePath}'.";
            return false;
        }

        return true;
    }

    private static bool TryNormalizeWorkingDirectory(
        string? rawWorkingDirectory,
        string? executablePath,
        bool requireExists,
        out string? workingDirectory,
        out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(rawWorkingDirectory))
        {
            workingDirectory = string.IsNullOrWhiteSpace(executablePath)
                ? null
                : Path.GetDirectoryName(executablePath);
        }
        else
        {
            try
            {
                workingDirectory = Path.GetFullPath(rawWorkingDirectory.Trim());
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                workingDirectory = null;
                error = $"Working directory path is invalid: {ex.Message}";
                return false;
            }
        }

        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            error = "Working directory could not be resolved.";
            return false;
        }

        if (File.Exists(workingDirectory))
        {
            error = $"Working directory points to a file: '{workingDirectory}'.";
            return false;
        }

        if (requireExists && !Directory.Exists(workingDirectory))
        {
            error = $"Working directory not found: '{workingDirectory}'.";
            return false;
        }

        return true;
    }

    private static string? NormalizeOptionalValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed record DesktopLaunchProfileValidationResult(
    bool IsValid,
    DesktopLaunchProfile? Profile,
    string? Error)
{
    public static DesktopLaunchProfileValidationResult Valid(DesktopLaunchProfile profile)
        => new(true, profile, null);

    public static DesktopLaunchProfileValidationResult Invalid(string error)
        => new(false, null, error);
}
