using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SCTools.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class WikiRewards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RewardCurrency",
                table: "WikiMissions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "RewardMax",
                table: "WikiMissions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "RewardMin",
                table: "WikiMissions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WikiUuid",
                table: "MissionKinds",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RewardCurrency",
                table: "WikiMissions");

            migrationBuilder.DropColumn(
                name: "RewardMax",
                table: "WikiMissions");

            migrationBuilder.DropColumn(
                name: "RewardMin",
                table: "WikiMissions");

            migrationBuilder.DropColumn(
                name: "WikiUuid",
                table: "MissionKinds");
        }
    }
}
