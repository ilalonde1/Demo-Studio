using DemoStudio.Desktop.App.Services;
using DemoStudio.Desktop.Core.Sessions;

namespace DemoStudio.Desktop.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private async Task SaveDraftStateAsync()
    {
        try
        {
            _lastDraftFingerprint = await _draftSessionUseCase.SaveAsync(
                new DesktopDraftSessionState(
                    _snapshot.SessionId,
                    _snapshot.State,
                    CaptureMode,
                    WindowTitleContains,
                    CaptureNarration,
                    MicrophoneDeviceName,
                    SelectedComposeQualityPreset,
                    SelectedExportStyle,
                    AiNarrationProvider,
                    AiNarrationBaseUrl,
                    AiNarrationModel,
                    AiNarrationVoice,
                    AiAutoTrimScript,
                    AiWordsPerSecond,
                    _lastOutputPath,
                    CurrentSessionClips
                        .OrderBy(x => x.Order)
                        .Select(x => new DesktopDraftClipState(
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
                        .ToArray()),
                _lastDraftFingerprint);
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
        var restore = await _draftSessionUseCase.RestoreAsync();
        if (!restore.Restored || restore.Draft is null)
        {
            if (!string.IsNullOrWhiteSpace(restore.RuntimeMessage))
            {
                _lastRuntimeMessage = restore.RuntimeMessage;
                OnPropertyChanged(nameof(LastRuntimeMessage));
            }
            return;
        }

        var draft = restore.Draft;
        CaptureMode = draft.CaptureMode;
        WindowTitleContains = draft.WindowTitleContains;
        CaptureNarration = draft.CaptureNarration;
        MicrophoneDeviceName = draft.MicrophoneDeviceName;
        SelectedComposeQualityPreset = draft.QualityPreset;
        SelectedExportStyle = draft.ExportStyle;
        AiNarrationProvider = draft.AiProvider;
        AiNarrationBaseUrl = draft.AiBaseUrl;
        AiNarrationModel = draft.AiModel;
        AiNarrationVoice = draft.AiVoice;
        AiNarrationApiKey = string.Empty;
        AiAutoTrimScript = draft.AiAutoTrimScript;
        AiWordsPerSecond = draft.AiWordsPerSecond;

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
            _lastRuntimeMessage = restore.RuntimeMessage;
            OnPropertyChanged(nameof(LastRuntimeMessage));
            _ = GenerateMissingClipThumbnailsAsync(draft.LastOutputPath, draft.SessionId);
        }

        _lastDraftFingerprint = restore.Fingerprint;
        RaiseWorkflowAndClipState();
    }

    private async Task ClearDraftStateAsync()
    {
        _lastDraftFingerprint = string.Empty;
        await _draftSessionUseCase.ClearAsync();
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

}
