using DemoStudio.Desktop.App.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace DemoStudio.Desktop.App.Tests;

public sealed class DesktopDemoNarrationGeneratorTests
{
    [Fact]
    public void Generate_CreatesNarrationForNavigateClickAndInputSteps()
    {
        var generator = new DesktopDemoNarrationGenerator(NullLogger<DesktopDemoNarrationGenerator>.Instance);
        var script = new DemoScript(new[]
        {
            new DemoStep(1, DemoActionTypes.Navigate, "Navigate to https://example.com/login", null, "https://example.com/login", "nav.png", DateTimeOffset.Parse("2026-03-14T12:00:00Z")),
            new DemoStep(2, DemoActionTypes.Click, "Click \"Login\"", "#login", "https://example.com/login", "click.png", DateTimeOffset.Parse("2026-03-14T12:00:05Z")),
            new DemoStep(3, DemoActionTypes.Input, "Enter text in username field", "#username", "https://example.com/login", null, DateTimeOffset.Parse("2026-03-14T12:00:06Z"))
        });

        var narration = generator.Generate(script);

        Assert.Equal(3, narration.Count);
        Assert.Equal("First, navigate to https://example.com/login.", narration[0].NarrationText);
        Assert.Equal("Next, click the Login button.", narration[1].NarrationText);
        Assert.Equal("Finally, enter text into the username field.", narration[2].NarrationText);
        Assert.Equal("click.png", narration[1].ScreenshotPath);
    }

    [Fact]
    public void ExportDocumentationMarkdown_WritesTutorialFormat()
    {
        var generator = new DesktopDemoNarrationGenerator(NullLogger<DesktopDemoNarrationGenerator>.Instance);
        var steps = new[]
        {
            new DemoNarrationStep(1, "First, navigate to https://example.com/login.", "Navigate to login", "nav.png"),
            new DemoNarrationStep(2, "Next, click the Login button.", "Click login", "click.png")
        };

        var markdown = generator.ExportDocumentationMarkdown(steps);

        Assert.Contains("# Demo Tutorial", markdown, StringComparison.Ordinal);
        Assert.Contains("Step 1", markdown, StringComparison.Ordinal);
        Assert.Contains("First, navigate to https://example.com/login.", markdown, StringComparison.Ordinal);
        Assert.Contains("Step 2", markdown, StringComparison.Ordinal);
        Assert.Contains("Next, click the Login button.", markdown, StringComparison.Ordinal);
    }
}
