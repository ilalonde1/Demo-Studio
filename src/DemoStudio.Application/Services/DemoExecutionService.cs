namespace DemoStudio.Application.Services;

using System.Collections.Concurrent;
using DemoStudio.Application.Abstractions.Persistence;
using DemoStudio.Application.DTOs;
using DemoStudio.Application.Requests;
using DemoStudio.Application.Validation;
using DemoStudio.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

public sealed class DemoExecutionService : IDemoExecutionService
{
    private static readonly ConcurrentDictionary<Guid, RunExecutionLock> RunExecutionLocks = new();

    private readonly IDemoRunRepository _runRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICommandValidator<QueueDemoRunCommand> _validator;
    private readonly IDemoRunPipeline _pipeline;
    private readonly ILogger<DemoExecutionService> _logger;

    public DemoExecutionService(
        IDemoRunRepository runRepository,
        IUnitOfWork unitOfWork,
        ICommandValidator<QueueDemoRunCommand> validator,
        IDemoRunPipeline pipeline)
        : this(runRepository, unitOfWork, validator, pipeline, NullLogger<DemoExecutionService>.Instance)
    {
    }

    public DemoExecutionService(
        IDemoRunRepository runRepository,
        IUnitOfWork unitOfWork,
        ICommandValidator<QueueDemoRunCommand> validator,
        IDemoRunPipeline pipeline,
        ILogger<DemoExecutionService> logger)
    {
        _runRepository = runRepository;
        _unitOfWork = unitOfWork;
        _validator = validator;
        _pipeline = pipeline;
        _logger = logger;
    }

    public async Task<DemoRunDto> QueueRunAsync(QueueDemoRunCommand command, CancellationToken cancellationToken = default)
    {
        var validation = _validator.Validate(command);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(string.Join("; ", validation.Errors));
        }

        var run = new DemoRun(command.DemoProjectId, command.DemoFlowId, command.RequestedBy);
        await _runRepository.AddAsync(run, cancellationToken);
        await SaveChangesWithConcurrencyHandlingAsync("queueing run", cancellationToken);
        _logger.LogInformation(
            "Queued demo run {RunId} for project {ProjectId} and flow {FlowId}.",
            run.Id,
            run.DemoProjectId,
            run.DemoFlowId);

        return ToDto(run);
    }

    public async Task<DemoRunDto?> ProcessNextQueuedRunAsync(CancellationToken cancellationToken = default)
    {
        var claimedRun = await _runRepository.ClaimNextQueuedAsync(cancellationToken);
        if (claimedRun is null)
        {
            return null;
        }

        return await ExecuteRunCoreAsync(claimedRun.Id, requireQueued: false, cancellationToken);
    }

    public async Task<DemoRunDto> ExecuteRunAsync(Guid demoRunId, CancellationToken cancellationToken = default)
    {
        return await ExecuteRunCoreAsync(demoRunId, requireQueued: true, cancellationToken);
    }

    private async Task<DemoRunDto> ExecuteRunCoreAsync(Guid demoRunId, bool requireQueued, CancellationToken cancellationToken)
    {
        var runLock = RunExecutionLocks.GetOrAdd(demoRunId, _ => new RunExecutionLock());
        runLock.AddRef();
        var gateAcquired = false;

        try
        {
            await runLock.Gate.WaitAsync(cancellationToken);
            gateAcquired = true;

            var context = await _runRepository.GetExecutionContextAsync(demoRunId, cancellationToken)
                ?? throw new InvalidOperationException($"Run '{demoRunId}' was not found.");
            using var scope = _logger.BeginScope(new Dictionary<string, object?>
            {
                ["RunId"] = context.Run.Id,
                ["ProjectId"] = context.Run.DemoProjectId,
                ["FlowId"] = context.Run.DemoFlowId
            });

            if (requireQueued)
            {
                if (context.Run.Status != Domain.Enums.DemoRunStatus.Queued)
                {
                    throw new InvalidOperationException($"Run '{demoRunId}' is in status '{context.Run.Status}' and cannot be executed.");
                }

                context.Run.MarkRunning($"pending/{context.Run.Id:N}");
                await SaveChangesWithConcurrencyHandlingAsync("transitioning run to Running", cancellationToken);
                _logger.LogInformation("Run moved to Running state.");
            }
            else if (context.Run.Status != Domain.Enums.DemoRunStatus.Running)
            {
                throw new InvalidOperationException($"Run '{demoRunId}' is in status '{context.Run.Status}' and cannot be executed.");
            }

            try
            {
                var pipelineResult = await _pipeline.ExecuteAsync(context, cancellationToken);

                if (pipelineResult.Succeeded)
                {
                    var primaryOutputPath = pipelineResult.RedactedVideoPath ?? pipelineResult.RawVideoPath
                        ?? throw new InvalidOperationException("Pipeline succeeded without output paths.");

                    context.Run.SetOutputArtifacts(
                        pipelineResult.OutputDirectory,
                        pipelineResult.RawVideoPath,
                        pipelineResult.RedactedVideoPath,
                        pipelineResult.LogPath);
                    context.Run.MarkSucceeded(
                        primaryOutputPath,
                        pipelineResult.RawVideoPath,
                        pipelineResult.RedactedVideoPath,
                        pipelineResult.LogPath);
                    _logger.LogInformation("Run completed successfully.");
                }
                else
                {
                    var failure = RunFailureClassifier.FromReason(pipelineResult.FailureReason ?? "Pipeline failed.");
                    context.Run.SetOutputArtifacts(
                        pipelineResult.OutputDirectory,
                        pipelineResult.RawVideoPath,
                        pipelineResult.RedactedVideoPath,
                        pipelineResult.LogPath);
                    context.Run.MarkFailed(
                        failure.Formatted,
                        pipelineResult.RawVideoPath,
                        pipelineResult.RedactedVideoPath,
                        pipelineResult.LogPath);
                    _logger.LogWarning(
                        "Run completed with failure {FailureCode}: {FailureReason}",
                        failure.Code,
                        failure.Message);
                }
            }
            catch (Exception ex)
            {
                var failure = RunFailureClassifier.FromException(ex);
                context.Run.MarkFailed(failure.Formatted);
                _logger.LogError(ex, "Run execution failed unexpectedly with {FailureCode}: {FailureReason}", failure.Code, failure.Message);
            }

            await SaveChangesWithConcurrencyHandlingAsync("persisting run completion", cancellationToken);
            return ToDto(context.Run);
        }
        finally
        {
            if (gateAcquired)
            {
                runLock.Gate.Release();
            }

            if (runLock.ReleaseRef() == 0)
            {
                RunExecutionLocks.TryRemove(demoRunId, out _);
                runLock.Dispose();
            }
        }
    }

    public async Task<IReadOnlyCollection<DemoRunDto>> ListRecentRunsAsync(int take = 50, CancellationToken cancellationToken = default)
    {
        var runs = await _runRepository.ListRecentAsync(take, cancellationToken);
        return runs.Select(ToDto).ToArray();
    }

    private static DemoRunDto ToDto(DemoRun run)
    {
        return new DemoRunDto(
            run.Id,
            run.DemoProjectId,
            run.DemoFlowId,
            run.Status,
            run.RequestedBy,
            run.QueuedAtUtc,
            run.StartedAtUtc,
            run.CompletedAtUtc,
            run.OutputVideoPath,
            run.FailureReason,
            run.OutputDirectory,
            run.RawVideoPath,
            run.RedactedVideoPath,
            run.LogPath);
    }

    private async Task SaveChangesWithConcurrencyHandlingAsync(string operation, CancellationToken cancellationToken)
    {
        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException ex)
        {
            throw new InvalidOperationException($"A concurrency conflict occurred while {operation}.", ex);
        }
    }

    private sealed class RunExecutionLock : IDisposable
    {
        private int _refCount;

        public SemaphoreSlim Gate { get; } = new(1, 1);

        public void AddRef()
        {
            Interlocked.Increment(ref _refCount);
        }

        public int ReleaseRef()
        {
            return Interlocked.Decrement(ref _refCount);
        }

        public void Dispose()
        {
            Gate.Dispose();
        }
    }
}
