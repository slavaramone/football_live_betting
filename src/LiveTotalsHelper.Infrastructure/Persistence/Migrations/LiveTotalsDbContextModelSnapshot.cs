using System;
using LiveTotalsHelper.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LiveTotalsHelper.Infrastructure.Persistence.Migrations;

[DbContext(typeof(LiveTotalsDbContext))]
partial class LiveTotalsDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
#pragma warning disable 612, 618
        modelBuilder
            .HasAnnotation("ProductVersion", "8.0.11")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);

        NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

        modelBuilder.Entity("LiveTotalsHelper.Infrastructure.Persistence.Entities.MatchEntity", b =>
        {
            b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("integer");
            NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));
            b.Property<string>("EventId").IsRequired().HasMaxLength(100).HasColumnType("character varying(100)");
            b.Property<string>("FlashscoreId").IsRequired().HasMaxLength(100).HasColumnType("character varying(100)");
            b.Property<int>("TournamentId").HasColumnType("integer");
            b.Property<string>("LeagueName").IsRequired().HasMaxLength(200).HasColumnType("character varying(200)");
            b.Property<string>("LeagueSlug").IsRequired().HasMaxLength(200).HasColumnType("character varying(200)");
            b.Property<string>("CountryName").IsRequired().HasMaxLength(100).HasColumnType("character varying(100)");
            b.Property<string>("CountryCode").IsRequired().HasMaxLength(10).HasColumnType("character varying(10)");
            b.Property<int>("SeasonId").HasColumnType("integer");
            b.Property<string>("SeasonName").IsRequired().HasMaxLength(200).HasColumnType("character varying(200)");
            b.Property<string>("SeasonYear").IsRequired().HasMaxLength(20).HasColumnType("character varying(20)");
            b.Property<int>("RoundNumber").HasColumnType("integer");
            b.Property<string>("HomeTeamId").IsRequired().HasMaxLength(100).HasColumnType("character varying(100)");
            b.Property<string>("HomeTeamName").IsRequired().HasMaxLength(200).HasColumnType("character varying(200)");
            b.Property<string>("HomeTeamSlug").IsRequired().HasMaxLength(200).HasColumnType("character varying(200)");
            b.Property<string>("HomeTeamShortName").IsRequired().HasMaxLength(200).HasColumnType("character varying(200)");
            b.Property<string>("AwayTeamId").IsRequired().HasMaxLength(100).HasColumnType("character varying(100)");
            b.Property<string>("AwayTeamName").IsRequired().HasMaxLength(200).HasColumnType("character varying(200)");
            b.Property<string>("AwayTeamSlug").IsRequired().HasMaxLength(200).HasColumnType("character varying(200)");
            b.Property<string>("AwayTeamShortName").IsRequired().HasMaxLength(200).HasColumnType("character varying(200)");
            b.Property<string>("Slug").IsRequired().HasMaxLength(300).HasColumnType("character varying(300)");
            b.Property<DateTimeOffset?>("StartTimeUtc").HasColumnType("timestamp with time zone");
            b.Property<string>("StatusType").IsRequired().HasMaxLength(50).HasColumnType("character varying(50)");
            b.Property<string>("StatusDescription").IsRequired().HasMaxLength(100).HasColumnType("character varying(100)");
            b.Property<int?>("HomeScoreCurrent").HasColumnType("integer");
            b.Property<int?>("AwayScoreCurrent").HasColumnType("integer");
            b.Property<int?>("HomeScorePeriod1").HasColumnType("integer");
            b.Property<int?>("AwayScorePeriod1").HasColumnType("integer");
            b.Property<int?>("HomeScorePeriod2").HasColumnType("integer");
            b.Property<int?>("AwayScorePeriod2").HasColumnType("integer");
            b.Property<string>("CalendarJsonPath").IsRequired().HasMaxLength(1000).HasColumnType("character varying(1000)");
            b.Property<string>("EventMetaJsonPath").IsRequired().HasMaxLength(1000).HasColumnType("character varying(1000)");
            b.Property<DateTimeOffset>("CalendarUpdatedAtUtc").HasColumnType("timestamp with time zone");
            b.HasKey("Id");
            b.HasIndex("EventId").IsUnique();
            b.HasIndex("FlashscoreId");
            b.HasIndex("TournamentId", "SeasonId", "RoundNumber");
            b.HasIndex("HomeTeamId", "AwayTeamId", "StartTimeUtc");
            b.ToTable("Matches");
        });

        modelBuilder.Entity("LiveTotalsHelper.Infrastructure.Persistence.Entities.MatchEventEntity", b =>
        {
            b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("integer");
            NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));
            b.Property<int>("MatchId").HasColumnType("integer");
            b.Property<string>("EventId").IsRequired().HasMaxLength(100).HasColumnType("character varying(100)");
            b.Property<string>("IncidentId").IsRequired().HasMaxLength(100).HasColumnType("character varying(100)");
            b.Property<string>("IncidentType").IsRequired().HasMaxLength(50).HasColumnType("character varying(50)");
            b.Property<string>("IncidentClass").IsRequired().HasMaxLength(50).HasColumnType("character varying(50)");
            b.Property<int>("Minute").HasColumnType("integer");
            b.Property<int?>("AddedTime").HasColumnType("integer");
            b.Property<int?>("TimeSeconds").HasColumnType("integer");
            b.Property<bool>("IsHome").HasColumnType("boolean");
            b.Property<int?>("HomeScore").HasColumnType("integer");
            b.Property<int?>("AwayScore").HasColumnType("integer");
            b.Property<string>("PlayerName").IsRequired().HasMaxLength(200).HasColumnType("character varying(200)");
            b.Property<string>("PlayerId").IsRequired().HasMaxLength(100).HasColumnType("character varying(100)");
            b.Property<string>("AssistPlayerName").IsRequired().HasMaxLength(200).HasColumnType("character varying(200)");
            b.Property<string>("AssistPlayerId").IsRequired().HasMaxLength(100).HasColumnType("character varying(100)");
            b.Property<string>("Reason").IsRequired().HasMaxLength(200).HasColumnType("character varying(200)");
            b.HasKey("Id");
            b.HasIndex("MatchId", "Minute");
            b.HasIndex("MatchId", "IncidentType");
            b.HasIndex("EventId", "IncidentId", "IncidentType").IsUnique();
            b.ToTable("MatchEvents");
        });

        modelBuilder.Entity("LiveTotalsHelper.Infrastructure.Persistence.Entities.MatchStatEntity", b =>
        {
            b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("integer");
            NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));
            b.Property<int>("MatchId").HasColumnType("integer");
            b.Property<string>("EventId").IsRequired().HasMaxLength(100).HasColumnType("character varying(100)");
            b.Property<string>("Period").IsRequired().HasMaxLength(30).HasColumnType("character varying(30)");
            b.Property<double?>("HomeExpectedGoals").HasColumnType("double precision");
            b.Property<double?>("AwayExpectedGoals").HasColumnType("double precision");
            b.Property<double?>("HomeBallPossession").HasColumnType("double precision");
            b.Property<double?>("AwayBallPossession").HasColumnType("double precision");
            b.Property<double?>("HomeTotalShots").HasColumnType("double precision");
            b.Property<double?>("AwayTotalShots").HasColumnType("double precision");
            b.Property<double?>("HomeShotsOnTarget").HasColumnType("double precision");
            b.Property<double?>("AwayShotsOnTarget").HasColumnType("double precision");
            b.Property<double?>("HomeShotsOffTarget").HasColumnType("double precision");
            b.Property<double?>("AwayShotsOffTarget").HasColumnType("double precision");
            b.Property<double?>("HomeBlockedShots").HasColumnType("double precision");
            b.Property<double?>("AwayBlockedShots").HasColumnType("double precision");
            b.Property<double?>("HomeBigChances").HasColumnType("double precision");
            b.Property<double?>("AwayBigChances").HasColumnType("double precision");
            b.Property<double?>("HomeBigChancesMissed").HasColumnType("double precision");
            b.Property<double?>("AwayBigChancesMissed").HasColumnType("double precision");
            b.Property<double?>("HomeCornerKicks").HasColumnType("double precision");
            b.Property<double?>("AwayCornerKicks").HasColumnType("double precision");
            b.Property<double?>("HomeFouls").HasColumnType("double precision");
            b.Property<double?>("AwayFouls").HasColumnType("double precision");
            b.Property<double?>("HomeYellowCards").HasColumnType("double precision");
            b.Property<double?>("AwayYellowCards").HasColumnType("double precision");
            b.Property<double?>("HomeRedCards").HasColumnType("double precision");
            b.Property<double?>("AwayRedCards").HasColumnType("double precision");
            b.Property<double?>("HomeGoalkeeperSaves").HasColumnType("double precision");
            b.Property<double?>("AwayGoalkeeperSaves").HasColumnType("double precision");
            b.Property<double?>("HomeOffsides").HasColumnType("double precision");
            b.Property<double?>("AwayOffsides").HasColumnType("double precision");
            b.Property<double?>("HomeThrowIns").HasColumnType("double precision");
            b.Property<double?>("AwayThrowIns").HasColumnType("double precision");
            b.Property<double?>("HomeFreeKicks").HasColumnType("double precision");
            b.Property<double?>("AwayFreeKicks").HasColumnType("double precision");
            b.Property<double?>("HomePasses").HasColumnType("double precision");
            b.Property<double?>("AwayPasses").HasColumnType("double precision");
            b.Property<double?>("HomeAccuratePasses").HasColumnType("double precision");
            b.Property<double?>("AwayAccuratePasses").HasColumnType("double precision");
            b.Property<double?>("HomeLongBalls").HasColumnType("double precision");
            b.Property<double?>("AwayLongBalls").HasColumnType("double precision");
            b.Property<double?>("HomeCrosses").HasColumnType("double precision");
            b.Property<double?>("AwayCrosses").HasColumnType("double precision");
            b.Property<double?>("HomeTackles").HasColumnType("double precision");
            b.Property<double?>("AwayTackles").HasColumnType("double precision");
            b.Property<double?>("HomeClearances").HasColumnType("double precision");
            b.Property<double?>("AwayClearances").HasColumnType("double precision");
            b.Property<double?>("HomeTouchesInOppositionBox").HasColumnType("double precision");
            b.Property<double?>("AwayTouchesInOppositionBox").HasColumnType("double precision");
            b.Property<double?>("HomeFinalThirdEntries").HasColumnType("double precision");
            b.Property<double?>("AwayFinalThirdEntries").HasColumnType("double precision");
            b.Property<string>("StatisticsJsonPath").IsRequired().HasMaxLength(1000).HasColumnType("character varying(1000)");
            b.Property<DateTimeOffset>("ImportedAtUtc").HasColumnType("timestamp with time zone");
            b.HasKey("Id");
            b.HasIndex("MatchId", "Period").IsUnique();
            b.HasIndex("EventId", "Period");
            b.ToTable("MatchStats");
        });

        modelBuilder.Entity("LiveTotalsHelper.Infrastructure.Persistence.Entities.FlashscoreOddsEntity", b =>
        {
            b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("integer");
            NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));
            b.Property<int>("MatchId").HasColumnType("integer");
            b.Property<string>("EventId").IsRequired().HasMaxLength(100).HasColumnType("character varying(100)");
            b.Property<string>("Market").IsRequired().HasMaxLength(100).HasColumnType("character varying(100)");
            b.Property<string>("Bookmaker").IsRequired().HasMaxLength(200).HasColumnType("character varying(200)");
            b.Property<string>("Selection").IsRequired().HasMaxLength(100).HasColumnType("character varying(100)");
            b.Property<double?>("Line").HasColumnType("double precision");
            b.Property<double>("Odds").HasColumnType("double precision");
            b.Property<string>("SourceUrl").IsRequired().HasMaxLength(1000).HasColumnType("character varying(1000)");
            b.Property<string>("OddsJsonPath").IsRequired().HasMaxLength(1000).HasColumnType("character varying(1000)");
            b.Property<DateTime?>("DownloadedAtUtc").HasColumnType("timestamp with time zone");
            b.Property<DateTimeOffset>("ImportedAtUtc").HasColumnType("timestamp with time zone");
            b.HasKey("Id");
            b.HasIndex("MatchId", "Market", "Bookmaker", "Selection", "Line", "Odds").IsUnique();
            b.HasIndex("EventId", "Market");
            b.ToTable("FlashscoreOdds");
        });

        modelBuilder.Entity("LiveTotalsHelper.Infrastructure.Persistence.Entities.MatchEventEntity", b =>
        {
            b.HasOne("LiveTotalsHelper.Infrastructure.Persistence.Entities.MatchEntity", "Match")
                .WithMany("Events")
                .HasForeignKey("MatchId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            b.Navigation("Match");
        });

        modelBuilder.Entity("LiveTotalsHelper.Infrastructure.Persistence.Entities.MatchStatEntity", b =>
        {
            b.HasOne("LiveTotalsHelper.Infrastructure.Persistence.Entities.MatchEntity", "Match")
                .WithMany("Stats")
                .HasForeignKey("MatchId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            b.Navigation("Match");
        });

        modelBuilder.Entity("LiveTotalsHelper.Infrastructure.Persistence.Entities.FlashscoreOddsEntity", b =>
        {
            b.HasOne("LiveTotalsHelper.Infrastructure.Persistence.Entities.MatchEntity", "Match")
                .WithMany("FlashscoreOdds")
                .HasForeignKey("MatchId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            b.Navigation("Match");
        });

        modelBuilder.Entity("LiveTotalsHelper.Infrastructure.Persistence.Entities.MatchEntity", b =>
        {
            b.Navigation("Events");
            b.Navigation("Stats");
            b.Navigation("FlashscoreOdds");
        });
#pragma warning restore 612, 618
    }
}
