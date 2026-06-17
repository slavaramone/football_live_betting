using System;
using LiveTotalsHelper.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LiveTotalsHelper.Infrastructure.Persistence.Migrations;

[Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(LiveTotalsDbContext))]
[Migration("20260617000000_InitialFlashscoreSchema")]
public partial class InitialFlashscoreSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Matches",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false).Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                EventId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                FlashscoreId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                TournamentId = table.Column<int>(type: "integer", nullable: false),
                LeagueName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                LeagueSlug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                CountryName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                CountryCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                SeasonId = table.Column<int>(type: "integer", nullable: false),
                SeasonName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                SeasonYear = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                RoundNumber = table.Column<int>(type: "integer", nullable: false),
                HomeTeamId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                HomeTeamName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                HomeTeamSlug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                HomeTeamShortName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                AwayTeamId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                AwayTeamName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                AwayTeamSlug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                AwayTeamShortName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Slug = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                StartTimeUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                StatusType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                StatusDescription = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                HomeScoreCurrent = table.Column<int>(type: "integer", nullable: true),
                AwayScoreCurrent = table.Column<int>(type: "integer", nullable: true),
                HomeScorePeriod1 = table.Column<int>(type: "integer", nullable: true),
                AwayScorePeriod1 = table.Column<int>(type: "integer", nullable: true),
                HomeScorePeriod2 = table.Column<int>(type: "integer", nullable: true),
                AwayScorePeriod2 = table.Column<int>(type: "integer", nullable: true),
                CalendarJsonPath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                EventMetaJsonPath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                CalendarUpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Matches", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "FlashscoreOdds",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false).Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                MatchId = table.Column<int>(type: "integer", nullable: false),
                EventId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Market = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Bookmaker = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Selection = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Line = table.Column<double>(type: "double precision", nullable: true),
                Odds = table.Column<double>(type: "double precision", nullable: false),
                SourceUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                OddsJsonPath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                DownloadedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ImportedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_FlashscoreOdds", x => x.Id);
                table.ForeignKey("FK_FlashscoreOdds_Matches_MatchId", x => x.MatchId, "Matches", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "MatchEvents",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false).Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                MatchId = table.Column<int>(type: "integer", nullable: false),
                EventId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                IncidentId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                IncidentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                IncidentClass = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Minute = table.Column<int>(type: "integer", nullable: false),
                AddedTime = table.Column<int>(type: "integer", nullable: true),
                TimeSeconds = table.Column<int>(type: "integer", nullable: true),
                IsHome = table.Column<bool>(type: "boolean", nullable: false),
                HomeScore = table.Column<int>(type: "integer", nullable: true),
                AwayScore = table.Column<int>(type: "integer", nullable: true),
                PlayerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                PlayerId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                AssistPlayerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                AssistPlayerId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MatchEvents", x => x.Id);
                table.ForeignKey("FK_MatchEvents_Matches_MatchId", x => x.MatchId, "Matches", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "MatchStats",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false).Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                MatchId = table.Column<int>(type: "integer", nullable: false),
                EventId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Period = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                HomeExpectedGoals = table.Column<double>(type: "double precision", nullable: true),
                AwayExpectedGoals = table.Column<double>(type: "double precision", nullable: true),
                HomeBallPossession = table.Column<double>(type: "double precision", nullable: true),
                AwayBallPossession = table.Column<double>(type: "double precision", nullable: true),
                HomeTotalShots = table.Column<double>(type: "double precision", nullable: true),
                AwayTotalShots = table.Column<double>(type: "double precision", nullable: true),
                HomeShotsOnTarget = table.Column<double>(type: "double precision", nullable: true),
                AwayShotsOnTarget = table.Column<double>(type: "double precision", nullable: true),
                HomeShotsOffTarget = table.Column<double>(type: "double precision", nullable: true),
                AwayShotsOffTarget = table.Column<double>(type: "double precision", nullable: true),
                HomeBlockedShots = table.Column<double>(type: "double precision", nullable: true),
                AwayBlockedShots = table.Column<double>(type: "double precision", nullable: true),
                HomeBigChances = table.Column<double>(type: "double precision", nullable: true),
                AwayBigChances = table.Column<double>(type: "double precision", nullable: true),
                HomeBigChancesMissed = table.Column<double>(type: "double precision", nullable: true),
                AwayBigChancesMissed = table.Column<double>(type: "double precision", nullable: true),
                HomeCornerKicks = table.Column<double>(type: "double precision", nullable: true),
                AwayCornerKicks = table.Column<double>(type: "double precision", nullable: true),
                HomeFouls = table.Column<double>(type: "double precision", nullable: true),
                AwayFouls = table.Column<double>(type: "double precision", nullable: true),
                HomeYellowCards = table.Column<double>(type: "double precision", nullable: true),
                AwayYellowCards = table.Column<double>(type: "double precision", nullable: true),
                HomeRedCards = table.Column<double>(type: "double precision", nullable: true),
                AwayRedCards = table.Column<double>(type: "double precision", nullable: true),
                HomeGoalkeeperSaves = table.Column<double>(type: "double precision", nullable: true),
                AwayGoalkeeperSaves = table.Column<double>(type: "double precision", nullable: true),
                HomeOffsides = table.Column<double>(type: "double precision", nullable: true),
                AwayOffsides = table.Column<double>(type: "double precision", nullable: true),
                HomeThrowIns = table.Column<double>(type: "double precision", nullable: true),
                AwayThrowIns = table.Column<double>(type: "double precision", nullable: true),
                HomeFreeKicks = table.Column<double>(type: "double precision", nullable: true),
                AwayFreeKicks = table.Column<double>(type: "double precision", nullable: true),
                HomePasses = table.Column<double>(type: "double precision", nullable: true),
                AwayPasses = table.Column<double>(type: "double precision", nullable: true),
                HomeAccuratePasses = table.Column<double>(type: "double precision", nullable: true),
                AwayAccuratePasses = table.Column<double>(type: "double precision", nullable: true),
                HomeLongBalls = table.Column<double>(type: "double precision", nullable: true),
                AwayLongBalls = table.Column<double>(type: "double precision", nullable: true),
                HomeCrosses = table.Column<double>(type: "double precision", nullable: true),
                AwayCrosses = table.Column<double>(type: "double precision", nullable: true),
                HomeTackles = table.Column<double>(type: "double precision", nullable: true),
                AwayTackles = table.Column<double>(type: "double precision", nullable: true),
                HomeClearances = table.Column<double>(type: "double precision", nullable: true),
                AwayClearances = table.Column<double>(type: "double precision", nullable: true),
                HomeTouchesInOppositionBox = table.Column<double>(type: "double precision", nullable: true),
                AwayTouchesInOppositionBox = table.Column<double>(type: "double precision", nullable: true),
                HomeFinalThirdEntries = table.Column<double>(type: "double precision", nullable: true),
                AwayFinalThirdEntries = table.Column<double>(type: "double precision", nullable: true),
                StatisticsJsonPath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                ImportedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MatchStats", x => x.Id);
                table.ForeignKey("FK_MatchStats_Matches_MatchId", x => x.MatchId, "Matches", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("IX_Matches_EventId", "Matches", "EventId", unique: true);
        migrationBuilder.CreateIndex("IX_Matches_FlashscoreId", "Matches", "FlashscoreId");
        migrationBuilder.CreateIndex("IX_Matches_TournamentId_SeasonId_RoundNumber", "Matches", new[] { "TournamentId", "SeasonId", "RoundNumber" });
        migrationBuilder.CreateIndex("IX_Matches_HomeTeamId_AwayTeamId_StartTimeUtc", "Matches", new[] { "HomeTeamId", "AwayTeamId", "StartTimeUtc" });
        migrationBuilder.CreateIndex("IX_FlashscoreOdds_MatchId_Market_Bookmaker_Selection_Line_Odds", "FlashscoreOdds", new[] { "MatchId", "Market", "Bookmaker", "Selection", "Line", "Odds" }, unique: true);
        migrationBuilder.CreateIndex("IX_FlashscoreOdds_EventId_Market", "FlashscoreOdds", new[] { "EventId", "Market" });
        migrationBuilder.CreateIndex("IX_MatchEvents_MatchId_Minute", "MatchEvents", new[] { "MatchId", "Minute" });
        migrationBuilder.CreateIndex("IX_MatchEvents_MatchId_IncidentType", "MatchEvents", new[] { "MatchId", "IncidentType" });
        migrationBuilder.CreateIndex("IX_MatchEvents_EventId_IncidentId_IncidentType", "MatchEvents", new[] { "EventId", "IncidentId", "IncidentType" }, unique: true);
        migrationBuilder.CreateIndex("IX_MatchStats_MatchId_Period", "MatchStats", new[] { "MatchId", "Period" }, unique: true);
        migrationBuilder.CreateIndex("IX_MatchStats_EventId_Period", "MatchStats", new[] { "EventId", "Period" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "FlashscoreOdds");
        migrationBuilder.DropTable(name: "MatchEvents");
        migrationBuilder.DropTable(name: "MatchStats");
        migrationBuilder.DropTable(name: "Matches");
    }
}
