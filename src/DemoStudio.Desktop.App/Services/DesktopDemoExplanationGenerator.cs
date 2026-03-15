using System.Text;
using Microsoft.Extensions.Logging;

namespace DemoStudio.Desktop.App.Services;

public sealed class DesktopDemoExplanationGenerator
{
    private readonly ILogger<DesktopDemoExplanationGenerator> _logger;

    public DesktopDemoExplanationGenerator(ILogger<DesktopDemoExplanationGenerator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IReadOnlyList<DemoDocumentationStep> Generate(DemoScript script)
    {
        if (script is null)
        {
            throw new ArgumentNullException(nameof(script));
        }

        var sourceSteps = script.Steps ?? Array.Empty<DemoStep>();
        var documentationSteps = sourceSteps
            .Select(step => new DemoDocumentationStep(
                step.StepNumber,
                BuildInstructionText(step),
                BuildExplanationText(step),
                step.ScreenshotPath))
            .ToArray();

        _logger.LogInformation(
            "Generated {DocumentationStepCount} explanation steps from {DemoStepCount} demo steps.",
            documentationSteps.Length,
            sourceSteps.Count);

        return documentationSteps;
    }

    public string ExportDocumentationMarkdown(IReadOnlyList<DemoDocumentationStep> steps)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Tutorial Guide");
        builder.AppendLine();

        foreach (var step in steps ?? Array.Empty<DemoDocumentationStep>())
        {
            builder.AppendLine($"Step {step.StepNumber}");
            builder.AppendLine($"Instruction: {step.InstructionText}");
            builder.AppendLine($"Explanation: {step.ExplanationText}");
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildInstructionText(DemoStep step)
    {
        return step.ActionType switch
        {
            DemoActionTypes.Navigate => BuildNavigateInstruction(step),
            DemoActionTypes.Click => BuildClickInstruction(step),
            DemoActionTypes.Input => BuildInputInstruction(step),
            DemoActionTypes.Submit => BuildSubmitInstruction(step),
            _ => step.Description
        };
    }

    private static string BuildExplanationText(DemoStep step)
    {
        return step.ActionType switch
        {
            DemoActionTypes.Navigate => "This step opens the required page in the application.",
            DemoActionTypes.Click => BuildClickExplanation(step),
            DemoActionTypes.Input => BuildInputExplanation(step),
            DemoActionTypes.Submit => "This action submits the entered data to continue.",
            _ => "This step continues the walkthrough."
        };
    }

    private static string BuildNavigateInstruction(DemoStep step)
    {
        if (!string.IsNullOrWhiteSpace(step.Url))
        {
            return $"Navigate to {step.Url}.";
        }

        return "Navigate to the required page.";
    }

    private static string BuildClickInstruction(DemoStep step)
    {
        var target = ExtractQuotedLabel(step.Description)
            ?? ExtractSelectorName(step.Selector)
            ?? "selected control";
        return $"Click the {target}.";
    }

    private static string BuildInputInstruction(DemoStep step)
    {
        var fieldName = ExtractFieldName(step.Description)
            ?? ExtractSelectorName(step.Selector)
            ?? "required field";
        return $"Enter your {fieldName}.";
    }

    private static string BuildSubmitInstruction(DemoStep step)
    {
        var target = ExtractSubmitName(step.Description)
            ?? ExtractSelectorName(step.Selector)
            ?? "form";
        return $"Submit the {target}.";
    }

    private static string BuildClickExplanation(DemoStep step)
    {
        var target = ExtractQuotedLabel(step.Description)
            ?? ExtractSelectorName(step.Selector);
        if (!string.IsNullOrWhiteSpace(target)
            && target.Contains("login", StringComparison.OrdinalIgnoreCase))
        {
            return "Clicking this button begins authentication.";
        }

        return "This interaction activates the selected control.";
    }

    private static string BuildInputExplanation(DemoStep step)
    {
        var target = ExtractFieldName(step.Description)
            ?? ExtractSelectorName(step.Selector)
            ?? string.Empty;

        if (target.Contains("username", StringComparison.OrdinalIgnoreCase))
        {
            return "This identifies your account during login.";
        }

        if (target.Contains("password", StringComparison.OrdinalIgnoreCase))
        {
            return "This secures access to your account.";
        }

        if (target.Contains("email", StringComparison.OrdinalIgnoreCase))
        {
            return "This step enters the required email address for the workflow.";
        }

        return "This step enters the required information into the field.";
    }

    private static string? ExtractQuotedLabel(string? description)
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
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? ExtractFieldName(string? description)
    {
        const string marker = "Enter text in ";
        if (string.IsNullOrWhiteSpace(description) || !description.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var value = description[marker.Length..].Trim();
        if (value.EndsWith(".", StringComparison.Ordinal))
        {
            value = value[..^1].Trim();
        }

        if (value.EndsWith(" field", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^" field".Length].Trim();
        }

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? ExtractSubmitName(string? description)
    {
        const string marker = "Submit ";
        if (string.IsNullOrWhiteSpace(description) || !description.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var value = description[marker.Length..].Trim();
        if (value.EndsWith(".", StringComparison.Ordinal))
        {
            value = value[..^1].Trim();
        }

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

public sealed record DemoDocumentationStep(
    int StepNumber,
    string InstructionText,
    string ExplanationText,
    string? ScreenshotPath);
