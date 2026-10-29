using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SCTools.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class WikiMissionInfo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "WikiMissions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Faction",
                table: "WikiMissions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Legality",
                table: "WikiMissions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ListOnly",
                table: "WikiMissions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "Xp",
                table: "WikiMissions",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Category",
                table: "WikiMissions");

            migrationBuilder.DropColumn(
                name: "Faction",
                table: "WikiMissions");

            migrationBuilder.DropColumn(
                name: "Legality",
                table: "WikiMissions");

            migrationBuilder.DropColumn(
                name: "ListOnly",
                table: "WikiMissions");

            migrationBuilder.DropColumn(
                name: "Xp",
                table: "WikiMissions");
        }
    }
}
