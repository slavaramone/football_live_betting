using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LiveTotalsHelper.Infrastructure.Persistence.Migrations;

[Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(LiveTotalsDbContext))]
[Migration("20260505000000_InitialCreate")]
public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Matches",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false).Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                SofaScoreEventId = table.Column<long>(type: "bigint", nullable: false),
                SofaScoreUniqueTournamentId = table.Column<int>(type: "integer", nullable: false),
                LeagueName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                LeagueSlug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                CountryName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                CountryCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                SofaScoreSeasonId = table.Column<int>(type: "integer", nullable: false),
                SeasonName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                SeasonYear = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                RoundNumber = table.Column<int>(type: "integer", nullable: false),
                HomeTeamSofaScoreId = table.Column<long>(type: "bigint", nullable: false),
                HomeTeamName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                HomeTeamSlug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                HomeTeamShortName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                AwayTeamSofaScoreId = table.Column<long>(type: "bigint", nullable: false),
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
            constraints: table => table.PrimaryKey("PK_Matches", x => x.Id));

        migrationBuilder.CreateTable(
            name: "MatchEvents",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false).Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                MatchId = table.Column<int>(type: "integer", nullable: false),
                SofaScoreEventId = table.Column<long>(type: "bigint", nullable: false),
                SofaScoreIncidentId = table.Column<long>(type: "bigint", nullable: true),
                IncidentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                IncidentClass = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Minute = table.Column<int>(type: "integer", nullable: false),
                AddedTime = table.Column<int>(type: "integer", nullable: true),
                TimeSeconds = table.Column<int>(type: "integer", nullable: true),
                IsHome = table.Column<bool>(type: "boolean", nullable: false),
                HomeScore = table.Column<int>(type: "integer", nullable: true),
                AwayScore = table.Column<int>(type: "integer", nullable: true),
                PlayerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                SofaScorePlayerId = table.Column<long>(type: "bigint", nullable: true),
                AssistPlayerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                SofaScoreAssistPlayerId = table.Column<long>(type: "bigint", nullable: true),
                Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MatchEvents", x => x.Id);
                table.ForeignKey("FK_MatchEvents_Matches_MatchId", x => x.MatchId, "Matches", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "MatchTeamStats",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false).Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                MatchId = table.Column<int>(type: "integer", nullable: false),
                SofaScoreEventId = table.Column<long>(type: "bigint", nullable: false),
                Period = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                GroupName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                ValueType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                StatisticsType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                HomeRaw = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                AwayRaw = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                HomeValue = table.Column<double>(type: "double precision", nullable: true),
                AwayValue = table.Column<double>(type: "double precision", nullable: true),
                HomeTotal = table.Column<double>(type: "double precision", nullable: true),
                AwayTotal = table.Column<double>(type: "double precision", nullable: true),
                StatisticsJsonPath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MatchTeamStats", x => x.Id);
                table.ForeignKey("FK_MatchTeamStats_Matches_MatchId", x => x.MatchId, "Matches", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("IX_Matches_SofaScoreEventId", "Matches", "SofaScoreEventId", unique: true);
        migrationBuilder.CreateIndex("IX_Matches_Tournament_Season_Round", "Matches", new[] { "SofaScoreUniqueTournamentId", "SofaScoreSeasonId", "RoundNumber" });
        migrationBuilder.CreateIndex("IX_Matches_Teams_StartTime", "Matches", new[] { "HomeTeamSofaScoreId", "AwayTeamSofaScoreId", "StartTimeUtc" });
        migrationBuilder.CreateIndex("IX_MatchEvents_MatchId_Minute", "MatchEvents", new[] { "MatchId", "Minute" });
        migrationBuilder.CreateIndex("IX_MatchEvents_MatchId_IncidentType", "MatchEvents", new[] { "MatchId", "IncidentType" });
        migrationBuilder.CreateIndex("IX_MatchEvents_Event_Incident_Type", "MatchEvents", new[] { "SofaScoreEventId", "SofaScoreIncidentId", "IncidentType" }, unique: true);
        migrationBuilder.CreateIndex("IX_MatchTeamStats_MatchId_Period_Key", "MatchTeamStats", new[] { "MatchId", "Period", "Key" }, unique: true);
        migrationBuilder.CreateIndex("IX_MatchTeamStats_Event_Period", "MatchTeamStats", new[] { "SofaScoreEventId", "Period" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("MatchEvents");
        migrationBuilder.DropTable("MatchTeamStats");
        migrationBuilder.DropTable("Matches");
    }
}
