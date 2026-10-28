using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SCTools.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class MapPlaces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MapAliases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Text = table.Column<string>(type: "TEXT", nullable: false),
                    PlaceKey = table.Column<string>(type: "TEXT", nullable: true),
                    AddedUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MapAliases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MapPlaces",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    System = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Short = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Parent = table.Column<string>(type: "TEXT", nullable: true),
                    X = table.Column<double>(type: "REAL", nullable: false),
                    Y = table.Column<double>(type: "REAL", nullable: false),
                    Added = table.Column<bool>(type: "INTEGER", nullable: false),
                    ChangedUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MapPlaces", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MapAliases_Text",
                table: "MapAliases",
                column: "Text",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MapAliases");

            migrationBuilder.DropTable(
                name: "MapPlaces");
        }
    }
}
