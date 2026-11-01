using System.Net;
using System.Text;
using SCTools.Tests.Support;

namespace SCTools.Tests.Locations;

/// <summary>
/// Where a player was to the metre: the game's /showlocation puts it on the clipboard, the tracker sends it, the server keeps it with his log, and the
/// location tracker places him by it and measures his next jump from it (Frank, 1 Nov 2026).
/// </summary>
public class PositionFixTests
{
    static readonly DateTime T = new(2026, 11, 1, 11, 20, 0, DateTimeKind.Utc);
    const string Frank = "Coordinates: x:-43498588585.727356 y:-18397314648.922775 z:33676.674324";   // zzoega in Nyx, 1 Nov 2026

    [Fact]
    public void Only_the_games_showlocation_text_is_a_fix()
    {
        var fix = PositionFix.Parse(Frank, T)!;
        Assert.Equal((T, -43498588585.727356, -18397314648.922775, 33676.674324), (fix.Time, fix.X, fix.Y, fix.Z));
        Assert.Equal((-43.498588585727356, -18.397314648922775), fix.Gm);
        Assert.NotNull(PositionFix.Parse("  " + Frank + "\r\n", T));
        Assert.Null(PositionFix.Parse("my password is hunter2", T));
        Assert.Null(PositionFix.Parse("Coordinates: somewhere", T));
        Assert.Null(PositionFix.Parse("note: " + Frank, T));   // only the game's text alone, nothing around it
        Assert.Null(PositionFix.Parse(null, T));
    }

    sealed class Stub : HttpMessageHandler
    {
        public readonly List<(HttpRequestMessage Request, string Body)> Seen = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Seen.Add((request, request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct)));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"site\":null}", Encoding.UTF8, "application/json") };
        }
    }

    [Fact]
    public async Task The_tracker_sends_the_fix_with_its_key()
    {
        var stub = new Stub();
        using var server = new HttpLogServer("https://example.test", "k3y-k3y-k3y-k3y-k3y", stub);
        await server.SendPositionAsync(PositionFix.Parse(Frank, T)!, CancellationToken.None);
        var (request, body) = Assert.Single(stub.Seen);
        Assert.Equal((HttpMethod.Post, "https://example.test/api/position"), (request.Method, request.RequestUri!.ToString()));
        Assert.Equal("k3y-k3y-k3y-k3y-k3y", request.Headers.GetValues("X-Tracker-Key").Single());
        Assert.Contains("\"x\":-43498588585.727356", body);
        Assert.Contains("\"time\":\"2026-11-01T11:20:00Z\"", body);
    }

    [Fact]
    public void The_server_keeps_the_fixes_with_the_log_and_reads_them_again()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ht-fix-" + Guid.NewGuid().ToString("N"));
        try
        {
            var now = DateTime.UtcNow;
            var uploads = new LogUploads(dir);
            var fix = PositionFix.Parse(Frank, now.AddMinutes(-5))!;
            uploads.AddFix("zzoega", fix);
            uploads.AddFix("zzoega", fix);   // the same twice: once
            Assert.Equal(new[] { fix }, uploads.Logs.Single().Fixes);
            var again = new LogUploads(dir);
            again.LoadKept(TimeSpan.FromHours(36));
            Assert.Equal(new[] { fix }, again.Logs.Single().Fixes);
            var old = new LogUploads(dir);
            old.LoadKept(TimeSpan.FromMinutes(1));   // older than what is kept
            Assert.Empty(old.Logs.Single().Fixes);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void A_fix_at_a_spot_is_there_and_the_next_beacon_is_measured_from_the_fix()
    {
        // a mission's site saved before (hidden), and a fix 30 km from it: he is there
        var site = new Place("nyx", "asteroid-mining-base", "Asteroid Mining Base", "Asteroid Mining Base", PlaceKind.Station, null, -43.4986, -18.3973, Hidden: true);
        var other = new Place("nyx", "far-base", "Far Base", "Far Base", PlaceKind.Station, null, 39.0, -27.0, Hidden: true);
        var atlas = new Atlas(StarMap.Places, Array.Empty<PlaceAlias>(), new[] { site, other });
        var seen = new[] { new LocationSeen(T.AddMinutes(-30), "Nyx_Levski") };
        var fix = new PositionFix(T, -43.4986e9 + 30_000, -18.3973e9, 0);
        var w = LocationTracker.Follow(new List<QtEvent>(), atlas, null, seen, null, null, fixes: new[] { fix });
        Assert.Equal(("nyx/asteroid-mining-base", T), (w.At?.Key, w.Since!.Value));
        Assert.Equal(fix, w.Fix);
        // 60 km off: not the site (the map leaves him where it had him), but still where he is to measure from
        var off = new PositionFix(T, -43.4986e9 + 60_000, -18.3973e9, 0);
        Assert.Equal("nyx/levski", LocationTracker.Follow(new List<QtEvent>(), atlas, null, seen, null, null, fixes: new[] { off }).At?.Key);

        // he picks a mission's beacon after the fix: its fuel estimate is the distance from exactly here, and the spot at that distance is where it goes
        var gm = StarMap.Distance(site, other);
        var beacon = "MISSION_QT_Distress_Beacon_MinningAsteroidBase_864932668710";
        var point = new MissionPoint(beacon, "1952", "Restore Order", "Go to Asteroid Mining Base", "nyx", null, T, null);
        var pick = new QtEvent { Time = T.AddMinutes(1), Kind = QtKind.Selected, Destination = beacon, From = "Nyx System", Ship = "RSI_Perseus", FuelEstimate = gm * 32273 };
        var jump = LocationTracker.Follow(new List<QtEvent> { pick }, atlas, null, seen, new[] { point }, null, fuelPerGm: _ => 32273, fixes: new[] { off });
        Assert.Equal("nyx/far-base", jump.Ranged?[beacon].Key);
        // without the fix the game names no start ("Nyx System"): no place to measure from
        Assert.Null(LocationTracker.Follow(new List<QtEvent> { pick }, atlas, null, seen, new[] { point }, null, fuelPerGm: _ => 32273).Ranged);

        // after a jump's arrival the fix is no longer where he is
        var arrived = new QtEvent { Time = T.AddMinutes(5), Kind = QtKind.Arrived };
        Assert.Null(LocationTracker.Follow(new List<QtEvent> { pick, arrived }, atlas, null, seen, new[] { point }, null, fixes: new[] { off }).Fix);
    }
}
