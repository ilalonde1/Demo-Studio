using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DemoStudio.Desktop.App.Services;

public sealed class DesktopDemoNarrationGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ILogger<DesktopDemoNarrationGenerator> _logger;

    public DesktopDemoNarrationGenerator(ILogger<DesktopDemoNarrationGenerator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IReadOnlyList<DemoNarrationStep> Generate(DemoScript script)
    {
        if (script is null)
        {
            throw new ArgumentNullException(nameof(script));
        }

        var steps = script.Steps ?? Array.Empty<DemoStep>();
        var narration = new List<DemoNarrationStep>(steps.Count);
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            narration.Add(new DemoNarrationStep(
                StepNumber: step.StepNumber,
                NarrationText: BuildNarrationText(step, i, steps.Count),
                OptionalCaption: BuildCaption(step),
                ScreenshotPath: step.ScreenshotPath));
        }

        _logger.LogInformation("Generated {NarrationCount} narration steps from {StepCount} demo steps.", narration.Count, steps.Count);
        return narration;
    }

    public string ExportNarrationScript(IReadOnlyList<DemoNarrationStep> steps)
    {
        var builder = new StringBuilder();
        foreach (var step in steps ?? Array.Empty<DemoNarrationStep>())
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.AppendLine($"Step {step.StepNumber}");
            builder.AppendLine(step.NarrationText);
        }

        return builder.ToString().TrimEnd();
    }

    public string ExportDocumentationMarkdown(IReadOnlyList<DemoNarrationStep> steps)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Demo Tutorial");
        builder.AppendLine();

        foreach (var step in steps ?? Array.Empty<DemoNarrationStep>())
        {
            builder.AppendLine($"Step {step.StepNumber}");
            builder.AppendLine(step.NarrationText);
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    public string ExportNarrationJson(IReadOnlyList<DemoNarrationStep> steps)
    {
        var narration = new DemoNarration(steps ?? Array.Empty<DemoNarrationStep>());
        return JsonSerializer.Serialize(narration, JsonOptions);
    }

    private static string BuildNarrationText(DemoStep step, int index, int totalCount)
    {
        var transition = ResolveTransition(index, totalCount);
        var body = step.ActionType switch
        {
            DemoActionTypes.Navigate => BuildNavigateNarration(step),
            DemoActionTypes.Click => BuildClickNarration(step),
            DemoActionTypes.Input => BuildInputNarration(step),
            DemoActionTypes.Submit => BuildSubmitNarration(step),
            DemoActionTypes.NetworkRequest => BuildDiagnosticNarration(step),
            _ => step.Description
        };

        return $"{transition}, {body}";
    }

    private static string BuildCaption(DemoStep step)
        => step.Description;

    private static string ResolveTransition(int index, int totalCount)
    {
        if (index <= 0)
        {
            return "First";
        }

        if (index >= totalCount - 1)
        {
            return "Finally";
        }

        return index == 1 ? "Next" : "Then";
    }

    private static string BuildNavigateNarration(DemoStep step)
    {
        if (!string.IsNullOrWhiteSpace(step.Url))
        {
            return $"navigate to {step.Url}.";
        }

        return "navigate to the next page.";
    }

    private static string BuildClickNarration(DemoStep step)
    {
        var target = ExtractQuotedTarget(step.Description)
            ?? ExtractSelectorName(step.Selector)
            ?? "button";
        return $"click the {target}.";
    }

    private static string BuildInputNarration(DemoStep step)
    {
        var target = ExtractFieldName(step.Description)
            ?? ExtractSelectorName(step.Selector)
            ?? "field";
        return $"enter text into the {target}.";
    }

    private static string BuildSubmitNarration(DemoStep step)
    {
        var target = ExtractSubmitTarget(step.Description)
            ?? ExtractSelectorName(step.Selector)
            ?? "form";
        return $"submit the {target}.";
    }

    private static string BuildDiagnosticNarration(DemoStep step)
        => $"{step.Description}.";

    private static string? ExtractQuotedTarget(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        var firstQuote = description.IndexOf('"');
        if (firstQuote < 0)
        {
            return null;
        }

        var secondQuote = description.IndexOf('"', firstQuote + 1);
        if (secondQuote <= firstQuote)
        {
            return null;
        }

        var value = description[(firstQuote + 1)..secondQuote].Trim();
        return string.IsNullOrWhiteSpace(value) ? null : $"{value} button";
    }

    private static string? ExtractFieldName(string description)
    {
        const string marker = "Enter text in ";
        if (string.IsNullOrWhiteSpace(description) || !description.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var value = description[marker.Length..].Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? ExtractSubmitTarget(string description)
    {
        const string marker = "Submit ";
        if (string.IsNullOrWhiteSpace(description) || !description.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var value = description[marker.Length..].Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? ExtractSelectorName(string? selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            return null;
        }

        var trimmed = selector.Trim();
        if (trimmed.StartsWith("#", StringComparison.Ordinal))
        {
            return HumanizeIdentifier(trimmed[1..]);
        }

        var tagName = trimmed.Split(new[] { '[', '.', '#', ' ' }, 2, StringSplitOptions.RemoveEmptyEntries)[0];
        return string.IsNullOrWhiteSpace(tagName) ? null : tagName.ToLowerInvariant();
    }

    private static string HumanizeIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return "field";
        }

        var normalized = identifier.Replace("-", " ", StringComparison.Ordinal)
            .Replace("_", " ", StringComparison.Ordinal);
        var builder = new StringBuilder();
        for (var i = 0; i < normalized.Length; i++)
        {
            var current = normalized[i];
            if (i > 0 && char.IsUpper(current) && !char.IsWhiteSpace(normalized[i - 1]))
            {
                builder.Append(' ');
            }

            builder.Append(char.ToLowerInvariant(current));
        }

        return builder.ToString().Trim();
    }
}

public sealed record DemoNarrationStep(
    int StepNumber,
    string NarrationText,
    string? OptionalCaption,
    string? ScreenshotPath);

public sealed record DemoNarration(IReadOnlyList<DemoNarrationStep> Steps);
