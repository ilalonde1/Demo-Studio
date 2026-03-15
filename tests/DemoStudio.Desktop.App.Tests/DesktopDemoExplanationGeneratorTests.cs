using DemoStudio.Desktop.App.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace DemoStudio.Desktop.App.Tests;

public sealed class DesktopDemoExplanationGeneratorTests
{
    [Fact]
    public void Generate_CreatesInstructionAndExplanationForNavigateClickInputAndSubmit()
    {
        var generator = new DesktopDemoExplanationGenerator(NullLogger<DesktopDemoExplanationGenerator>.Instance);
        var script = new DemoScript(new[]
        {
            new DemoStep(1, DemoActionTypes.Navigate, "Navigate to https://example.com/login", null, "https://example.com/login", "nav.png", DateTimeOffset.Parse("2026-03-14T12:00:00Z")),
            new DemoStep(2, DemoActionTypes.Click, "Click \"Login\"", "#login", "https://example.com/login", "click.png", DateTimeOffset.Parse("2026-03-14T12:00:05Z")),
            new DemoStep(3, DemoActionTypes.Input, "Enter text in username field", "#username", "https://example.com/login", null, DateTimeOffset.Parse("2026-03-14T12:00:06Z")),
            new DemoStep(4, DemoActionTypes.Submit, "Submit login form", "#loginForm", "https://example.com/login", null, DateTimeOffset.Parse("2026-03-14T12:00:07Z"))
        });

        var documentation = generator.Generate(script);

        Assert.Equal(4, documentation.Count);

        Assert.Equal("Navigate to https://example.com/login.", documentation[0].InstructionText);
        Assert.Equal("This step opens the required page in the application.", documentation[0].ExplanationText);

        Assert.Equal("Click the Login.", documentation[1].InstructionText);
        Assert.Equal("Clicking this button begins authentication.", documentation[1].ExplanationText);

        Assert.Equal("Enter your username.", documentation[2].InstructionText);
        Assert.Equal("This identifies your account during login.", documentation[2].ExplanationText);

        Assert.Equal("Submit the login form.", documentation[3].InstructionText);
        Assert.Equal("This action submits the entered data to continue.", documentation[3].ExplanationText);
    }

    [Fact]
    public void ExportDocumentationMarkdown_WritesGuideFormat()
    {
        var generator = new DesktopDemoExplanationGenerator(NullLogger<DesktopDemoExplanationGenerator>.Instance);
        var steps = new[]
        {
            new DemoDocumentationStep(1, "Navigate to https://example.com/login.", "This step opens the required page in the application.", "nav.png"),
            new DemoDocumentationStep(2, "Click the Login.", "Clicking this button begins authentication.", "click.png")
        };

        var markdown = generator.ExportDocumentationMarkdown(steps);

        Assert.Contains("# Tutorial Guide", markdown, StringComparison.Ordinal);
        Assert.Contains("Step 1", markdown, StringComparison.Ordinal);
        Assert.Contains("Instruction: Navigate to https://example.com/login.", markdown, StringComparison.Ordinal);
        Assert.Contains("Explanation: This step opens the required page in the application.", markdown, StringComparison.Ordinal);
        Assert.Contains("Step 2", markdown, StringComparison.Ordinal);
    }
}
