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

    public string? LastLoadDiagnostic { get; private set; }

    public DesktopSessionRecoveryService(string storageRoot)
    {
        Directory.CreateDirectory(storageRoot);
        _draftPath = Path.Combine(storageRoot, "session-draft.json");
    }

    public async Task SaveAsync(DesktopSessionDraft draft, CancellationToken cancellationToken = default)
    {
        await DesktopAtomicJsonFile.SaveAsync(_draftPath, draft, JsonOptions, cancellationToken);
    }

    public async Task<DesktopSessionDraft?> TryLoadAsync(CancellationToken cancellationToken = default)
    {
        var load = await DesktopAtomicJsonFile.LoadAsync<DesktopSessionDraft>(_draftPath, JsonOptions, cancellationToken).ConfigureAwait(false);
        LastLoadDiagnostic = load.Diagnostic;
        if (!load.Exists)
        {
            return null;
        }

        if (load.Value is not null)
        {
            return load.Value;
        }

        throw new InvalidOperationException(LastLoadDiagnostic ?? "Session draft could not be loaded.");
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        return DesktopAtomicJsonFile.DeleteAsync(_draftPath, cancellationToken);
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
