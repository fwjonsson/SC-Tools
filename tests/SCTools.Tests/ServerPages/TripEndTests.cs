using System.Globalization;
using System.Net;
using SCTools.Tests.Support;

namespace SCTools.Tests.ServerPages;

/// <summary>The Submit button of a worker's Current Route (Frank, 30 Oct 2026): his run on the way ends, counted as it is; only he (or the admin) presses it.</summary>
public class TripEndTests
{
    static string Id(DateTime t) => t.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    [Fact]
    public async Task The_worker_submits_his_route_and_the_run_ends_with_what_it_picked_up()
    {
        using var f = new ServerFactory { FollowSessions = true };
        var now = DateTime.UtcNow;
        var m = GameLogWriter.MissionId(91);
        var contract = string.Format(GameLogWriter.HaulingContract, 2);
        // zzoega picked up at Gaslight and flies on: a run on the way
        var log = new GameLogWriter().At(now.AddMinutes(-30)).Login("zzoega").Accepted(m)
            .Marker(m, contract, "pickup_m91_0", PlayedSession.GaslightZone, GameLogWriter.Gaslight)
            .Marker(m, contract, "pickup_m91_1", PlayedSession.PatchZone, GameLogWriter.PatchCity)
            .Marker(m, contract, "dropoff_m91_0", PlayedSession.RuinZone, GameLogWriter.RuinStation)
            .Minutes(10).Unstow().Seconds(5).Objective(m, "pickup_m91_0", "COMPLETED");
        Assert.True(f.Live.Uploads.Add("zzoega", Id(now.AddMinutes(-30)), 0, 1000, log.Lines.ToList(), now).Ok);
        f.Live.Refresh(now);
        Assert.NotNull(f.Live.Read(t => t.Runs.ActiveRunOf("zzoega")));
        Assert.Contains("\"running\":true", f.Live.Json);   // the page shows the button
        Assert.Contains("\"tripEnd\":\"/hauling/trips/end\"", f.Live.Json);

        var zzatvo = await f.Player(f.AddKey("zzatvo"));
        var other = await ServerFactory.Send(zzatvo, "/hauling", "/hauling/trips/end", ("name", "zzoega"));   // not his route
        Assert.Equal(HttpStatusCode.Redirect, other.StatusCode);
        Assert.NotNull(f.Live.Read(t => t.Runs.ActiveRunOf("zzoega")));

        var zzoega = await f.Player(f.AddKey("zzoega"));
        var r = await ServerFactory.Send(zzoega, "/hauling", "/hauling/trips/end", ("name", "zzoega"));
        Assert.Equal((HttpStatusCode.Redirect, "/hauling/planner"), (r.StatusCode, r.Headers.Location?.OriginalString));
        Assert.Null(f.Live.Read(t => t.Runs.ActiveRunOf("zzoega")));
        var run = f.Live.Read(t => t.State.Trips.Single(x => x.Submitted));
        Assert.Equal(1, run.HandedIn);
        Assert.False(run.Manual);
        Assert.Contains("\"running\":false", f.Live.Json);
    }
}
