namespace DemoStudio.Infrastructure.Persistence.Repositories;

using DemoStudio.Application.Abstractions.Persistence;
using DemoStudio.Domain.Entities;
using Microsoft.EntityFrameworkCore;

public sealed class DemoFlowRepository : IDemoFlowRepository
{
    private readonly DemoStudioDbContext _dbContext;

    public DemoFlowRepository(DemoStudioDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(DemoFlow flow, CancellationToken cancellationToken = default)
    {
        await _dbContext.DemoFlows.AddAsync(flow, cancellationToken);
    }

    public Task<DemoFlow?> GetByIdWithStepsAsync(Guid flowId, CancellationToken cancellationToken = default)
    {
        return _dbContext.DemoFlows
            .Include(x => x.Steps)
            .FirstOrDefaultAsync(x => x.Id == flowId, cancellationToken);
    }

    public async Task<int> GetNextVersionAsync(Guid demoProjectId, string flowName, CancellationToken cancellationToken = default)
    {
        var maxVersion = await _dbContext.DemoFlows
            .AsNoTracking()
            .Where(x => x.DemoProjectId == demoProjectId && x.Name == flowName)
            .Select(x => (int?)x.Version)
            .MaxAsync(cancellationToken);

        return (maxVersion ?? 0) + 1;
    }
}
