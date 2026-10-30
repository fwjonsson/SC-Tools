using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SCTools.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class DropDeadTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GauntletSessions");

            migrationBuilder.DropTable(
                name: "HaulScu");

            migrationBuilder.DropIndex(
                name: "IX_Missions_ContractId",
                table: "Missions");

            migrationBuilder.DropIndex(
                name: "IX_Missions_Kind",
                table: "Missions");

            migrationBuilder.DropIndex(
                name: "IX_FleetShips_Player",
                table: "FleetShips");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GauntletSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EndedUtc = table.Column<string>(type: "TEXT", nullable: true),
                    Player = table.Column<string>(type: "TEXT", nullable: false),
                    StartedUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GauntletSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HaulScu",
                columns: table => new
                {
                    MissionId = table.Column<string>(type: "TEXT", nullable: false),
                    By = table.Column<string>(type: "TEXT", nullable: false),
                    Scu = table.Column<long>(type: "INTEGER", nullable: false),
                    Utc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HaulScu", x => x.MissionId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Missions_ContractId",
                table: "Missions",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_Missions_Kind",
                table: "Missions",
                column: "Kind");

            migrationBuilder.CreateIndex(
                name: "IX_FleetShips_Player",
                table: "FleetShips",
                column: "Player");
        }
    }
}
