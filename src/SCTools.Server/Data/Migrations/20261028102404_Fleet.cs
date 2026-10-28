using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SCTools.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class Fleet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "DriveSpeedBonus",
                table: "Members",
                type: "REAL",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ActiveShips",
                columns: table => new
                {
                    Player = table.Column<string>(type: "TEXT", nullable: false),
                    FleetShipId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActiveShips", x => x.Player);
                });

            migrationBuilder.CreateTable(
                name: "FleetShips",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Player = table.Column<string>(type: "TEXT", nullable: false),
                    Ship = table.Column<string>(type: "TEXT", nullable: false),
                    Drive = table.Column<string>(type: "TEXT", nullable: true),
                    Quality = table.Column<int>(type: "INTEGER", nullable: true),
                    AddedUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FleetShips", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FleetShips_Player",
                table: "FleetShips",
                column: "Player");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActiveShips");

            migrationBuilder.DropTable(
                name: "FleetShips");

            migrationBuilder.DropColumn(
                name: "DriveSpeedBonus",
                table: "Members");
        }
    }
}
