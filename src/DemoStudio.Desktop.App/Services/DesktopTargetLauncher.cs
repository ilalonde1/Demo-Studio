using System.Collections.Concurrent;
using System.IO;
using DemoStudio.Application.Abstractions.System;
using Microsoft.Extensions.Logging;

namespace DemoStudio.Desktop.App.Services;

public sealed class DesktopTargetLauncher : IDisposable, IAsyncDisposable
{
    private readonly IProcessLauncher _processLauncher;
    private readonly ILogger<DesktopTargetLauncher> _logger;
    private readonly ConcurrentDictionary<int, IProcessHandle> _activeLaunchHandles = new();

    public DesktopTargetLauncher(IProcessLauncher processLauncher, ILogger<DesktopTargetLauncher> logger)
    {
        _processLauncher = processLauncher ?? throw new ArgumentNullException(nameof(processLauncher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<DesktopTargetLaunchResult> LaunchAsync(DesktopLaunchProfile profile, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var validation = DesktopLaunchProfilePolicy.ValidateForExecution(profile);
        if (!validation.IsValid || validation.Profile is null)
        {
            _logger.LogWarning("Rejected invalid launch profile {ProfileName}: {Error}", profile.Name, validation.Error);
            return DesktopTargetLaunchResult.Failure(validation.Error ?? "Launch profile is invalid.");
        }

        var normalizedProfile = validation.Profile;
        var executablePath = normalizedProfile.ExecutablePath;
        var workingDirectory = normalizedProfile.WorkingDirectory!;

        IProcessHandle handle;
        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["ProcessCorrelationId"] = Path.GetFileNameWithoutExtension(executablePath),
            ["ProcessFileName"] = executablePath
        });
        try
        {
            handle = await _processLauncher.StartProcessAsync(
                new ProcessStartRequest(
                    executablePath,
                    normalizedProfile.Arguments ?? string.Empty,
                    workingDirectory,
                    Timeout: null,
                    ArgumentList: null,
                    OperationName: "target-launch",
                    CorrelationId: Path.GetFileNameWithoutExtension(executablePath)),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Target launch failed for {ExecutablePath}.", executablePath);
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

        if (normalizedProfile.StartupDelaySeconds > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(normalizedProfile.StartupDelaySeconds), cancellationToken);
        }

        _logger.LogInformation("Target launch succeeded for {ExecutablePath}.", executablePath);
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
                _logger.LogDebug("Ignoring target launcher dispose failure.");
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
                _logger.LogDebug("Ignoring target launcher synchronous dispose failure.");
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
