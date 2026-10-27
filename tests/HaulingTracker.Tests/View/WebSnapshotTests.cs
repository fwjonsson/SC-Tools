using System.Text.Json;
using HaulingTracker.Tests.Support;

namespace HaulingTracker.Tests.View;

/// <summary>The live web view as data (data.json): every tab, made from the tracker alone, with the same tables, texts and colours as the program.</summary>
public class WebSnapshotTests
{
    static JsonElement Snapshot(bool dark = true)
    {
        var (t, _) = PlayedSession.Load();
        return JsonDocument.Parse(WebSnapshot.Json(t, new WebSnapshotOptions { Dark = dark })).RootElement;
    }

    static string[] Headers(JsonElement table) => table.GetProperty("headers").EnumerateArray().Select(h => h.GetProperty("text").GetString()!).ToArray();
    static JsonElement[] Rows(JsonElement table) => table.GetProperty("rows").EnumerateArray().ToArray();
    static string[] Cells(JsonElement row) => row.GetProperty("cells").EnumerateArray().Select(c => c.GetString()!).ToArray();
    static string Hex(int rgb) => ViewPalette.Hex(rgb);

    [Fact]
    public void Every_tab_is_in_it_and_every_row_has_a_cell_and_colours_for_every_column()
    {
        var d = Snapshot();
        foreach (var key in new[] { "updated", "session", "theme", "summary", "rewards", "party", "pickups", "finished", "leaders", "runs", "planRuns", "runsLive", "planner", "debts", "history" })
            Assert.True(d.TryGetProperty(key, out _), key + " is missing");

        var tables = 0;
        void Walk(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Object)
            {
                if (e.TryGetProperty("headers", out var h) && e.TryGetProperty("rows", out var rows))
                {
                    tables++;
                    var n = h.GetArrayLength();
                    foreach (var r in rows.EnumerateArray())
                    {
                        Assert.Equal(n, r.GetProperty("cells").GetArrayLength());
                        Assert.Equal(n, r.GetProperty("back").GetArrayLength());
                        Assert.Equal(n, r.GetProperty("fore").GetArrayLength());
                    }
                }
                foreach (var p in e.EnumerateObject()) Walk(p.Value);
            }
            else if (e.ValueKind == JsonValueKind.Array) foreach (var x in e.EnumerateArray()) Walk(x);
        }
        Walk(d);
        Assert.True(tables >= 12, tables + " tables");
    }

    [Fact]
    public void The_session_tab_has_the_stops_the_contracts_and_the_session()
    {
        var d = Snapshot();
        Assert.StartsWith("Session: ", d.GetProperty("session").GetString());
        Assert.EndsWith("(2 missions)", d.GetProperty("session").GetString());

        var stops = d.GetProperty("pickups");
        Assert.Equal(new[] { "#", "Pickup Location", "From Ruin", "Batches", "Cargo", "Value", "Notes", "Drop-off" }, Headers(stops));
        var patch = Rows(stops).Select(Cells).Single(c => c[1] == "Patch City");
        Assert.Equal("1/1", patch[3]);   // the open mission's batch still waiting (the completed mission is gone from the board)
        Assert.Equal(CargoRuns.WaitingScuText(new LocationRow { Missions = 1, OpenMissions = { new Mission { Multi = 2 } } }), patch[4]);
        Assert.All(Rows(stops).Select(Cells).Select((c, i) => (c, i)), x => Assert.Equal((x.i + 1).ToString(), x.c[0]));   // numbered

        Assert.Equal(new[] { "Contract", "Mission Reward", "Active", "Completed", "Total" }, Headers(d.GetProperty("rewards")));
        var rewards = Rows(d.GetProperty("rewards"));
        Assert.Equal(2, rewards.Length);   // only the kind of contract the session has, and the Total
        Assert.Equal(new[] { "2 To Ruin", Earnings.DefaultReward2.ToString("N0"), "1", "1", (2 * Earnings.DefaultReward2).ToString("N0") }, Cells(rewards[0]));   // one still going, one done
        Assert.Equal(new[] { "Total", "", "1", "1", (2 * Earnings.DefaultReward2).ToString("N0") }, Cells(rewards[^1]));
        Assert.Equal("Total", Cells(rewards[^1])[0]);
        Assert.True(rewards[^1].GetProperty("bold").GetBoolean());

        var summary = d.GetProperty("summary").EnumerateArray().ToDictionary(x => x.GetProperty("k").GetString()!, x => x.GetProperty("v").GetString()!);
        Assert.Equal(7, summary.Count);
        Assert.False(string.IsNullOrEmpty(summary["Start Time"]));
        Assert.Equal(Earnings.DefaultReward2.ToString("N0"), summary["Total aUEC Earned"]);   // the first mission is completed
    }

    [Fact]
    public void With_more_than_one_party_in_the_game_each_has_a_session_tab_of_its_own()
    {
        var (t, _) = PlayedSession.Load();
        t.State.Data.Party.Add("Pilot_B");   // no log: he is not in Pilot_A's party
        var parties = new AutoParty.Result();
        parties.Groups.Add(new PartyGroup(PlayedSession.Player, new[] { PlayedSession.Player }));
        parties.Groups.Add(new PartyGroup("Pilot_B", new[] { "Pilot_B" }));
        t.State.Parties = parties;
        var trips = t.State.Trips.Count;

        var d = JsonDocument.Parse(WebSnapshot.Json(t, new WebSnapshotOptions())).RootElement;
        var list = d.GetProperty("parties").EnumerateArray().ToArray();
        Assert.Equal(new[] { PlayedSession.Player, "Pilot_B" }, list.Select(p => p.GetProperty("title").GetString()));
        var a = list[0].GetProperty("view");
        var b = list[1].GetProperty("view");
        Assert.Equal(Rows(d.GetProperty("runs")).Length, Rows(a.GetProperty("runs")).Length);   // the runs are all Pilot_A's
        Assert.Contains(Rows(a.GetProperty("pickups")).Select(Cells), c => c[1] == "Patch City");
        Assert.Equal(Cells(Rows(d.GetProperty("rewards"))[0]), Cells(Rows(a.GetProperty("rewards"))[0]));
        Assert.Empty(Rows(b.GetProperty("runs")));      // Pilot_B: no runs,
        Assert.Empty(Rows(b.GetProperty("pickups")));   // no missions
        Assert.Equal("", Cells(Rows(b.GetProperty("rewards"))[^1])[^1]);   // their contracts pay nothing
        Assert.Equal(2, t.State.Current!.Missions.Count);   // and the session is the whole one again
        Assert.Equal(trips, t.State.Trips.Count);
        Assert.Null(t.State.Focus);

        parties.Groups.RemoveAt(1);   // one party: its name only, the whole session is theirs
        d = JsonDocument.Parse(WebSnapshot.Json(t, new WebSnapshotOptions())).RootElement;
        var one = Assert.Single(d.GetProperty("parties").EnumerateArray());
        Assert.Equal(PlayedSession.Player, one.GetProperty("title").GetString());
        Assert.False(one.TryGetProperty("view", out _));

        t.State.Parties = null;   // the logs do not say (the program)
        Assert.Empty(JsonDocument.Parse(WebSnapshot.Json(t, new WebSnapshotOptions())).RootElement.GetProperty("parties").EnumerateArray());
    }

    [Fact]
    public void Runs_have_the_colour_of_how_they_went()
    {
        var d = Snapshot();
        var runs = d.GetProperty("runs");
        Assert.Equal(new[] { "#", "Worker", "Pickup Location & Time", "Batches", "Value", "Started", "Completed", "Time", "In flight", "aUEC/h", "Lost Cargo" }, Headers(runs));
        var rows = Rows(runs);
        Assert.Equal(2, rows.Length);
        Assert.All(rows[0].GetProperty("back").EnumerateArray(), c => Assert.Equal(Hex(ViewPalette.Dark.Yellow), c.GetString()));   // on the way
        Assert.All(rows[1].GetProperty("back").EnumerateArray(), c => Assert.Equal(Hex(ViewPalette.Dark.Green), c.GetString()));    // handed in
        Assert.Equal("None", Cells(rows[1])[10]);
        Assert.Equal(2, Rows(d.GetProperty("planRuns")).Length);
        Assert.Equal(2, d.GetProperty("runsLive").GetArrayLength());
    }

    [Fact]
    public void The_party_rows_have_the_member_colour_and_the_planner_his_tables()
    {
        var d = Snapshot();
        var member = Rows(d.GetProperty("party")).Single();
        Assert.Equal("PILOT_A", Cells(member)[0]);
        var colour = Hex(ViewPalette.MapPalette[0]);   // not zzoega or zzatvo: the first colour of the map palette
        Assert.All(member.GetProperty("back").EnumerateArray(), c => Assert.Equal(colour, c.GetString()));

        var worker = d.GetProperty("planner").GetProperty("workers").EnumerateArray().Single();
        Assert.Equal(PlayedSession.Player, worker.GetProperty("name").GetString());
        Assert.Equal("PILOT_A", worker.GetProperty("heading").GetString());
        Assert.Equal(JsonValueKind.Object, worker.GetProperty("current").ValueKind);
        Assert.True(d.GetProperty("planner").GetProperty("actionWidth").GetInt32() > 16);
    }

    [Fact]
    public void The_light_theme_has_light_colours()
    {
        var d = Snapshot(dark: false);
        Assert.Equal("#ffffff", d.GetProperty("theme").GetProperty("bg").GetString());
        Assert.Equal("#000000", d.GetProperty("theme").GetProperty("fg").GetString());
    }

    [Fact]
    public void Colours_of_workers_and_runs()
    {
        Assert.Equal(ViewPalette.Rgb(255, 150, 40), ViewPalette.WorkerColor("ZZOEGA", null, 3));
        Assert.Equal(ViewPalette.Rgb(60, 205, 95), ViewPalette.WorkerColor("zzatvo", null, 0));
        Assert.Equal(0x123456, ViewPalette.WorkerColor("zzoega", unchecked((int)0xFF123456), 0));   // picked in the Party table
        Assert.Equal(ViewPalette.MapPalette[1], ViewPalette.WorkerColor("Bileh123", null, 7));
        Assert.Equal(0x000000, ViewPalette.TextOn(0xFFFFFF));
        Assert.Equal(0xFFFFFF, ViewPalette.TextOn(0x202020));
        var p = ViewPalette.Dark;
        Assert.Equal(p.Red, p.RunColor("All", true));
        Assert.Equal(p.Orange, p.RunColor("Little", true));
        Assert.Equal(p.Green, p.RunColor("None", true));
        Assert.Equal(p.Yellow, p.RunColor("None", false));
        Assert.Null(p.PlanRowColor("plan"));
        Assert.Equal("#1e1e20", ViewPalette.Hex(p.Window));
    }
}
