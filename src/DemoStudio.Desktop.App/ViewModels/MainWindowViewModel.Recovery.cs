using DemoStudio.Desktop.App.Services;
using DemoStudio.Desktop.Core.Sessions;

namespace DemoStudio.Desktop.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private async Task SaveDraftStateAsync()
    {
        try
        {
            var draft = new DesktopSessionDraft(
                SessionId: _snapshot.SessionId,
                State: _snapshot.State,
                SavedUtc: DateTimeOffset.UtcNow,
                CaptureMode: CaptureMode,
                WindowTitleContains: string.IsNullOrWhiteSpace(WindowTitleContains) ? null : WindowTitleContains.Trim(),
                CaptureNarration: CaptureNarration,
                MicrophoneDeviceName: string.IsNullOrWhiteSpace(MicrophoneDeviceName) ? null : MicrophoneDeviceName.Trim(),
                QualityPreset: SelectedComposeQualityPreset,
                ExportStyle: SelectedExportStyle,
                AiProvider: string.IsNullOrWhiteSpace(AiNarrationProvider) ? null : AiNarrationProvider.Trim(),
                AiBaseUrl: string.IsNullOrWhiteSpace(AiNarrationBaseUrl) ? null : AiNarrationBaseUrl.Trim(),
                AiModel: string.IsNullOrWhiteSpace(AiNarrationModel) ? null : AiNarrationModel.Trim(),
                AiVoice: string.IsNullOrWhiteSpace(AiNarrationVoice) ? null : AiNarrationVoice.Trim(),
                AiAutoTrimScript: AiAutoTrimScript,
                AiWordsPerSecond: AiWordsPerSecond,
                LastOutputPath: _lastOutputPath,
                Clips: CurrentSessionClips
                    .OrderBy(x => x.Order)
                    .Select(x => new DesktopSessionDraftClip(
                        x.Sequence,
                        x.Order,
                        x.Label,
                        x.BannerText,
                        x.DurationDisplay,
                        x.StartSeconds,
                        x.DurationSeconds,
                        x.IncludeNarration,
                        x.NarrationAudioPath,
                        x.NarrationSource,
                        x.NarrationScript))
                    .ToArray());
            var fingerprint = BuildDraftFingerprint(draft);
            if (string.Equals(fingerprint, _lastDraftFingerprint, StringComparison.Ordinal))
            {
                return;
            }

            await _sessionRecoveryService.SaveAsync(draft);
            _lastDraftFingerprint = fingerprint;
        }
        catch (Exception ex)
        {
            await ReportBackgroundFailureAsync(
                "DS-DESK-RECOVERY-001",
                "Save draft state failed.",
                ex,
                TimeSpan.FromSeconds(30));
        }
    }

    private async Task RestoreDraftStateAsync()
    {
        var draft = await _sessionRecoveryService.TryLoadAsync();
        if (draft is null)
        {
            return;
        }

        CaptureMode = draft.CaptureMode;
        WindowTitleContains = draft.WindowTitleContains ?? string.Empty;
        CaptureNarration = draft.CaptureNarration;
        MicrophoneDeviceName = draft.MicrophoneDeviceName ?? string.Empty;
        SelectedComposeQualityPreset = draft.QualityPreset;
        SelectedExportStyle = draft.ExportStyle;
        AiNarrationProvider = draft.AiProvider ?? "OpenAI";
        AiNarrationBaseUrl = draft.AiBaseUrl ?? string.Empty;
        AiNarrationModel = draft.AiModel ?? "gpt-4o-mini-tts";
        AiNarrationVoice = draft.AiVoice ?? "alloy";
        AiNarrationApiKey = string.Empty;
        AiAutoTrimScript = draft.AiAutoTrimScript;
        AiWordsPerSecond = draft.AiWordsPerSecond <= 0d ? 2.6d : draft.AiWordsPerSecond;

        if (!string.IsNullOrWhiteSpace(draft.LastOutputPath))
        {
            _lastOutputPath = draft.LastOutputPath;
            OnPropertyChanged(nameof(LastOutputPath));
        }

        CurrentSessionClips.Clear();
        foreach (var clip in draft.Clips.OrderBy(x => x.Order))
        {
            CurrentSessionClips.Add(new CurrentSessionClipItem(
                clip.Sequence,
                clip.Label,
                clip.DurationDisplay,
                clip.BannerText ?? string.Empty,
                clip.StartSeconds,
                clip.DurationSeconds,
                clip.NarrationAudioPath)
            {
                Order = clip.Order,
                IncludeNarration = clip.IncludeNarration,
                NarrationSource = clip.NarrationSource ?? string.Empty,
                NarrationScript = clip.NarrationScript ?? string.Empty
            });
        }

        if (CurrentSessionClips.Count > 0)
        {
            IsClipCurationExpanded = true;
            _lastRuntimeMessage = "Recovered previous draft session.";
            OnPropertyChanged(nameof(LastRuntimeMessage));
            _ = GenerateMissingClipThumbnailsAsync(draft.LastOutputPath, draft.SessionId);
        }

        _lastDraftFingerprint = BuildDraftFingerprint(draft);
        RaiseWorkflowAndClipState();
    }

    private async Task ClearDraftStateAsync()
    {
        _lastDraftFingerprint = string.Empty;
        await _sessionRecoveryService.ClearAsync();
    }

    private static string BuildFixHint(string? failureCode)
    {
        return (failureCode ?? string.Empty).ToUpperInvariant() switch
        {
            "DS-DESK-START-001" => "Check target window is visible, then click Refresh and Focus.",
            "DS-DESK-START-002" => "Restart recording app and retry with Desktop mode once.",
            "DS-DESK-STOP-001" => "Wait 3 seconds and press Stop again to finalize file.",
            "DS-DESK-WATCH-001" => "Re-open target window and lock it again before recording.",
            _ => "Open diagnostics bundle and verify FFmpeg path, target window, and permissions."
        };
    }

    private static string BuildDraftFingerprint(DesktopSessionDraft draft)
    {
        var clipPart = string.Join("|", draft.Clips.Select(x =>
            $"{x.Sequence}:{x.Order}:{x.Label}:{x.BannerText}:{x.DurationDisplay}:{x.StartSeconds:0.###}:{x.DurationSeconds:0.###}:{x.IncludeNarration}:{x.NarrationAudioPath}:{x.NarrationSource}:{x.NarrationScript}"));
        return string.Join(";", new[]
        {
            draft.SessionId.ToString("N"),
            draft.State.ToString(),
            draft.CaptureMode,
            draft.WindowTitleContains ?? string.Empty,
            draft.CaptureNarration.ToString(),
            draft.MicrophoneDeviceName ?? string.Empty,
            draft.QualityPreset,
            draft.ExportStyle,
            draft.AiProvider ?? string.Empty,
            draft.AiBaseUrl ?? string.Empty,
            draft.AiModel ?? string.Empty,
            draft.AiVoice ?? string.Empty,
            draft.AiAutoTrimScript.ToString(),
            draft.AiWordsPerSecond.ToString("0.###"),
            draft.LastOutputPath ?? string.Empty,
            clipPart
        });
    }
}
