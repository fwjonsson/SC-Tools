using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SCTools.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class LogFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LogFiles",
                columns: table => new
                {
                    Player = table.Column<string>(type: "TEXT", nullable: false),
                    File = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    StartUtc = table.Column<string>(type: "TEXT", nullable: false),
                    EndUtc = table.Column<string>(type: "TEXT", nullable: true),
                    Build = table.Column<string>(type: "TEXT", nullable: true),
                    Lines = table.Column<int>(type: "INTEGER", nullable: false),
                    ImportedUtc = table.Column<string>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LogFiles", x => new { x.Player, x.File });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LogFiles");
        }
    }
}
