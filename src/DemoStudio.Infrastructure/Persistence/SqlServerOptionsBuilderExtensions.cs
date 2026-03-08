namespace DemoStudio.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

internal static class SqlServerOptionsBuilderExtensions
{
    private const int MaxRetryCount = 5;
    private const int MaxRetryDelaySeconds = 10;
    private const int CommandTimeoutSeconds = 60;

    public static DbContextOptionsBuilder<DemoStudioDbContext> UseDemoStudioSqlServer(
        this DbContextOptionsBuilder<DemoStudioDbContext> optionsBuilder,
        string connectionString)
    {
        return optionsBuilder.UseSqlServer(
            connectionString,
            sql =>
            {
                sql.EnableRetryOnFailure(
                    maxRetryCount: MaxRetryCount,
                    maxRetryDelay: TimeSpan.FromSeconds(MaxRetryDelaySeconds),
                    errorNumbersToAdd: null);
                sql.CommandTimeout(CommandTimeoutSeconds);
            });
    }
}
