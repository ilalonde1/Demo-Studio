using System.Globalization;
using System.IO;
using System.Text;
using System.Diagnostics;

namespace DemoStudio.Desktop.App.Services;

public sealed class DesktopRuntimeLogService
{
    private readonly string _logsRoot;
    private readonly long _maxFileBytes;
    private readonly int _maxFiles;
    private readonly TimeSpan _maxAge;
    private readonly object _sync = new();
    private DateTimeOffset _lastPruneUtc = DateTimeOffset.MinValue;

    public DesktopRuntimeLogService(
        string storageRoot,
        long maxFileBytes = 5L * 1024L * 1024L,
        int maxFiles = 20,
        TimeSpan? maxAge = null)
    {
        if (string.IsNullOrWhiteSpace(storageRoot))
        {
            throw new ArgumentException("Storage root is required.", nameof(storageRoot));
        }

        _logsRoot = Path.Combine(storageRoot, "logs");
        _maxFileBytes = maxFileBytes <= 0 ? 5L * 1024L * 1024L : maxFileBytes;
        _maxFiles = maxFiles <= 0 ? 20 : maxFiles;
        _maxAge = maxAge ?? TimeSpan.FromDays(14);
        Directory.CreateDirectory(_logsRoot);
    }

    public void Info(string message, string eventName = "Runtime")
        => Write("INFO", eventName, message, null);

    public void Warn(string message, string eventName = "Runtime")
        => Write("WARN", eventName, message, null);

    public void Error(string message, Exception? exception = null, string eventName = "Runtime")
        => Write("ERROR", eventName, message, exception);

    public void PruneNow()
    {
        lock (_sync)
        {
            PruneLogs(DateTimeOffset.UtcNow);
        }
    }

    private void Write(string level, string eventName, string message, Exception? exception)
    {
        lock (_sync)
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                var path = ResolveActiveLogPath(now);
                var line = BuildLine(now, level, eventName, message, exception);
                File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
                if ((now - _lastPruneUtc) >= TimeSpan.FromMinutes(5))
                {
                    PruneLogs(now);
                }
            }
            catch
            {
                Trace.TraceError("DesktopRuntimeLogService failed to write runtime log line.");
            }
        }
    }

    private string ResolveActiveLogPath(DateTimeOffset nowUtc)
    {
        var baseName = $"runtime-{nowUtc:yyyyMMdd}.log";
        var path = Path.Combine(_logsRoot, baseName);
        if (!File.Exists(path))
        {
            return path;
        }

        var info = new FileInfo(path);
        if (info.Length < _maxFileBytes)
        {
            return path;
        }

        var index = 1;
        while (true)
        {
            var candidate = Path.Combine(_logsRoot, $"runtime-{nowUtc:yyyyMMdd}.{index:000}.log");
            if (!File.Exists(candidate))
            {
                return candidate;
            }

            var candidateInfo = new FileInfo(candidate);
            if (candidateInfo.Length < _maxFileBytes)
            {
                return candidate;
            }

            index++;
        }
    }

    private static string BuildLine(DateTimeOffset nowUtc, string level, string eventName, string message, Exception? exception)
    {
        var safeMessage = (message ?? string.Empty).Replace(Environment.NewLine, " ", StringComparison.Ordinal).Trim();
        if (safeMessage.Length > 1200)
        {
            safeMessage = safeMessage[..1200];
        }

        var exceptionType = exception?.GetType().FullName ?? string.Empty;
        var exceptionMessage = exception?.Message?.Replace(Environment.NewLine, " ", StringComparison.Ordinal) ?? string.Empty;
        var exceptionStack = exception?.StackTrace?.Replace(Environment.NewLine, " | ", StringComparison.Ordinal) ?? string.Empty;
        if (exceptionStack.Length > 2000)
        {
            exceptionStack = exceptionStack[..2000];
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{nowUtc:O}\t{level}\t{eventName}\t{safeMessage}\t{exceptionType}\t{exceptionMessage}\t{exceptionStack}");
    }

    private void PruneLogs(DateTimeOffset nowUtc)
    {
        _lastPruneUtc = nowUtc;
        var files = Directory
            .EnumerateFiles(_logsRoot, "runtime-*.log", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .ToList();

        var expireUtc = nowUtc - _maxAge;
        foreach (var file in files)
        {
            if (file.LastWriteTimeUtc < expireUtc.UtcDateTime)
            {
                TryDelete(file.FullName);
            }
        }

        files = Directory
            .EnumerateFiles(_logsRoot, "runtime-*.log", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .ToList();

        if (files.Count <= _maxFiles)
        {
            return;
        }

        foreach (var file in files.Skip(_maxFiles))
        {
            TryDelete(file.FullName);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            Trace.TraceWarning("DesktopRuntimeLogService failed to delete log file: " + path);
        }
    }
}
