using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LiveTotalsHelper.Infrastructure.Persistence.Migrations;

[Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(LiveTotalsDbContext))]
[Migration("20260506000000_FixMatchTeamStatsUniqueIndex")]
public partial class FixMatchTeamStatsUniqueIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_MatchTeamStats_MatchId_Period_Key",
            table: "MatchTeamStats");

        migrationBuilder.CreateIndex(
            name: "IX_MatchTeamStats_MatchId_Period_GroupName_Key",
            table: "MatchTeamStats",
            columns: new[] { "MatchId", "Period", "GroupName", "Key" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_MatchTeamStats_MatchId_Period_GroupName_Key",
            table: "MatchTeamStats");

        migrationBuilder.CreateIndex(
            name: "IX_MatchTeamStats_MatchId_Period_Key",
            table: "MatchTeamStats",
            columns: new[] { "MatchId", "Period", "Key" },
            unique: true);
    }
}
