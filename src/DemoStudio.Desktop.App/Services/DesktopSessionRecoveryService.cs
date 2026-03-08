using System.Text.Json;
using DemoStudio.Desktop.Core.Sessions;
using System.IO;

namespace DemoStudio.Desktop.App.Services;

public sealed class DesktopSessionRecoveryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _draftPath;

    public DesktopSessionRecoveryService(string storageRoot)
    {
        Directory.CreateDirectory(storageRoot);
        _draftPath = Path.Combine(storageRoot, "session-draft.json");
    }

    public async Task SaveAsync(DesktopSessionDraft draft, CancellationToken cancellationToken = default)
    {
        var raw = JsonSerializer.Serialize(draft, JsonOptions);
        await File.WriteAllTextAsync(_draftPath, raw, cancellationToken);
    }

    public async Task<DesktopSessionDraft?> TryLoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_draftPath))
        {
            return null;
        }

        try
        {
            var raw = await File.ReadAllTextAsync(_draftPath, cancellationToken);
            return JsonSerializer.Deserialize<DesktopSessionDraft>(raw, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (File.Exists(_draftPath))
            {
                File.Delete(_draftPath);
            }
        }
        catch
        {
        }

        return Task.CompletedTask;
    }
}

public sealed record DesktopSessionDraft(
    Guid SessionId,
    RecorderSessionState State,
    DateTimeOffset SavedUtc,
    string CaptureMode,
    string? WindowTitleContains,
    bool CaptureNarration,
    string? MicrophoneDeviceName,
    string QualityPreset,
    string ExportStyle,
    string? AiProvider,
    string? AiBaseUrl,
    string? AiModel,
    string? AiVoice,
    bool AiAutoTrimScript,
    double AiWordsPerSecond,
    string? LastOutputPath,
    IReadOnlyList<DesktopSessionDraftClip> Clips);

public sealed record DesktopSessionDraftClip(
    int Sequence,
    int Order,
    string Label,
    string? BannerText,
    string DurationDisplay,
    double StartSeconds,
    double DurationSeconds,
    bool IncludeNarration,
    string? NarrationAudioPath = null,
    string? NarrationSource = null,
    string? NarrationScript = null);
