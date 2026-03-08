namespace DemoStudio.Application.Abstractions.Persistence;

using DemoStudio.Domain.Entities;

public interface IDemoRunRepository
{
    Task AddAsync(DemoRun run, CancellationToken cancellationToken = default);

    Task<DemoRun?> GetByIdAsync(Guid runId, CancellationToken cancellationToken = default);

    Task<DemoRunExecutionContext?> GetExecutionContextAsync(Guid runId, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<DemoRun>> ListRecentAsync(int take, CancellationToken cancellationToken = default);

    Task<DemoRun?> GetNextQueuedAsync(CancellationToken cancellationToken = default);

    Task<DemoRun?> ClaimNextQueuedAsync(CancellationToken cancellationToken = default);
}
