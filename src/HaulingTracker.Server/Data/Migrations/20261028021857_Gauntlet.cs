using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HaulingTracker.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class Gauntlet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GauntletRuns",
                columns: table => new
                {
                    Player = table.Column<string>(type: "TEXT", nullable: false),
                    MissionId = table.Column<string>(type: "TEXT", nullable: false),
                    Scenario = table.Column<int>(type: "INTEGER", nullable: false),
                    AcceptedUtc = table.Column<string>(type: "TEXT", nullable: true),
                    EndedUtc = table.Column<string>(type: "TEXT", nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GauntletRuns", x => new { x.Player, x.MissionId });
                });

            migrationBuilder.CreateTable(
                name: "GauntletSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Player = table.Column<string>(type: "TEXT", nullable: false),
                    StartedUtc = table.Column<string>(type: "TEXT", nullable: false),
                    EndedUtc = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GauntletSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GauntletSettings",
                columns: table => new
                {
                    Player = table.Column<string>(type: "TEXT", nullable: false),
                    ScripValue = table.Column<long>(type: "INTEGER", nullable: false),
                    RewardAuec = table.Column<string>(type: "TEXT", nullable: false),
                    RewardScrip = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GauntletSettings", x => x.Player);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GauntletRuns");

            migrationBuilder.DropTable(
                name: "GauntletSessions");

            migrationBuilder.DropTable(
                name: "GauntletSettings");
        }
    }
}
