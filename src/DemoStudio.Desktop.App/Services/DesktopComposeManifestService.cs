using System.Text.Json;
using System.IO;

namespace DemoStudio.Desktop.App.Services;

public sealed class DesktopComposeManifestService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public ComposeManifestResult WriteManifest(DesktopComposeManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.RawVideoPath))
        {
            return ComposeManifestResult.Failure("Raw video path is required.");
        }

        var rawPath = Path.GetFullPath(manifest.RawVideoPath);
        var directory = Path.GetDirectoryName(rawPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return ComposeManifestResult.Failure("Raw video directory does not exist.");
        }

        var outputPath = Path.Combine(directory, "compose-manifest.json");
        var payload = JsonSerializer.Serialize(manifest, JsonOptions);
        File.WriteAllText(outputPath, payload);
        return ComposeManifestResult.Success(outputPath);
    }
}

public sealed record DesktopComposeManifest(
    Guid SessionId,
    string RawVideoPath,
    DateTimeOffset GeneratedUtc,
    IReadOnlyList<DesktopComposeClip> Clips,
    string QualityPreset = "Balanced",
    string ExportStyle = "Portfolio Clean");

public sealed record DesktopComposeClip(
    int Order,
    int Sequence,
    string Label,
    string? BannerText,
    double StartSeconds,
    double DurationSeconds,
    string DurationDisplay,
    bool IncludeNarration = true,
    string? NarrationAudioPath = null);

public sealed record ComposeManifestResult(bool Succeeded, string Message, string? OutputPath)
{
    public static ComposeManifestResult Success(string outputPath)
        => new(true, $"Compose manifest written: {outputPath}", outputPath);

    public static ComposeManifestResult Failure(string message)
        => new(false, message, null);
}
