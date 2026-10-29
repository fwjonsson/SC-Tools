using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SCTools.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class MissionDefinition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Definition",
                table: "Missions",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Definition",
                table: "Missions");
        }
    }
}
