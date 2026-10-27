using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HaulingTracker.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AutoParty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AutoPartySince",
                table: "Settings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "PartyChanges",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoPartySince",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "PartyChanges");
        }
    }
}
