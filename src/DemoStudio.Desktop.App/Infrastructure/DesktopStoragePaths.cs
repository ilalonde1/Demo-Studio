using System.IO;

namespace DemoStudio.Desktop.App.Infrastructure;

/// <summary>
/// Canonical path computations for DemoStudio desktop storage locations.
/// All code that needs the recorder storage root should call this helper
/// rather than duplicating the path formula.
/// </summary>
public static class DesktopStoragePaths
{
    private const string AppFolder = "DemoStudio";
    private const string RecorderSubfolder = "RecorderDesktop";

    /// <summary>
    /// Returns the default recorder storage root:
    /// %LOCALAPPDATA%\DemoStudio\RecorderDesktop
    /// </summary>
    public static string GetDefaultRecorderRoot() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppFolder,
            RecorderSubfolder);
}
