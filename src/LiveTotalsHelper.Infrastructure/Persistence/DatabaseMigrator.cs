using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LiveTotalsHelper.Infrastructure.Persistence;

public static class DatabaseMigrator
{
    public static async Task<LiveTotalsDbContext> CreateMigratedDbContextAsync(IConfiguration configuration, TextWriter log, CancellationToken cancellationToken)
    {
        string connectionString = configuration.GetConnectionString("LiveTotalsDb")
            ?? throw new InvalidOperationException("Connection string 'LiveTotalsDb' was not found in appsettings.json.");

        var options = new DbContextOptionsBuilder<LiveTotalsDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        var dbContext = new LiveTotalsDbContext(options);
        await log.WriteLineAsync("Applying pending PostgreSQL database migrations...");
        await dbContext.Database.MigrateAsync(cancellationToken);
        await log.WriteLineAsync("Database is ready.");
        return dbContext;
    }
}
