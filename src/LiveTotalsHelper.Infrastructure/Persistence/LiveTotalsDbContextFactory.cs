using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LiveTotalsHelper.Infrastructure.Persistence;

public sealed class LiveTotalsDbContextFactory : IDesignTimeDbContextFactory<LiveTotalsDbContext>
{
    public LiveTotalsDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<LiveTotalsDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Port=5432;Database=livetotals;Username=postgres;Password=Tartaruga_050910");
        return new LiveTotalsDbContext(optionsBuilder.Options);
    }
}
