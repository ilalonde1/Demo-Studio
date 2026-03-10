using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.IO;

namespace DemoStudio.Desktop.App.Services;

public sealed class DesktopCrashReporter
{
    private readonly string _storageRoot;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public DesktopCrashReporter(string storageRoot)
    {
        _storageRoot = string.IsNullOrWhiteSpace(storageRoot)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DemoStudio", "RecorderDesktop")
            : storageRoot;
    }

    public string? TryWrite(string source, Exception? exception)
    {
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
            return path ?? string.Empty;
        }
        catch (Exception)
        {
            // Crash reporter must never throw — returning null signals write failure to callers.
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
