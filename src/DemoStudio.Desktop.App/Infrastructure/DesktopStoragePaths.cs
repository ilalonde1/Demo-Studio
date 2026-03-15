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

    /// <summary>Returns the curated output directory for a raw video file.</summary>
    public static string GetCuratedDirectory(string rawVideoPath) =>
        Path.Combine(
            Path.GetDirectoryName(rawVideoPath)
                ?? throw new ArgumentException("Path has no directory.", nameof(rawVideoPath)),
            "curated");

    /// <summary>Returns the narration directory for a session.</summary>
    public static string GetNarrationDirectory(string storageRoot, Guid sessionId) =>
        Path.Combine(storageRoot, "narration", sessionId.ToString("N"));

    /// <summary>Returns the previews directory for a session.</summary>
    public static string GetPreviewsDirectory(string storageRoot, string sessionKey) =>
        Path.Combine(storageRoot, "previews", sessionKey);

    /// <summary>Returns the thumbnails directory for a session.</summary>
    public static string GetThumbnailsDirectory(string storageRoot, string sessionKey) =>
        Path.Combine(storageRoot, "thumbnails", sessionKey);

    /// <summary>Returns the compose cache directory for a curated directory.</summary>
    public static string GetComposeCacheDirectory(string curatedDirectory) =>
        Path.Combine(curatedDirectory, "compose-cache");

    /// <summary>Returns the compose telemetry log path.</summary>
    public static string GetComposeTelemetryPath(string curatedDirectory) =>
        Path.Combine(curatedDirectory, "compose-telemetry.jsonl");

    /// <summary>Returns the compose health snapshot path.</summary>
    public static string GetComposeHealthPath(string curatedDirectory) =>
        Path.Combine(curatedDirectory, "compose-health.json");

    /// <summary>Returns the publish output directory.</summary>
    public static string GetPublishDirectory(string storageRoot) =>
        Path.Combine(storageRoot, "publish");

    /// <summary>Returns the browser interaction root directory.</summary>
    public static string GetBrowserInteractionsDirectory(string storageRoot) =>
        Path.Combine(storageRoot, "browser-interactions");

    /// <summary>Returns the browser interaction event log path.</summary>
    public static string GetBrowserInteractionsLogPath(string storageRoot) =>
        Path.Combine(GetBrowserInteractionsDirectory(storageRoot), "stage-browser-events.jsonl");

    /// <summary>Returns the browser interaction screenshot directory.</summary>
    public static string GetBrowserInteractionScreenshotsDirectory(string storageRoot) =>
        Path.Combine(GetBrowserInteractionsDirectory(storageRoot), "screenshots");
}
