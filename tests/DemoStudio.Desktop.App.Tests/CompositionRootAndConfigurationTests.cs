using DemoStudio.Application.Abstractions.System;
using DemoStudio.Desktop.App.Services;
using DemoStudio.Desktop.App.ViewModels;
using DemoStudio.Infrastructure.Process;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DemoStudio.Desktop.App.Tests;

public sealed class CompositionRootAndConfigurationTests
{
    [Fact]
    public void Build_BindsTypedOptionsAndResolvesRuntimeServices()
    {
        using var fixture = DesktopAppSettingsFixture.Create(
            """
            {
              "DesktopRecorder": {
                "StorageRoot": "  recorder-root  ",
                "Capture": {
                  "Enabled": true,
                  "FfmpegPath": "  ffmpeg-custom.exe  ",
                  "FrameRate": 60,
                  "Crf": 18,
                  "MaxDurationSeconds": 3600,
                  "OutputFileExtension": ".mkv",
                  "CaptureMode": "Desktop",
                  "Preset": "fast"
                }
              },
              "Automation": {
                "DesktopEngine": "Stub"
              },
              "Capture": {
                "Provider": "Stub"
              },
              "DemoExecution": {
                "EnableBackgroundPolling": false,
                "PollingIntervalSeconds": 10,
                "MaxRunDurationMinutes": 20,
                "OutputRoot": "./App_Data/DemoRuns"
              }
            }
            """);

        using var provider = DesktopCompositionRoot.Build(fixture.Root);

        var options = provider.GetRequiredService<IOptions<DesktopRecorderOptions>>().Value;
        var logger = provider.GetRequiredService<ILogger<ProcessLauncher>>();
        var processLauncher = provider.GetRequiredService<IProcessLauncher>();
        var runtime = provider.GetRequiredService<DesktopCaptureRuntime>();
        var smokeCheck = provider.GetRequiredService<DesktopSmokeCheckService>();
        var mainWindowViewModel = provider.GetRequiredService<MainWindowViewModel>();
        var targetingUseCase = provider.GetRequiredService<IDesktopTargetingUseCase>();
        var sessionHistoryUseCase = provider.GetRequiredService<IDesktopSessionHistoryUseCase>();
        var publishWorkflowUseCase = provider.GetRequiredService<IDesktopPublishWorkflowUseCase>();

        Assert.Equal("recorder-root", Path.GetFileName(options.StorageRoot));
        Assert.Equal("ffmpeg-custom.exe", options.Capture.FfmpegPath);
        Assert.Equal(60, options.Capture.FrameRate);
        Assert.Equal(".mkv", options.Capture.OutputFileExtension);
        Assert.Equal("Desktop", options.Capture.CaptureMode);
        Assert.NotNull(logger);
        Assert.IsType<ProcessLauncher>(processLauncher);
        Assert.NotNull(runtime);
        Assert.NotNull(smokeCheck);
        Assert.NotNull(mainWindowViewModel);
        Assert.NotNull(targetingUseCase);
        Assert.NotNull(sessionHistoryUseCase);
        Assert.NotNull(publishWorkflowUseCase);
    }

    [Fact]
    public void Build_FailsFast_WhenConfigurationIsInvalid()
    {
        using var fixture = DesktopAppSettingsFixture.Create(
            """
            {
              "DesktopRecorder": {
                "StorageRoot": "recorder-root",
                "Capture": {
                  "Enabled": true,
                  "FfmpegPath": "",
                  "FrameRate": 0,
                  "Crf": 99
                }
              }
            }
            """);

        var ex = Assert.Throws<AggregateException>(() => DesktopCompositionRoot.Build(fixture.Root));

        Assert.Contains("FrameRate", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class DesktopAppSettingsFixture : IDisposable
    {
        private DesktopAppSettingsFixture(string root)
        {
            Root = root;
        }

        public string Root { get; }

        public static DesktopAppSettingsFixture Create(string appSettingsJson)
        {
            var root = Path.Combine(Path.GetTempPath(), "demostudio-app-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "appsettings.json"), appSettingsJson);
            return new DesktopAppSettingsFixture(root);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, true);
            }
        }
    }
}
