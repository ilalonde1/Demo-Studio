using DemoStudio.Desktop.Core.Sessions;

namespace DemoStudio.Desktop.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private async Task StartNewSessionAsync()
    {
        if (!CanStartNewSession)
        {
            return;
        }

        _curation.ResetSessionState();
        _sessionState.ResetForSessionRestart();
        _production.ResetSessionOutputs();
        _snapshot = _sessionEngine.Reset();
        await ClearDraftStateAsync();
        _lastRuntimeMessage = "Started a new session. Previous draft was cleared.";
        RaiseWorkflowAndClipState();
    }

    private async Task SaveSessionDraftAsync()
    {
        if (!CanSaveSessionDraft)
        {
            return;
        }

        await SaveDraftStateAsync();
        _lastRuntimeMessage = "Session draft saved.";
        RaiseWorkflowAndClipState();
    }

    private async Task CloseSessionAsync()
    {
        if (!CanCloseSession)
        {
            return;
        }

        _curation.ResetSessionState();
        _sessionState.ResetForSessionRestart();
        _production.ResetSessionOutputs();
        _snapshot = _sessionEngine.Reset();
        await ClearDraftStateAsync();
        _lastRuntimeMessage = "Session closed.";
        RaiseWorkflowAndClipState();
    }
}
