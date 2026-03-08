namespace DemoStudio.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

public sealed class DemoStudioDesignTimeDbContextFactory : IDesignTimeDbContextFactory<DemoStudioDbContext>
{
    public DemoStudioDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<DemoStudioDbContext>();
        var connectionString = "Server=(localdb)\\MSSQLLocalDB;Database=DemoStudio;Trusted_Connection=True;TrustServerCertificate=True";

        optionsBuilder.UseDemoStudioSqlServer(connectionString);
        return new DemoStudioDbContext(optionsBuilder.Options);
    }
}
