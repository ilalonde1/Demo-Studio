namespace DemoStudio.Application.Abstractions.Persistence;

using DemoStudio.Domain.Entities;

public interface IDemoProjectRepository
{
    Task AddAsync(DemoProject project, CancellationToken cancellationToken = default);

    Task<DemoProject?> GetByIdAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<DemoProject?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<ApplicationTarget?> GetPrimaryTargetAsync(Guid demoProjectId, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<DemoProject>> ListAsync(CancellationToken cancellationToken = default);
}
