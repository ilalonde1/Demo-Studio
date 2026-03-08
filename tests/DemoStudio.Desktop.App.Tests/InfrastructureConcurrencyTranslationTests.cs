using DemoStudio.Application.Abstractions.Persistence;
using DemoStudio.Domain.Entities;
using DemoStudio.Domain.ValueObjects;
using DemoStudio.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DemoStudio.Desktop.App.Tests;

public sealed class InfrastructureConcurrencyTranslationTests
{
    [Fact]
    public async Task SaveChangesAsync_ThrowsConcurrencyConflictException_OnConcurrentDelete()
    {
        var baseConnectionString =
            Environment.GetEnvironmentVariable("DEMOSTUDIO_TEST_SQL_CONNECTION")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__DemoStudio");
        if (string.IsNullOrWhiteSpace(baseConnectionString))
        {
            return;
        }

        var builder = new SqlConnectionStringBuilder(baseConnectionString)
        {
            InitialCatalog = $"DemoStudio_Concurrency_{Guid.NewGuid():N}"
        };
        var connectionString = builder.ConnectionString;

        var options = new DbContextOptionsBuilder<DemoStudioDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        try
        {
            await using (var setup = new DemoStudioDbContext(options))
            {
                await setup.Database.EnsureDeletedAsync();
                await setup.Database.EnsureCreatedAsync();
                var project = new DemoProject("Project A", new ProjectCode("PROJA"), "desc");
                await setup.DemoProjects.AddAsync(project);
                await setup.SaveChangesAsync();
            }

            await using var updater = new DemoStudioDbContext(options);
            await using var deleter = new DemoStudioDbContext(options);

            var inUpdater = await updater.DemoProjects.SingleAsync();
            var inDeleter = await deleter.DemoProjects.SingleAsync();

            deleter.DemoProjects.Remove(inDeleter);
            await deleter.SaveChangesAsync();

            inUpdater.Rename("Project A Updated");

            var ex = await Assert.ThrowsAsync<ConcurrencyConflictException>(() => updater.SaveChangesAsync());
            Assert.Contains("concurrency conflict", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await using var cleanup = new DemoStudioDbContext(options);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }
}
