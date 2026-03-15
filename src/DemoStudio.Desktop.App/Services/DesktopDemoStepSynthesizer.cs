using System.IO;
using System.Text;
using System.Text.Json;
using DemoStudio.Desktop.App.Infrastructure;
using Microsoft.Extensions.Logging;

namespace DemoStudio.Desktop.App.Services;

public sealed class DesktopDemoStepSynthesizer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _defaultEventsPath;
    private readonly ILogger<DesktopDemoStepSynthesizer> _logger;

    public DesktopDemoStepSynthesizer(string storageRoot, ILogger<DesktopDemoStepSynthesizer> logger)
    {
        if (string.IsNullOrWhiteSpace(storageRoot))
        {
            throw new ArgumentException("Storage root is required.", nameof(storageRoot));
        }

        _defaultEventsPath = DesktopStoragePaths.GetBrowserInteractionsLogPath(storageRoot);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<IReadOnlyList<DemoStep>> SynthesizeAsync(bool includeDiagnostics = false, CancellationToken cancellationToken = default)
        => SynthesizeAsync(_defaultEventsPath, includeDiagnostics, cancellationToken);

    public async Task<IReadOnlyList<DemoStep>> SynthesizeAsync(string eventsPath, bool includeDiagnostics = false, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(eventsPath))
        {
            throw new ArgumentException("Events path is required.", nameof(eventsPath));
        }

        if (!File.Exists(eventsPath))
        {
            _logger.LogInformation("Browser interaction log not found at {EventsPath}.", eventsPath);
            return Array.Empty<DemoStep>();
        }

        var rawEvents = await LoadEventsAsync(eventsPath, cancellationToken).ConfigureAwait(false);
        var orderedEvents = rawEvents
            .OrderBy(x => x.Timestamp)
            .ToArray();

        var steps = new List<DemoStep>();
        DemoStep? pendingInputStep = null;
        foreach (var browserEvent in orderedEvents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var step = TryMapEvent(browserEvent, includeDiagnostics);
            if (step is null)
            {
                continue;
            }

            if (step.ActionType.Equals(DemoActionTypes.Input, StringComparison.Ordinal))
            {
                if (pendingInputStep is not null && CanMergeInputSteps(pendingInputStep, step))
                {
                    pendingInputStep = pendingInputStep with
                    {
                        Description = step.Description,
                        ScreenshotPath = step.ScreenshotPath ?? pendingInputStep.ScreenshotPath,
                        Timestamp = step.Timestamp
                    };
                    continue;
                }

                if (pendingInputStep is not null)
                {
                    steps.Add(FinalizeStep(pendingInputStep, steps.Count + 1));
                }

                pendingInputStep = step;
                continue;
            }

            if (pendingInputStep is not null)
            {
                steps.Add(FinalizeStep(pendingInputStep, steps.Count + 1));
                pendingInputStep = null;
            }

            steps.Add(FinalizeStep(step, steps.Count + 1));
        }

        if (pendingInputStep is not null)
        {
            steps.Add(FinalizeStep(pendingInputStep, steps.Count + 1));
        }

        _logger.LogInformation("Synthesized {StepCount} demo steps from {EventCount} browser events.", steps.Count, orderedEvents.Length);
        return steps;
    }

    public string ExportDemoScript(IReadOnlyList<DemoStep> steps)
    {
        var script = new DemoScript(steps ?? Array.Empty<DemoStep>());
        return JsonSerializer.Serialize(script, JsonOptions);
    }

    public string ExportMarkdown(IReadOnlyList<DemoStep> steps)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Demo Walkthrough");
        builder.AppendLine();

        foreach (var step in steps ?? Array.Empty<DemoStep>())
        {
            builder.Append(step.StepNumber);
            builder.Append(". ");
            builder.AppendLine(step.Description);
        }

        return builder.ToString().TrimEnd();
    }

    private static DemoStep FinalizeStep(DemoStep step, int stepNumber)
        => step with { StepNumber = stepNumber };

    private static bool CanMergeInputSteps(DemoStep existing, DemoStep candidate)
    {
        if (!string.Equals(existing.Selector, candidate.Selector, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals(existing.Url, candidate.Url, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return Math.Abs((candidate.Timestamp - existing.Timestamp).TotalSeconds) <= 2d;
    }

    private static DemoStep? TryMapEvent(BrowserInteractionEvent browserEvent, bool includeDiagnostics)
    {
        var eventType = NormalizeEventType(browserEvent.EventType);
        return eventType switch
        {
            BrowserInteractionEventTypes.BrowserNavigate => new DemoStep(
                0,
                DemoActionTypes.Navigate,
                BuildNavigateDescription(browserEvent),
                null,
                browserEvent.Url,
                browserEvent.ScreenshotPath,
                browserEvent.Timestamp),
            BrowserInteractionEventTypes.BrowserClick => new DemoStep(
                0,
                DemoActionTypes.Click,
                BuildClickDescription(browserEvent),
                browserEvent.Selector,
                browserEvent.Url,
                browserEvent.ScreenshotPath,
                browserEvent.Timestamp),
            BrowserInteractionEventTypes.BrowserInput => new DemoStep(
                0,
                DemoActionTypes.Input,
                BuildInputDescription(browserEvent),
                browserEvent.Selector,
                browserEvent.Url,
                browserEvent.ScreenshotPath,
                browserEvent.Timestamp),
            BrowserInteractionEventTypes.BrowserSubmit => new DemoStep(
                0,
                DemoActionTypes.Submit,
                BuildSubmitDescription(browserEvent),
                browserEvent.Selector,
                browserEvent.Url,
                browserEvent.ScreenshotPath,
                browserEvent.Timestamp),
            BrowserInteractionEventTypes.NetworkRequest when includeDiagnostics => new DemoStep(
                0,
                DemoActionTypes.NetworkRequest,
                BuildNetworkRequestDescription(browserEvent),
                null,
                browserEvent.Url,
                browserEvent.ScreenshotPath,
                browserEvent.Timestamp),
            "navigate" => new DemoStep(
                0,
                DemoActionTypes.Navigate,
                BuildNavigateDescription(browserEvent),
                null,
                browserEvent.Url,
                browserEvent.ScreenshotPath,
                browserEvent.Timestamp),
            "click" => new DemoStep(
                0,
                DemoActionTypes.Click,
                BuildClickDescription(browserEvent),
                browserEvent.Selector,
                browserEvent.Url,
                browserEvent.ScreenshotPath,
                browserEvent.Timestamp),
            "input" or "change" => new DemoStep(
                0,
                DemoActionTypes.Input,
                BuildInputDescription(browserEvent),
                browserEvent.Selector,
                browserEvent.Url,
                browserEvent.ScreenshotPath,
                browserEvent.Timestamp),
            "submit" => new DemoStep(
                0,
                DemoActionTypes.Submit,
                BuildSubmitDescription(browserEvent),
                browserEvent.Selector,
                browserEvent.Url,
                browserEvent.ScreenshotPath,
                browserEvent.Timestamp),
            "network_request" when includeDiagnostics => new DemoStep(
                0,
                DemoActionTypes.NetworkRequest,
                BuildNetworkRequestDescription(browserEvent),
                null,
                browserEvent.Url,
                browserEvent.ScreenshotPath,
                browserEvent.Timestamp),
            _ => null
        };
    }

    private static string NormalizeEventType(string? eventType)
        => string.IsNullOrWhiteSpace(eventType) ? string.Empty : eventType.Trim();

    private static string BuildNavigateDescription(BrowserInteractionEvent browserEvent)
        => string.IsNullOrWhiteSpace(browserEvent.Url)
            ? "Navigate to a page"
            : $"Navigate to {browserEvent.Url}";

    private static string BuildClickDescription(BrowserInteractionEvent browserEvent)
    {
        var text = NormalizeDescriptionText(browserEvent.Text);
        if (!string.IsNullOrWhiteSpace(text))
        {
            return $"Click {text}";
        }

        return $"Click {DescribeClickTarget(browserEvent.Selector)}";
    }

    private static string BuildInputDescription(BrowserInteractionEvent browserEvent)
        => $"Enter text in {DescribeInputTarget(browserEvent.Selector)}";

    private static string BuildSubmitDescription(BrowserInteractionEvent browserEvent)
        => $"Submit {DescribeSubmitTarget(browserEvent.Selector)}";

    private static string BuildNetworkRequestDescription(BrowserInteractionEvent browserEvent)
    {
        var method = string.IsNullOrWhiteSpace(browserEvent.Method) ? "Request" : browserEvent.Method!.Trim().ToUpperInvariant();
        return string.IsNullOrWhiteSpace(browserEvent.Url)
            ? $"{method} network resource"
            : $"{method} {browserEvent.Url}";
    }

    private static string NormalizeDescriptionText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text.Trim();
        return normalized.StartsWith("\"", StringComparison.Ordinal) ? normalized : $"\"{normalized}\"";
    }

    private static string DescribeClickTarget(string? selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            return "element";
        }

        var trimmed = selector.Trim();
        if (trimmed.StartsWith("#", StringComparison.Ordinal))
        {
            return $"{HumanizeIdentifier(trimmed[1..])} element";
        }

        if (trimmed.StartsWith("[", StringComparison.Ordinal))
        {
            return "element";
        }

        var tagName = trimmed.Split(new[] { '[', '.', '#', ' ' }, 2, StringSplitOptions.RemoveEmptyEntries)[0];
        return string.IsNullOrWhiteSpace(tagName)
            ? "element"
            : $"{tagName.ToLowerInvariant()} element";
    }

    private static string DescribeInputTarget(string? selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            return "field";
        }

        var trimmed = selector.Trim();
        if (trimmed.StartsWith("#", StringComparison.Ordinal))
        {
            return $"{HumanizeIdentifier(trimmed[1..])} field";
        }

        if (trimmed.StartsWith("[", StringComparison.Ordinal))
        {
            return "field";
        }

        var tagName = trimmed.Split(new[] { '[', '.', '#', ' ' }, 2, StringSplitOptions.RemoveEmptyEntries)[0];
        return string.IsNullOrWhiteSpace(tagName)
            ? "field"
            : $"{tagName.ToLowerInvariant()} field";
    }

    private static string DescribeSubmitTarget(string? selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            return "form";
        }

        var trimmed = selector.Trim();
        if (trimmed.StartsWith("#", StringComparison.Ordinal))
        {
            return $"{HumanizeIdentifier(trimmed[1..])} form";
        }

        if (trimmed.StartsWith("[", StringComparison.Ordinal))
        {
            return "form";
        }

        var tagName = trimmed.Split(new[] { '[', '.', '#', ' ' }, 2, StringSplitOptions.RemoveEmptyEntries)[0];
        return string.IsNullOrWhiteSpace(tagName)
            ? "form"
            : $"{tagName.ToLowerInvariant()} form";
    }

    private static string HumanizeIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return "input";
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

            builder.Append(current);
        }

        return builder.ToString().Trim().ToLowerInvariant();
    }

    private static async Task<IReadOnlyList<BrowserInteractionEvent>> LoadEventsAsync(string eventsPath, CancellationToken cancellationToken)
    {
        var lines = await File.ReadAllLinesAsync(eventsPath, cancellationToken).ConfigureAwait(false);
        var events = new List<BrowserInteractionEvent>(lines.Length);
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var browserEvent = JsonSerializer.Deserialize<BrowserInteractionEvent>(line, JsonOptions);
                if (browserEvent is not null)
                {
                    events.Add(browserEvent);
                }
            }
            catch (JsonException)
            {
                // Ignore malformed lines to keep synthesis resilient.
            }
        }

        return events;
    }
}

public sealed record DemoStep(
    int StepNumber,
    string ActionType,
    string Description,
    string? Selector,
    string? Url,
    string? ScreenshotPath,
    DateTimeOffset Timestamp);

public sealed record DemoScript(IReadOnlyList<DemoStep> Steps);

public static class DemoActionTypes
{
    public const string Navigate = "Navigate";
    public const string Click = "Click";
    public const string Input = "Input";
    public const string Submit = "Submit";
    public const string NetworkRequest = "NetworkRequest";
}
