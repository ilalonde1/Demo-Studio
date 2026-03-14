using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.IO;
using Microsoft.Extensions.Logging;

namespace DemoStudio.Desktop.App.Services;

public sealed class DesktopCrashReporter
{
    private readonly string _storageRoot;
    private readonly ILogger<DesktopCrashReporter> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public DesktopCrashReporter(string storageRoot, ILogger<DesktopCrashReporter> logger)
    {
        _storageRoot = string.IsNullOrWhiteSpace(storageRoot)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DemoStudio", "RecorderDesktop")
            : storageRoot;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string? TryWrite(string source, Exception? exception)
    {
        var diagnosticsOperationId = Guid.NewGuid().ToString("N");
        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["DiagnosticsOperationId"] = diagnosticsOperationId,
            ["DiagnosticsSource"] = source
        });

        try
        {
            var diagnosticsRoot = Path.Combine(_storageRoot, "diagnostics");
            Directory.CreateDirectory(diagnosticsRoot);
            var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
            var path = Path.Combine(diagnosticsRoot, $"crash-{timestamp}.json");
            var payload = new DesktopCrashDiagnostic(
                RecordedUtc: DateTimeOffset.UtcNow,
                Source: source,
                MachineName: Environment.MachineName,
                UserName: Environment.UserName,
                OsVersion: Environment.OSVersion.ToString(),
                ProcessArchitecture: RuntimeInformation.ProcessArchitecture.ToString(),
                DotNetVersion: Environment.Version.ToString(),
                AppVersion: Assembly.GetEntryAssembly()?.GetName().Version?.ToString(),
                ExceptionType: exception?.GetType().FullName,
                ExceptionMessage: exception?.Message,
                ExceptionStackTrace: exception?.StackTrace);
            var json = JsonSerializer.Serialize(payload, _jsonOptions);
            File.WriteAllText(path, json, Encoding.UTF8);
            _logger.LogInformation("Crash diagnostic written to {CrashPath}.", path);
            return path ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed writing crash diagnostic.");
            return null;
        }
    }
}

public sealed record DesktopCrashDiagnostic(
    DateTimeOffset RecordedUtc,
    string Source,
    string MachineName,
    string UserName,
    string OsVersion,
    string ProcessArchitecture,
    string DotNetVersion,
    string? AppVersion,
    string? ExceptionType,
    string? ExceptionMessage,
    string? ExceptionStackTrace);
