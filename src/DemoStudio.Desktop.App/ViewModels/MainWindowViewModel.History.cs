using System.IO;
using DemoStudio.Desktop.App.Services;
using DemoStudio.Desktop.Core.Sessions;
using System.Windows;

namespace DemoStudio.Desktop.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private async Task RefreshSessionHistoryAsync()
    {
        var history = await _sessionHistoryUseCase.ListAsync();
        SessionHistory.Clear();
        foreach (var record in history.Records)
        {
            SessionHistory.Add(record);
        }

        SelectedSessionRecord = SessionHistory.FirstOrDefault();
        SessionHistoryStatus = history.Records.Count == 0
            ? "No recorded sessions yet."
            : $"Loaded {history.Records.Count} session records.";
        if (!string.IsNullOrWhiteSpace(history.LoadDiagnostic))
        {
            SessionHistoryStatus += $" Warning: {history.LoadDiagnostic}";
        }
        OnPropertyChanged(nameof(SessionHistoryStatus));
        OnPropertyChanged(nameof(SessionHistory));
        RaiseCommandState();
    }

    private void OpenSelectedSessionFolder()
    {
        if (!CanOpenSelectedSessionFolder || SelectedSessionRecord is null)
        {
            return;
        }

        try
        {
            var rawPath = SelectedSessionRecord.RawVideoPath!;
            var openResult = _shellIntegrationUseCase.RevealPath(rawPath);
            SessionHistoryStatus = openResult.Succeeded
                ? SessionHistoryStatus
                : openResult.Message;
            OnPropertyChanged(nameof(SessionHistoryStatus));
        }
        catch (Exception ex)
        {
            SessionHistoryStatus = BuildFailureDisplay("DS-DESK-HIST-001", "Failed to open session folder.", ex.Message);
            OnPropertyChanged(nameof(SessionHistoryStatus));
        }
    }

    private void OpenLatestOutputFolder()
    {
        try
        {
            var rawPath = _lastOutputPath;
            if (string.IsNullOrWhiteSpace(rawPath) || rawPath == "-")
            {
                SessionHistoryStatus = "No output path is available yet.";
                OnPropertyChanged(nameof(SessionHistoryStatus));
                return;
            }

            var openResult = _shellIntegrationUseCase.RevealPath(rawPath);
            SessionHistoryStatus = openResult.Succeeded
                ? SessionHistoryStatus
                : openResult.Message;
            OnPropertyChanged(nameof(SessionHistoryStatus));
        }
        catch (Exception ex)
        {
            SessionHistoryStatus = BuildFailureDisplay("DS-DESK-HIST-002", "Failed to open latest output.", ex.Message);
            OnPropertyChanged(nameof(SessionHistoryStatus));
        }
    }

    private async Task DeleteSelectedSessionAsync()
    {
        if (!CanDeleteSelectedSession || SelectedSessionRecord is null)
        {
            return;
        }

        var sessionId = SelectedSessionRecord.SessionId;
        var confirm = MessageBox.Show(
            $"Delete session '{sessionId}' and associated artifacts?",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        var result = await _sessionHistoryUseCase.DeleteAsync(sessionId, deleteArtifacts: true);
        if (result.Succeeded)
        {
            SessionHistoryStatus = result.Message;
            await RefreshSessionHistoryAsync();
            return;
        }

        SessionHistoryStatus = result.Message;
        OnPropertyChanged(nameof(SessionHistoryStatus));
    }

    private void CopySelectedFixHint()
    {
        if (!CanCopyFixHint || SelectedSessionRecord is null)
        {
            return;
        }

        try
        {
            Clipboard.SetText(SelectedSessionRecord.FixHint ?? string.Empty);
            _lastRuntimeMessage = "Fix hint copied to clipboard.";
            OnPropertyChanged(nameof(LastRuntimeMessage));
        }
        catch (Exception ex)
        {
            SetRuntimeFailure("DS-DESK-HIST-003", "Copy fix hint failed.", ex);
        }
    }

    private async Task PersistFinalizedSessionToHistoryAsync(RecorderSessionSnapshot finalizedSnapshot, string? preferredRawPath)
    {
        var rawPath = string.IsNullOrWhiteSpace(preferredRawPath)
            ? (finalizedSnapshot.State == RecorderSessionState.Completed ? _lastOutputPath : null)
            : preferredRawPath;
        if (string.IsNullOrWhiteSpace(rawPath) || rawPath == "-" || !File.Exists(rawPath))
        {
            rawPath = null;
        }

        long fileSizeBytes = 0;
        if (!string.IsNullOrWhiteSpace(rawPath))
        {
            fileSizeBytes = new FileInfo(rawPath).Length;
        }

        var effectiveClipCount = Math.Max(finalizedSnapshot.ClipCount, CurrentSessionClips.Count);
        var effectiveDurationSeconds = Math.Max(
            finalizedSnapshot.CapturedDuration.TotalSeconds,
            CurrentSessionClips.Sum(x => Math.Max(0d, x.DurationSeconds)));

        var status = finalizedSnapshot.State switch
        {
            RecorderSessionState.Failed => "Failed",
            RecorderSessionState.Completed when effectiveClipCount > 0 => "Completed",
            RecorderSessionState.Completed => "Cancelled",
            _ => "Cancelled"
        };
        var clipSummary = CurrentSessionClips.Count == 0
            ? null
            : string.Join("; ", CurrentSessionClips.Select(clip =>
            {
                var banner = string.IsNullOrWhiteSpace(clip.BannerText) ? string.Empty : $" [{clip.BannerText.Trim()}]";
                return $"{clip.Order}:{clip.Label}{banner}";
            }));
        var record = new DesktopSessionRecord(
            SessionId: finalizedSnapshot.SessionId,
            Status: status,
            StartedUtc: finalizedSnapshot.StartedUtc,
            CompletedUtc: DateTimeOffset.UtcNow,
            ClipCount: effectiveClipCount,
            ClipSummary: clipSummary,
            DurationSeconds: effectiveDurationSeconds,
            RawVideoPath: rawPath,
            FileSizeBytes: fileSizeBytes,
            FailureReason: status == "Failed" ? finalizedSnapshot.FailureReason : null,
            FailureCode: status == "Failed" ? _lastFailureCode : null,
            DiagnosticsPath: status == "Failed" ? _lastDiagnosticsPath : null,
            FixHint: status == "Failed" ? BuildFixHint(_lastFailureCode) : null);

        await _sessionHistoryUseCase.UpsertAsync(record);
        _lastFinalizedSessionId = finalizedSnapshot.SessionId;
    }

}
