using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SCTools.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class MissionKinds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "Missions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MissionKinds",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: true),
                    Giver = table.Column<string>(type: "TEXT", nullable: true),
                    Auec = table.Column<long>(type: "INTEGER", nullable: true),
                    MgScrip = table.Column<long>(type: "INTEGER", nullable: true),
                    CouncilScrip = table.Column<long>(type: "INTEGER", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    EditedBy = table.Column<string>(type: "TEXT", nullable: true),
                    EditedUtc = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MissionKinds", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Missions_Kind",
                table: "Missions",
                column: "Kind");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MissionKinds");

            migrationBuilder.DropIndex(
                name: "IX_Missions_Kind",
                table: "Missions");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Missions");
        }
    }
}
