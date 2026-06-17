using LiveTotalsHelper.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace LiveTotalsHelper.Infrastructure.Persistence;

public sealed class LiveTotalsDbContext : DbContext
{
    public LiveTotalsDbContext(DbContextOptions<LiveTotalsDbContext> options) : base(options)
    {
    }

    public DbSet<MatchEntity> Matches => Set<MatchEntity>();
    public DbSet<MatchEventEntity> MatchEvents => Set<MatchEventEntity>();
    public DbSet<MatchStatEntity> MatchStats => Set<MatchStatEntity>();
    public DbSet<FlashscoreOddsEntity> FlashscoreOdds => Set<FlashscoreOddsEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MatchEntity>(entity =>
        {
            entity.ToTable("Matches");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.EventId).IsUnique();
            entity.HasIndex(x => x.FlashscoreId);
            entity.HasIndex(x => new { x.TournamentId, x.SeasonId, x.RoundNumber });
            entity.HasIndex(x => new { x.HomeTeamId, x.AwayTeamId, x.StartTimeUtc });

            entity.Property(x => x.EventId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.FlashscoreId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.LeagueName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.LeagueSlug).HasMaxLength(200).IsRequired();
            entity.Property(x => x.CountryName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.CountryCode).HasMaxLength(10).IsRequired();
            entity.Property(x => x.SeasonName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.SeasonYear).HasMaxLength(20).IsRequired();
            entity.Property(x => x.HomeTeamId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.HomeTeamName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.HomeTeamSlug).HasMaxLength(200).IsRequired();
            entity.Property(x => x.HomeTeamShortName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.AwayTeamId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.AwayTeamName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.AwayTeamSlug).HasMaxLength(200).IsRequired();
            entity.Property(x => x.AwayTeamShortName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Slug).HasMaxLength(300).IsRequired();
            entity.Property(x => x.StatusType).HasMaxLength(50).IsRequired();
            entity.Property(x => x.StatusDescription).HasMaxLength(100).IsRequired();
            entity.Property(x => x.CalendarJsonPath).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.EventMetaJsonPath).HasMaxLength(1000).IsRequired();
        });

        modelBuilder.Entity<MatchEventEntity>(entity =>
        {
            entity.ToTable("MatchEvents");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.EventId, x.IncidentId, x.IncidentType }).IsUnique();
            entity.HasIndex(x => new { x.MatchId, x.Minute });
            entity.HasIndex(x => new { x.MatchId, x.IncidentType });

            entity.Property(x => x.EventId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.IncidentId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.IncidentType).HasMaxLength(50).IsRequired();
            entity.Property(x => x.IncidentClass).HasMaxLength(50).IsRequired();
            entity.Property(x => x.PlayerName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.PlayerId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.AssistPlayerName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.AssistPlayerId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Reason).HasMaxLength(200).IsRequired();

            entity.HasOne(x => x.Match)
                .WithMany(x => x.Events)
                .HasForeignKey(x => x.MatchId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MatchStatEntity>(entity =>
        {
            entity.ToTable("MatchStats");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.MatchId, x.Period }).IsUnique();
            entity.HasIndex(x => new { x.EventId, x.Period });

            entity.Property(x => x.EventId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Period).HasMaxLength(30).IsRequired();
            entity.Property(x => x.StatisticsJsonPath).HasMaxLength(1000).IsRequired();

            entity.HasOne(x => x.Match)
                .WithMany(x => x.Stats)
                .HasForeignKey(x => x.MatchId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FlashscoreOddsEntity>(entity =>
        {
            entity.ToTable("FlashscoreOdds");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.MatchId, x.Market, x.Bookmaker, x.Selection, x.Line, x.Odds }).IsUnique();
            entity.HasIndex(x => new { x.EventId, x.Market });

            entity.Property(x => x.EventId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Market).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Bookmaker).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Selection).HasMaxLength(100).IsRequired();
            entity.Property(x => x.SourceUrl).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.OddsJsonPath).HasMaxLength(1000).IsRequired();

            entity.HasOne(x => x.Match)
                .WithMany(x => x.FlashscoreOdds)
                .HasForeignKey(x => x.MatchId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
