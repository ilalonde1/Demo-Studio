using System.Text.Json;
using DemoStudio.Desktop.App.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace DemoStudio.Desktop.App.Tests;

public sealed class DesktopBrowserInteractionRecorderTests
{
    [Fact]
    public async Task RecordAsync_WritesStructuredBrowserEvent()
    {
        var root = CreateTempRoot();

        try
        {
            var notifier = new RecorderFeedbackNotifier();
            var notifications = 0;
            notifier.StepCaptured += (_, args) =>
            {
                if (args.Source == "browser-interaction")
                {
                    notifications++;
                }
            };

            var recorder = new DesktopBrowserInteractionRecorder(root, NullLogger<DesktopBrowserInteractionRecorder>.Instance, notifier);
            var browserEvent = new BrowserInteractionEvent(
                EventType: BrowserInteractionEventTypes.BrowserClick,
                Selector: "#searchBox",
                Value: null,
                Text: "Search",
                Timestamp: DateTimeOffset.UtcNow,
                Url: "https://www.bing.com/",
                ScreenshotPath: Path.Combine(root, "browser-interactions", "screenshots", "click.png"));

            await recorder.RecordAsync(browserEvent);

            Assert.True(File.Exists(recorder.EventsPath));
            var line = (await File.ReadAllLinesAsync(recorder.EventsPath)).Single();
            using var document = JsonDocument.Parse(line);
            Assert.Equal(BrowserInteractionEventTypes.BrowserClick, document.RootElement.GetProperty("eventType").GetString());
            Assert.Equal("#searchBox", document.RootElement.GetProperty("selector").GetString());
            Assert.Equal("https://www.bing.com/", document.RootElement.GetProperty("url").GetString());
            Assert.Equal(browserEvent.ScreenshotPath, document.RootElement.GetProperty("screenshotPath").GetString());
            Assert.Equal(1, notifications);
        }
        finally
        {
            SafeDelete(root);
        }
    }

    [Fact]
    public void CreateScreenshotPath_UsesBrowserInteractionScreenshotDirectory()
    {
        var root = CreateTempRoot();

        try
        {
            var recorder = new DesktopBrowserInteractionRecorder(root, NullLogger<DesktopBrowserInteractionRecorder>.Instance, new RecorderFeedbackNotifier());

            var path = recorder.CreateScreenshotPath(BrowserInteractionEventTypes.BrowserNavigate, DateTimeOffset.Parse("2026-03-14T12:00:00Z"));

            Assert.Contains(Path.Combine("browser-interactions", "screenshots"), path, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith(".png", path, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            SafeDelete(root);
        }
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "demostudio-browser-recorder-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void SafeDelete(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }
}
