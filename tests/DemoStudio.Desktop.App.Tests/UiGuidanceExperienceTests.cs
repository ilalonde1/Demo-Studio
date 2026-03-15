using DemoStudio.Desktop.App.Tests.Helpers;
using DemoStudio.Desktop.App.ViewModels;
using DemoStudio.Desktop.Core.Sessions;

namespace DemoStudio.Desktop.App.Tests;

public sealed class UiGuidanceExperienceTests
{
    [Fact]
    public void WorkflowState_UsesFirstRunFriendlyLabels()
    {
        var workflow = new WorkflowStateViewModel();

        workflow.Refresh(
            RecorderSessionState.Armed,
            failureReason: null,
            clipCount: 0,
            canComposeManifest: false,
            canStartClip: true,
            canPauseClip: false);

        Assert.Equal("Record Demo", workflow.PrimaryWorkflowActionText);
        Assert.Contains("Record Demo", workflow.GuidanceText, StringComparison.Ordinal);

        workflow.Refresh(
            RecorderSessionState.Recording,
            failureReason: null,
            clipCount: 1,
            canComposeManifest: false,
            canStartClip: false,
            canPauseClip: true);

        Assert.Equal("Pause Recording", workflow.PrimaryWorkflowActionText);
        Assert.Contains("Finish Recording", workflow.GuidanceText, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_DefaultGuidance_PointsToRecordDemo()
    {
        var root = Path.Combine(Path.GetTempPath(), "demostudio-ui-guidance-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var viewModel = MainWindowViewModelTestBuilder.CreateMinimal(root);

            Assert.Equal("Record Demo", viewModel.RecordDemoActionText);
            Assert.Equal("Ready to record", viewModel.WorkflowStatusText);
            Assert.Contains("Record Demo", viewModel.NextWorkflowHintText, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public void MainWindow_WithCapturedClips_ShowsRecordingCompleteState()
    {
        var root = Path.Combine(Path.GetTempPath(), "demostudio-ui-guidance-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var viewModel = MainWindowViewModelTestBuilder.CreateMinimal(root);
            viewModel.CurrentSessionClips.Add(new CurrentSessionClipItem(1, "Clip 1", "00:02", string.Empty, 0, 2) { Order = 1 });

            Assert.Equal("Recording complete", viewModel.WorkflowStatusText);

            typeof(MainWindowViewModel)
                .GetField("_lastRuntimeMessage", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(viewModel, "Recording complete. Review the captured clips, then generate your tutorial.");

            Assert.Contains("Recording complete", viewModel.FriendlyRuntimeMessage, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }
}
