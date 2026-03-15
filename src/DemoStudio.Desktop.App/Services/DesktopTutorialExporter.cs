using System.IO;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DemoStudio.Desktop.App.Services;

public sealed class DesktopTutorialExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly ILogger<DesktopTutorialExporter> _logger;

    public DesktopTutorialExporter(ILogger<DesktopTutorialExporter> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<TutorialExportResult> ExportTutorialHtml(
        DemoScript script,
        DemoNarration narration,
        string outputPath,
        string title = "Demo Tutorial",
        CancellationToken cancellationToken = default)
    {
        return ExportTutorialHtml(script, narration.Steps, outputPath, title, cancellationToken);
    }

    public async Task<TutorialExportResult> ExportTutorialHtml(
        DemoScript script,
        IReadOnlyList<DemoNarrationStep> narrationSteps,
        string outputPath,
        string title = "Demo Tutorial",
        CancellationToken cancellationToken = default)
    {
        if (script is null)
        {
            throw new ArgumentNullException(nameof(script));
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("Output path is required.", nameof(outputPath));
        }

        var outputFilePath = Path.GetFullPath(outputPath);
        var tutorialDirectory = Path.GetDirectoryName(outputFilePath)
            ?? throw new InvalidOperationException("Output path must include a directory.");
        var imagesDirectory = Path.Combine(tutorialDirectory, "images");
        var assetsDirectory = Path.Combine(tutorialDirectory, "assets");
        Directory.CreateDirectory(tutorialDirectory);
        Directory.CreateDirectory(imagesDirectory);
        Directory.CreateDirectory(assetsDirectory);

        var mergedSteps = await MergeStepsAsync(script.Steps, narrationSteps, imagesDirectory, cancellationToken).ConfigureAwait(false);
        var metadata = new TutorialMetadata(title, mergedSteps);

        var html = BuildHtml(title, mergedSteps);
        var css = BuildCss();
        var metadataJson = JsonSerializer.Serialize(metadata, JsonOptions);

        await File.WriteAllTextAsync(outputFilePath, html, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(assetsDirectory, "style.css"), css, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(tutorialDirectory, "tutorial.json"), metadataJson, Encoding.UTF8, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Tutorial exported to {OutputPath} with {StepCount} steps.",
            outputFilePath,
            mergedSteps.Count);

        return new TutorialExportResult(
            outputFilePath,
            Path.Combine(assetsDirectory, "style.css"),
            Path.Combine(tutorialDirectory, "tutorial.json"),
            mergedSteps);
    }

    private static async Task<IReadOnlyList<TutorialStep>> MergeStepsAsync(
        IReadOnlyList<DemoStep> demoSteps,
        IReadOnlyList<DemoNarrationStep> narrationSteps,
        string imagesDirectory,
        CancellationToken cancellationToken)
    {
        var narrationByStep = (narrationSteps ?? Array.Empty<DemoNarrationStep>())
            .ToDictionary(x => x.StepNumber, x => x);

        var merged = new List<TutorialStep>(demoSteps?.Count ?? 0);
        foreach (var step in demoSteps ?? Array.Empty<DemoStep>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            narrationByStep.TryGetValue(step.StepNumber, out var narration);
            var screenshotRelativePath = await CopyScreenshotAsync(
                narration?.ScreenshotPath ?? step.ScreenshotPath,
                imagesDirectory,
                step.StepNumber,
                cancellationToken).ConfigureAwait(false);

            merged.Add(new TutorialStep(
                StepNumber: step.StepNumber,
                ActionType: step.ActionType,
                Description: step.Description,
                NarrationText: narration?.NarrationText ?? step.Description,
                OptionalCaption: narration?.OptionalCaption,
                Selector: step.Selector,
                Url: step.Url,
                ScreenshotPath: screenshotRelativePath,
                Timestamp: step.Timestamp));
        }

        return merged;
    }

    private static async Task<string?> CopyScreenshotAsync(
        string? sourcePath,
        string imagesDirectory,
        int stepNumber,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return null;
        }

        string fullSourcePath;
        try
        {
            fullSourcePath = Path.GetFullPath(sourcePath);
        }
        catch
        {
            return null;
        }

        if (!File.Exists(fullSourcePath))
        {
            return null;
        }

        var extension = Path.GetExtension(fullSourcePath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".png";
        }

        var destinationFileName = $"step-{stepNumber:000}{extension}";
        var destinationPath = Path.Combine(imagesDirectory, destinationFileName);

        await using var sourceStream = File.OpenRead(fullSourcePath);
        await using var destinationStream = File.Create(destinationPath);
        await sourceStream.CopyToAsync(destinationStream, cancellationToken).ConfigureAwait(false);
        return $"images/{destinationFileName.Replace('\\', '/')}";
    }

    private static string BuildHtml(string title, IReadOnlyList<TutorialStep> steps)
    {
        var escapedTitle = WebUtility.HtmlEncode(title);
        var stepsJson = JsonSerializer.Serialize(steps, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        var builder = new StringBuilder();
        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine("<html lang=\"en\">");
        builder.AppendLine("<head>");
        builder.AppendLine("  <meta charset=\"utf-8\" />");
        builder.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
        builder.AppendLine($"  <title>{escapedTitle}</title>");
        builder.AppendLine("  <link rel=\"stylesheet\" href=\"assets/style.css\" />");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine("  <main class=\"tutorial-shell\">");
        builder.AppendLine("    <header class=\"tutorial-header\">");
        builder.AppendLine($"      <h1>{escapedTitle}</h1>");
        builder.AppendLine("      <p class=\"tutorial-subtitle\">Step-by-step interactive walkthrough</p>");
        builder.AppendLine("    </header>");
        builder.AppendLine("    <section class=\"step-panel\">");
        builder.AppendLine("      <div class=\"step-meta\">");
        builder.AppendLine("        <span id=\"stepIndicator\" class=\"step-indicator\"></span>");
        builder.AppendLine("        <span id=\"stepAction\" class=\"step-action\"></span>");
        builder.AppendLine("      </div>");
        builder.AppendLine("      <h2 id=\"stepTitle\" class=\"step-title\"></h2>");
        builder.AppendLine("      <p id=\"stepNarration\" class=\"step-narration\"></p>");
        builder.AppendLine("      <p id=\"stepCaption\" class=\"step-caption\"></p>");
        builder.AppendLine("      <figure id=\"stepFigure\" class=\"step-figure\" hidden>");
        builder.AppendLine("        <img id=\"stepImage\" class=\"step-image\" alt=\"Tutorial step screenshot\" />");
        builder.AppendLine("      </figure>");
        builder.AppendLine("    </section>");
        builder.AppendLine("    <nav class=\"step-navigation\">");
        builder.AppendLine("      <button id=\"prevButton\" type=\"button\">Previous</button>");
        builder.AppendLine("      <button id=\"nextButton\" type=\"button\">Next</button>");
        builder.AppendLine("    </nav>");
        builder.AppendLine("  </main>");
        builder.AppendLine("  <script>");
        builder.AppendLine($"    const steps = {stepsJson};");
        builder.AppendLine("    let currentIndex = 0;");
        builder.AppendLine("    const indicator = document.getElementById('stepIndicator');");
        builder.AppendLine("    const action = document.getElementById('stepAction');");
        builder.AppendLine("    const title = document.getElementById('stepTitle');");
        builder.AppendLine("    const narration = document.getElementById('stepNarration');");
        builder.AppendLine("    const caption = document.getElementById('stepCaption');");
        builder.AppendLine("    const figure = document.getElementById('stepFigure');");
        builder.AppendLine("    const image = document.getElementById('stepImage');");
        builder.AppendLine("    const prevButton = document.getElementById('prevButton');");
        builder.AppendLine("    const nextButton = document.getElementById('nextButton');");
        builder.AppendLine("    function renderStep(index) {");
        builder.AppendLine("      const step = steps[index];");
        builder.AppendLine("      indicator.textContent = `Step ${step.stepNumber} of ${steps.length}`;");
        builder.AppendLine("      action.textContent = step.actionType;");
        builder.AppendLine("      title.textContent = step.description;");
        builder.AppendLine("      narration.textContent = step.narrationText;");
        builder.AppendLine("      caption.textContent = step.optionalCaption || '';");
        builder.AppendLine("      caption.hidden = !step.optionalCaption;");
        builder.AppendLine("      if (step.screenshotPath) {");
        builder.AppendLine("        image.src = step.screenshotPath;");
        builder.AppendLine("        image.alt = `Screenshot for step ${step.stepNumber}`;");
        builder.AppendLine("        figure.hidden = false;");
        builder.AppendLine("      } else {");
        builder.AppendLine("        image.removeAttribute('src');");
        builder.AppendLine("        figure.hidden = true;");
        builder.AppendLine("      }");
        builder.AppendLine("      prevButton.disabled = index <= 0;");
        builder.AppendLine("      nextButton.disabled = index >= steps.length - 1;");
        builder.AppendLine("    }");
        builder.AppendLine("    prevButton.addEventListener('click', () => {");
        builder.AppendLine("      if (currentIndex > 0) { currentIndex -= 1; renderStep(currentIndex); }");
        builder.AppendLine("    });");
        builder.AppendLine("    nextButton.addEventListener('click', () => {");
        builder.AppendLine("      if (currentIndex < steps.length - 1) { currentIndex += 1; renderStep(currentIndex); }");
        builder.AppendLine("    });");
        builder.AppendLine("    if (steps.length > 0) {");
        builder.AppendLine("      renderStep(currentIndex);");
        builder.AppendLine("    } else {");
        builder.AppendLine("      indicator.textContent = 'No steps available';");
        builder.AppendLine("      action.textContent = '';");
        builder.AppendLine("      title.textContent = 'No tutorial steps were exported.';");
        builder.AppendLine("      narration.textContent = 'Generate a demo script and narration first.';");
        builder.AppendLine("      caption.hidden = true;");
        builder.AppendLine("      figure.hidden = true;");
        builder.AppendLine("      prevButton.disabled = true;");
        builder.AppendLine("      nextButton.disabled = true;");
        builder.AppendLine("    }");
        builder.AppendLine("  </script>");
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");
        return builder.ToString();
    }

    private static string BuildCss()
    {
        return """
body {
  margin: 0;
  font-family: "Segoe UI", Tahoma, Geneva, Verdana, sans-serif;
  background: linear-gradient(180deg, #eef3ff 0%, #f8fbff 100%);
  color: #1f2937;
}

.tutorial-shell {
  max-width: 960px;
  margin: 0 auto;
  padding: 32px 20px 48px;
}

.tutorial-header {
  text-align: center;
  margin-bottom: 28px;
}

.tutorial-header h1 {
  margin: 0;
  font-size: 2.2rem;
}

.tutorial-subtitle {
  margin-top: 8px;
  color: #5b6475;
}

.step-panel {
  background: #ffffff;
  border: 1px solid #d8e2f2;
  border-radius: 18px;
  box-shadow: 0 18px 40px rgba(31, 41, 55, 0.08);
  padding: 28px;
}

.step-meta {
  display: flex;
  justify-content: space-between;
  gap: 12px;
  align-items: center;
  margin-bottom: 12px;
}

.step-indicator {
  font-size: 0.95rem;
  color: #475569;
  font-weight: 600;
}

.step-action {
  background: #dbeafe;
  color: #1d4ed8;
  border-radius: 999px;
  padding: 6px 12px;
  font-size: 0.85rem;
  font-weight: 700;
}

.step-title {
  margin: 0 0 14px;
  font-size: 1.5rem;
}

.step-narration {
  font-size: 1.1rem;
  line-height: 1.7;
  margin: 0 0 12px;
}

.step-caption {
  color: #64748b;
  margin: 0 0 18px;
}

.step-figure {
  margin: 0;
  text-align: center;
}

.step-image {
  max-width: 100%;
  max-height: 520px;
  border-radius: 14px;
  border: 1px solid #d8e2f2;
  box-shadow: 0 14px 30px rgba(15, 23, 42, 0.12);
}

.step-navigation {
  display: flex;
  justify-content: space-between;
  gap: 12px;
  margin-top: 20px;
}

.step-navigation button {
  border: none;
  border-radius: 999px;
  background: #2563eb;
  color: white;
  padding: 12px 20px;
  font-size: 1rem;
  font-weight: 600;
  cursor: pointer;
}

.step-navigation button[disabled] {
  background: #cbd5e1;
  cursor: not-allowed;
}

@media (max-width: 640px) {
  .tutorial-shell {
    padding: 20px 14px 32px;
  }

  .step-panel {
    padding: 20px;
  }

  .step-meta {
    flex-direction: column;
    align-items: flex-start;
  }

  .step-navigation {
    flex-direction: column;
  }

  .step-navigation button {
    width: 100%;
  }
}
""";
    }
}

public sealed record TutorialStep(
    int StepNumber,
    string ActionType,
    string Description,
    string NarrationText,
    string? OptionalCaption,
    string? Selector,
    string? Url,
    string? ScreenshotPath,
    DateTimeOffset Timestamp);

public sealed record TutorialMetadata(
    string Title,
    IReadOnlyList<TutorialStep> Steps);

public sealed record TutorialExportResult(
    string HtmlPath,
    string CssPath,
    string MetadataPath,
    IReadOnlyList<TutorialStep> Steps);
