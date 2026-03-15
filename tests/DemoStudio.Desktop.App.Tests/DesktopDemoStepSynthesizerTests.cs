using DemoStudio.Desktop.App.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace DemoStudio.Desktop.App.Tests;

public sealed class DesktopDemoStepSynthesizerTests
{
    [Fact]
    public async Task SynthesizeAsync_ConvertsNavigateClickAndInputEventsIntoDemoSteps()
    {
        var root = CreateTempRoot();

        try
        {
            var recorder = new DesktopBrowserInteractionRecorder(root, NullLogger<DesktopBrowserInteractionRecorder>.Instance);
            var synthesizer = new DesktopDemoStepSynthesizer(root, NullLogger<DesktopDemoStepSynthesizer>.Instance);

            await recorder.RecordAsync(new BrowserInteractionEvent(
                EventType: BrowserInteractionEventTypes.BrowserNavigate,
                Selector: null,
                Value: null,
                Text: null,
                Timestamp: DateTimeOffset.Parse("2026-03-14T12:00:00Z"),
                Url: "https://example.com/login",
                ScreenshotPath: Path.Combine(root, "browser-interactions", "screenshots", "nav.png")));
            await recorder.RecordAsync(new BrowserInteractionEvent(
                EventType: BrowserInteractionEventTypes.BrowserClick,
                Selector: "#login",
                Value: null,
                Text: "Login",
                Timestamp: DateTimeOffset.Parse("2026-03-14T12:00:05Z"),
                Url: "https://example.com/login",
                ScreenshotPath: Path.Combine(root, "browser-interactions", "screenshots", "click.png")));
            await recorder.RecordAsync(new BrowserInteractionEvent(
                EventType: BrowserInteractionEventTypes.BrowserInput,
                Selector: "#username",
                Value: "alice",
                Text: null,
                Timestamp: DateTimeOffset.Parse("2026-03-14T12:00:06Z"),
                Url: "https://example.com/login"));

            var steps = await synthesizer.SynthesizeAsync();

            Assert.Equal(3, steps.Count);

            Assert.Equal(1, steps[0].StepNumber);
            Assert.Equal(DemoActionTypes.Navigate, steps[0].ActionType);
            Assert.Equal("Navigate to https://example.com/login", steps[0].Description);
            Assert.EndsWith("nav.png", steps[0].ScreenshotPath, StringComparison.OrdinalIgnoreCase);

            Assert.Equal(2, steps[1].StepNumber);
            Assert.Equal(DemoActionTypes.Click, steps[1].ActionType);
            Assert.Equal("Click \"Login\"", steps[1].Description);
            Assert.Equal("#login", steps[1].Selector);

            Assert.Equal(3, steps[2].StepNumber);
            Assert.Equal(DemoActionTypes.Input, steps[2].ActionType);
            Assert.Equal("Enter text in username field", steps[2].Description);
            Assert.Equal("#username", steps[2].Selector);
        }
        finally
        {
            SafeDelete(root);
        }
    }

    [Fact]
    public void ExportMarkdown_WritesOrderedWalkthrough()
    {
        var root = CreateTempRoot();

        try
        {
            var synthesizer = new DesktopDemoStepSynthesizer(root, NullLogger<DesktopDemoStepSynthesizer>.Instance);
            var steps = new[]
            {
                new DemoStep(1, DemoActionTypes.Navigate, "Navigate to https://example.com", null, "https://example.com", null, DateTimeOffset.UtcNow),
                new DemoStep(2, DemoActionTypes.Click, "Click \"Login\"", "#login", "https://example.com", null, DateTimeOffset.UtcNow)
            };

            var markdown = synthesizer.ExportMarkdown(steps);

            Assert.Contains("# Demo Walkthrough", markdown, StringComparison.Ordinal);
            Assert.Contains("1. Navigate to https://example.com", markdown, StringComparison.Ordinal);
            Assert.Contains("2. Click \"Login\"", markdown, StringComparison.Ordinal);
        }
        finally
        {
            SafeDelete(root);
        }
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "demostudio-step-synthesizer-tests", Guid.NewGuid().ToString("N"));
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
