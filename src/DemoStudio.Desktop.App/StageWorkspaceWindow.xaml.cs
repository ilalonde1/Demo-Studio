using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using DemoStudio.Desktop.App.Services;
using Microsoft.Web.WebView2.Core;

namespace DemoStudio.Desktop.App;

public partial class StageWorkspaceWindow : Window
{
    private const string DefaultAddress = "https://www.bing.com";
    private const string InteractionCaptureScript = """
(() => {
  if (window.__demoStudioRecorderInstalled) {
    return;
  }

  window.__demoStudioRecorderInstalled = true;
  const safeText = value => {
    if (typeof value !== "string") {
      return null;
    }

    const normalized = value.replace(/\s+/g, " ").trim();
    return normalized.length === 0 ? null : normalized.slice(0, 500);
  };

  const selectorFor = target => {
    if (!(target instanceof Element)) {
      return null;
    }

    if (target.id) {
      return "#" + target.id;
    }

    if (target.getAttribute("name")) {
      return target.tagName.toLowerCase() + "[name=\"" + target.getAttribute("name") + "\"]";
    }

    if (target.getAttribute("data-testid")) {
      return target.tagName.toLowerCase() + "[data-testid=\"" + target.getAttribute("data-testid") + "\"]";
    }

    return target.tagName.toLowerCase();
  };

  const post = payload => {
    try {
      window.chrome.webview.postMessage(payload);
    } catch {
      // Keep page behavior stable even if host messaging fails.
    }
  };

  document.addEventListener("click", event => {
    const target = event.target instanceof Element ? event.target : null;
    if (!target) {
      return;
    }

    post({
      type: "click",
      selector: selectorFor(target),
      text: safeText(target.innerText || target.textContent || null)
    });
  }, true);

  const emitInput = (type, event) => {
    const target = event.target;
    if (!(target instanceof HTMLInputElement) && !(target instanceof HTMLTextAreaElement) && !(target instanceof HTMLSelectElement)) {
      return;
    }

    post({
      type: type,
      selector: selectorFor(target),
      value: typeof target.value === "string" ? target.value.slice(0, 2000) : null,
      text: safeText(target.innerText || target.textContent || null)
    });
  };

  document.addEventListener("input", event => emitInput("input", event), true);
  document.addEventListener("change", event => emitInput("change", event), true);

  document.addEventListener("submit", event => {
    const target = event.target instanceof HTMLFormElement ? event.target : null;
    if (!target) {
      return;
    }

    post({
      type: "submit",
      selector: selectorFor(target),
      text: safeText(target.innerText || target.textContent || null)
    });
  }, true);

  window.addEventListener("error", event => {
    post({
      type: "console-error",
      text: safeText(event.message || "Script error"),
      value: safeText(`${event.filename || ""}:${event.lineno || 0}:${event.colno || 0}`)
    });
  });

  window.addEventListener("unhandledrejection", event => {
    post({
      type: "console-error",
      text: "Unhandled promise rejection",
      value: safeText(String(event.reason))
    });
  });
})();
""";
    private bool _initialized;
    private bool _browserReady;
    private bool _instrumentationReady;
    private readonly DesktopBrowserInteractionRecorder? _interactionRecorder;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private CoreWebView2DevToolsProtocolEventReceiver? _networkRequestReceiver;
    private CoreWebView2DevToolsProtocolEventReceiver? _networkResponseReceiver;
    private CoreWebView2DevToolsProtocolEventReceiver? _consoleReceiver;

    public StageWorkspaceWindow()
    {
        InitializeComponent();
        _interactionRecorder = (System.Windows.Application.Current as App)?.GetService<DesktopBrowserInteractionRecorder>();
        Loaded += OnLoaded;
        Browser.SourceChanged += OnBrowserSourceChanged;
    }

    public string CurrentAddress
    {
        get => AddressTextBox.Text;
        set => AddressTextBox.Text = string.IsNullOrWhiteSpace(value) ? DefaultAddress : value.Trim();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        await EnsureBrowserInitializedAsync();
        NavigateToAddress(CurrentAddress);
    }

    private void GoButton_OnClick(object sender, RoutedEventArgs e)
    {
        NavigateToAddress(AddressTextBox.Text);
    }

    private void BackButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_browserReady && Browser.CanGoBack)
        {
            Browser.GoBack();
        }
    }

    private void ForwardButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_browserReady && Browser.CanGoForward)
        {
            Browser.GoForward();
        }
    }

    private void RefreshButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_browserReady)
        {
            Browser.Reload();
        }
    }

    private void OpenExternalButton_OnClick(object sender, RoutedEventArgs e)
    {
        var address = NormalizeAddress(AddressTextBox.Text);
        if (address is null)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(address)
            {
                UseShellExecute = true
            });
        }
        catch
        {
            // Keep stage workspace stable even if shell launch fails.
        }
    }

    private void AddressTextBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        NavigateToAddress(AddressTextBox.Text);
    }

    private void NavigateToAddress(string? rawAddress)
    {
        var address = NormalizeAddress(rawAddress);
        if (address is null)
        {
            return;
        }

        AddressTextBox.Text = address;
        try
        {
            if (!_browserReady)
            {
                return;
            }

            Browser.Source = new Uri(address, UriKind.Absolute);
        }
        catch
        {
            // No-op to avoid breaking stage session due to navigation error.
        }
    }

    private async Task EnsureBrowserInitializedAsync()
    {
        if (_browserReady)
        {
            return;
        }

        try
        {
            await Browser.EnsureCoreWebView2Async();
            Browser.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = true;
            await InitializeBrowserInstrumentationAsync();
            _browserReady = true;
        }
        catch (WebView2RuntimeNotFoundException)
        {
            _browserReady = false;
        }
        catch
        {
            _browserReady = false;
        }
    }

    private async Task InitializeBrowserInstrumentationAsync()
    {
        if (_instrumentationReady || Browser.CoreWebView2 is null)
        {
            return;
        }

        var core = Browser.CoreWebView2;
        await core.CallDevToolsProtocolMethodAsync("Runtime.enable", "{}");
        await core.CallDevToolsProtocolMethodAsync("DOM.enable", "{}");
        await core.CallDevToolsProtocolMethodAsync("Page.enable", "{}");
        await core.CallDevToolsProtocolMethodAsync("Network.enable", "{}");
        await core.AddScriptToExecuteOnDocumentCreatedAsync(InteractionCaptureScript);

        core.WebMessageReceived += OnBrowserWebMessageReceived;
        core.NavigationCompleted += OnBrowserNavigationCompleted;

        _networkRequestReceiver = core.GetDevToolsProtocolEventReceiver("Network.requestWillBeSent");
        _networkRequestReceiver.DevToolsProtocolEventReceived += OnNetworkRequestWillBeSent;

        _networkResponseReceiver = core.GetDevToolsProtocolEventReceiver("Network.responseReceived");
        _networkResponseReceiver.DevToolsProtocolEventReceived += OnNetworkResponseReceived;

        _consoleReceiver = core.GetDevToolsProtocolEventReceiver("Runtime.consoleAPICalled");
        _consoleReceiver.DevToolsProtocolEventReceived += OnRuntimeConsoleApiCalled;

        _instrumentationReady = true;
    }

    private void OnBrowserSourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
    {
        if (Browser.Source is not null)
        {
            AddressTextBox.Text = Browser.Source.AbsoluteUri;
        }
    }

    private void OnBrowserWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        BrowserInteractionScriptMessage? payload;
        try
        {
            payload = JsonSerializer.Deserialize<BrowserInteractionScriptMessage>(e.WebMessageAsJson, _jsonOptions);
        }
        catch
        {
            payload = null;
        }

        if (payload is null)
        {
            return;
        }

        var eventType = payload.Type?.Trim().ToLowerInvariant() switch
        {
            "click" => BrowserInteractionEventTypes.BrowserClick,
            "input" => BrowserInteractionEventTypes.BrowserInput,
            "change" => BrowserInteractionEventTypes.BrowserInput,
            "submit" => BrowserInteractionEventTypes.BrowserSubmit,
            "console-error" => BrowserInteractionEventTypes.BrowserConsoleError,
            _ => BrowserInteractionEventTypes.BrowserUnknown
        };

        _ = RecordBrowserEventAsync(new BrowserInteractionEvent(
            EventType: eventType,
            Selector: payload.Selector,
            Value: payload.Value,
            Text: payload.Text,
            Timestamp: DateTimeOffset.UtcNow,
            Url: ResolveCurrentUrl(),
            Message: payload.Type));
    }

    private void OnBrowserNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        _ = RecordBrowserEventAsync(new BrowserInteractionEvent(
            EventType: BrowserInteractionEventTypes.BrowserNavigate,
            Selector: null,
            Value: null,
            Text: e.IsSuccess ? "Navigation completed." : "Navigation failed.",
            Timestamp: DateTimeOffset.UtcNow,
            Url: ResolveCurrentUrl(),
            StatusCode: e.HttpStatusCode > 0 ? (int)e.HttpStatusCode : null,
            Message: e.WebErrorStatus.ToString()));
    }

    private void OnNetworkRequestWillBeSent(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        try
        {
            using var document = JsonDocument.Parse(e.ParameterObjectAsJson);
            if (!document.RootElement.TryGetProperty("request", out var requestElement))
            {
                return;
            }

            var url = requestElement.TryGetProperty("url", out var urlElement) ? urlElement.GetString() : null;
            var method = requestElement.TryGetProperty("method", out var methodElement) ? methodElement.GetString() : null;
            _ = RecordBrowserEventAsync(new BrowserInteractionEvent(
                EventType: BrowserInteractionEventTypes.NetworkRequest,
                Selector: null,
                Value: null,
                Text: null,
                Timestamp: DateTimeOffset.UtcNow,
                Url: url,
                Method: method));
        }
        catch
        {
            // Keep browser instrumentation best-effort.
        }
    }

    private void OnNetworkResponseReceived(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        try
        {
            using var document = JsonDocument.Parse(e.ParameterObjectAsJson);
            if (!document.RootElement.TryGetProperty("response", out var responseElement))
            {
                return;
            }

            var url = responseElement.TryGetProperty("url", out var urlElement) ? urlElement.GetString() : null;
            var statusCode = responseElement.TryGetProperty("status", out var statusElement) && statusElement.TryGetInt32(out var status)
                ? status
                : (int?)null;
            var mimeType = responseElement.TryGetProperty("mimeType", out var mimeTypeElement) ? mimeTypeElement.GetString() : null;
            _ = RecordBrowserEventAsync(new BrowserInteractionEvent(
                EventType: BrowserInteractionEventTypes.NetworkResponse,
                Selector: null,
                Value: null,
                Text: mimeType,
                Timestamp: DateTimeOffset.UtcNow,
                Url: url,
                StatusCode: statusCode));
        }
        catch
        {
            // Keep browser instrumentation best-effort.
        }
    }

    private void OnRuntimeConsoleApiCalled(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        try
        {
            using var document = JsonDocument.Parse(e.ParameterObjectAsJson);
            var logType = document.RootElement.TryGetProperty("type", out var typeElement)
                ? typeElement.GetString()
                : null;
            if (!string.Equals(logType, "error", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string? message = null;
            if (document.RootElement.TryGetProperty("args", out var argsElement)
                && argsElement.ValueKind == JsonValueKind.Array)
            {
                var parts = argsElement.EnumerateArray()
                    .Select(x => x.TryGetProperty("value", out var valueElement) ? valueElement.ToString() : x.ToString())
                    .Where(x => !string.IsNullOrWhiteSpace(x));
                message = string.Join(" ", parts);
            }

            _ = RecordBrowserEventAsync(new BrowserInteractionEvent(
                EventType: BrowserInteractionEventTypes.BrowserConsoleError,
                Selector: null,
                Value: null,
                Text: message,
                Timestamp: DateTimeOffset.UtcNow,
                Url: ResolveCurrentUrl(),
                Message: logType));
        }
        catch
        {
            // Keep browser instrumentation best-effort.
        }
    }

    private async Task RecordBrowserEventAsync(BrowserInteractionEvent browserEvent)
    {
        if (_interactionRecorder is null)
        {
            return;
        }

        try
        {
            var screenshotPath = await TryCaptureScreenshotAsync(browserEvent.EventType).ConfigureAwait(true);
            await _interactionRecorder.RecordAsync(browserEvent with
            {
                Url = string.IsNullOrWhiteSpace(browserEvent.Url) ? ResolveCurrentUrl() : browserEvent.Url,
                Timestamp = browserEvent.Timestamp == default ? DateTimeOffset.UtcNow : browserEvent.Timestamp,
                ScreenshotPath = screenshotPath
            }).ConfigureAwait(true);
        }
        catch
        {
            // Do not disrupt the stage workspace for instrumentation failures.
        }
    }

    private async Task<string?> TryCaptureScreenshotAsync(string eventType)
    {
        if (Browser.CoreWebView2 is null)
        {
            return null;
        }

        if (!string.Equals(eventType, BrowserInteractionEventTypes.BrowserClick, StringComparison.Ordinal)
            && !string.Equals(eventType, BrowserInteractionEventTypes.BrowserNavigate, StringComparison.Ordinal))
        {
            return null;
        }

        if (_interactionRecorder is null)
        {
            return null;
        }

        try
        {
            var screenshotPath = _interactionRecorder.CreateScreenshotPath(eventType, DateTimeOffset.UtcNow);
            Directory.CreateDirectory(Path.GetDirectoryName(screenshotPath)!);
            await using var stream = File.Create(screenshotPath);
            await Browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
            return screenshotPath;
        }
        catch
        {
            return null;
        }
    }

    private string? ResolveCurrentUrl()
    {
        if (Browser.Source is not null)
        {
            return Browser.Source.AbsoluteUri;
        }

        return Browser.CoreWebView2?.Source;
    }

    private static string? NormalizeAddress(string? rawAddress)
    {
        var candidate = string.IsNullOrWhiteSpace(rawAddress) ? DefaultAddress : rawAddress.Trim();
        if (!candidate.Contains("://", StringComparison.Ordinal))
        {
            candidate = "https://" + candidate;
        }

        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            ? uri.AbsoluteUri
            : null;
    }
}

internal sealed record BrowserInteractionScriptMessage(
    string? Type,
    string? Selector,
    string? Value,
    string? Text);
