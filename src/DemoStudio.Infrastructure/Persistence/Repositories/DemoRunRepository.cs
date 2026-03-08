namespace DemoStudio.Infrastructure.Persistence.Repositories;

using System.Data;
using System.Diagnostics;
using DemoStudio.Application.Abstractions.Persistence;
using DemoStudio.Domain.Entities;
using DemoStudio.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

public sealed class DemoRunRepository : IDemoRunRepository
{
    private readonly DemoStudioDbContext _dbContext;
    private readonly ILogger<DemoRunRepository> _logger;

    public DemoRunRepository(DemoStudioDbContext dbContext)
        : this(dbContext, NullLogger<DemoRunRepository>.Instance)
    {
    }

    public DemoRunRepository(DemoStudioDbContext dbContext, ILogger<DemoRunRepository> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task AddAsync(DemoRun run, CancellationToken cancellationToken = default)
    {
        await _dbContext.DemoRuns.AddAsync(run, cancellationToken);
    }

    public Task<DemoRun?> GetByIdAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        return _dbContext.DemoRuns
            .FirstOrDefaultAsync(x => x.Id == runId, cancellationToken);
    }

    public async Task<DemoRunExecutionContext?> GetExecutionContextAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var total = Stopwatch.StartNew();
        var run = await _dbContext.DemoRuns
            .FirstOrDefaultAsync(x => x.Id == runId, cancellationToken);
        if (run is null)
        {
            _logger.LogDebug("Execution context lookup for run {RunId} returned no run in {ElapsedMs} ms.", runId, total.ElapsedMilliseconds);
            return null;
        }

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["RunId"] = run.Id,
            ["ProjectId"] = run.DemoProjectId,
            ["FlowId"] = run.DemoFlowId
        });

        var metadataTimer = Stopwatch.StartNew();
        var metadata = await _dbContext.DemoProjects
            .AsNoTracking()
            .Where(project => project.Id == run.DemoProjectId)
            .Select(project => new
            {
                Project = project,
                Flow = _dbContext.DemoFlows
                    .AsNoTracking()
                    .FirstOrDefault(flow => flow.Id == run.DemoFlowId),
                Target = _dbContext.ApplicationTargets
                    .AsNoTracking()
                    .Where(target => target.DemoProjectId == run.DemoProjectId)
                    .OrderBy(target => target.CreatedUtc)
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync(cancellationToken);
        metadataTimer.Stop();

        var project = metadata?.Project
            ?? throw new InvalidOperationException($"Project '{run.DemoProjectId}' was not found.");
        var target = metadata.Target
            ?? throw new InvalidOperationException($"Project '{run.DemoProjectId}' has no application target configured.");
        var flow = metadata.Flow
            ?? throw new InvalidOperationException($"Flow '{run.DemoFlowId}' was not found.");

        var stepsTimer = Stopwatch.StartNew();
        var steps = await _dbContext.FlowSteps
            .AsNoTracking()
            .Where(x => x.DemoFlowId == run.DemoFlowId)
            .OrderBy(x => x.Sequence)
            .ToListAsync(cancellationToken);
        stepsTimer.Stop();

        var rulesTimer = Stopwatch.StartNew();
        var redactionRules = await _dbContext.RedactionRules
            .AsNoTracking()
            .Where(x => x.DemoProjectId == run.DemoProjectId && x.IsEnabled)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
        rulesTimer.Stop();

        total.Stop();
        _logger.LogDebug(
            "Loaded execution context in {TotalMs} ms (metadata={MetadataMs} ms, steps={StepsMs} ms, rules={RulesMs} ms, stepCount={StepCount}, ruleCount={RuleCount}).",
            total.ElapsedMilliseconds,
            metadataTimer.ElapsedMilliseconds,
            stepsTimer.ElapsedMilliseconds,
            rulesTimer.ElapsedMilliseconds,
            steps.Count,
            redactionRules.Count);

        return new DemoRunExecutionContext(run, project, target, flow, steps, redactionRules);
    }

    public async Task<IReadOnlyCollection<DemoRun>> ListRecentAsync(int take, CancellationToken cancellationToken = default)
    {
        return await _dbContext.DemoRuns
            .AsNoTracking()
            .OrderByDescending(x => x.QueuedAtUtc)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync(cancellationToken);
    }

    public Task<DemoRun?> GetNextQueuedAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.DemoRuns
            .Where(x => x.Status == DemoRunStatus.Queued)
            .OrderBy(x => x.QueuedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<DemoRun?> ClaimNextQueuedAsync(CancellationToken cancellationToken = default)
    {
        if (string.Equals(_dbContext.Database.ProviderName, "Microsoft.EntityFrameworkCore.InMemory", StringComparison.OrdinalIgnoreCase))
        {
            var inMemoryRun = await _dbContext.DemoRuns
                .Where(x => x.Status == DemoRunStatus.Queued)
                .OrderBy(x => x.QueuedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            if (inMemoryRun is null)
            {
                return null;
            }

            inMemoryRun.MarkRunning($"pending/{inMemoryRun.Id:N}");
            await _dbContext.SaveChangesAsync(cancellationToken);
            return inMemoryRun;
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var run = await _dbContext.DemoRuns
            .Where(x => x.Status == DemoRunStatus.Queued)
            .OrderBy(x => x.QueuedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (run is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        run.MarkRunning($"pending/{run.Id:N}");
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return run;
    }
}
