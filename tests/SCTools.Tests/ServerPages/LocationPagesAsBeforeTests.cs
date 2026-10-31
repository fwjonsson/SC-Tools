using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using SCTools.Tests.Support;

namespace SCTools.Tests.ServerPages;

/// <summary>
/// What the pages that say where a player is showed before the location tracker (recorded on 31 Oct 2026, with the map and the Hauling Planner still
/// learning from each other through PlannerPositions): the Map tool's map.json, the Hauling Planner's map.json and its Current location, the Companion's
/// map, My page, the admin's Party page and the Party tool's page of a member. The location tracker has to show the same.
/// </summary>
public class LocationPagesAsBeforeTests
{
    static string Id(DateTime t) => t.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    const string Gaslight = "rs_ext_pyro5_l2", PatchCity = "rs_ext_pyro3_l3", HurL2 = "LOC_RR_S1_L2";
    const string Beacon = "MISSION_QT_Quantum_Beacon_LongRange_865476577647";
    const long Pad = 2026442305, GaslightArea = 3688917516;

    /// <summary>
    /// The played logs, every one of them playing now: a pilot and his crew (only the pilot's log has the destination), a player at Levski by his inventory,
    /// one on his way, one in Stanton, one at a mission's marker, one heading to a mission's beacon, one off the grid, and Pilot_A whose pickups (the Hauling
    /// Planner) are all the logs say about where he is.
    /// </summary>
    static ServerFactory Played(DateTime now)
    {
        var data = PlayedSession.Data();
        data.Party.AddRange(new[] { "zzoega", "zzatvo" });
        var f = new ServerFactory { Data = data };
        _ = f.Live;
        var m = GameLogWriter.MissionId(5);
        var n = GameLogWriter.MissionId(6);
        var logs = new (string Who, GameLogWriter Log)[]
        {
            // zzoega flies his C2 from Ruin to Gaslight with zzatvo aboard, lands, and picks Patch City on the pad
            ("zzoega", new GameLogWriter().At(now.AddMinutes(-30)).Login("zzoega").PartyCreated().Minutes(1).MemberJoined("zzatvo")
                .Minutes(4).QuantumSelected(Gaslight).RouteStart("Ruin Station", Gaslight).Minutes(5).QuantumArrived()
                .Minutes(10).AreaChange("zzoega", 0, Pad, GaslightArea, Pad).Minutes(5).QuantumSelected(PatchCity).RouteStart("Gaslight", PatchCity)),
            ("zzatvo", new GameLogWriter().At(now.AddMinutes(-29.5)).Login("zzatvo").PartyJoined().Minutes(9.5).QuantumArrived()),
            // zzoii opened his inventory at Levski
            ("zzoii", new GameLogWriter().At(now.AddMinutes(-10)).Login("zzoii").Minutes(1).Inventory("zzoii", "Nyx_Levski")),
            // bileh123 picked Gaslight at Ruin a minute ago, out in space: on his way
            ("bileh123", new GameLogWriter().At(now.AddMinutes(-12)).Login("bileh123").Minutes(11).QuantumSelected(Gaslight, "ANVL_Arrow", "555555555555")
                .RouteStart("Ruin Station", Gaslight)),
            // stantonian jumps from Aberdeen to HUR-L2
            ("stantonian", new GameLogWriter().At(now.AddMinutes(-3)).Login("stantonian").Minutes(2).QuantumSelected(HurL2, "RSI_Aurora", "666666666666").RouteStart("Aberdeen", HurL2)),
            // trila took a bounty at Levski and jumped at its marker: at the mission
            ("trila", new GameLogWriter().At(now.AddMinutes(-8)).Login("trila").Inventory("trila", "Nyx_Levski")
                .MissionMarker(m, GameLogWriter.BountyContract, "obj-1", "21517").ObjectivePopup(m, "obj-1", "Go to Asteroid Cluster").BeaconPaired("21517", Beacon)
                .Minutes(4).QuantumArrived("RSI_Perseus", "898683539008")),
            // hunter took one too and picked its beacon: heading there
            ("hunter", new GameLogWriter().At(now.AddMinutes(-8)).Login("hunter").Inventory("hunter", "Nyx_Levski")
                .MissionMarker(n, GameLogWriter.BountyContract, "obj-1", "21518").ObjectivePopup(n, "obj-1", "Go to Asteroid Cluster").BeaconPaired("21518", Beacon)
                .Minutes(6).QuantumSelected(Beacon, "RSI_Perseus", "898683539009")),
            // lost jumped from Levski at something with no mission going on: off the grid
            ("lost", new GameLogWriter().At(now.AddMinutes(-6)).Login("lost").Inventory("lost", "Nyx_Levski").Minutes(4).QuantumArrived("ANVL_Arrow", "777777777777")),
        };
        foreach (var (who, log) in logs) Assert.True(f.Live.Uploads.Add(who, Id(now.AddMinutes(-40)), 0, 10000, log.Lines.ToList(), now).Ok);
        // Pilot_A hauled: his last pickup was at Gaslight, and his log has no quantum travel at all
        Assert.True(f.Live.Uploads.Add(PlayedSession.Player, Id(GameLogWriter.Start), 0, 10000, PlayedSession.Writer().Lines.ToList(), now).Ok);
        Tick(f, now);
        return f;
    }

    /// <summary>What the server's refresh does every two seconds (RefreshService; its loop does not run beside a test): the logs worked out, and the map's places given to the Hauling Planner.</summary>
    static void Tick(ServerFactory f, DateTime now)
    {
        f.Live.Refresh(now);
        f.Services.GetRequiredService<SCTools.Server.PlannerPositions>().Push(now, force: true);
        f.Live.Refresh(now);
    }

    /// <summary>
    /// What a map says about the players and the missions, written out: every field, the times as seconds from now (the page's clocks and moving markers
    /// count from them), the systems left out (the game data).
    /// </summary>
    static string Written(JsonElement map, DateTime now)
    {
        var sb = new StringBuilder();
        foreach (var part in new[] { "players", "objectives", "unplaced", "missions", "me", "party" })
        {
            if (!map.TryGetProperty(part, out var e)) continue;
            sb.Append(part).Append(": ");
            Write(sb, e, now, part);
            sb.Append('\n');
        }
        return sb.ToString();
    }

    static void Write(StringBuilder sb, JsonElement e, DateTime now, string name)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                sb.Append('{');
                foreach (var p in e.EnumerateObject())
                {
                    sb.Append(p.Name).Append('=');
                    Write(sb, p.Value, now, p.Name);
                    sb.Append(' ');
                }
                sb.Append('}');
                if (name == "players") sb.Append('\n');
                break;
            case JsonValueKind.Array:
                sb.Append('[');
                foreach (var x in e.EnumerateArray())
                {
                    if (name == "players" || name == "objectives" || name == "missions") sb.Append('\n');
                    Write(sb, x, now, name);
                    sb.Append(',');
                }
                sb.Append(']');
                break;
            case JsonValueKind.String when DateTime.TryParse(e.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t)
                && e.GetString()!.Contains('T'):
                sb.Append(When(t, now));
                break;
            case JsonValueKind.Number when name == "start":
                sb.Append(When(DateTimeOffset.FromUnixTimeMilliseconds(e.GetInt64()).UtcDateTime, now));
                break;
            default:
                sb.Append(e.GetRawText());
                break;
        }
    }

    /// <summary>A time as seconds from now ("now-1200s"), or as itself when it is days away (Pilot_A's session of 21 Oct 2026).</summary>
    static string When(DateTime t, DateTime now)
    {
        if ((t - now).Duration() > TimeSpan.FromDays(1)) return t.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture);
        var s = Math.Round((t - now).TotalSeconds);
        return "now" + (s >= 0 ? "+" : "") + s.ToString(CultureInfo.InvariantCulture) + "s";
    }

    static async Task<JsonElement> Get(HttpClient c, string address) => JsonDocument.Parse(await c.GetStringAsync(address)).RootElement;

    [Fact]
    public async Task Every_map_shows_every_player_where_he_was_before()
    {
        var now = DateTime.UtcNow;
        using var f = Played(now);
        var admin = await f.Admin();
        var map = Written(await Get(admin, "/map/map.json"), now);
        Same(AdminMap, map);
        Assert.Equal(map, Written(await Get(admin, "/hauling/map.json"), now));   // the Hauling Planner's Route Planner draws the same
        Same(AdminCompanion, Written(await Get(admin, "/companion/map.json"), now));   // the admin's own Companion: everybody

        // a player sees himself and his party, on every map; a visitor nobody
        await ServerFactory.Send(admin, "/admin/tools", "/admin/tools/set", ("id", "companion"), ("value", "true"));
        var zzoega = await f.Player(f.AddKey("zzoega"));
        Same(PlayerMap, Written(await Get(zzoega, "/hauling/map.json"), now));
        Same(PlayerCompanion, Written(await Get(zzoega, "/companion/map.json"), now));
        Same(VisitorMap, Written(await Get(f.Browser(), "/hauling/map.json"), now));
    }

    [Fact]
    public async Task The_planner_my_page_and_the_party_pages_have_every_member_where_they_had_him_before()
    {
        var now = DateTime.UtcNow;
        using var f = Played(now);
        // the Hauling Planner's Current location of every worker (the Trip Planner), as data.json has it
        Tick(f, now.AddSeconds(1));
        var current = string.Join("\n", JsonDocument.Parse(f.Live.Json).RootElement.GetProperty("planner").GetProperty("workers").EnumerateArray()
            .Select(w => w.GetProperty("name").GetString() + ": " + w.GetProperty("title").GetString()!.Trim(' ', '–') + " | " + w.GetProperty("location").GetString()));
        Same(PlannerCurrent, current);

        // My page: Automatic is where the logs have him; the admin's Party page: Automatic for every member; the Party tool's page of a member: his
        // location by the Hauling Planner
        var admin = await f.Admin();
        var pages = new StringBuilder();
        foreach (var who in new[] { "zzoega", "zzatvo", PlayedSession.Player, "zzoii" })
        {
            var me = WebUtility.HtmlDecode(await (await f.Player(f.AddKey(who))).GetStringAsync("/me"));
            pages.Append(who).Append(": My page ").Append(Regex.Match(me, "Automatic: [^<]*").Value);
            var tool = WebUtility.HtmlDecode(await admin.GetStringAsync("/party/" + who));
            pages.Append(", Party tool ").Append(Regex.Match(tool, "<th>Location</th><td>([^<]*)").Groups[1].Value.Trim()).Append('\n');
        }
        var party = WebUtility.HtmlDecode(await admin.GetStringAsync("/admin/party"));
        pages.Append("Party page: ").Append(string.Join(", ", Regex.Matches(party, "Automatic: [^<]*").Select(x => x.Value)));
        Same(MemberPages, pages.ToString());
    }

    /// <summary>The same as recorded (the recordings are written with the file's line ends, which git may change).</summary>
    static void Same(string recorded, string actual) => Assert.Equal(recorded.ReplaceLineEndings("\n"), actual);

    const string AdminMap = """
        players: [
        {name="zzoega" color="#ff9628" ship="Crusader C2 Hercules Starlifter" drive="Odyssey S2 (assumed)" top=238314300 system="pyro" state="at" at="pyro/gaslight" atName="Gaslight" mission=null since=now-1200s via=null leaving="Patch City" }
        ,
        {name="zzatvo" color="#3ccd5f" ship="Crusader C2 Hercules Starlifter" drive="Odyssey S2 (assumed)" top=238314300 system="pyro" state="at" at="pyro/gaslight" atName="Gaslight" mission=null since=now-1200s via=null leaving=null }
        ,
        {name="zzoii" color="#4682ff" ship="" drive="Odyssey S2 (assumed)" top=238314300 system="nyx" state="at" at="nyx/levski" atName="Levski" mission=null since=now-540s via=null leaving=null }
        ,
        {name="bileh123" color="#4682ff" ship="Crusader C2 Hercules Starlifter" drive="Odyssey S2 (assumed)" top=238314300 system="pyro" state="jump" from="pyro/ruin-station" fromName="Ruin Station" to="pyro/gaslight" toName="Gaslight" start=now-50s dist=105090000000 seconds=476.80504749488534 motion={v=238314300 a1=5705428 a2=8631428 sw=84000000 } }
        ,
        {name="stantonian" color="#4682ff" ship="Crusader C2 Hercules Starlifter" drive="Odyssey S2 (assumed)" top=238314300 system="stanton" state="jump" from="stanton/aberdeen" fromName="Aberdeen" to="stanton/hur-l2-faithful-dream-station" toName="HUR-L2 Faithful Dream Station" start=now-50s dist=1234549083.6738737 seconds=29.419823426399002 motion={v=238314300 a1=5705428 a2=8631428 sw=84000000 } }
        ,
        {name="trila" color="#4682ff" ship="RSI Perseus" drive="Odyssey S2 (assumed)" top=238314300 system="nyx" state="mission" atName="the mission: Go to Asteroid Cluster" mission="A mission" since=now-240s }
        ,
        {name="hunter" color="#4682ff" ship="RSI Perseus" drive="Odyssey S2 (assumed)" top=238314300 system="nyx" state="heading" to=null toName="the mission: Go to Asteroid Cluster" mission="A mission" since=now-120s }
        ,
        {name="lost" color="#4682ff" ship="Anvil Arrow" drive="Odyssey S2 (assumed)" top=238314300 system="nyx" state="unknown" off=true since=now-120s lastPing="Levski" }
        ,
        {name="Pilot_A" color="#4682ff" ship="" drive="Odyssey S2 (assumed)" top=238314300 system="pyro" state="at" at="pyro/gaslight" atName="Gaslight" mission=null since=2026-10-21T09:10:21.000 via="the Hauling Planner" leaving=null }
        ,]
        objectives: [
        {player="trila" title="A mission" system="nyx" where="Go to Asteroid Cluster" who=["trila",] },
        {player="hunter" title="A mission" system="nyx" where="Go to Asteroid Cluster" who=["hunter",] },]
        unplaced: 2
        missions: [
        {title="A mission" who=["trila",] open=1 faction="" category="" scu="" reward="" mg="" council="" xp="" legality="" key=null },
        {title="A mission" who=["hunter",] open=1 faction="" category="" scu="" reward="" mg="" council="" xp="" legality="" key=null },]

        """;
    const string AdminCompanion = """
        players: [
        {name="zzoega" color="#ff9628" ship="Crusader C2 Hercules Starlifter" drive="Odyssey S2 (assumed)" top=238314300 system="pyro" state="at" at="pyro/gaslight" atName="Gaslight" mission=null since=now-1200s via=null leaving="Patch City" }
        ,
        {name="zzatvo" color="#3ccd5f" ship="Crusader C2 Hercules Starlifter" drive="Odyssey S2 (assumed)" top=238314300 system="pyro" state="at" at="pyro/gaslight" atName="Gaslight" mission=null since=now-1200s via=null leaving=null }
        ,
        {name="zzoii" color="#4682ff" ship="" drive="Odyssey S2 (assumed)" top=238314300 system="nyx" state="at" at="nyx/levski" atName="Levski" mission=null since=now-540s via=null leaving=null }
        ,
        {name="bileh123" color="#4682ff" ship="Crusader C2 Hercules Starlifter" drive="Odyssey S2 (assumed)" top=238314300 system="pyro" state="jump" from="pyro/ruin-station" fromName="Ruin Station" to="pyro/gaslight" toName="Gaslight" start=now-50s dist=105090000000 seconds=476.80504749488534 motion={v=238314300 a1=5705428 a2=8631428 sw=84000000 } }
        ,
        {name="stantonian" color="#4682ff" ship="Crusader C2 Hercules Starlifter" drive="Odyssey S2 (assumed)" top=238314300 system="stanton" state="jump" from="stanton/aberdeen" fromName="Aberdeen" to="stanton/hur-l2-faithful-dream-station" toName="HUR-L2 Faithful Dream Station" start=now-50s dist=1234549083.6738737 seconds=29.419823426399002 motion={v=238314300 a1=5705428 a2=8631428 sw=84000000 } }
        ,
        {name="trila" color="#4682ff" ship="RSI Perseus" drive="Odyssey S2 (assumed)" top=238314300 system="nyx" state="mission" atName="the mission: Go to Asteroid Cluster" mission="A mission" since=now-240s }
        ,
        {name="hunter" color="#4682ff" ship="RSI Perseus" drive="Odyssey S2 (assumed)" top=238314300 system="nyx" state="heading" to=null toName="the mission: Go to Asteroid Cluster" mission="A mission" since=now-120s }
        ,
        {name="lost" color="#4682ff" ship="Anvil Arrow" drive="Odyssey S2 (assumed)" top=238314300 system="nyx" state="unknown" off=true since=now-120s lastPing="Levski" }
        ,
        {name="Pilot_A" color="#4682ff" ship="" drive="Odyssey S2 (assumed)" top=238314300 system="pyro" state="at" at="pyro/gaslight" atName="Gaslight" mission=null since=2026-10-21T09:10:21.000 via="the Hauling Planner" leaving=null }
        ,]
        objectives: [
        {player="trila" title="A mission" system="nyx" where="Go to Asteroid Cluster" who=["trila",] },
        {player="hunter" title="A mission" system="nyx" where="Go to Asteroid Cluster" who=["hunter",] },]
        unplaced: 2
        missions: []
        me: "admin"
        party: ["admin",]

        """;
    const string PlayerMap = """
        players: [
        {name="zzoega" color="#ff9628" ship="Crusader C2 Hercules Starlifter" drive="Odyssey S2 (assumed)" top=238314300 system="pyro" state="at" at="pyro/gaslight" atName="Gaslight" mission=null since=now-1200s via=null leaving="Patch City" }
        ,
        {name="zzatvo" color="#3ccd5f" ship="Crusader C2 Hercules Starlifter" drive="Odyssey S2 (assumed)" top=238314300 system="pyro" state="at" at="pyro/gaslight" atName="Gaslight" mission=null since=now-1200s via=null leaving=null }
        ,]
        objectives: []
        unplaced: 2
        missions: []

        """;
    const string PlayerCompanion = """
        players: [
        {name="zzoega" color="#ff9628" ship="Crusader C2 Hercules Starlifter" drive="Odyssey S2 (assumed)" top=238314300 system="pyro" state="at" at="pyro/gaslight" atName="Gaslight" mission=null since=now-1200s via=null leaving="Patch City" }
        ,
        {name="zzatvo" color="#3ccd5f" ship="Crusader C2 Hercules Starlifter" drive="Odyssey S2 (assumed)" top=238314300 system="pyro" state="at" at="pyro/gaslight" atName="Gaslight" mission=null since=now-1200s via=null leaving=null }
        ,]
        objectives: []
        unplaced: 2
        missions: []
        me: "zzoega"

        """;
    const string VisitorMap = """
        players: []
        objectives: []
        unplaced: 0
        missions: []

        """;
    const string PlannerCurrent = """
        Pilot_A: Current location: Gaslight | Gaslight
        zzoega: Current location: Gaslight | Gaslight
        zzatvo: Current location: Gaslight | Gaslight
        """;
    const string MemberPages = """
        zzoega: My page Automatic: Gaslight, Party tool Gaslight
        zzatvo: My page Automatic: Gaslight, Party tool Gaslight
        Pilot_A: My page Automatic: Gaslight, Party tool Gaslight
        zzoii: My page Automatic: Ruin Station, Party tool 
        Party page: Automatic: Gaslight, Automatic: Gaslight, Automatic: Gaslight
        """;
}
