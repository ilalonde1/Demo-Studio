using System.Text.Json;
using System.IO;
using Microsoft.Extensions.Logging;

namespace DemoStudio.Desktop.App.Services;

public sealed class DesktopSessionHistoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _storePath;
    private readonly SemaphoreSlim _sync = new(1, 1);
    private readonly ILogger<DesktopSessionHistoryService> _logger;

    public string? LastLoadDiagnostic { get; private set; }

    public DesktopSessionHistoryService(string storageRoot, ILogger<DesktopSessionHistoryService> logger)
    {
        if (string.IsNullOrWhiteSpace(storageRoot))
        {
            throw new ArgumentException("Storage root is required.", nameof(storageRoot));
        }

        Directory.CreateDirectory(storageRoot);
        _storePath = Path.Combine(storageRoot, "session-history.json");
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<DesktopSessionRecord>> ListAsync(CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return (await LoadUnsafeAsync(cancellationToken).ConfigureAwait(false))
                .OrderByDescending(x => x.CompletedUtc ?? x.StartedUtc)
                .ToArray();
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task UpsertAsync(DesktopSessionRecord record, CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var list = await LoadUnsafeAsync(cancellationToken).ConfigureAwait(false);
            var existingIndex = list.FindIndex(x => x.SessionId == record.SessionId);
            if (existingIndex >= 0)
            {
                list[existingIndex] = record;
            }
            else
            {
                list.Add(record);
            }

            await PersistUnsafeAsync(list, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Session history updated for session {CaptureSessionId}.", record.SessionId);
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<(bool Succeeded, string Message)> DeleteAsync(Guid sessionId, bool deleteArtifacts, CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var list = await LoadUnsafeAsync(cancellationToken).ConfigureAwait(false);
            var match = list.FirstOrDefault(x => x.SessionId == sessionId);
            if (match is null)
            {
                return (false, "Session record not found.");
            }

            if (deleteArtifacts && !string.IsNullOrWhiteSpace(match.RawVideoPath))
            {
                try
                {
                    var directory = Path.GetDirectoryName(match.RawVideoPath);
                    if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                    {
                        Directory.Delete(directory, true);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed deleting artifacts for session {CaptureSessionId}.", sessionId);
                    return (false, $"Failed deleting artifacts: {ex.Message}");
                }
            }

            if (deleteArtifacts && !string.IsNullOrWhiteSpace(match.DiagnosticsPath))
            {
                try
                {
                    if (File.Exists(match.DiagnosticsPath))
                    {
                        File.Delete(match.DiagnosticsPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed deleting diagnostics bundle {DiagnosticsPath} for session {CaptureSessionId}.", match.DiagnosticsPath, sessionId);
                }
            }

            list.RemoveAll(x => x.SessionId == sessionId);
            await PersistUnsafeAsync(list, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Session history entry deleted for session {CaptureSessionId}.", sessionId);
            return (true, "Session deleted.");
        }
        finally
        {
            _sync.Release();
        }
    }

    private async Task<List<DesktopSessionRecord>> LoadUnsafeAsync(CancellationToken cancellationToken)
    {
        var load = await DesktopAtomicJsonFile.LoadAsync<List<DesktopSessionRecord>>(_storePath, JsonOptions, cancellationToken).ConfigureAwait(false);
        LastLoadDiagnostic = load.Diagnostic;
        if (!load.Exists)
        {
            return new List<DesktopSessionRecord>();
        }

        if (load.Value is not null)
        {
            if (!string.IsNullOrWhiteSpace(load.Diagnostic))
            {
                _logger.LogWarning("Session history loaded with recovery diagnostic: {Diagnostic}", load.Diagnostic);
            }

            return load.Value;
        }

        _logger.LogError("Session history load failed: {Diagnostic}", LastLoadDiagnostic);
        throw new InvalidOperationException(LastLoadDiagnostic ?? "Session history could not be loaded.");
    }

    private async Task PersistUnsafeAsync(List<DesktopSessionRecord> list, CancellationToken cancellationToken)
    {
        await DesktopAtomicJsonFile.SaveAsync(_storePath, list, JsonOptions, cancellationToken).ConfigureAwait(false);
    }
}

public sealed record DesktopSessionRecord(
    Guid SessionId,
    string Status,
    DateTimeOffset StartedUtc,
    DateTimeOffset? CompletedUtc,
    int ClipCount,
    string? ClipSummary,
    double DurationSeconds,
    string? RawVideoPath,
    long FileSizeBytes,
    string? FailureReason,
    string? FailureCode = null,
    string? DiagnosticsPath = null,
    string? FixHint = null)
{
    public string DurationDisplay => TimeSpan.FromSeconds(DurationSeconds).ToString(@"mm\:ss");

    public string FileSizeDisplay => FileSizeBytes <= 0
        ? "-"
        : $"{Math.Round(FileSizeBytes / 1024d / 1024d, 2)} MB";

    public string ClipSummaryDisplay => string.IsNullOrWhiteSpace(ClipSummary) ? "-" : ClipSummary;

    public string FailureCodeDisplay => string.IsNullOrWhiteSpace(FailureCode) ? "-" : FailureCode;

    public string DiagnosticsPathDisplay => string.IsNullOrWhiteSpace(DiagnosticsPath) ? "-" : DiagnosticsPath;
    public string FixHintDisplay => string.IsNullOrWhiteSpace(FixHint) ? "-" : FixHint;
}
