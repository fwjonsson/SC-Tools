using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SCTools.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class KindCategoryXpLegality : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "MissionKinds",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Legality",
                table: "MissionKinds",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Xp",
                table: "MissionKinds",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Category",
                table: "MissionKinds");

            migrationBuilder.DropColumn(
                name: "Legality",
                table: "MissionKinds");

            migrationBuilder.DropColumn(
                name: "Xp",
                table: "MissionKinds");
        }
    }
}
