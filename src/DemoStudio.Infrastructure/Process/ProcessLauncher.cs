namespace DemoStudio.Infrastructure.Process;

using DemoStudio.Application.Abstractions.System;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text;

public sealed class ProcessLauncher : IProcessLauncher
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);
    private readonly ILogger<ProcessLauncher> _logger;

    public ProcessLauncher()
        : this(NullLogger<ProcessLauncher>.Instance)
    {
    }

    public ProcessLauncher(ILogger<ProcessLauncher> logger)
    {
        _logger = logger;
    }

    public Task<IProcessHandle> StartProcessAsync(ProcessStartRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var operationId = Guid.NewGuid().ToString("N");
        var operationName = string.IsNullOrWhiteSpace(request.OperationName) ? "process-start" : request.OperationName;

        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        ApplyArguments(startInfo, request.Arguments, request.ArgumentList);

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["ProcessOperationId"] = operationId,
            ["ProcessOperationName"] = operationName,
            ["ProcessCorrelationId"] = request.CorrelationId,
            ["ProcessFileName"] = request.FileName
        });
        _logger.LogInformation(
            "Starting process {OperationName} ({OperationId}) for {FileName} in {WorkingDirectory}.",
            operationName,
            operationId,
            request.FileName,
            request.WorkingDirectory);

        var process = System.Diagnostics.Process.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException("Process failed to start.");
        }

        _logger.LogInformation(
            "Process {OperationName} ({OperationId}) started with pid {ProcessId}.",
            operationName,
            operationId,
            process.Id);

        return Task.FromResult<IProcessHandle>(new ProcessHandle(process, request.Timeout, _logger, operationId, operationName, request.CorrelationId));
    }

    public async Task<ProcessLaunchResult> LaunchAsync(ProcessLaunchRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var handle = await StartProcessAsync(
                new ProcessStartRequest(
                    request.FileName,
                    request.Arguments,
                    request.WorkingDirectory,
                    DefaultTimeout,
                    request.ArgumentList,
                    request.OperationName,
                    request.CorrelationId),
                cancellationToken);

            var execution = await handle.WaitAsync(cancellationToken);
            return new ProcessLaunchResult(true, handle.ProcessId, execution, null);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Process launch for {FileName} was cancelled.", request.FileName);
            return new ProcessLaunchResult(
                true,
                null,
                new ProcessExecutionResult(-1, string.Empty, "Process execution was cancelled.", TimedOut: false, Cancelled: true),
                "Process execution was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Process launch failed for {FileName}.", request.FileName);
            return new ProcessLaunchResult(false, null, null, ex.Message);
        }
    }

    private sealed class ProcessHandle : IProcessHandle, IAsyncDisposable
    {
        private readonly Process _process;
        private readonly int _processId;
        private readonly Task<string> _stdOutTask;
        private readonly Task<string> _stdErrTask;
        private readonly TimeSpan? _timeout;
        private readonly SemaphoreSlim _sync = new(1, 1);
        private readonly ILogger _logger;
        private readonly string _operationId;
        private readonly string _operationName;
        private readonly string? _correlationId;

        private ProcessExecutionResult? _cachedExecution;
        private bool _disposed;

        public ProcessHandle(Process process, TimeSpan? timeout, ILogger logger, string operationId, string operationName, string? correlationId)
        {
            _process = process;
            _processId = process.Id;
            _timeout = timeout;
            _logger = logger;
            _operationId = operationId;
            _operationName = operationName;
            _correlationId = correlationId;
            _stdOutTask = process.StandardOutput.ReadToEndAsync();
            _stdErrTask = process.StandardError.ReadToEndAsync();
        }

        public int? ProcessId => _processId;

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await _sync.WaitAsync(cancellationToken);
            try
            {
                if (_disposed || _process.HasExited)
                {
                    return;
                }

                try
                {
                    await _process.StandardInput.WriteLineAsync("q");
                    await _process.StandardInput.FlushAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to send graceful stop input to process {ProcessId}.", _processId);
                }

                using var graceCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                try
                {
                    await _process.WaitForExitAsync(graceCts.Token);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogDebug("Graceful stop timed out for process {ProcessId}; killing process tree.", _processId);
                    TryKillTree();
                }
            }
            finally
            {
                _sync.Release();
            }
        }

        public async Task<ProcessExecutionResult> WaitAsync(CancellationToken cancellationToken = default)
        {
            await _sync.WaitAsync(cancellationToken);
            try
            {
                if (_cachedExecution is not null)
                {
                    return _cachedExecution;
                }

                var timedOut = false;
                var cancelled = false;

                using var timeoutCts = _timeout.HasValue ? new CancellationTokenSource(_timeout.Value) : null;
                using var linkedCts = timeoutCts is null
                    ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                    : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                using var killRegistration = linkedCts.Token.Register(TryKillTree);

                try
                {
                    await _process.WaitForExitAsync(linkedCts.Token);
                }
                catch (OperationCanceledException) when (timeoutCts is not null && timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    timedOut = true;
                    TryKillTree();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    cancelled = true;
                    TryKillTree();
                }

                var stdOut = await SafeReadAsync(_stdOutTask, "stdout");
                var stdErr = await SafeReadAsync(_stdErrTask, "stderr");

                var exitCode = _process.HasExited ? _process.ExitCode : -1;
                _cachedExecution = new ProcessExecutionResult(exitCode, stdOut, stdErr, timedOut, cancelled);
                LogCompletion(_cachedExecution);
                return _cachedExecution;
            }
            finally
            {
                _sync.Release();
            }
        }

        public ValueTask DisposeAsync()
        {
            DisposeCore();
            return ValueTask.CompletedTask;
        }

        private void TryKillTree()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to kill process tree for process {ProcessId}.", _processId);
            }
        }

        private async Task<string> SafeReadAsync(Task<string> streamTask, string streamName)
        {
            try
            {
                return await streamTask;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed reading process {StreamName} for process {ProcessId}.", streamName, _processId);
                return string.Empty;
            }
        }

        private void DisposeCore()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            try
            {
                _process.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to dispose process handle for process {ProcessId}.", _processId);
            }

        }

        private void LogCompletion(ProcessExecutionResult execution)
        {
            using var scope = _logger.BeginScope(new Dictionary<string, object?>
            {
                ["ProcessOperationId"] = _operationId,
                ["ProcessOperationName"] = _operationName,
                ["ProcessCorrelationId"] = _correlationId,
                ["ProcessId"] = _processId
            });

            if (execution.Cancelled)
            {
                _logger.LogWarning(
                    "Process {OperationName} ({OperationId}) was cancelled for pid {ProcessId}. stderr={StdErr}",
                    _operationName,
                    _operationId,
                    _processId,
                    SummarizeStream(execution.StdErr));
                return;
            }

            if (execution.TimedOut)
            {
                _logger.LogWarning(
                    "Process {OperationName} ({OperationId}) timed out for pid {ProcessId}. stderr={StdErr}",
                    _operationName,
                    _operationId,
                    _processId,
                    SummarizeStream(execution.StdErr));
                return;
            }

            if (execution.ExitCode != 0)
            {
                _logger.LogWarning(
                    "Process {OperationName} ({OperationId}) exited with code {ExitCode} for pid {ProcessId}. stderr={StdErr}",
                    _operationName,
                    _operationId,
                    execution.ExitCode,
                    _processId,
                    SummarizeStream(execution.StdErr));
                return;
            }

            _logger.LogInformation(
                "Process {OperationName} ({OperationId}) completed successfully for pid {ProcessId}. stdout={StdOut}",
                _operationName,
                _operationId,
                _processId,
                SummarizeStream(execution.StdOut));
        }

        private static string SummarizeStream(string stream)
        {
            if (string.IsNullOrWhiteSpace(stream))
            {
                return string.Empty;
            }

            var flattened = stream
                .Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal)
                .Trim();
            return flattened.Length <= 400 ? flattened : flattened[^400..];
        }
    }

    private static void ApplyArguments(ProcessStartInfo startInfo, string arguments, IReadOnlyList<string>? argumentList)
    {
        if (argumentList is { Count: > 0 })
        {
            foreach (var argument in argumentList)
            {
                startInfo.ArgumentList.Add(argument ?? string.Empty);
            }

            return;
        }

        startInfo.Arguments = arguments;
    }
}
