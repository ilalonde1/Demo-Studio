using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.IO;
using Microsoft.Extensions.Logging;

namespace DemoStudio.Desktop.App.Services;

public sealed class DesktopDiagnosticsBundleService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _storageRoot;
    private readonly ILogger<DesktopDiagnosticsBundleService> _logger;

    public DesktopDiagnosticsBundleService(string storageRoot, ILogger<DesktopDiagnosticsBundleService> logger)
    {
        if (string.IsNullOrWhiteSpace(storageRoot))
        {
            throw new ArgumentException("Storage root is required.", nameof(storageRoot));
        }

        _storageRoot = storageRoot;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Directory.CreateDirectory(_storageRoot);
    }

    public string? TryWriteFailureBundle(
        Guid sessionId,
        string? rawVideoPath,
        string failureCode,
        string failureReason,
        Exception? exception,
        CaptureTargetSettings targetSettings,
        string ffmpegPath,
        string? launchExecutablePath,
        string? launchArguments,
        string? launchWorkingDirectory)
    {
        var diagnosticsOperationId = Guid.NewGuid().ToString("N");
        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["DiagnosticsOperationId"] = diagnosticsOperationId,
            ["CaptureSessionId"] = sessionId,
            ["FailureCode"] = failureCode
        });

        try
        {
            var outputDirectory = ResolveOutputDirectory(rawVideoPath);
            Directory.CreateDirectory(outputDirectory);
            var outputPath = Path.Combine(
                outputDirectory,
                $"diagnostic-{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss_fff}-{sessionId:N}.json");

            var payload = new DesktopFailureDiagnosticBundle(
                SessionId: sessionId,
                RecordedUtc: DateTimeOffset.UtcNow,
                FailureCode: failureCode,
                FailureReason: failureReason,
                RawVideoPath: rawVideoPath,
                TargetMode: targetSettings.Mode,
                WindowTitleContains: targetSettings.WindowTitleContains,
                WindowProcessName: targetSettings.WindowProcessName,
                WindowHandleHex: targetSettings.WindowHandleHex,
                FallbackToDesktop: targetSettings.FallbackToDesktop,
                FfmpegPath: ffmpegPath,
                LaunchExecutablePath: string.IsNullOrWhiteSpace(launchExecutablePath) ? null : launchExecutablePath,
                LaunchArguments: string.IsNullOrWhiteSpace(launchArguments) ? null : launchArguments,
                LaunchWorkingDirectory: string.IsNullOrWhiteSpace(launchWorkingDirectory) ? null : launchWorkingDirectory,
                MachineName: Environment.MachineName,
                UserName: Environment.UserName,
                OsVersion: Environment.OSVersion.ToString(),
                ProcessArchitecture: RuntimeInformation.ProcessArchitecture.ToString(),
                DotNetVersion: Environment.Version.ToString(),
                AppVersion: Assembly.GetEntryAssembly()?.GetName().Version?.ToString(),
                ExceptionType: exception?.GetType().FullName,
                ExceptionMessage: exception?.Message,
                ExceptionStackTrace: exception?.StackTrace);

            File.WriteAllText(outputPath, JsonSerializer.Serialize(payload, JsonOptions));
            _logger.LogWarning("Failure diagnostics bundle written to {OutputPath}.", outputPath);
            return outputPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed writing diagnostics bundle.");
            return null;
        }
    }

    private string ResolveOutputDirectory(string? rawVideoPath)
    {
        if (!string.IsNullOrWhiteSpace(rawVideoPath))
        {
            var directory = Path.GetDirectoryName(rawVideoPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                return directory;
            }
        }

        return Path.Combine(_storageRoot, "diagnostics");
    }
}

public sealed record DesktopFailureDiagnosticBundle(
    Guid SessionId,
    DateTimeOffset RecordedUtc,
    string FailureCode,
    string FailureReason,
    string? RawVideoPath,
    string TargetMode,
    string? WindowTitleContains,
    string? WindowProcessName,
    string? WindowHandleHex,
    bool FallbackToDesktop,
    string FfmpegPath,
    string? LaunchExecutablePath,
    string? LaunchArguments,
    string? LaunchWorkingDirectory,
    string MachineName,
    string UserName,
    string OsVersion,
    string ProcessArchitecture,
    string DotNetVersion,
    string? AppVersion,
    string? ExceptionType,
    string? ExceptionMessage,
    string? ExceptionStackTrace);
