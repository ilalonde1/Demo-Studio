namespace DemoStudio.Infrastructure.Persistence.Repositories;

using DemoStudio.Application.Abstractions.Persistence;
using DemoStudio.Domain.Entities;
using Microsoft.EntityFrameworkCore;

public sealed class DemoProjectRepository : IDemoProjectRepository
{
    private readonly DemoStudioDbContext _dbContext;

    public DemoProjectRepository(DemoStudioDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(DemoProject project, CancellationToken cancellationToken = default)
    {
        await _dbContext.DemoProjects.AddAsync(project, cancellationToken);
    }

    public Task<DemoProject?> GetByIdAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        return _dbContext.DemoProjects
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == projectId, cancellationToken);
    }

    public Task<DemoProject?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalized = code.Trim().ToUpperInvariant();

        return _dbContext.DemoProjects
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Code.Value == normalized, cancellationToken);
    }

    public Task<ApplicationTarget?> GetPrimaryTargetAsync(Guid demoProjectId, CancellationToken cancellationToken = default)
    {
        return _dbContext.ApplicationTargets
            .AsNoTracking()
            .Where(x => x.DemoProjectId == demoProjectId)
            .OrderBy(x => x.CreatedUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<DemoProject>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.DemoProjects
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
    }
}
