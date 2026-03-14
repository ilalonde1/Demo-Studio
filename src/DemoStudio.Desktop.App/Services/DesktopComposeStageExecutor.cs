using DemoStudio.Application.Abstractions.System;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace DemoStudio.Desktop.App.Services;

internal sealed class DesktopComposeStageExecutor
{
    private readonly IProcessLauncher _processLauncher;
    private readonly ILogger _logger;

    public DesktopComposeStageExecutor(IProcessLauncher processLauncher, ILogger logger)
    {
        _processLauncher = processLauncher ?? throw new ArgumentNullException(nameof(processLauncher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ComposeStageOutcome> RunAsync(
        string ffmpegPath,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string stageName,
        TimeSpan timeout,
        string composeOperationId,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["ComposeOperationId"] = composeOperationId,
            ["ComposeStage"] = stageName
        });

        try
        {
            await using var handle = await _processLauncher.StartProcessAsync(
                new ProcessStartRequest(
                    ffmpegPath,
                    string.Empty,
                    workingDirectory,
                    timeout,
                    arguments,
                    $"ffmpeg-compose-{stageName.Replace(' ', '-')}",
                    composeOperationId),
                cancellationToken);
            var execution = await handle.WaitAsync(cancellationToken);
            if (execution.Cancelled)
            {
                _logger.LogWarning("Compose stage {StageName} cancelled after {ElapsedMs} ms.", stageName, stopwatch.ElapsedMilliseconds);
                return ComposeStageOutcome.Failure("DS-COMP-CANCEL", $"{stageName} cancelled.", stopwatch.ElapsedMilliseconds);
            }

            if (execution.TimedOut)
            {
                _logger.LogWarning("Compose stage {StageName} timed out after {TimeoutSeconds} seconds.", stageName, timeout.TotalSeconds);
                return ComposeStageOutcome.Failure("DS-COMP-TIMEOUT", $"{stageName} timed out after {timeout.TotalSeconds:0}s.", stopwatch.ElapsedMilliseconds);
            }

            if (execution.ExitCode != 0)
            {
                _logger.LogWarning("Compose stage {StageName} exited with code {ExitCode}.", stageName, execution.ExitCode);
                return ComposeStageOutcome.Failure("DS-COMP-FFMPEG", execution.StdErr, stopwatch.ElapsedMilliseconds);
            }

            _logger.LogInformation("Compose stage {StageName} completed in {ElapsedMs} ms.", stageName, stopwatch.ElapsedMilliseconds);
            return ComposeStageOutcome.Success(stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Compose stage {StageName} cancelled after {ElapsedMs} ms.", stageName, stopwatch.ElapsedMilliseconds);
            return ComposeStageOutcome.Failure("DS-COMP-CANCEL", $"{stageName} cancelled.", stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Compose stage {StageName} failed to start after {ElapsedMs} ms.", stageName, stopwatch.ElapsedMilliseconds);
            return ComposeStageOutcome.Failure("DS-COMP-START", $"{stageName} failed to start: {ex.Message}", stopwatch.ElapsedMilliseconds);
        }
    }
}

internal sealed record ComposeStageOutcome(bool Succeeded, string FailureCode, string Message, long ElapsedMs)
{
    public static ComposeStageOutcome Success(long elapsedMs) => new(true, string.Empty, string.Empty, elapsedMs);

    public static ComposeStageOutcome Failure(string code, string message, long elapsedMs)
        => new(false, code, message ?? string.Empty, elapsedMs);
}
