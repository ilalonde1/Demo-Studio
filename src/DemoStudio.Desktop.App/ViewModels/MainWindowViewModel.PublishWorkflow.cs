using DemoStudio.Desktop.App.Services;

namespace DemoStudio.Desktop.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private async Task CreatePublishPackageAsync()
    {
        if (!CanCreatePublishPackage)
        {
            return;
        }

        await _publishWorkflow.CreatePublishPackageAsync(
            new DesktopPublishWorkflowRequest(
                _lastFinalizedSessionId,
                _lastOutputPath,
                SelectedComposeQualityPreset,
                SelectedExportStyle,
                CurrentSessionClips,
                SessionHistory,
                SelectedSessionRecord),
            _lifecycleCancellation.Token,
            SetBusy,
            message =>
            {
                _lastRuntimeMessage = message;
                OnPropertyChanged(nameof(LastRuntimeMessage));
                OnPropertyChanged(nameof(FriendlyRuntimeMessage));
            },
            status =>
            {
                SessionHistoryStatus = status;
                OnPropertyChanged(nameof(SessionHistoryStatus));
            },
            BuildFailureDisplay);
    }

    private void CopyShareSummary() =>
        _publishWorkflow.CopyShareSummary(
            message =>
            {
                _lastRuntimeMessage = message;
                OnPropertyChanged(nameof(LastRuntimeMessage));
                OnPropertyChanged(nameof(FriendlyRuntimeMessage));
            },
            SetRuntimeFailure);

    private void OpenPublishZip()
    {
        if (!CanOpenPublishZip)
        {
            return;
        }

        _publishWorkflow.OpenPublishZip(SetRuntimeFailure);
    }

    private void ViewTutorial()
    {
        if (!CanViewTutorial)
        {
            return;
        }

        _publishWorkflow.ViewTutorial(
            message =>
            {
                _lastRuntimeMessage = message;
                OnPropertyChanged(nameof(LastRuntimeMessage));
                OnPropertyChanged(nameof(FriendlyRuntimeMessage));
            },
            SetRuntimeFailure);
    }

    private void OpenComposeHealth()
    {
        if (!CanOpenComposeHealth)
        {
            _lastRuntimeMessage = "Tutorial diagnostics are not available yet. Generate Tutorial first.";
            OnPropertyChanged(nameof(LastRuntimeMessage));
            OnPropertyChanged(nameof(FriendlyRuntimeMessage));
            return;
        }

        _publishWorkflow.OpenComposeHealth(
            GetComposeHealthPath(),
            message =>
            {
                _lastRuntimeMessage = message;
                OnPropertyChanged(nameof(LastRuntimeMessage));
                OnPropertyChanged(nameof(FriendlyRuntimeMessage));
            },
            SetRuntimeFailure);
    }

    private string GetComposeHealthPath() =>
        _publishWorkflow.GetComposeHealthPath(_lastOutputPath, SelectedSessionRecord);
}
