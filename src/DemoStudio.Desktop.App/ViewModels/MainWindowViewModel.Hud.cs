using System.IO;
using Microsoft.Extensions.Logging;

namespace DemoStudio.Desktop.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private async Task AddMarkerAsync()
    {
        if (!CanAddMarker)
        {
            return;
        }

        var markerDirectory = ResolveMarkerOutputDirectory();
        if (string.IsNullOrWhiteSpace(markerDirectory))
        {
            _lastRuntimeMessage = "Step marker could not be added because the recording output path is not ready yet.";
            OnPropertyChanged(nameof(LastRuntimeMessage));
            OnPropertyChanged(nameof(FriendlyRuntimeMessage));
            return;
        }

        try
        {
            await _timelineMarkerWriter.WriteStageMarkerAsync(
                markerDirectory,
                $"UserStepMarker-{DateTimeOffset.UtcNow:HHmmss}",
                DateTimeOffset.UtcNow,
                _lifecycleCancellation.Token);

            _feedbackNotifier.NotifyStepCaptured("marker");
            _lastRuntimeMessage = "Step marker added to the current recording.";
            OnPropertyChanged(nameof(LastRuntimeMessage));
            OnPropertyChanged(nameof(FriendlyRuntimeMessage));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Recorder HUD marker write failed for session {SessionId}.", _snapshot.SessionId);
            _lastRuntimeMessage = "Step marker could not be saved for this recording.";
            OnPropertyChanged(nameof(LastRuntimeMessage));
            OnPropertyChanged(nameof(FriendlyRuntimeMessage));
        }
    }

    private string? ResolveMarkerOutputDirectory()
    {
        var candidatePath = !string.IsNullOrWhiteSpace(_captureRuntime.LastRawVideoPath)
            ? _captureRuntime.LastRawVideoPath
            : _lastOutputPath;

        if (string.IsNullOrWhiteSpace(candidatePath) || candidatePath == "-")
        {
            return null;
        }

        var fullPath = Path.GetFullPath(candidatePath);
        return Path.GetDirectoryName(fullPath);
    }
}
