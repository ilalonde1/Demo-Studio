using System.IO;
using System.Text;
using System.Text.Json;
using DemoStudio.Desktop.App.Infrastructure;
using Microsoft.Extensions.Logging;

namespace DemoStudio.Desktop.App.Services;

public sealed class DesktopBrowserInteractionRecorder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _eventsPath;
    private readonly string _screenshotsRoot;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly ILogger<DesktopBrowserInteractionRecorder> _logger;
    private readonly IRecorderFeedbackNotifier _feedbackNotifier;

    public DesktopBrowserInteractionRecorder(string storageRoot, ILogger<DesktopBrowserInteractionRecorder> logger, IRecorderFeedbackNotifier feedbackNotifier)
    {
        if (string.IsNullOrWhiteSpace(storageRoot))
        {
            throw new ArgumentException("Storage root is required.", nameof(storageRoot));
        }

        _eventsPath = DesktopStoragePaths.GetBrowserInteractionsLogPath(storageRoot);
        _screenshotsRoot = DesktopStoragePaths.GetBrowserInteractionScreenshotsDirectory(storageRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(_eventsPath)!);
        Directory.CreateDirectory(_screenshotsRoot);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _feedbackNotifier = feedbackNotifier ?? throw new ArgumentNullException(nameof(feedbackNotifier));
    }

    public string EventsPath => _eventsPath;

    public string CreateScreenshotPath(string eventType, DateTimeOffset timestampUtc)
    {
        var safeEventType = string.IsNullOrWhiteSpace(eventType) ? "event" : eventType.Trim().ToLowerInvariant();
        var fileName = $"{timestampUtc:yyyyMMdd_HHmmss_fff}-{safeEventType}-{Guid.NewGuid():N}.png";
        return Path.Combine(_screenshotsRoot, fileName);
    }

    public async Task RecordAsync(BrowserInteractionEvent browserEvent, CancellationToken cancellationToken = default)
    {
        var effectiveEvent = browserEvent with
        {
            EventType = NormalizeEventType(browserEvent.EventType),
            Selector = NormalizeOptional(browserEvent.Selector, 300),
            Value = NormalizeOptional(browserEvent.Value, 2000),
            Text = NormalizeOptional(browserEvent.Text, 500),
            Message = NormalizeOptional(browserEvent.Message, 2000),
            Method = NormalizeOptional(browserEvent.Method, 64),
            Url = NormalizeOptional(browserEvent.Url, 2048),
            ScreenshotPath = NormalizeOptional(browserEvent.ScreenshotPath, 2048),
            Timestamp = browserEvent.Timestamp == default ? DateTimeOffset.UtcNow : browserEvent.Timestamp
        };

        var line = JsonSerializer.Serialize(effectiveEvent, JsonOptions);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await File.AppendAllTextAsync(_eventsPath, line + Environment.NewLine, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }

        _logger.LogInformation(
            "Browser interaction recorded. EventType={EventType} Url={Url} Selector={Selector} StatusCode={StatusCode}",
            effectiveEvent.EventType,
            effectiveEvent.Url,
            effectiveEvent.Selector,
            effectiveEvent.StatusCode);
        _feedbackNotifier.NotifyStepCaptured("browser-interaction");
    }

    private static string NormalizeEventType(string? eventType)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            return BrowserInteractionEventTypes.BrowserUnknown;
        }

        return eventType.Trim();
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }
}

public static class BrowserInteractionEventTypes
{
    public const string BrowserNavigate = "BrowserNavigate";
    public const string BrowserClick = "BrowserClick";
    public const string BrowserInput = "BrowserInput";
    public const string BrowserSubmit = "BrowserSubmit";
    public const string BrowserConsoleError = "BrowserConsoleError";
    public const string NetworkRequest = "NetworkRequest";
    public const string NetworkResponse = "NetworkResponse";
    public const string BrowserUnknown = "BrowserUnknown";
}

public sealed record BrowserInteractionEvent(
    string EventType,
    string? Selector,
    string? Value,
    string? Text,
    DateTimeOffset Timestamp,
    string? Url,
    string? Method = null,
    int? StatusCode = null,
    string? Message = null,
    string? ScreenshotPath = null);
