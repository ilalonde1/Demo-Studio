using System.Reflection;
using DemoStudio.Desktop.App;
using DemoStudio.Desktop.App.Services;
using DemoStudio.Desktop.App.Tests.Helpers;
using DemoStudio.Desktop.App.ViewModels;
using DemoStudio.Desktop.Core.Sessions;
using DemoStudio.Infrastructure.Execution;

namespace DemoStudio.Desktop.App.Tests;

public sealed class RecorderHudExperienceTests
{
    [Fact]
    public void RecorderHudVisibility_FollowsRecordingState()
    {
        var root = CreateRoot();

        try
        {
            var viewModel = MainWindowViewModelTestBuilder.CreateMinimal(root);

            Assert.False(viewModel.ShouldShowRecorderHud);

            SetSnapshot(viewModel, RecorderSessionState.Recording);
            Assert.True(viewModel.ShouldShowRecorderHud);
            Assert.True(viewModel.CanAddMarker);

            SetSnapshot(viewModel, RecorderSessionState.Paused);
            Assert.True(viewModel.ShouldShowRecorderHud);
            Assert.Equal("Resume", viewModel.HudRecordActionText);

            SetSnapshot(viewModel, RecorderSessionState.Completed);
            Assert.False(viewModel.ShouldShowRecorderHud);
            Assert.False(viewModel.CanAddMarker);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task AddMarkerAsync_WritesTimelineMarkerForCurrentRecording()
    {
        var root = CreateRoot();

        try
        {
            var viewModel = MainWindowViewModelTestBuilder.CreateMinimal(root);
            var notifier = (RecorderFeedbackNotifier)typeof(MainWindowViewModel)
                .GetField("_feedbackNotifier", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(viewModel)!;
            var notifications = 0;
            notifier.StepCaptured += (_, args) =>
            {
                if (args.Source == "marker")
                {
                    notifications++;
                }
            };
            var recordingDirectory = Path.Combine(root, "recording-session");
            Directory.CreateDirectory(recordingDirectory);
            var rawVideoPath = Path.Combine(recordingDirectory, "capture.mp4");
            File.WriteAllText(rawVideoPath, "stub");

            SetSnapshot(viewModel, RecorderSessionState.Recording);
            SetRuntimeLastRawVideoPath(viewModel, rawVideoPath);

            var addMarker = typeof(MainWindowViewModel)
                .GetMethod("AddMarkerAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;

            var task = (Task)addMarker.Invoke(viewModel, null)!;
            await task;

            var timelinePath = Path.Combine(recordingDirectory, "timeline.json");
            Assert.True(File.Exists(timelinePath));
            var timeline = File.ReadAllText(timelinePath);
            Assert.Contains("UserStepMarker", timeline, StringComparison.Ordinal);
            Assert.Equal(1, notifications);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void RecorderHudWindow_NoLongerContainsPollingFeedbackFields()
    {
        var fields = typeof(RecorderHudWindow)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Select(field => field.Name)
            .ToArray();

        Assert.DoesNotContain("_feedbackPollTimer", fields);
        Assert.DoesNotContain("_lastTimelinePath", fields);
        Assert.DoesNotContain("_lastBrowserInteractionsWriteTicks", fields);
    }

    [Fact]
    public void RecorderHudFeedbackState_IncrementsAndResetsStepCount()
    {
        var state = new RecorderHudFeedbackState();

        Assert.Equal("Step 1 captured", state.RegisterStepCapture());
        Assert.Equal("Step 2 captured", state.RegisterStepCapture());
        Assert.Equal(2, state.CurrentStepNumber);

        state.ResetSession();

        Assert.Equal(0, state.CurrentStepNumber);
        Assert.Equal("Step 1 captured", state.RegisterStepCapture());
    }

    [Fact]
    public void RecorderHud_TitleAndNoiseFilter_UseConsistentName()
    {
        var solutionRoot = FindSolutionRoot();
        var hudXamlPath = Path.Combine(solutionRoot, "src", "DemoStudio.Desktop.App", "RecorderHudWindow.xaml");
        var windowCatalogPath = Path.Combine(solutionRoot, "src", "DemoStudio.Desktop.App", "Services", "DesktopWindowCatalogService.cs");

        var hudXaml = File.ReadAllText(hudXamlPath);
        var windowCatalog = File.ReadAllText(windowCatalogPath);

        Assert.Contains("Title=\"Recorder HUD\"", hudXaml, StringComparison.Ordinal);
        Assert.Contains("Recorder HUD", windowCatalog, StringComparison.Ordinal);
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

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "demostudio-hud-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void SetSnapshot(MainWindowViewModel viewModel, RecorderSessionState state)
    {
        var now = DateTimeOffset.UtcNow;
        var clips = state == RecorderSessionState.Completed
            ? new[] { new RecorderClip(1, now.AddSeconds(-5), now.AddSeconds(-1)) }
            : Array.Empty<RecorderClip>();
        var snapshot = new RecorderSessionSnapshot(
            Guid.NewGuid(),
            state,
            now.AddMinutes(-1),
            state == RecorderSessionState.Recording ? now.AddSeconds(-3) : null,
            clips,
            null);

        typeof(MainWindowViewModel)
            .GetField("_snapshot", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(viewModel, snapshot);
    }

    private static void SetRuntimeLastRawVideoPath(MainWindowViewModel viewModel, string rawVideoPath)
    {
        var runtimeField = typeof(MainWindowViewModel)
            .GetField("_captureRuntime", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var runtime = runtimeField.GetValue(viewModel)!;
        typeof(DesktopCaptureRuntime)
            .GetField("<LastRawVideoPath>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(runtime, rawVideoPath);
    }
}
