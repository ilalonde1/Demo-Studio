using System.Diagnostics;
using System.IO;
using DemoStudio.Desktop.App.Services;
using DemoStudio.Desktop.Core.Sessions;
using System.Windows;

namespace DemoStudio.Desktop.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private async Task CreatePublishPackageAsync()
    {
        if (!CanCreatePublishPackage)
        {
            return;
        }

        SetBusy(true);
        try
        {
            var sourceVideo = _lastOutputPath;
            var record = SessionHistory.FirstOrDefault(x => x.SessionId == _lastFinalizedSessionId)
                         ?? SelectedSessionRecord;

            var request = new DesktopPublishPackageRequest(
                SessionId: _lastFinalizedSessionId == Guid.Empty ? (record?.SessionId ?? Guid.NewGuid()) : _lastFinalizedSessionId,
                SourceVideoPath: sourceVideo,
                OutputRoot: Path.Combine(_captureRuntime.StorageRoot, "publish"),
                Title: record is null ? "DemoStudio Recording" : $"Demo {record.SessionId:N}",
                Description: string.IsNullOrWhiteSpace(record?.ClipSummary) ? "Curated demo output package." : record!.ClipSummary!,
                QualityPreset: SelectedComposeQualityPreset,
                ExportStyle: SelectedExportStyle,
                ClipCount: record?.ClipCount ?? CurrentSessionClips.Count,
                DurationSeconds: record?.DurationSeconds ?? CurrentSessionClips.Sum(x => x.DurationSeconds),
                StartedUtc: record?.StartedUtc ?? DateTimeOffset.UtcNow,
                CompletedUtc: record?.CompletedUtc);

            var queuedPublish = await _ffmpegOperationQueue.EnqueueAsync(
                "Publish Package",
                ct => _publishPackageService.CreateAsync(request, _captureRuntime.FfmpegPath, ct),
                _lifecycleCancellation.Token);
            if (!queuedPublish.Accepted || queuedPublish.Value is null)
            {
                var rejection = string.IsNullOrWhiteSpace(queuedPublish.Message)
                    ? "Publish package skipped: render queue is full."
                    : queuedPublish.Message;
                PublishStatus = rejection;
                _lastRuntimeMessage = rejection;
                SessionHistoryStatus = rejection;
                OnPropertyChanged(nameof(PublishStatus));
                OnPropertyChanged(nameof(LastRuntimeMessage));
                OnPropertyChanged(nameof(SessionHistoryStatus));
                return;
            }

            var result = queuedPublish.Value;
            PublishStatus = result.Message;
            _lastRuntimeMessage = result.Message;
            SessionHistoryStatus = result.Message;
            if (queuedPublish.QueueDelay > TimeSpan.FromMilliseconds(200))
            {
                _lastRuntimeMessage = $"{result.Message} (queued {queuedPublish.QueueDelay.TotalSeconds:0.0}s)";
            }
            if (result.Succeeded && !string.IsNullOrWhiteSpace(result.PackagePath))
            {
                _production.LastPublishPackagePath = result.PackagePath;
                _production.ShareSummary = $"Demo package ready: {Path.GetFileName(result.PackagePath)}";
                _processRunner.StartDetached(new ProcessStartInfo("explorer.exe", $"/select,\"{result.PackagePath}\"")
                {
                    UseShellExecute = true
                });
            }

            OnPropertyChanged(nameof(PublishStatus));
            OnPropertyChanged(nameof(LastRuntimeMessage));
            OnPropertyChanged(nameof(SessionHistoryStatus));
            OnPropertyChanged(nameof(ShareSummary));
            OnPropertyChanged(nameof(CanCopyShareSummary));
            OnPropertyChanged(nameof(CanOpenPublishZip));
        }
        catch (Exception ex)
        {
            PublishStatus = BuildFailureDisplay("DS-DESK-PUB-001", "Publish package failed.", ex.Message);
            _lastRuntimeMessage = PublishStatus;
            SessionHistoryStatus = PublishStatus;
            OnPropertyChanged(nameof(PublishStatus));
            OnPropertyChanged(nameof(LastRuntimeMessage));
            OnPropertyChanged(nameof(SessionHistoryStatus));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void CopyShareSummary()
    {
        try
        {
            var packageName = string.IsNullOrWhiteSpace(_production.LastPublishPackagePath) ? "-" : Path.GetFileName(_production.LastPublishPackagePath);
            var summary = $"{_production.ShareSummary} | Package: {packageName}";
            Clipboard.SetText(summary);
            _lastRuntimeMessage = "Share summary copied to clipboard.";
            OnPropertyChanged(nameof(LastRuntimeMessage));
        }
        catch (Exception ex)
        {
            SetRuntimeFailure("DS-DESK-SHARE-001", "Copy share summary failed.", ex);
        }
    }

    private void OpenPublishZip()
    {
        if (!CanOpenPublishZip)
        {
            return;
        }

        try
        {
            _processRunner.StartDetached(new ProcessStartInfo("explorer.exe", $"/select,\"{_production.LastPublishPackagePath}\"")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            SetRuntimeFailure("DS-DESK-PUB-002", "Open package failed.", ex);
        }
    }

    private void OpenComposeHealth()
    {
        if (!CanOpenComposeHealth)
        {
            _lastRuntimeMessage = "Compose health snapshot not available yet. Build Final Video first.";
            OnPropertyChanged(nameof(LastRuntimeMessage));
            return;
        }

        try
        {
            var healthPath = GetComposeHealthPath();
            _processRunner.StartDetached(new ProcessStartInfo("explorer.exe", $"/select,\"{healthPath}\"")
            {
                UseShellExecute = true
            });
            _lastRuntimeMessage = "Opened compose health snapshot.";
            OnPropertyChanged(nameof(LastRuntimeMessage));
        }
        catch (Exception ex)
        {
            SetRuntimeFailure("DS-DESK-COMP-001", "Open compose health failed.", ex);
        }
    }

    private string GetComposeHealthPath()
    {
        if (!string.IsNullOrWhiteSpace(_lastOutputPath) && _lastOutputPath != "-" && File.Exists(_lastOutputPath))
        {
            var outputDirectory = Path.GetDirectoryName(_lastOutputPath);
            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                return Path.Combine(outputDirectory, "compose-health.json");
            }
        }

        if (!string.IsNullOrWhiteSpace(SelectedSessionRecord?.RawVideoPath))
        {
            var rawDirectory = Path.GetDirectoryName(SelectedSessionRecord.RawVideoPath!);
            if (!string.IsNullOrWhiteSpace(rawDirectory))
            {
                return Path.Combine(rawDirectory, "curated", "compose-health.json");
            }
        }

        return Path.Combine(_captureRuntime.StorageRoot, "compose-health.json");
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

    private string BuildFailureReason(string failureCode, string reason, Exception? exception)
    {
        var cleanReason = string.IsNullOrWhiteSpace(reason) ? "Unknown failure." : reason.Trim();
        _lastFailureCode = failureCode;
        _lastDiagnosticsPath = _diagnosticsBundleService.TryWriteFailureBundle(
            _snapshot.SessionId,
            _captureRuntime.LastRawVideoPath,
            failureCode,
            cleanReason,
            exception,
            BuildTargetSettings(),
            _captureRuntime.FfmpegPath,
            LaunchExecutablePath,
            LaunchArguments,
            LaunchWorkingDirectory);

        var diagnosticsNote = string.IsNullOrWhiteSpace(_lastDiagnosticsPath) ? null : $"Diagnostics: {_lastDiagnosticsPath}";
        return BuildFailureDisplay(failureCode, cleanReason, diagnosticsNote);
    }
}
