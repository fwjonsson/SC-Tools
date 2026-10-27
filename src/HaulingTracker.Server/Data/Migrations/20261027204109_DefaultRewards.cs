using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HaulingTracker.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class DefaultRewards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DefaultRewards",
                table: "Settings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultRewards",
                table: "Settings");
        }
    }
}
