using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using DemoStudio.Desktop.App.Infrastructure;
using DemoStudio.Desktop.App.Services;

namespace DemoStudio.Desktop.App.ViewModels;

public sealed class PublishWorkflowViewModel : INotifyPropertyChanged
{
    private readonly DesktopPublishPackageService _publishPackageService;
    private readonly DesktopProcessRunner _processRunner;
    private readonly DesktopFfmpegOperationQueue _ffmpegOperationQueue;
    private readonly DesktopCaptureRuntime _captureRuntime;
    private ProductionWorkspaceViewModel? _production;

    public PublishWorkflowViewModel(
        DesktopPublishPackageService publishPackageService,
        DesktopProcessRunner processRunner,
        DesktopFfmpegOperationQueue ffmpegOperationQueue,
        DesktopCaptureRuntime captureRuntime)
    {
        _publishPackageService = publishPackageService ?? throw new ArgumentNullException(nameof(publishPackageService));
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _ffmpegOperationQueue = ffmpegOperationQueue ?? throw new ArgumentNullException(nameof(ffmpegOperationQueue));
        _captureRuntime = captureRuntime ?? throw new ArgumentNullException(nameof(captureRuntime));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string PublishStatus => _production?.PublishStatus ?? "Publish package not generated.";

    public string ShareSummary => _production?.ShareSummary ?? "Share summary not generated yet.";

    public string LastPublishPackagePath => _production?.LastPublishPackagePath ?? string.Empty;

    public void AttachProductionWorkspace(ProductionWorkspaceViewModel production)
    {
        ArgumentNullException.ThrowIfNull(production);
        if (ReferenceEquals(_production, production))
        {
            return;
        }

        if (_production is not null)
        {
            _production.PropertyChanged -= OnProductionPropertyChanged;
        }

        _production = production;
        _production.PropertyChanged += OnProductionPropertyChanged;
        OnPropertyChanged(nameof(PublishStatus));
        OnPropertyChanged(nameof(ShareSummary));
        OnPropertyChanged(nameof(LastPublishPackagePath));
    }

    public async Task CreatePublishPackageAsync(
        PublishWorkflowContext context,
        CancellationToken cancellationToken,
        Action<bool> setBusy,
        Action<string> setLastRuntimeMessage,
        Action<string> setSessionHistoryStatus,
        Func<string, string, string?, string> buildFailureDisplay)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(setBusy);
        ArgumentNullException.ThrowIfNull(setLastRuntimeMessage);
        ArgumentNullException.ThrowIfNull(setSessionHistoryStatus);
        ArgumentNullException.ThrowIfNull(buildFailureDisplay);

        var production = RequireProduction();
        setBusy(true);
        try
        {
            var sourceVideo = context.LastOutputPath;
            var record = context.SessionHistory.FirstOrDefault(x => x.SessionId == context.LastFinalizedSessionId)
                         ?? context.SelectedSessionRecord;

            var request = new DesktopPublishPackageRequest(
                SessionId: context.LastFinalizedSessionId == Guid.Empty ? (record?.SessionId ?? Guid.NewGuid()) : context.LastFinalizedSessionId,
                SourceVideoPath: sourceVideo,
                OutputRoot: DesktopStoragePaths.GetPublishDirectory(_captureRuntime.StorageRoot),
                Title: record is null ? "DemoStudio Recording" : $"Demo {record.SessionId:N}",
                Description: string.IsNullOrWhiteSpace(record?.ClipSummary) ? "Curated demo output package." : record!.ClipSummary!,
                QualityPreset: context.SelectedComposeQualityPreset,
                ExportStyle: context.SelectedExportStyle,
                ClipCount: record?.ClipCount ?? context.CurrentSessionClips.Count,
                DurationSeconds: record?.DurationSeconds ?? context.CurrentSessionClips.Sum(x => x.DurationSeconds),
                StartedUtc: record?.StartedUtc ?? DateTimeOffset.UtcNow,
                CompletedUtc: record?.CompletedUtc);

            var queuedPublish = await _ffmpegOperationQueue.EnqueueAsync(
                "Publish Package",
                ct => _publishPackageService.CreateAsync(request, _captureRuntime.FfmpegPath, ct),
                cancellationToken);
            if (!queuedPublish.Accepted || queuedPublish.Value is null)
            {
                var rejection = string.IsNullOrWhiteSpace(queuedPublish.Message)
                    ? "Publish package skipped: render queue is full."
                    : queuedPublish.Message;
                production.PublishStatus = rejection;
                setLastRuntimeMessage(rejection);
                setSessionHistoryStatus(rejection);
                return;
            }

            var result = queuedPublish.Value;
            production.PublishStatus = result.Message;
            setLastRuntimeMessage(result.Message);
            setSessionHistoryStatus(result.Message);
            if (queuedPublish.QueueDelay > TimeSpan.FromMilliseconds(200))
            {
                setLastRuntimeMessage($"{result.Message} (queued {queuedPublish.QueueDelay.TotalSeconds:0.0}s)");
            }

            if (result.Succeeded && !string.IsNullOrWhiteSpace(result.PackagePath))
            {
                production.LastPublishPackagePath = result.PackagePath;
                production.ShareSummary = $"Demo package ready: {Path.GetFileName(result.PackagePath)}";
                _processRunner.StartDetached(new ProcessStartInfo("explorer.exe", $"/select,\"{result.PackagePath}\"")
                {
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            var failure = buildFailureDisplay("DS-DESK-PUB-001", "Publish package failed.", ex.Message);
            production.PublishStatus = failure;
            setLastRuntimeMessage(failure);
            setSessionHistoryStatus(failure);
        }
        finally
        {
            setBusy(false);
        }
    }

    public void CopyShareSummary(Action<string> setLastRuntimeMessage, Action<string, string, Exception> setRuntimeFailure)
    {
        ArgumentNullException.ThrowIfNull(setLastRuntimeMessage);
        ArgumentNullException.ThrowIfNull(setRuntimeFailure);

        try
        {
            var packageName = string.IsNullOrWhiteSpace(LastPublishPackagePath) ? "-" : Path.GetFileName(LastPublishPackagePath);
            var summary = $"{ShareSummary} | Package: {packageName}";
            Clipboard.SetText(summary);
            setLastRuntimeMessage("Share summary copied to clipboard.");
        }
        catch (Exception ex)
        {
            setRuntimeFailure("DS-DESK-SHARE-001", "Copy share summary failed.", ex);
        }
    }

    public void OpenPublishZip(Action<string, string, Exception> setRuntimeFailure)
    {
        ArgumentNullException.ThrowIfNull(setRuntimeFailure);

        try
        {
            _processRunner.StartDetached(new ProcessStartInfo("explorer.exe", $"/select,\"{LastPublishPackagePath}\"")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            setRuntimeFailure("DS-DESK-PUB-002", "Open package failed.", ex);
        }
    }

    public void OpenComposeHealth(string healthPath, Action<string> setLastRuntimeMessage, Action<string, string, Exception> setRuntimeFailure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(healthPath);
        ArgumentNullException.ThrowIfNull(setLastRuntimeMessage);
        ArgumentNullException.ThrowIfNull(setRuntimeFailure);

        try
        {
            _processRunner.StartDetached(new ProcessStartInfo("explorer.exe", $"/select,\"{healthPath}\"")
            {
                UseShellExecute = true
            });
            setLastRuntimeMessage("Opened compose health snapshot.");
        }
        catch (Exception ex)
        {
            setRuntimeFailure("DS-DESK-COMP-001", "Open compose health failed.", ex);
        }
    }

    public string GetComposeHealthPath(string? lastOutputPath, DesktopSessionRecord? selectedSessionRecord)
    {
        if (!string.IsNullOrWhiteSpace(lastOutputPath) && lastOutputPath != "-" && File.Exists(lastOutputPath))
        {
            var outputDirectory = Path.GetDirectoryName(lastOutputPath);
            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                return Path.Combine(outputDirectory, "compose-health.json");
            }
        }

        if (!string.IsNullOrWhiteSpace(selectedSessionRecord?.RawVideoPath))
        {
            var rawDirectory = Path.GetDirectoryName(selectedSessionRecord.RawVideoPath!);
            if (!string.IsNullOrWhiteSpace(rawDirectory))
            {
                return DesktopStoragePaths.GetComposeHealthPath(
                    DesktopStoragePaths.GetCuratedDirectory(selectedSessionRecord.RawVideoPath!));
            }
        }

        return Path.Combine(_captureRuntime.StorageRoot, "compose-health.json");
    }

    private ProductionWorkspaceViewModel RequireProduction()
    {
        return _production ?? throw new InvalidOperationException("Publish workflow is not attached to a production workspace.");
    }

    private void OnProductionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(ProductionWorkspaceViewModel.PublishStatus), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(PublishStatus));
        }
        else if (string.Equals(e.PropertyName, nameof(ProductionWorkspaceViewModel.ShareSummary), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(ShareSummary));
        }
        else if (string.Equals(e.PropertyName, nameof(ProductionWorkspaceViewModel.LastPublishPackagePath), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(LastPublishPackagePath));
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record PublishWorkflowContext(
    Guid LastFinalizedSessionId,
    string LastOutputPath,
    string SelectedComposeQualityPreset,
    string SelectedExportStyle,
    IReadOnlyList<CurrentSessionClipItem> CurrentSessionClips,
    IReadOnlyList<DesktopSessionRecord> SessionHistory,
    DesktopSessionRecord? SelectedSessionRecord);
