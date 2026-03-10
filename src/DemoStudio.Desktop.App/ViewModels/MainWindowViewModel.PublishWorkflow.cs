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
            new PublishWorkflowContext(
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

    private void OpenComposeHealth()
    {
        if (!CanOpenComposeHealth)
        {
            _lastRuntimeMessage = "Compose health snapshot not available yet. Build Final Video first.";
            OnPropertyChanged(nameof(LastRuntimeMessage));
            return;
        }

        _publishWorkflow.OpenComposeHealth(
            GetComposeHealthPath(),
            message =>
            {
                _lastRuntimeMessage = message;
                OnPropertyChanged(nameof(LastRuntimeMessage));
            },
            SetRuntimeFailure);
    }

    private string GetComposeHealthPath() =>
        _publishWorkflow.GetComposeHealthPath(_lastOutputPath, SelectedSessionRecord);
}
