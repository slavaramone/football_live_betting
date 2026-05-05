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
            b.Property<int>("SofaScoreUniqueTournamentId").HasColumnType("integer");
            b.Property<long>("SofaScoreEventId").HasColumnType("bigint");
            b.Property<int>("SofaScoreSeasonId").HasColumnType("integer");
            b.Property<string>("LeagueName").IsRequired().HasMaxLength(200).HasColumnType("character varying(200)");
            b.Property<string>("LeagueSlug").IsRequired().HasMaxLength(200).HasColumnType("character varying(200)");
            b.Property<string>("CountryName").IsRequired().HasMaxLength(100).HasColumnType("character varying(100)");
            b.Property<string>("CountryCode").IsRequired().HasMaxLength(10).HasColumnType("character varying(10)");
            b.Property<string>("SeasonName").IsRequired().HasMaxLength(200).HasColumnType("character varying(200)");
            b.Property<string>("SeasonYear").IsRequired().HasMaxLength(20).HasColumnType("character varying(20)");
            b.Property<int>("RoundNumber").HasColumnType("integer");
            b.Property<long>("HomeTeamSofaScoreId").HasColumnType("bigint");
            b.Property<string>("HomeTeamName").IsRequired().HasMaxLength(200).HasColumnType("character varying(200)");
            b.Property<string>("HomeTeamSlug").IsRequired().HasMaxLength(200).HasColumnType("character varying(200)");
            b.Property<string>("HomeTeamShortName").IsRequired().HasMaxLength(200).HasColumnType("character varying(200)");
            b.Property<long>("AwayTeamSofaScoreId").HasColumnType("bigint");
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
            b.HasIndex("SofaScoreEventId").IsUnique();
            b.HasIndex("SofaScoreUniqueTournamentId", "SofaScoreSeasonId", "RoundNumber");
            b.HasIndex("HomeTeamSofaScoreId", "AwayTeamSofaScoreId", "StartTimeUtc");
            b.ToTable("Matches");
        });

        modelBuilder.Entity("LiveTotalsHelper.Infrastructure.Persistence.Entities.MatchEventEntity", b =>
        {
            b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("integer");
            NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));
            b.Property<int>("MatchId").HasColumnType("integer");
            b.Property<long>("SofaScoreEventId").HasColumnType("bigint");
            b.Property<long?>("SofaScoreIncidentId").HasColumnType("bigint");
            b.Property<string>("IncidentType").IsRequired().HasMaxLength(50).HasColumnType("character varying(50)");
            b.Property<string>("IncidentClass").IsRequired().HasMaxLength(50).HasColumnType("character varying(50)");
            b.Property<int>("Minute").HasColumnType("integer");
            b.Property<int?>("AddedTime").HasColumnType("integer");
            b.Property<int?>("TimeSeconds").HasColumnType("integer");
            b.Property<bool>("IsHome").HasColumnType("boolean");
            b.Property<int?>("HomeScore").HasColumnType("integer");
            b.Property<int?>("AwayScore").HasColumnType("integer");
            b.Property<string>("PlayerName").IsRequired().HasMaxLength(200).HasColumnType("character varying(200)");
            b.Property<long?>("SofaScorePlayerId").HasColumnType("bigint");
            b.Property<string>("AssistPlayerName").IsRequired().HasMaxLength(200).HasColumnType("character varying(200)");
            b.Property<long?>("SofaScoreAssistPlayerId").HasColumnType("bigint");
            b.Property<string>("Reason").IsRequired().HasMaxLength(200).HasColumnType("character varying(200)");
            b.HasKey("Id");
            b.HasIndex("MatchId", "Minute");
            b.HasIndex("MatchId", "IncidentType");
            b.HasIndex("SofaScoreEventId", "SofaScoreIncidentId", "IncidentType").IsUnique();
            b.ToTable("MatchEvents");
        });

        modelBuilder.Entity("LiveTotalsHelper.Infrastructure.Persistence.Entities.MatchTeamStatEntity", b =>
        {
            b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("integer");
            NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));
            b.Property<int>("MatchId").HasColumnType("integer");
            b.Property<long>("SofaScoreEventId").HasColumnType("bigint");
            b.Property<string>("Period").IsRequired().HasMaxLength(30).HasColumnType("character varying(30)");
            b.Property<string>("GroupName").IsRequired().HasMaxLength(100).HasColumnType("character varying(100)");
            b.Property<string>("Key").IsRequired().HasMaxLength(100).HasColumnType("character varying(100)");
            b.Property<string>("Name").IsRequired().HasMaxLength(200).HasColumnType("character varying(200)");
            b.Property<string>("ValueType").IsRequired().HasMaxLength(50).HasColumnType("character varying(50)");
            b.Property<string>("StatisticsType").IsRequired().HasMaxLength(50).HasColumnType("character varying(50)");
            b.Property<string>("HomeRaw").IsRequired().HasMaxLength(100).HasColumnType("character varying(100)");
            b.Property<string>("AwayRaw").IsRequired().HasMaxLength(100).HasColumnType("character varying(100)");
            b.Property<double?>("HomeValue").HasColumnType("double precision");
            b.Property<double?>("AwayValue").HasColumnType("double precision");
            b.Property<double?>("HomeTotal").HasColumnType("double precision");
            b.Property<double?>("AwayTotal").HasColumnType("double precision");
            b.Property<string>("StatisticsJsonPath").IsRequired().HasMaxLength(1000).HasColumnType("character varying(1000)");
            b.HasKey("Id");
            b.HasIndex("MatchId", "Period", "Key").IsUnique();
            b.HasIndex("SofaScoreEventId", "Period");
            b.ToTable("MatchTeamStats");
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

        modelBuilder.Entity("LiveTotalsHelper.Infrastructure.Persistence.Entities.MatchTeamStatEntity", b =>
        {
            b.HasOne("LiveTotalsHelper.Infrastructure.Persistence.Entities.MatchEntity", "Match")
                .WithMany("TeamStats")
                .HasForeignKey("MatchId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            b.Navigation("Match");
        });

        modelBuilder.Entity("LiveTotalsHelper.Infrastructure.Persistence.Entities.MatchEntity", b =>
        {
            b.Navigation("Events");
            b.Navigation("TeamStats");
        });
#pragma warning restore 612, 618
    }
}
