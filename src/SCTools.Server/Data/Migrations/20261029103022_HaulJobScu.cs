using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SCTools.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class HaulJobScu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ScuSuggested",
                table: "MissionKinds",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScuSuggestedBy",
                table: "MissionKinds",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScuSuggestedUtc",
                table: "MissionKinds",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HaulScu",
                columns: table => new
                {
                    MissionId = table.Column<string>(type: "TEXT", nullable: false),
                    Scu = table.Column<long>(type: "INTEGER", nullable: false),
                    By = table.Column<string>(type: "TEXT", nullable: false),
                    Utc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HaulScu", x => x.MissionId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HaulScu");

            migrationBuilder.DropColumn(
                name: "ScuSuggested",
                table: "MissionKinds");

            migrationBuilder.DropColumn(
                name: "ScuSuggestedBy",
                table: "MissionKinds");

            migrationBuilder.DropColumn(
                name: "ScuSuggestedUtc",
                table: "MissionKinds");
        }
    }
}
