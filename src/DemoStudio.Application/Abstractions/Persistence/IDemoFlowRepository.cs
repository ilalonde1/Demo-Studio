namespace DemoStudio.Application.Abstractions.Persistence;

using DemoStudio.Domain.Entities;

public interface IDemoFlowRepository
{
    Task AddAsync(DemoFlow flow, CancellationToken cancellationToken = default);

    Task<DemoFlow?> GetByIdWithStepsAsync(Guid flowId, CancellationToken cancellationToken = default);

    Task<int> GetNextVersionAsync(Guid demoProjectId, string flowName, CancellationToken cancellationToken = default);
}
