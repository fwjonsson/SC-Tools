using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HaulingTracker.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DroppedMissions",
                columns: table => new
                {
                    MissionId = table.Column<string>(type: "TEXT", nullable: false),
                    Time = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DroppedMissions", x => x.MissionId);
                });

            migrationBuilder.CreateTable(
                name: "LocationNames",
                columns: table => new
                {
                    LocationId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocationNames", x => x.LocationId);
                });

            migrationBuilder.CreateTable(
                name: "ManualSessions",
                columns: table => new
                {
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    StartUtc = table.Column<string>(type: "TEXT", nullable: false),
                    StopUtc = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManualSessions", x => x.Position);
                });

            migrationBuilder.CreateTable(
                name: "Members",
                columns: table => new
                {
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    PartyPosition = table.Column<int>(type: "INTEGER", nullable: true),
                    Inactive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Ship = table.Column<string>(type: "TEXT", nullable: true),
                    QuantumDrive = table.Column<string>(type: "TEXT", nullable: true),
                    Color = table.Column<int>(type: "INTEGER", nullable: true),
                    ShipCapacity = table.Column<int>(type: "INTEGER", nullable: true),
                    LocationId = table.Column<string>(type: "TEXT", nullable: true),
                    LocationSince = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Members", x => x.Name);
                });

            migrationBuilder.CreateTable(
                name: "MissionHolders",
                columns: table => new
                {
                    MissionId = table.Column<string>(type: "TEXT", nullable: false),
                    Member = table.Column<string>(type: "TEXT", nullable: false),
                    Has = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MissionHolders", x => new { x.MissionId, x.Member });
                });

            migrationBuilder.CreateTable(
                name: "PairAdjust",
                columns: table => new
                {
                    Pair = table.Column<string>(type: "TEXT", nullable: false),
                    Amount = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PairAdjust", x => x.Pair);
                });

            migrationBuilder.CreateTable(
                name: "PartyChanges",
                columns: table => new
                {
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    TimeUtc = table.Column<string>(type: "TEXT", nullable: false),
                    Active = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartyChanges", x => x.Position);
                });

            migrationBuilder.CreateTable(
                name: "PayoutOverrides",
                columns: table => new
                {
                    MissionId = table.Column<string>(type: "TEXT", nullable: false),
                    Payout = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayoutOverrides", x => x.MissionId);
                });

            migrationBuilder.CreateTable(
                name: "Payouts",
                columns: table => new
                {
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    MissionId = table.Column<string>(type: "TEXT", nullable: false),
                    Time = table.Column<string>(type: "TEXT", nullable: false),
                    Contract = table.Column<string>(type: "TEXT", nullable: false),
                    Payout = table.Column<string>(type: "TEXT", nullable: false),
                    Members = table.Column<string>(type: "TEXT", nullable: false),
                    Each = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payouts", x => x.Position);
                });

            migrationBuilder.CreateTable(
                name: "Sessions",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Start = table.Column<string>(type: "TEXT", nullable: true),
                    End = table.Column<string>(type: "TEXT", nullable: true),
                    StartUtc = table.Column<string>(type: "TEXT", nullable: true),
                    EndUtc = table.Column<string>(type: "TEXT", nullable: true),
                    MissionCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Revenue = table.Column<string>(type: "TEXT", nullable: false),
                    Reward2 = table.Column<string>(type: "TEXT", nullable: false),
                    Reward3 = table.Column<string>(type: "TEXT", nullable: false),
                    Reward4 = table.Column<string>(type: "TEXT", nullable: false),
                    WasteReward2 = table.Column<string>(type: "TEXT", nullable: false),
                    WasteReward3 = table.Column<string>(type: "TEXT", nullable: false),
                    WasteReward4 = table.Column<string>(type: "TEXT", nullable: false),
                    DirectReward = table.Column<string>(type: "TEXT", nullable: false),
                    NextStopsLocked = table.Column<bool>(type: "INTEGER", nullable: false),
                    WorkersResetAt = table.Column<string>(type: "TEXT", nullable: true),
                    Rewards = table.Column<string>(type: "TEXT", nullable: false),
                    LostRuns = table.Column<string>(type: "TEXT", nullable: false),
                    LostCargo = table.Column<string>(type: "TEXT", nullable: false),
                    ManualEnds = table.Column<string>(type: "TEXT", nullable: false),
                    Rows = table.Column<string>(type: "TEXT", nullable: false),
                    PickupClaims = table.Column<string>(type: "TEXT", nullable: false),
                    LockedTrips = table.Column<string>(type: "TEXT", nullable: false),
                    PastWorkers = table.Column<string>(type: "TEXT", nullable: false),
                    Carried = table.Column<string>(type: "TEXT", nullable: false),
                    RunValues = table.Column<string>(type: "TEXT", nullable: false),
                    FailedValues = table.Column<string>(type: "TEXT", nullable: false),
                    MissionIds = table.Column<string>(type: "TEXT", nullable: false),
                    Party = table.Column<string>(type: "TEXT", nullable: true),
                    Stats = table.Column<string>(type: "TEXT", nullable: false),
                    Received = table.Column<string>(type: "TEXT", nullable: false),
                    Members = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sessions", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    PartyTrackingSince = table.Column<string>(type: "TEXT", nullable: true),
                    PlanCreditSince = table.Column<string>(type: "TEXT", nullable: true),
                    GameLogPath = table.Column<string>(type: "TEXT", nullable: true),
                    DarkMode = table.Column<bool>(type: "INTEGER", nullable: false),
                    Channel = table.Column<string>(type: "TEXT", nullable: true),
                    WebView = table.Column<bool>(type: "INTEGER", nullable: false),
                    WebViewPort = table.Column<int>(type: "INTEGER", nullable: false),
                    WatchPaths = table.Column<string>(type: "TEXT", nullable: false),
                    DisabledWatchPaths = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrackerKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Player = table.Column<string>(type: "TEXT", nullable: false),
                    KeyHash = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackerKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Transfers",
                columns: table => new
                {
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Time = table.Column<string>(type: "TEXT", nullable: false),
                    From = table.Column<string>(type: "TEXT", nullable: false),
                    To = table.Column<string>(type: "TEXT", nullable: false),
                    Amount = table.Column<string>(type: "TEXT", nullable: false),
                    Payment = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transfers", x => x.Position);
                });

            migrationBuilder.CreateTable(
                name: "TypedScu",
                columns: table => new
                {
                    Objective = table.Column<string>(type: "TEXT", nullable: false),
                    Scu = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TypedScu", x => x.Objective);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrackerKeys_KeyHash",
                table: "TrackerKeys",
                column: "KeyHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DroppedMissions");

            migrationBuilder.DropTable(
                name: "LocationNames");

            migrationBuilder.DropTable(
                name: "ManualSessions");

            migrationBuilder.DropTable(
                name: "Members");

            migrationBuilder.DropTable(
                name: "MissionHolders");

            migrationBuilder.DropTable(
                name: "PairAdjust");

            migrationBuilder.DropTable(
                name: "PartyChanges");

            migrationBuilder.DropTable(
                name: "PayoutOverrides");

            migrationBuilder.DropTable(
                name: "Payouts");

            migrationBuilder.DropTable(
                name: "Sessions");

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "TrackerKeys");

            migrationBuilder.DropTable(
                name: "Transfers");

            migrationBuilder.DropTable(
                name: "TypedScu");
        }
    }
}
