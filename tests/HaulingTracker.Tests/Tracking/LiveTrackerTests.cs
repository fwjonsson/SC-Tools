using System.Text.Json;
using HaulingTracker.Tests.Support;

namespace HaulingTracker.Tests.Tracking;

/// <summary>The tracker on the server: what the trackers upload is worked out again (only when something new came in) and data.json is ready for the page.</summary>
public class LiveTrackerTests
{
    static string[][] Cells(JsonElement table) =>
        table.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("cells").EnumerateArray().Select(c => c.GetString()!).ToArray()).ToArray();

    [Fact]
    public void Uploaded_logs_make_the_session_and_its_page()
    {
        var uploads = new LogUploads();
        var live = new LiveTracker(PlayedSession.Data(), null, uploads);
        live.Refresh(DateTime.UtcNow);
        Assert.Empty(Cells(JsonDocument.Parse(live.Json).RootElement.GetProperty("runs")));   // nothing uploaded yet: the session started by hand, empty

        uploads.Add(PlayedSession.Player, "f", 0, 100, PlayedSession.Writer().Lines.ToList());
        live.Refresh(DateTime.UtcNow);
        Assert.NotNull(live.Tracker.State.Current);
        var d = JsonDocument.Parse(live.Json).RootElement;
        Assert.Contains(Cells(d.GetProperty("pickups")), c => c[1] == "Patch City");
        Assert.Equal(2, Cells(d.GetProperty("runs")).Length);
        Assert.StartsWith("Session: ", d.GetProperty("session").GetString());
    }

    [Fact]
    public void A_new_piece_of_log_is_worked_out_on_the_next_refresh()
    {
        var uploads = new LogUploads();
        var live = new LiveTracker(PlayedSession.Data(), null, uploads);
        var w = PlayedSession.Writer();
        var lines = w.Lines.ToList();
        uploads.Add(PlayedSession.Player, "f", 0, 100, lines);
        live.Refresh(DateTime.UtcNow);
        var runsBefore = live.Tracker.State.Trips.Count;

        // the second mission is picked up at Patch City and handed in: its run ends
        var more = w.Minutes(15).Unstow().Seconds(5).Objective(PlayedSession.M2, "pickup_m2_1", "COMPLETED")
            .Minutes(15).LoadingPlatform().Seconds(5).Objective(PlayedSession.M2, "dropoff_m2_0", "COMPLETED").Seconds(1).MissionEnded(PlayedSession.M2, "COMPLETED");
        uploads.Add(PlayedSession.Player, "f", 100, 200, more.Lines.Skip(lines.Count).ToList());
        live.Refresh(DateTime.UtcNow);
        Assert.Equal(runsBefore, live.Tracker.State.Trips.Count);
        Assert.All(live.Tracker.State.Trips, t => Assert.True(t.End.HasValue));   // both runs handed in
        Assert.Empty(live.Tracker.State.PickupRows.Where(r => r.Loaded < r.Missions));
    }
}
