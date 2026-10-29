using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SCTools.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class WikiMissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WikiMissions",
                columns: table => new
                {
                    Uuid = table.Column<string>(type: "TEXT", nullable: false),
                    Found = table.Column<bool>(type: "INTEGER", nullable: false),
                    AskedUtc = table.Column<string>(type: "TEXT", nullable: false),
                    DebugName = table.Column<string>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", nullable: true),
                    Orders = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WikiMissions", x => x.Uuid);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WikiMissions");
        }
    }
}
