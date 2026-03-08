using System.ComponentModel;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;

namespace DemoStudio.Desktop.App.Services;

public sealed class DesktopProcessRunner
{
    private static readonly ConcurrentDictionary<string, DateTimeOffset> StartFailureCooldownUtc = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan StartFailureBackoff = TimeSpan.FromSeconds(30);

    public async Task<DesktopProcessRunResult> RunAsync(
        ProcessStartInfo startInfo,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (startInfo is null)
        {
            throw new ArgumentNullException(nameof(startInfo));
        }

        if (TryShortCircuitStartFailure(startInfo, out var shortCircuitFailure))
        {
            return shortCircuitFailure;
        }

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Win32Exception ex)
        {
            RegisterStartFailure(startInfo.FileName);
            return DesktopProcessRunResult.FromStartFailure(ex.Message);
        }
        catch (Exception ex)
        {
            RegisterStartFailure(startInfo.FileName);
            return DesktopProcessRunResult.FromStartFailure(ex.Message);
        }

        if (process is null)
        {
            RegisterStartFailure(startInfo.FileName);
            return DesktopProcessRunResult.FromStartFailure("Process failed to start.");
        }

        ClearStartFailure(startInfo.FileName);

        using (process)
        {
            var readStdOutTask = startInfo.RedirectStandardOutput
                ? process.StandardOutput.ReadToEndAsync()
                : Task.FromResult(string.Empty);
            var readStdErrTask = startInfo.RedirectStandardError
                ? process.StandardError.ReadToEndAsync()
                : Task.FromResult(string.Empty);

            var waitTask = process.WaitForExitAsync(cancellationToken);
            var timedOut = false;
            var cancelled = false;
            if (timeout > TimeSpan.Zero)
            {
                var timeoutTask = Task.Delay(timeout);
                var completedTask = await Task.WhenAny(waitTask, timeoutTask);
                if (completedTask == timeoutTask)
                {
                    timedOut = true;
                    TryKill(process);
                }
                else
                {
                    try
                    {
                        await waitTask;
                    }
                    catch (OperationCanceledException)
                    {
                        cancelled = true;
                        TryKill(process);
                    }
                }
            }
            else
            {
                try
                {
                    await waitTask;
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                    TryKill(process);
                }
            }

            try
            {
                await process.WaitForExitAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"DesktopProcessRunner: final WaitForExitAsync failed: {ex.Message}");
            }

            var stdOut = await readStdOutTask;
            var stdErr = await readStdErrTask;
            var succeeded = !timedOut && process.ExitCode == 0;
            return new DesktopProcessRunResult(
                succeeded && !cancelled,
                process.ExitCode,
                timedOut,
                cancelled,
                false,
                null,
                stdOut,
                stdErr);
        }
    }

    public DesktopProcessRunResult StartDetached(ProcessStartInfo startInfo)
    {
        if (startInfo is null)
        {
            throw new ArgumentNullException(nameof(startInfo));
        }

        if (TryShortCircuitStartFailure(startInfo, out var shortCircuitFailure))
        {
            return shortCircuitFailure;
        }

        try
        {
            var process = Process.Start(startInfo);
            if (process is null)
            {
                RegisterStartFailure(startInfo.FileName);
                return DesktopProcessRunResult.FromStartFailure("Process failed to start.");
            }

            ClearStartFailure(startInfo.FileName);
            return new DesktopProcessRunResult(
                true,
                null,
                false,
                false,
                false,
                null,
                string.Empty,
                string.Empty);
        }
        catch (Exception ex)
        {
            RegisterStartFailure(startInfo.FileName);
            return DesktopProcessRunResult.FromStartFailure(ex.Message);
        }
    }

    public DesktopProcessRunResult OpenWithShell(string path)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        };

        return StartDetached(startInfo);
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"DesktopProcessRunner: failed to kill process {process.Id}: {ex.Message}");
        }
    }

    private static bool TryShortCircuitStartFailure(ProcessStartInfo startInfo, out DesktopProcessRunResult failure)
    {
        failure = default!;

        var executable = startInfo.FileName?.Trim();
        if (string.IsNullOrWhiteSpace(executable))
        {
            failure = DesktopProcessRunResult.FromStartFailure("Executable path is required.");
            return true;
        }

        if (IsInCooldown(executable))
        {
            failure = DesktopProcessRunResult.FromStartFailure(
                $"Process launch temporarily disabled after recent start failure: '{executable}'.");
            return true;
        }

        if (!startInfo.UseShellExecute && !CanResolveExecutable(executable, allowShellScriptExtensions: false))
        {
            RegisterStartFailure(executable);
            failure = DesktopProcessRunResult.FromStartFailure($"Executable not found: '{executable}'.");
            return true;
        }

        return false;
    }

    private static bool IsInCooldown(string executable)
    {
        if (!StartFailureCooldownUtc.TryGetValue(executable, out var cooldownUntilUtc))
        {
            return false;
        }

        if (DateTimeOffset.UtcNow <= cooldownUntilUtc)
        {
            return true;
        }

        StartFailureCooldownUtc.TryRemove(executable, out _);
        return false;
    }

    private static void RegisterStartFailure(string? executable)
    {
        if (string.IsNullOrWhiteSpace(executable))
        {
            return;
        }

        StartFailureCooldownUtc[executable.Trim()] = DateTimeOffset.UtcNow.Add(StartFailureBackoff);
    }

    private static void ClearStartFailure(string? executable)
    {
        if (string.IsNullOrWhiteSpace(executable))
        {
            return;
        }

        StartFailureCooldownUtc.TryRemove(executable.Trim(), out _);
    }

    private static bool CanResolveExecutable(string executable, bool allowShellScriptExtensions)
    {
        if (File.Exists(executable))
        {
            return true;
        }

        var hasDirectorySeparator = executable.Contains(Path.DirectorySeparatorChar) || executable.Contains(Path.AltDirectorySeparatorChar);
        var hasExtension = Path.HasExtension(executable);
        if (Path.IsPathRooted(executable) || hasDirectorySeparator)
        {
            if (hasExtension)
            {
                return false;
            }

            var pathext = GetExecutableExtensions(allowShellScriptExtensions);
            return pathext.Any(ext => File.Exists(executable + ext));
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return false;
        }

        var extensions = hasExtension
            ? new[] { string.Empty }
            : GetExecutableExtensions(allowShellScriptExtensions);

        foreach (var pathEntry in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var ext in extensions)
            {
                var candidate = Path.Combine(pathEntry, executable + ext);
                if (File.Exists(candidate))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string[] GetExecutableExtensions(bool allowShellScriptExtensions)
    {
        var ext = Environment.GetEnvironmentVariable("PATHEXT");
        if (string.IsNullOrWhiteSpace(ext))
        {
            return allowShellScriptExtensions
                ? new[] { ".exe", ".cmd", ".bat", ".com" }
                : new[] { ".exe", ".com" };
        }

        var candidates = ext
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.StartsWith(".", StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        if (!allowShellScriptExtensions)
        {
            candidates = candidates.Where(IsNativeExecutableExtension);
        }

        return candidates
            .ToArray();
    }

    private static bool IsNativeExecutableExtension(string extension)
    {
        return string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".com", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record DesktopProcessRunResult(
    bool Succeeded,
    int? ExitCode,
    bool TimedOut,
    bool Cancelled,
    bool StartFailed,
    string? ErrorMessage,
    string StandardOutput,
    string StandardError)
{
    public static DesktopProcessRunResult FromStartFailure(string message)
        => new(false, null, false, false, true, message, string.Empty, string.Empty);
}
