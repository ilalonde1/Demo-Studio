using System.Reflection;
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
        }
        finally
        {
            Directory.Delete(root, true);
        }
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
