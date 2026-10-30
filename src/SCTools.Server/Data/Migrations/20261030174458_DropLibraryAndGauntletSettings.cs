using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SCTools.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class DropLibraryAndGauntletSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GauntletSettings");

            migrationBuilder.DropTable(
                name: "Library");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GauntletSettings",
                columns: table => new
                {
                    Player = table.Column<string>(type: "TEXT", nullable: false),
                    RewardAuec = table.Column<string>(type: "TEXT", nullable: false),
                    RewardScrip = table.Column<string>(type: "TEXT", nullable: false),
                    ScripValue = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GauntletSettings", x => x.Player);
                });

            migrationBuilder.CreateTable(
                name: "Library",
                columns: table => new
                {
                    ContractId = table.Column<string>(type: "TEXT", nullable: false),
                    Auec = table.Column<long>(type: "INTEGER", nullable: true),
                    CouncilScrip = table.Column<long>(type: "INTEGER", nullable: true),
                    EditedBy = table.Column<string>(type: "TEXT", nullable: true),
                    EditedUtc = table.Column<string>(type: "TEXT", nullable: true),
                    Giver = table.Column<string>(type: "TEXT", nullable: true),
                    MgScrip = table.Column<long>(type: "INTEGER", nullable: true),
                    Name = table.Column<string>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    WikiCheckedUtc = table.Column<string>(type: "TEXT", nullable: true),
                    WikiCurrency = table.Column<string>(type: "TEXT", nullable: true),
                    WikiFound = table.Column<bool>(type: "INTEGER", nullable: false),
                    WikiGiver = table.Column<string>(type: "TEXT", nullable: true),
                    WikiRewardMax = table.Column<long>(type: "INTEGER", nullable: true),
                    WikiRewardMin = table.Column<long>(type: "INTEGER", nullable: true),
                    WikiTitle = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Library", x => x.ContractId);
                });
        }
    }
}
