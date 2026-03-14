namespace DemoStudio.Application.Abstractions.System;

public interface IProcessLauncher
{
    Task<IProcessHandle> StartProcessAsync(ProcessStartRequest request, CancellationToken cancellationToken = default);

    Task<ProcessLaunchResult> LaunchAsync(ProcessLaunchRequest request, CancellationToken cancellationToken = default);
}

public sealed record ProcessStartRequest(
    string FileName,
    string Arguments,
    string WorkingDirectory,
    TimeSpan? Timeout = null,
    IReadOnlyList<string>? ArgumentList = null,
    string? OperationName = null,
    string? CorrelationId = null);

public sealed record ProcessLaunchRequest(
    string FileName,
    string Arguments,
    string WorkingDirectory,
    IReadOnlyList<string>? ArgumentList = null,
    string? OperationName = null,
    string? CorrelationId = null);

public sealed record ProcessExecutionResult(
    int ExitCode,
    string StdOut,
    string StdErr,
    bool TimedOut,
    bool Cancelled);

public sealed record ProcessLaunchResult(
    bool Started,
    int? ProcessId,
    ProcessExecutionResult? Execution,
    string? ErrorMessage);

public interface IProcessHandle : IAsyncDisposable
{
    int? ProcessId { get; }

    Task<ProcessExecutionResult> WaitAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
