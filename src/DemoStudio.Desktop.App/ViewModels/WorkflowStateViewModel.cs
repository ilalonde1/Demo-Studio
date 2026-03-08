using System.ComponentModel;
using System.Runtime.CompilerServices;
using DemoStudio.Desktop.Core.Sessions;

namespace DemoStudio.Desktop.App.ViewModels;

public sealed class WorkflowStateViewModel : INotifyPropertyChanged
{
    private string _guidanceText = "Unknown state.";
    private string _workflowStepTitle = "Workflow";
    private string _workflowStepDetail = "Workflow guidance unavailable.";
    private string _primaryWorkflowActionText = "No Primary Action";
    private bool _canExecutePrimaryWorkflow;
    private string _step1Background = "#F3F6FC";
    private string _step2Background = "#F3F6FC";
    private string _step3Background = "#F3F6FC";
    private string _step4Background = "#F3F6FC";
    private string _step1TextColor = "#4D628F";
    private string _step2TextColor = "#4D628F";
    private string _step3TextColor = "#4D628F";
    private string _step4TextColor = "#4D628F";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string GuidanceText
    {
        get => _guidanceText;
        private set => SetField(ref _guidanceText, value);
    }

    public string WorkflowStepTitle
    {
        get => _workflowStepTitle;
        private set => SetField(ref _workflowStepTitle, value);
    }

    public string WorkflowStepDetail
    {
        get => _workflowStepDetail;
        private set => SetField(ref _workflowStepDetail, value);
    }

    public string PrimaryWorkflowActionText
    {
        get => _primaryWorkflowActionText;
        private set => SetField(ref _primaryWorkflowActionText, value);
    }

    public bool CanExecutePrimaryWorkflow
    {
        get => _canExecutePrimaryWorkflow;
        private set => SetField(ref _canExecutePrimaryWorkflow, value);
    }

    public string Step1Background
    {
        get => _step1Background;
        private set => SetField(ref _step1Background, value);
    }

    public string Step2Background
    {
        get => _step2Background;
        private set => SetField(ref _step2Background, value);
    }

    public string Step3Background
    {
        get => _step3Background;
        private set => SetField(ref _step3Background, value);
    }

    public string Step4Background
    {
        get => _step4Background;
        private set => SetField(ref _step4Background, value);
    }

    public string Step1TextColor
    {
        get => _step1TextColor;
        private set => SetField(ref _step1TextColor, value);
    }

    public string Step2TextColor
    {
        get => _step2TextColor;
        private set => SetField(ref _step2TextColor, value);
    }

    public string Step3TextColor
    {
        get => _step3TextColor;
        private set => SetField(ref _step3TextColor, value);
    }

    public string Step4TextColor
    {
        get => _step4TextColor;
        private set => SetField(ref _step4TextColor, value);
    }

    public void Refresh(
        RecorderSessionState state,
        string? failureReason,
        int clipCount,
        bool canComposeManifest,
        bool canStartClip,
        bool canPauseClip)
    {
        GuidanceText = state switch
        {
            RecorderSessionState.Armed => "Prepare target UI, then click Record Now to lock/focus/start in one action.",
            RecorderSessionState.Recording => "Recording in progress. Pause Clip to create a boundary.",
            RecorderSessionState.Paused => "Capture paused. Start Clip to resume with a new segment.",
            RecorderSessionState.Completed => "Session completed.",
            RecorderSessionState.Failed => $"Session failed: {failureReason}",
            _ => "Unknown state."
        };

        WorkflowStepTitle = state switch
        {
            RecorderSessionState.Armed => "Step 1: Lock target and start first clip",
            RecorderSessionState.Recording => "Step 2: Demonstrate feature flow",
            RecorderSessionState.Paused => "Step 3: Label next clip and resume",
            RecorderSessionState.Completed => "Step 4: Build and export final video",
            RecorderSessionState.Failed => "Step 4: Inspect failure code and diagnostics, then retry",
            _ => "Workflow"
        };

        WorkflowStepDetail = state switch
        {
            RecorderSessionState.Armed => "Select capture target, then click the primary button to auto-lock target and begin capture.",
            RecorderSessionState.Recording => "Perform your demo actions. Use primary button to pause and create clip boundaries.",
            RecorderSessionState.Paused => "Update next clip label if needed, then use primary button to resume a new segment.",
            RecorderSessionState.Completed => "Reorder clips, add banners, and click Build Final Video.",
            RecorderSessionState.Failed => "Open diagnostics from session history, resolve issue, then start a new session.",
            _ => "Workflow guidance unavailable."
        };

        PrimaryWorkflowActionText = state switch
        {
            RecorderSessionState.Armed => "Record Now",
            RecorderSessionState.Recording => "Pause Clip",
            RecorderSessionState.Paused => "Resume Clip",
            _ => "No Primary Action"
        };

        CanExecutePrimaryWorkflow = state switch
        {
            RecorderSessionState.Armed => canStartClip,
            RecorderSessionState.Recording => canPauseClip,
            RecorderSessionState.Paused => canStartClip,
            _ => false
        };

        var step1 = state == RecorderSessionState.Armed && clipCount == 0;
        var step2 = state == RecorderSessionState.Recording;
        var step3 = state == RecorderSessionState.Paused || (state == RecorderSessionState.Armed && clipCount > 0);
        var step4 = canComposeManifest;
        ApplyStepStyles(step1, step2, step3, step4);
    }

    private void ApplyStepStyles(bool step1, bool step2, bool step3, bool step4)
    {
        Step1Background = step1 ? "#D9E8FF" : "#F3F6FC";
        Step2Background = step2 ? "#D9E8FF" : "#F3F6FC";
        Step3Background = step3 ? "#D9E8FF" : "#F3F6FC";
        Step4Background = step4 ? "#D9E8FF" : "#F3F6FC";
        Step1TextColor = step1 ? "#173C8E" : "#4D628F";
        Step2TextColor = step2 ? "#173C8E" : "#4D628F";
        Step3TextColor = step3 ? "#173C8E" : "#4D628F";
        Step4TextColor = step4 ? "#173C8E" : "#4D628F";
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
