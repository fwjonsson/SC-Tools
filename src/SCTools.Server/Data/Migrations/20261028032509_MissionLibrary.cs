using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SCTools.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class MissionLibrary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Library",
                columns: table => new
                {
                    ContractId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: true),
                    Giver = table.Column<string>(type: "TEXT", nullable: true),
                    Auec = table.Column<long>(type: "INTEGER", nullable: true),
                    MgScrip = table.Column<long>(type: "INTEGER", nullable: true),
                    CouncilScrip = table.Column<long>(type: "INTEGER", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    EditedBy = table.Column<string>(type: "TEXT", nullable: true),
                    EditedUtc = table.Column<string>(type: "TEXT", nullable: true),
                    WikiCheckedUtc = table.Column<string>(type: "TEXT", nullable: true),
                    WikiFound = table.Column<bool>(type: "INTEGER", nullable: false),
                    WikiTitle = table.Column<string>(type: "TEXT", nullable: true),
                    WikiGiver = table.Column<string>(type: "TEXT", nullable: true),
                    WikiRewardMin = table.Column<long>(type: "INTEGER", nullable: true),
                    WikiRewardMax = table.Column<long>(type: "INTEGER", nullable: true),
                    WikiCurrency = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Library", x => x.ContractId);
                });

            migrationBuilder.CreateTable(
                name: "Missions",
                columns: table => new
                {
                    Player = table.Column<string>(type: "TEXT", nullable: false),
                    MissionId = table.Column<string>(type: "TEXT", nullable: false),
                    ContractId = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: true),
                    Shared = table.Column<bool>(type: "INTEGER", nullable: false),
                    StartedUtc = table.Column<string>(type: "TEXT", nullable: true),
                    EndedUtc = table.Column<string>(type: "TEXT", nullable: true),
                    Outcome = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Missions", x => new { x.Player, x.MissionId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_Missions_ContractId",
                table: "Missions",
                column: "ContractId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Library");

            migrationBuilder.DropTable(
                name: "Missions");
        }
    }
}
