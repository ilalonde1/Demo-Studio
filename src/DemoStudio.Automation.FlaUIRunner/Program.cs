using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
#if FLAUI_RUNNER_ENABLED
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
#endif

internal static class Program
{
    private const int ExitSuccess = 0;
    private const int ExitFailure = 2;
    private const int ExitUsage = 64;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private static async Task<int> Main(string[] args)
    {
        var parsed = ParseArgs(args);
        var response = new RunnerResponse(1, false, null, new List<string>());

        if (string.IsNullOrWhiteSpace(parsed.RequestPath) || string.IsNullOrWhiteSpace(parsed.ResponsePath))
        {
            response.DiagnosticMessage = "Usage: DemoStudio.Automation.FlaUIRunner.exe --request <path> --response <path>";
            WriteResponseOrStderr(parsed.ResponsePath, response);
            return ExitUsage;
        }

        RunnerRequest? request;
        try
        {
            var requestJson = await File.ReadAllTextAsync(parsed.RequestPath).ConfigureAwait(false);
            request = JsonSerializer.Deserialize<RunnerRequest>(requestJson, JsonOptions);
            if (request is null)
            {
                response.DiagnosticMessage = "Request payload is invalid.";
                WriteResponseOrStderr(parsed.ResponsePath, response);
                return ExitUsage;
            }

            response.SchemaVersion = request.SchemaVersion ?? 1;
        }
        catch (Exception ex)
        {
            response.DiagnosticMessage = $"Failed to read request payload: {ex.Message}";
            WriteResponseOrStderr(parsed.ResponsePath, response);
            return ExitUsage;
        }

#if !FLAUI_RUNNER_ENABLED
        response.DiagnosticMessage = "FlaUI runner is not enabled in this build.";
        response.LogLines.Add("Runner executed without FlaUI dependencies enabled.");
        WriteResponseOrStderr(parsed.ResponsePath, response);
        return ExitUsage;
#else
        try
        {
            await ExecuteAsync(request, response).ConfigureAwait(false);
            response.Succeeded = true;
            response.DiagnosticMessage = null;
            WriteResponseOrStderr(parsed.ResponsePath, response);
            return ExitSuccess;
        }
        catch (Exception ex)
        {
            var safeMessage = MaskSecrets(ex.Message, request.GetSecrets());
            response.Succeeded = false;
            response.DiagnosticMessage = safeMessage;
            response.LogLines.Add($"Automation failed: {safeMessage}");
            WriteResponseOrStderr(parsed.ResponsePath, response);
            return ExitFailure;
        }
#endif
    }

#if FLAUI_RUNNER_ENABLED
    private static async Task ExecuteAsync(RunnerRequest request, RunnerResponse response)
    {
        if (string.IsNullOrWhiteSpace(request.ApplicationPath))
        {
            throw new InvalidOperationException("ApplicationPath is required.");
        }

        var timeoutMs = request.TimeoutMs.GetValueOrDefault(60000);
        if (timeoutMs < 1000)
        {
            timeoutMs = 1000;
        }

        var windowFindTimeoutMs = request.WindowFindTimeoutMs.GetValueOrDefault(20000);
        if (windowFindTimeoutMs < 1000)
        {
            windowFindTimeoutMs = 1000;
        }

        using var globalTimeout = new CancellationTokenSource(timeoutMs);

        Application? app = null;
        using var automation = new UIA3Automation();
        Window? mainWindow = null;

        try
        {
            foreach (var (step, idx) in request.Steps.Select((value, index) => (value, index + 1)))
            {
                globalTimeout.Token.ThrowIfCancellationRequested();

                var stepType = (step.Type ?? string.Empty).Trim();
                switch (stepType)
                {
                    case "Launch":
                    {
                        response.LogLines.Add($"Step {idx}: Launch");
                        app = LaunchApplication(request);
                        break;
                    }
                    case "WaitForWindow":
                    {
                        var titleContains = step.TitleContains ?? string.Empty;
                        response.LogLines.Add($"Step {idx}: WaitForWindow title contains '{titleContains}'");
                        if (app is null)
                        {
                            throw new InvalidOperationException("Application is not launched.");
                        }

                        mainWindow = WaitForWindow(app, automation, titleContains, windowFindTimeoutMs, globalTimeout.Token);
                        break;
                    }
                    case "Click":
                    {
                        response.LogLines.Add($"Step {idx}: Click automationId '{step.AutomationId}'");
                        EnsureMainWindow(ref app, ref mainWindow, request, automation, windowFindTimeoutMs, globalTimeout.Token, response.LogLines);
                        var root = RequireWindow(mainWindow);
                        var target = FindByAutomationId(root, step.AutomationId);
                        target.AsButton()?.Invoke();
                        break;
                    }
                    case "Type":
                    {
                        response.LogLines.Add($"Step {idx}: Type automationId '{step.AutomationId}' = (masked)");
                        EnsureMainWindow(ref app, ref mainWindow, request, automation, windowFindTimeoutMs, globalTimeout.Token, response.LogLines);
                        var root = RequireWindow(mainWindow);
                        var target = FindByAutomationId(root, step.AutomationId);
                        var textBox = target.AsTextBox();
                        if (textBox is null)
                        {
                            throw new InvalidOperationException($"Element '{step.AutomationId}' is not a text box.");
                        }

                        textBox.Text = step.Value ?? string.Empty;
                        break;
                    }
                    case "Wait":
                    {
                        var ms = Math.Max(1, step.Ms.GetValueOrDefault(250));
                        response.LogLines.Add($"Step {idx}: Wait {ms}ms");
                        await Task.Delay(ms, globalTimeout.Token).ConfigureAwait(false);
                        break;
                    }
                    case "CloseApplication":
                    {
                        response.LogLines.Add($"Step {idx}: CloseApplication");
                        TryCloseApplication(app);
                        app = null;
                        mainWindow = null;
                        break;
                    }
                    default:
                        throw new InvalidOperationException($"Unsupported step type '{stepType}'.");
                }
            }
        }
        finally
        {
            TryCloseApplication(app);
        }
    }

    private static Application LaunchApplication(RunnerRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.ApplicationPath,
            Arguments = request.LaunchArguments ?? string.Empty,
            WorkingDirectory = string.IsNullOrWhiteSpace(request.WorkingDirectory)
                ? Path.GetDirectoryName(request.ApplicationPath) ?? Environment.CurrentDirectory
                : request.WorkingDirectory,
            UseShellExecute = false
        };

        return Application.Launch(startInfo);
    }

    private static Window WaitForWindow(Application app, UIA3Automation automation, string titleContains, int timeoutMs, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var retry = Retry.WhileNull(
            () => app.GetAllTopLevelWindows(automation)
                .FirstOrDefault(w => string.IsNullOrWhiteSpace(titleContains)
                    || (w.Title?.Contains(titleContains, StringComparison.OrdinalIgnoreCase) ?? false)),
            TimeSpan.FromMilliseconds(timeoutMs),
            TimeSpan.FromMilliseconds(250),
            false);

        if (!retry.Success || retry.Result is null)
        {
            throw new InvalidOperationException($"Main window was not found with title containing '{titleContains}'.");
        }

        return retry.Result;
    }

    private static void EnsureMainWindow(
        ref Application? app,
        ref Window? mainWindow,
        RunnerRequest request,
        UIA3Automation automation,
        int windowFindTimeoutMs,
        CancellationToken cancellationToken,
        List<string> logLines)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (app is null)
        {
            app = LaunchApplication(request);
            logLines.Add("AutoLaunch: Application was started because no Launch step was present.");
        }

        if (mainWindow is null)
        {
            mainWindow = WaitForWindow(app, automation, string.Empty, windowFindTimeoutMs, cancellationToken);
            logLines.Add("AutoWaitForWindow: Main window was resolved automatically.");
        }
    }

    private static Window RequireWindow(Window? window)
    {
        return window ?? throw new InvalidOperationException("Main window has not been initialized. Ensure WaitForWindow is executed first.");
    }

    private static AutomationElement FindByAutomationId(Window root, string? automationId)
    {
        if (string.IsNullOrWhiteSpace(automationId))
        {
            throw new InvalidOperationException("automationId is required for this step.");
        }

        var element = root.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
        return element ?? throw new InvalidOperationException($"Element with automationId '{automationId}' was not found.");
    }

    private static void TryCloseApplication(Application? app)
    {
        if (app is null)
        {
            return;
        }

        try
        {
            app.Close();
        }
        catch
        {
        }

        try
        {
            app.Dispose();
        }
        catch
        {
        }
    }
#endif

    private static ParsedArgs ParseArgs(IReadOnlyList<string> args)
    {
        string? request = null;
        string? response = null;

        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] == "--request" && i + 1 < args.Count)
            {
                request = args[++i];
            }
            else if (args[i] == "--response" && i + 1 < args.Count)
            {
                response = args[++i];
            }
        }

        return new ParsedArgs(request, response);
    }

    private static void WriteResponseOrStderr(string? responsePath, RunnerResponse response)
    {
        if (!WriteResponseAtomic(responsePath, response))
        {
            Console.Error.WriteLine(response.DiagnosticMessage ?? "Runner failed without response output.");
        }
    }

    private static bool WriteResponseAtomic(string? responsePath, RunnerResponse response)
    {
        if (string.IsNullOrWhiteSpace(responsePath))
        {
            return false;
        }

        try
        {
            var responseDirectory = Path.GetDirectoryName(responsePath);
            if (!string.IsNullOrWhiteSpace(responseDirectory))
            {
                Directory.CreateDirectory(responseDirectory);
            }

            var tempPath = responsePath + ".tmp";
            var json = JsonSerializer.Serialize(response, JsonOptions);
            File.WriteAllText(tempPath, json);

            if (File.Exists(responsePath))
            {
                File.Delete(responsePath);
            }

            File.Move(tempPath, responsePath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string MaskSecrets(string message, IEnumerable<string> secrets)
    {
        var safe = message ?? string.Empty;
        foreach (var secret in secrets.Where(x => !string.IsNullOrWhiteSpace(x)).OrderByDescending(x => x.Length))
        {
            safe = safe.Replace(secret, "(masked)", StringComparison.Ordinal);
        }

        return safe;
    }

    private sealed record ParsedArgs(string? RequestPath, string? ResponsePath);

    private sealed class RunnerRequest
    {
        public int? SchemaVersion { get; set; }

        public string RunId { get; set; } = string.Empty;

        public string ApplicationPath { get; set; } = string.Empty;

        public string? WorkingDirectory { get; set; }

        public string? LaunchArguments { get; set; }

        public int? TimeoutMs { get; set; }

        public int? WindowFindTimeoutMs { get; set; }

        public List<RunnerStep> Steps { get; set; } = new();

        public IEnumerable<string> GetSecrets()
        {
            foreach (var step in Steps)
            {
                if ((step.Type?.Equals("Type", StringComparison.OrdinalIgnoreCase) ?? false)
                    && !string.IsNullOrWhiteSpace(step.Value))
                {
                    yield return step.Value!;
                }
            }
        }
    }

    private sealed class RunnerStep
    {
        public string? Type { get; set; }

        public string? TitleContains { get; set; }

        public string? AutomationId { get; set; }

        public string? Value { get; set; }

        public int? Ms { get; set; }
    }

    private sealed class RunnerResponse
    {
        public RunnerResponse(int schemaVersion, bool succeeded, string? diagnosticMessage, List<string> logLines)
        {
            SchemaVersion = schemaVersion;
            Succeeded = succeeded;
            DiagnosticMessage = diagnosticMessage;
            LogLines = logLines;
        }

        public int SchemaVersion { get; set; }

        public bool Succeeded { get; set; }

        public string? DiagnosticMessage { get; set; }

        public List<string> LogLines { get; set; }
    }
}
