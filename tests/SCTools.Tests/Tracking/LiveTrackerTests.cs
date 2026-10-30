using System.Text.Json;
using SCTools.Tests.Support;

namespace SCTools.Tests.Tracking;

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

    [Fact]
    public void A_change_on_a_page_is_saved_and_shown_at_once()
    {
        var uploads = new LogUploads();
        var saves = 0;
        var live = new LiveTracker(PlayedSession.Data(), _ => saves++, uploads);
        uploads.Add(PlayedSession.Player, "f", 0, 100, PlayedSession.Writer().Lines.ToList());
        live.Refresh(DateTime.UtcNow);
        var before = saves;

        live.Edit(t => t.Crew.SetColor(PlayedSession.Player, unchecked((int)0xFF112233)), DateTime.UtcNow);
        Assert.True(saves > before);
        var worker = JsonDocument.Parse(live.Json).RootElement.GetProperty("planner").GetProperty("workers")[0];
        Assert.Equal("#112233", worker.GetProperty("color").GetString());   // data.json has the new colour already (the planner's worker; no Party table since 29 Oct 2026)
        Assert.Equal(unchecked((int)0xFF112233), live.Read(t => t.Crew.PickedColor(PlayedSession.Player)));
    }

    [Fact]
    public void Data_json_stays_as_it_is_while_nothing_changes()
    {
        var uploads = new LogUploads();
        var live = new LiveTracker(PlayedSession.Data(), _ => { }, uploads);
        uploads.Add(PlayedSession.Player, "f", 0, 100, PlayedSession.Writer().Lines.ToList());
        var now = DateTime.UtcNow;
        live.Refresh(now);
        var made = live.Snapshot;
        Assert.StartsWith("\"", made.ETag);   // an ETag is a quoted string
        Assert.Equal(made.Json, System.Text.Encoding.UTF8.GetString(made.Bytes));

        live.Refresh(now);
        live.Refresh(now);
        Assert.Same(made, live.Snapshot);   // nothing came in, nothing was saved, the status line is the same: the same data.json, not made again
        Assert.Equal(1, live.Builds);
    }

    [Fact]
    public void A_new_status_line_alone_does_not_make_data_json_again()
    {
        var uploads = new LogUploads();
        var live = new LiveTracker(PlayedSession.Data(), _ => { }, uploads);
        uploads.Add(PlayedSession.Player, "f", 0, 100, PlayedSession.Writer().Lines.ToList());
        var now = DateTime.UtcNow;
        live.Refresh(now);
        var made = live.Snapshot;
        // the status line moves on every few seconds (a new line, and clock lines like "has spent 12m 04s on this trip"); nothing else changes
        for (var s = 2; s < 28; s += 2) live.Refresh(now.AddSeconds(s));
        Assert.Same(made, live.Snapshot);
    }

    [Fact]
    public void Data_json_is_made_anew_when_something_comes_in_and_after_half_a_minute()
    {
        var uploads = new LogUploads();
        var live = new LiveTracker(PlayedSession.Data(), _ => { }, uploads);
        var w = PlayedSession.Writer();
        var lines = w.Lines.ToList();
        uploads.Add(PlayedSession.Player, "f", 0, 100, lines);
        var now = DateTime.UtcNow;
        live.Refresh(now);
        var first = live.Snapshot;

        var more = w.Minutes(15).Unstow().Seconds(5).Objective(PlayedSession.M2, "pickup_m2_1", "COMPLETED");
        uploads.Add(PlayedSession.Player, "f", 100, 200, more.Lines.Skip(lines.Count).ToList());
        live.Refresh(now);
        var second = live.Snapshot;
        Assert.NotSame(first, second);   // a new piece of log: made again
        Assert.NotEqual(first.ETag, second.ETag);

        live.Refresh(now);
        Assert.Same(second, live.Snapshot);
        live.Refresh(now + LiveTracker.MaxAge);
        Assert.NotSame(second, live.Snapshot);   // half a minute gone by: made again, so what follows the clock alone keeps up
    }
}
