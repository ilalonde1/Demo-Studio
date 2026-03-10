namespace DemoStudio.Desktop.App.Infrastructure;

/// <summary>
/// Resolved runtime paths derived from DesktopCaptureRuntime at startup.
/// Registered as a singleton in DI to avoid re-resolving DesktopCaptureRuntime
/// in every storage-dependent service factory.
/// </summary>
internal sealed record DesktopRuntimePaths(string StorageRoot, string FfmpegPath);
