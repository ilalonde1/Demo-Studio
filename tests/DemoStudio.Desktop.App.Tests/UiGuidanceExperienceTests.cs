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

    [Fact]
    public void MainWindow_RefreshClearsUnavailableLockedWindow()
    {
        var root = Path.Combine(Path.GetTempPath(), "demostudio-ui-guidance-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var viewModel = MainWindowViewModelTestBuilder.CreateMinimal(root);
            viewModel.CaptureMode = "Window";
            viewModel.WindowHandleHex = "0xDEADBEEF";
            viewModel.WindowTitleContains = "Stale Window";
            viewModel.WindowProcessName = "stale-process";

            typeof(MainWindowViewModel)
                .GetMethod("RefreshWindowCandidates", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(viewModel, null);

            Assert.Equal(string.Empty, viewModel.WindowHandleHex);
            Assert.Equal(string.Empty, viewModel.WindowTitleContains);
            Assert.Equal(string.Empty, viewModel.WindowProcessName);
            Assert.Null(viewModel.SelectedWindowCandidate);
            Assert.False(viewModel.CanStartClip);
            Assert.Contains("Locked target", viewModel.WindowSelectionStatus, StringComparison.Ordinal);
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
    public async Task MainWindow_PreflightWithoutTargetRemainsNeutral()
    {
        var root = Path.Combine(Path.GetTempPath(), "demostudio-ui-guidance-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var viewModel = MainWindowViewModelTestBuilder.CreateMinimal(root);
            viewModel.CaptureMode = "Window";
            viewModel.WindowHandleHex = string.Empty;
            viewModel.WindowTitleContains = string.Empty;
            viewModel.WindowProcessName = string.Empty;
            viewModel.SelectedWindowCandidate = null;

            var task = (Task)typeof(MainWindowViewModel)
                .GetMethod("RunPreflightAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(viewModel, null)!;
            await task;

            Assert.Equal("Setup check not run yet.", viewModel.PreflightStatus);
            Assert.Equal("Select a target window, Stage Workspace, or desktop capture to begin.", viewModel.LastRuntimeMessage);
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
    public void MainWindow_RefreshSelectionDoesNotAutoLockWindowHandle()
    {
        var root = Path.Combine(Path.GetTempPath(), "demostudio-ui-guidance-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var viewModel = MainWindowViewModelTestBuilder.CreateMinimal(root);

            typeof(MainWindowViewModel)
                .GetMethod("RefreshWindowCandidates", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(viewModel, null);

            Assert.Equal(string.Empty, viewModel.WindowHandleHex);
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
    public void MainWindow_SwitchingFromStageToWindow_ClearsStageTargetFields()
    {
        var root = Path.Combine(Path.GetTempPath(), "demostudio-ui-guidance-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var viewModel = MainWindowViewModelTestBuilder.CreateMinimal(root);
            viewModel.CaptureMode = "Stage";
            viewModel.WindowHandleHex = "0x12345";
            viewModel.WindowProcessName = "DemoStudio.Desktop.App";
            viewModel.WindowTitleContains = "DemoStudio Stage Workspace";

            viewModel.CaptureMode = "Window";

            Assert.Equal(string.Empty, viewModel.WindowHandleHex);
            Assert.Equal(string.Empty, viewModel.WindowProcessName);
            Assert.Equal(string.Empty, viewModel.WindowTitleContains);
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
    public void CaptureFlow_RefreshesFriendlyRuntimeMessageAfterCountdown()
    {
        var solutionRoot = FindSolutionRoot();
        var captureViewModelPath = Path.Combine(solutionRoot, "src", "DemoStudio.Desktop.App", "ViewModels", "MainWindowViewModel.Capture.cs");
        var captureViewModel = File.ReadAllText(captureViewModelPath);

        Assert.Contains("RefreshRuntimeStatusMessage();", captureViewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void RecorderHud_StartsExpandedSoPauseAndStopAreVisible()
    {
        var solutionRoot = FindSolutionRoot();
        var hudCodeBehindPath = Path.Combine(solutionRoot, "src", "DemoStudio.Desktop.App", "RecorderHudWindow.xaml.cs");
        var hudCodeBehind = File.ReadAllText(hudCodeBehindPath);

        Assert.Contains("_isCompactMode = false;", hudCodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowCapture_DefaultsUsePaddedCropForContext()
    {
        var solutionRoot = FindSolutionRoot();
        var appSettingsPath = Path.Combine(solutionRoot, "src", "DemoStudio.Desktop.App", "appsettings.json");
        var appSettings = File.ReadAllText(appSettingsPath);

        Assert.Contains("\"CropEnabled\": true", appSettings, StringComparison.Ordinal);
        Assert.Contains("\"CropPaddingPixels\": 48", appSettings, StringComparison.Ordinal);
    }

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DemoStudio.Desktop.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root from the test output directory.");
    }
}
