using System.IO;

namespace DemoStudio.Desktop.App.Services;

internal sealed record ComposeQualityProfile(string Name, string Preset, double Crf)
{
    public static ComposeQualityProfile Normalize(string? presetName)
    {
        return (presetName ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "fast" => new ComposeQualityProfile("Fast", "ultrafast", 29d),
            "portfolio" => new ComposeQualityProfile("Portfolio", "slow", 18d),
            _ => new ComposeQualityProfile("Balanced", "veryfast", 23d)
        };
    }
}

internal sealed record ComposeStyleProfile(
    string Name,
    string FontPath,
    string FontColor,
    string SubColor,
    string LowerThirdBoxColor,
    string SlateColor,
    int TitleSize,
    int SubtitleSize,
    int LowerThirdSize,
    int IntroSeconds,
    int OutroSeconds)
{
    public static ComposeStyleProfile Normalize(string? style)
    {
        var font = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Fonts),
            "arial.ttf");
        return (style ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "tutorial" => new ComposeStyleProfile("Tutorial", font, "white", "white@0.82", "black@0.6", "0x1E293B", 64, 34, 30, 2, 2),
            "social reel" => new ComposeStyleProfile("Social Reel", font, "white", "white@0.9", "0x111111@0.75", "0x111827", 72, 36, 40, 1, 1),
            _ => new ComposeStyleProfile("Portfolio Clean", font, "white", "white@0.82", "black@0.45", "0x0F172A", 60, 32, 30, 2, 2)
        };
    }
}
