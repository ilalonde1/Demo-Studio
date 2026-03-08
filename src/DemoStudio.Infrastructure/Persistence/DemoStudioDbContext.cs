namespace DemoStudio.Infrastructure.Persistence;

using DemoStudio.Application.Abstractions.Persistence;
using DemoStudio.Domain.Entities;
using DemoStudio.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

public sealed class DemoStudioDbContext : DbContext, IUnitOfWork
{
    public DemoStudioDbContext(DbContextOptions<DemoStudioDbContext> options)
        : base(options)
    {
    }

    public DbSet<DemoProject> DemoProjects => Set<DemoProject>();

    public DbSet<ApplicationTarget> ApplicationTargets => Set<ApplicationTarget>();

    public DbSet<DemoFlow> DemoFlows => Set<DemoFlow>();

    public DbSet<FlowStep> FlowSteps => Set<FlowStep>();

    public DbSet<DemoRun> DemoRuns => Set<DemoRun>();

    public DbSet<RedactionRule> RedactionRules => Set<RedactionRule>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new DemoProjectConfiguration());
        modelBuilder.ApplyConfiguration(new ApplicationTargetConfiguration());
        modelBuilder.ApplyConfiguration(new DemoFlowConfiguration());
        modelBuilder.ApplyConfiguration(new FlowStepConfiguration());
        modelBuilder.ApplyConfiguration(new DemoRunConfiguration());
        modelBuilder.ApplyConfiguration(new RedactionRuleConfiguration());

        base.OnModelCreating(modelBuilder);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            var conflictedEntities = ex.Entries
                .Select(GetEntityTypeName)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            var summary = conflictedEntities.Length == 0
                ? "unknown entities"
                : string.Join(", ", conflictedEntities);

            throw new ConcurrencyConflictException(
                $"A concurrency conflict occurred while saving changes for {summary}.",
                ex);
        }
    }

    private static string GetEntityTypeName(EntityEntry entry)
    {
        return entry.Metadata.ClrType.Name;
    }
}
