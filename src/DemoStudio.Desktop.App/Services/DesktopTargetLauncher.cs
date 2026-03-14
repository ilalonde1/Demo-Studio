using System.Collections.Concurrent;
using System.IO;
using DemoStudio.Application.Abstractions.System;

namespace DemoStudio.Desktop.App.Services;

public sealed class DesktopTargetLauncher : IDisposable, IAsyncDisposable
{
    private readonly IProcessLauncher _processLauncher;
    private readonly ConcurrentDictionary<int, IProcessHandle> _activeLaunchHandles = new();

    public DesktopTargetLauncher(IProcessLauncher processLauncher)
    {
        _processLauncher = processLauncher ?? throw new ArgumentNullException(nameof(processLauncher));
    }

    public async Task<DesktopTargetLaunchResult> LaunchAsync(DesktopLaunchProfile profile, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(profile.ExecutablePath))
        {
            return DesktopTargetLaunchResult.Failure("Executable path is required.");
        }

        var executablePath = Path.GetFullPath(profile.ExecutablePath.Trim());
        if (!File.Exists(executablePath))
        {
            return DesktopTargetLaunchResult.Failure($"Executable not found: '{executablePath}'.");
        }

        var workingDirectory = string.IsNullOrWhiteSpace(profile.WorkingDirectory)
            ? Path.GetDirectoryName(executablePath)
            : Path.GetFullPath(profile.WorkingDirectory.Trim());
        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
        {
            return DesktopTargetLaunchResult.Failure($"Working directory not found: '{workingDirectory}'.");
        }

        IProcessHandle handle;
        try
        {
            handle = await _processLauncher.StartProcessAsync(
                new ProcessStartRequest(
                    executablePath,
                    profile.Arguments ?? string.Empty,
                    workingDirectory,
                    Timeout: null,
                    ArgumentList: null,
                    OperationName: "target-launch",
                    CorrelationId: Path.GetFileNameWithoutExtension(executablePath)),
                cancellationToken);
        }
        catch (Exception ex)
        {
            return DesktopTargetLaunchResult.Failure(ex.Message);
        }

        if (handle.ProcessId is int processId)
        {
            _activeLaunchHandles[processId] = handle;
        }
        else
        {
            await handle.DisposeAsync();
        }

        if (profile.StartupDelaySeconds > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(profile.StartupDelaySeconds), cancellationToken);
        }

        return DesktopTargetLaunchResult.Success($"Launched '{Path.GetFileName(executablePath)}'.");
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var handle in _activeLaunchHandles.Values)
        {
            try
            {
                await handle.DisposeAsync();
            }
            catch
            {
            }
        }

        _activeLaunchHandles.Clear();
    }

    public void Dispose()
    {
        foreach (var handle in _activeLaunchHandles.Values)
        {
            try
            {
                handle.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch
            {
            }
        }

        _activeLaunchHandles.Clear();
    }
}

public sealed record DesktopTargetLaunchResult(bool Succeeded, string Message)
{
    public static DesktopTargetLaunchResult Success(string message) => new(true, message);

    public static DesktopTargetLaunchResult Failure(string message) => new(false, message);
}
