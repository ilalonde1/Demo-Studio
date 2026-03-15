using System.Text.Json;
using DemoStudio.Desktop.App.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace DemoStudio.Desktop.App.Tests;

public sealed class DesktopTutorialExporterTests
{
    [Fact]
    public async Task ExportTutorialHtml_CreatesHtmlCssMetadataAndEmbeddedScreenshots()
    {
        var root = CreateTempRoot();
        var screenshotsRoot = Path.Combine(root, "source-shots");
        Directory.CreateDirectory(screenshotsRoot);
        var screenshotOne = Path.Combine(screenshotsRoot, "step1.png");
        var screenshotTwo = Path.Combine(screenshotsRoot, "step2.png");
        await File.WriteAllTextAsync(screenshotOne, "image-one");
        await File.WriteAllTextAsync(screenshotTwo, "image-two");

        try
        {
            var exporter = new DesktopTutorialExporter(NullLogger<DesktopTutorialExporter>.Instance);
            var script = new DemoScript(new[]
            {
                new DemoStep(1, DemoActionTypes.Navigate, "Navigate to https://example.com/login", null, "https://example.com/login", screenshotOne, DateTimeOffset.Parse("2026-03-14T12:00:00Z")),
                new DemoStep(2, DemoActionTypes.Click, "Click \"Login\"", "#login", "https://example.com/login", screenshotTwo, DateTimeOffset.Parse("2026-03-14T12:00:05Z")),
                new DemoStep(3, DemoActionTypes.Input, "Enter text in username field", "#username", "https://example.com/login", null, DateTimeOffset.Parse("2026-03-14T12:00:07Z"))
            });
            var narration = new DemoNarration(new[]
            {
                new DemoNarrationStep(1, "First, navigate to https://example.com/login.", "Navigate to login", screenshotOne),
                new DemoNarrationStep(2, "Next, click the Login button.", "Click login", screenshotTwo),
                new DemoNarrationStep(3, "Finally, enter text into the username field.", "Enter username", null)
            });

            var outputPath = Path.Combine(root, "export", "tutorial.html");
            var result = await exporter.ExportTutorialHtml(script, narration, outputPath);

            Assert.True(File.Exists(result.HtmlPath));
            Assert.True(File.Exists(result.CssPath));
            Assert.True(File.Exists(result.MetadataPath));
            Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(result.HtmlPath)!, "images", "step-001.png")));
            Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(result.HtmlPath)!, "images", "step-002.png")));

            var html = await File.ReadAllTextAsync(result.HtmlPath);
            Assert.Contains("Demo Tutorial", html, StringComparison.Ordinal);
            Assert.Contains("Step-by-step interactive walkthrough", html, StringComparison.Ordinal);
            Assert.Contains("images/step-001.png", html, StringComparison.Ordinal);
            Assert.Contains("Previous", html, StringComparison.Ordinal);
            Assert.Contains("Next", html, StringComparison.Ordinal);

            var metadataJson = await File.ReadAllTextAsync(result.MetadataPath);
            using var document = JsonDocument.Parse(metadataJson);
            Assert.Equal("Demo Tutorial", document.RootElement.GetProperty("title").GetString());
            Assert.Equal(3, document.RootElement.GetProperty("steps").GetArrayLength());
        }
        finally
        {
            SafeDelete(root);
        }
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "demostudio-tutorial-exporter-tests", Guid.NewGuid().ToString("N"));
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
