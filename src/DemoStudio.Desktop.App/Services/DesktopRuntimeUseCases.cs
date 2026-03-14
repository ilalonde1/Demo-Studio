using DemoStudio.Desktop.Core.Sessions;
using DemoStudio.Desktop.App.ViewModels;
using System.Collections.ObjectModel;
using System.IO;

namespace DemoStudio.Desktop.App.Services;

public sealed class DesktopRuntimeInitializationUseCase
{
    public async Task<MainWindowInitializationResult> InitializeAsync(
        IReadOnlyList<(string StepName, Func<Task> Step)> steps,
        Func<string, Exception, Task> onStepFailure)
    {
        var failures = new List<string>();
        foreach (var (stepName, step) in steps)
        {
            try
            {
                await step();
            }
            catch (Exception ex)
            {
                failures.Add($"{stepName}: {ex.Message}");
                await onStepFailure(stepName, ex);
            }
        }

        return failures.Count == 0
            ? MainWindowInitializationResult.Success()
            : new MainWindowInitializationResult(false, failures);
    }
}

public sealed record DesktopPreflightChecksRequest(
    bool IsStageMode,
    Func<bool, Task<bool>> EnsureStageWorkspaceReadyAsync,
    Func<Task<DesktopPreflightReport>> RunPreflightAsync);

public sealed record DesktopPreflightChecksResult(
    DesktopPreflightReport Report,
    string StatusText,
    string RuntimeMessage,
    string? ReadinessLastChecked);

public sealed class DesktopPreflightChecksUseCase
{
    public async Task<DesktopPreflightChecksResult> RunAsync(
        DesktopPreflightChecksRequest request,
        bool updateReadinessTimestamp)
    {
        if (request.IsStageMode)
        {
            _ = await request.EnsureStageWorkspaceReadyAsync(false);
        }

        var report = await request.RunPreflightAsync();
        var status = report.ToDisplayText();
        var checkedText = updateReadinessTimestamp
            ? $"Last checked: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}"
            : null;
        return new DesktopPreflightChecksResult(report, status, status, checkedText);
    }
}

public sealed record DesktopCaptureStartRequest(
    RecorderSessionSnapshot Snapshot,
    bool IsStageMode,
    bool IsWindowMode,
    bool PresenterViewEnabled,
    Func<Task> PrepareForNewCaptureAsync,
    Func<bool, Task<bool>> EnsureStageWorkspaceReadyAsync,
    Action EnsureWindowTargetLockedFromSelection,
    DesktopPreflightChecksRequest PreflightRequest,
    Action<string> SetPreflightStatus,
    Func<CaptureTargetSettings> BuildTargetSettings,
    Func<CaptureTargetSettings, CancellationToken, Task<bool>> TryActivateTargetAsync,
    Func<int, CancellationToken, Task<bool>> RunStartCountdownAsync,
    CaptureSessionViewModel CaptureSession,
    Func<CaptureTargetSettings, Task> StartTargetWatchdogAsync,
    Func<RecorderSessionSnapshot, string?, Task> PersistFinalizedSessionToHistoryAsync,
    Func<Task> RefreshSessionHistoryAsync,
    Func<Task> ClearDraftStateAsync);

public sealed record DesktopCaptureStartResult(
    RecorderSessionSnapshot Snapshot,
    string RuntimeMessage,
    string? LastOutputPath,
    bool ShouldRaiseWorkflowState);

public sealed record DesktopCaptureStopRequest(
    CaptureSessionViewModel CaptureSession,
    Func<Task> StopTargetWatchdogAsync,
    Action EndLiveClipTracking,
    Action<RecorderSessionSnapshot> CaptureCompletedClipMetadata,
    Func<RecorderSessionSnapshot, string?, Task> PersistFinalizedSessionToHistoryAsync,
    Func<string?, Guid, Task> GenerateMissingClipThumbnailsAsync,
    Action ExpandClipCuration,
    Func<Task> ClearDraftStateAsync,
    Func<Task> RefreshSessionHistoryAsync,
    RecorderSessionSnapshot Snapshot,
    Func<Guid> GetSessionId,
    Action<string?> SetLastOutputPath);

public sealed record DesktopCaptureStopResult(
    RecorderSessionSnapshot Snapshot,
    string RuntimeMessage,
    bool ShouldRaiseWorkflowState);

public sealed class DesktopCaptureSessionUseCase
{
    private readonly DesktopPreflightChecksUseCase _preflightChecksUseCase;

    public DesktopCaptureSessionUseCase(DesktopPreflightChecksUseCase preflightChecksUseCase)
    {
        _preflightChecksUseCase = preflightChecksUseCase;
    }

    public async Task<DesktopCaptureStartResult> StartAsync(DesktopCaptureStartRequest request, CancellationToken lifecycleCancellationToken)
    {
        using var startFlowCts = request.CaptureSession.BeginStartFlow(lifecycleCancellationToken);

        try
        {
            if (request.Snapshot.State == RecorderSessionState.Armed)
            {
                await request.PrepareForNewCaptureAsync();

                if (request.IsStageMode)
                {
                    if (!await request.EnsureStageWorkspaceReadyAsync(true))
                    {
                        return new DesktopCaptureStartResult(request.Snapshot, "Stage workspace is unavailable. Open Stage Workspace and retry.", null, false);
                    }
                }
                else
                {
                    request.EnsureWindowTargetLockedFromSelection();
                }

                var preflight = await _preflightChecksUseCase.RunAsync(request.PreflightRequest, updateReadinessTimestamp: false);
                request.SetPreflightStatus(preflight.StatusText);
                if (!preflight.Report.IsReady)
                {
                    return new DesktopCaptureStartResult(request.Snapshot, preflight.RuntimeMessage, null, false);
                }

                var targetSettings = request.BuildTargetSettings();
                if (string.Equals(targetSettings.Mode, "Window", StringComparison.OrdinalIgnoreCase))
                {
                    if (!await request.TryActivateTargetAsync(targetSettings, startFlowCts.Token))
                    {
                        return new DesktopCaptureStartResult(request.Snapshot, string.Empty, null, false);
                    }
                }

                if (!await request.RunStartCountdownAsync(3, startFlowCts.Token))
                {
                    return new DesktopCaptureStartResult(request.Snapshot, string.Empty, null, false);
                }

                var captureStart = await request.CaptureSession.EnsureCaptureStartedAsync(targetSettings, startFlowCts.Token);
                if (!captureStart.Succeeded)
                {
                    var failedReason = $"[DS-DESK-START-001] {captureStart.ErrorMessage ?? "Failed to start capture."}";
                    var failedSnapshot = request.CaptureSession.StopFailed(failedReason);
                    await request.PersistFinalizedSessionToHistoryAsync(failedSnapshot, captureStart.RawVideoPath);
                    await request.RefreshSessionHistoryAsync();
                    await request.ClearDraftStateAsync();
                    var resetSnapshot = request.CaptureSession.Reset();
                    return new DesktopCaptureStartResult(
                        resetSnapshot,
                        $"{failedReason} Session reset. Fix target/runtime issue and retry.",
                        captureStart.RawVideoPath,
                        true);
                }

                if (targetSettings.Mode.Equals("Window", StringComparison.OrdinalIgnoreCase))
                {
                    await request.StartTargetWatchdogAsync(targetSettings);
                }

                var activeSnapshot = request.CaptureSession.StartOrResumeClip();
                return new DesktopCaptureStartResult(
                    activeSnapshot,
                    $"Capture started in {targetSettings.Mode} mode.",
                    captureStart.RawVideoPath,
                    true);
            }

            var resumed = request.CaptureSession.StartOrResumeClip();
            return new DesktopCaptureStartResult(resumed, string.Empty, null, true);
        }
        catch (OperationCanceledException) when (lifecycleCancellationToken.IsCancellationRequested)
        {
            return new DesktopCaptureStartResult(request.Snapshot, string.Empty, null, false);
        }
        catch (OperationCanceledException)
        {
            var failedSnapshot = request.CaptureSession.StopFailed("[DS-DESK-START-003] Capture startup was interrupted before completion.");
            await request.PersistFinalizedSessionToHistoryAsync(failedSnapshot, null);
            await request.RefreshSessionHistoryAsync();
            await request.ClearDraftStateAsync();
            var resetSnapshot = request.CaptureSession.Reset();
            return new DesktopCaptureStartResult(
                resetSnapshot,
                "[DS-DESK-START-003] Capture startup was interrupted before completion. Session reset. Retry after confirming target window is active.",
                null,
                true);
        }
        catch (Exception ex)
        {
            var failedSnapshot = request.CaptureSession.StopFailed($"[DS-DESK-START-002] Start clip failed: {ex.Message}");
            await request.PersistFinalizedSessionToHistoryAsync(failedSnapshot, null);
            await request.RefreshSessionHistoryAsync();
            await request.ClearDraftStateAsync();
            var resetSnapshot = request.CaptureSession.Reset();
            return new DesktopCaptureStartResult(
                resetSnapshot,
                $"[DS-DESK-START-002] Start clip failed: {ex.Message} Session reset. Resolve error details and retry.",
                null,
                true);
        }
        finally
        {
            request.CaptureSession.CompleteStartFlow();
        }
    }

    public async Task<DesktopCaptureStopResult> StopAsync(DesktopCaptureStopRequest request)
    {
        try
        {
            await request.StopTargetWatchdogAsync();
            var stopResult = await request.CaptureSession.StopCaptureAsync();
            request.EndLiveClipTracking();
            if (!stopResult.Succeeded)
            {
                var failedSnapshot = request.CaptureSession.StopFailed($"[DS-DESK-STOP-001] {stopResult.ErrorMessage ?? "Capture stop failed."}");
                request.CaptureCompletedClipMetadata(failedSnapshot);
                await request.PersistFinalizedSessionToHistoryAsync(failedSnapshot, stopResult.RawVideoPath);
                request.ExpandClipCuration();
                await request.ClearDraftStateAsync();
                var resetSnapshot = request.CaptureSession.Reset();
                await request.RefreshSessionHistoryAsync();
                return new DesktopCaptureStopResult(
                    resetSnapshot,
                    $"{failedSnapshot.FailureReason} Open Clip Editor to review clips and build final video.",
                    true);
            }

            var completedSnapshot = request.CaptureSession.StopCompleted();
            request.CaptureCompletedClipMetadata(completedSnapshot);
            if (!string.IsNullOrWhiteSpace(stopResult.RawVideoPath))
            {
                request.SetLastOutputPath(stopResult.RawVideoPath);
            }

            await request.PersistFinalizedSessionToHistoryAsync(completedSnapshot, stopResult.RawVideoPath);
            _ = request.GenerateMissingClipThumbnailsAsync(stopResult.RawVideoPath, request.GetSessionId());
            request.ExpandClipCuration();
            await request.ClearDraftStateAsync();
            var finalSnapshot = request.CaptureSession.Reset();
            await request.RefreshSessionHistoryAsync();
            return new DesktopCaptureStopResult(
                finalSnapshot,
                "Capture process stopped. Open Clip Editor to reorder clips and build your final video.",
                true);
        }
        catch (Exception ex)
        {
            var failedSnapshot = request.CaptureSession.StopFailed($"[DS-DESK-STOP-002] Stop session failed: {ex.Message}");
            request.EndLiveClipTracking();
            return new DesktopCaptureStopResult(
                failedSnapshot,
                failedSnapshot.FailureReason ?? "Stop session failed.",
                true);
        }
    }
}

public sealed record DesktopComposeOutputClip(
    int Order,
    int Sequence,
    string Label,
    string? BannerText,
    double StartSeconds,
    double DurationSeconds,
    string DurationDisplay,
    bool IncludeNarration,
    string? NarrationAudioPath);

public sealed record DesktopComposeOutputRequest(
    Guid SessionId,
    string RawVideoPath,
    IReadOnlyList<DesktopComposeOutputClip> Clips,
    string QualityPreset,
    string ExportStyle,
    CancellationToken CancellationToken);

public sealed record DesktopComposeOutputResult(
    bool Succeeded,
    string ComposeStatus,
    string RuntimeMessage,
    string? OutputPath);

public sealed class DesktopComposeOutputUseCase
{
    private readonly DesktopComposeManifestService _composeManifestService;
    private readonly DesktopVideoComposeService _videoComposeService;
    private readonly DesktopFfmpegOperationQueue _ffmpegOperationQueue;
    private readonly DesktopCaptureRuntime _captureRuntime;

    public DesktopComposeOutputUseCase(
        DesktopComposeManifestService composeManifestService,
        DesktopVideoComposeService videoComposeService,
        DesktopFfmpegOperationQueue ffmpegOperationQueue,
        DesktopCaptureRuntime captureRuntime)
    {
        _composeManifestService = composeManifestService;
        _videoComposeService = videoComposeService;
        _ffmpegOperationQueue = ffmpegOperationQueue;
        _captureRuntime = captureRuntime;
    }

    public async Task<DesktopComposeOutputResult> ComposeAsync(DesktopComposeOutputRequest request)
    {
        var manifest = new DesktopComposeManifest(
            SessionId: request.SessionId,
            RawVideoPath: request.RawVideoPath,
            GeneratedUtc: DateTimeOffset.UtcNow,
            Clips: request.Clips
                .OrderBy(x => x.Order)
                .Select(x => new DesktopComposeClip(
                    x.Order,
                    x.Sequence,
                    x.Label,
                    x.BannerText,
                    x.StartSeconds,
                    x.DurationSeconds,
                    x.DurationDisplay,
                    x.IncludeNarration,
                    x.NarrationAudioPath))
                .ToArray(),
            QualityPreset: request.QualityPreset,
            ExportStyle: request.ExportStyle);

        var manifestResult = _composeManifestService.WriteManifest(manifest);
        if (!manifestResult.Succeeded)
        {
            return new DesktopComposeOutputResult(false, manifestResult.Message, manifestResult.Message, null);
        }

        var queuedCompose = await _ffmpegOperationQueue.EnqueueAsync(
            "Build Final Video",
            ct => _videoComposeService.ComposeAsync(manifest, _captureRuntime.FfmpegPath, ct),
            request.CancellationToken);
        if (!queuedCompose.Accepted || queuedCompose.Value is null)
        {
            var rejection = string.IsNullOrWhiteSpace(queuedCompose.Message)
                ? "Compose skipped: render queue is full."
                : queuedCompose.Message;
            return new DesktopComposeOutputResult(false, rejection, rejection, null);
        }

        var composeResult = queuedCompose.Value;
        var runtimeMessage = queuedCompose.QueueDelay > TimeSpan.FromMilliseconds(200)
            ? $"{composeResult.Message} (queued {queuedCompose.QueueDelay.TotalSeconds:0.0}s)"
            : composeResult.Message;
        return new DesktopComposeOutputResult(
            composeResult.Succeeded,
            composeResult.Message,
            runtimeMessage,
            composeResult.OutputPath);
    }
}

public sealed record DesktopDraftClipState(
    int Sequence,
    int Order,
    string Label,
    string BannerText,
    string DurationDisplay,
    double StartSeconds,
    double DurationSeconds,
    bool IncludeNarration,
    string? NarrationAudioPath,
    string NarrationSource,
    string NarrationScript);

public sealed record DesktopDraftSessionState(
    Guid SessionId,
    RecorderSessionState State,
    string CaptureMode,
    string WindowTitleContains,
    bool CaptureNarration,
    string MicrophoneDeviceName,
    string QualityPreset,
    string ExportStyle,
    string AiProvider,
    string AiBaseUrl,
    string AiModel,
    string AiVoice,
    bool AiAutoTrimScript,
    double AiWordsPerSecond,
    string LastOutputPath,
    IReadOnlyList<DesktopDraftClipState> Clips);

public sealed record DesktopDraftRestoreResult(
    bool Restored,
    DesktopDraftSessionState? Draft,
    string RuntimeMessage,
    string Fingerprint);

public sealed class DesktopDraftSessionUseCase
{
    private readonly DesktopSessionRecoveryService _sessionRecoveryService;

    public DesktopDraftSessionUseCase(DesktopSessionRecoveryService sessionRecoveryService)
    {
        _sessionRecoveryService = sessionRecoveryService;
    }

    public async Task<string> SaveAsync(DesktopDraftSessionState state, string lastFingerprint)
    {
        var draft = new DesktopSessionDraft(
            SessionId: state.SessionId,
            State: state.State,
            SavedUtc: DateTimeOffset.UtcNow,
            CaptureMode: state.CaptureMode,
            WindowTitleContains: string.IsNullOrWhiteSpace(state.WindowTitleContains) ? null : state.WindowTitleContains.Trim(),
            CaptureNarration: state.CaptureNarration,
            MicrophoneDeviceName: string.IsNullOrWhiteSpace(state.MicrophoneDeviceName) ? null : state.MicrophoneDeviceName.Trim(),
            QualityPreset: state.QualityPreset,
            ExportStyle: state.ExportStyle,
            AiProvider: string.IsNullOrWhiteSpace(state.AiProvider) ? null : state.AiProvider.Trim(),
            AiBaseUrl: string.IsNullOrWhiteSpace(state.AiBaseUrl) ? null : state.AiBaseUrl.Trim(),
            AiModel: string.IsNullOrWhiteSpace(state.AiModel) ? null : state.AiModel.Trim(),
            AiVoice: string.IsNullOrWhiteSpace(state.AiVoice) ? null : state.AiVoice.Trim(),
            AiAutoTrimScript: state.AiAutoTrimScript,
            AiWordsPerSecond: state.AiWordsPerSecond,
            LastOutputPath: state.LastOutputPath,
            Clips: state.Clips
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
        if (string.Equals(fingerprint, lastFingerprint, StringComparison.Ordinal))
        {
            return lastFingerprint;
        }

        await _sessionRecoveryService.SaveAsync(draft);
        return fingerprint;
    }

    public async Task<DesktopDraftRestoreResult> RestoreAsync()
    {
        var draft = await _sessionRecoveryService.TryLoadAsync();
        if (draft is null)
        {
            return new DesktopDraftRestoreResult(
                false,
                null,
                _sessionRecoveryService.LastLoadDiagnostic ?? string.Empty,
                string.Empty);
        }

        var restored = new DesktopDraftSessionState(
            draft.SessionId,
            draft.State,
            draft.CaptureMode,
            draft.WindowTitleContains ?? string.Empty,
            draft.CaptureNarration,
            draft.MicrophoneDeviceName ?? string.Empty,
            draft.QualityPreset,
            draft.ExportStyle,
            draft.AiProvider ?? "OpenAI",
            draft.AiBaseUrl ?? string.Empty,
            draft.AiModel ?? "gpt-4o-mini-tts",
            draft.AiVoice ?? "alloy",
            draft.AiAutoTrimScript,
            draft.AiWordsPerSecond <= 0d ? 2.6d : draft.AiWordsPerSecond,
            draft.LastOutputPath ?? string.Empty,
            draft.Clips
                .OrderBy(x => x.Order)
                .Select(x => new DesktopDraftClipState(
                    x.Sequence,
                    x.Order,
                    x.Label,
                    x.BannerText ?? string.Empty,
                    x.DurationDisplay,
                    x.StartSeconds,
                    x.DurationSeconds,
                    x.IncludeNarration,
                    x.NarrationAudioPath,
                    x.NarrationSource ?? string.Empty,
                    x.NarrationScript ?? string.Empty))
                .ToArray());

        var message = string.IsNullOrWhiteSpace(_sessionRecoveryService.LastLoadDiagnostic)
            ? "Recovered previous draft session."
            : $"Recovered previous draft session. {_sessionRecoveryService.LastLoadDiagnostic}";
        return new DesktopDraftRestoreResult(true, restored, message, BuildDraftFingerprint(draft));
    }

    public Task ClearAsync()
    {
        return _sessionRecoveryService.ClearAsync();
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

public sealed class DesktopSessionLifecycleUseCase
{
    public async Task<string> ResetSessionAsync(
        RecorderSessionEngine sessionEngine,
        Func<Task> clearDraftStateAsync,
        Action resetCurationState,
        Action resetSessionState,
        Action resetProductionState)
    {
        resetCurationState();
        resetSessionState();
        resetProductionState();
        _ = sessionEngine.Reset();
        await clearDraftStateAsync();
        return "Session closed.";
    }
}
