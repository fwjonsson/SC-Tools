using System.Text.Json;
using SCTools.Tests.Support;

namespace SCTools.Tests.View;

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
        foreach (var key in new[] { "updated", "session", "theme", "summary", "rewards", "pickups", "finished", "leaders", "runs", "planRuns", "runsLive", "planner", "debts", "history" })
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
        Assert.Equal(new[] { "#", "Pickup Location", "Batches", "SCU", "Boxes", "Value", "Drop-off Location" }, Headers(stops));   // no From Ruin and Notes; SCU and Boxes apart (29 Oct 2026)
        var patch = Rows(stops).Select(Cells).Single(c => c[1] == "Patch City");
        Assert.Equal("1/1", patch[2]);   // the open mission's batch still waiting (the completed mission is gone from the board)
        Assert.Equal(PyroDistances.NameOf(PyroDistances.Ruin), patch[6]);   // a pickup goes to Ruin
        var waiting = CargoRuns.WaitingScu(new LocationRow { Missions = 1, OpenMissions = { new Mission { Multi = 2 } } });
        Assert.Equal((PlanText.ScuSum(waiting), PlanText.ScuGuess(waiting)), (patch[3], patch[4]));   // a guess: "~250" and its boxes
        Assert.Equal(new[] { "#", "Pickup Location", "Delivered", "SCU", "Boxes", "Worker", "Value", "Handed In" }, Headers(d.GetProperty("finished")));
        Assert.All(Rows(stops).Select(Cells).Select((c, i) => (c, i)), x => Assert.Equal((x.i + 1).ToString(), x.c[0]));   // numbered

        Assert.Equal(new[] { "Contract", "Mission Reward", "Active", "Completed", "Total" }, Headers(d.GetProperty("rewards")));
        var rewards = Rows(d.GetProperty("rewards"));
        Assert.Equal(2, rewards.Length);   // only the kind of contract the session has, and the Total
        // the contract by its name as the game shows it (one still going, one done), paying the built-in reward: no library here
        Assert.Equal(new[] { "Senior | Stellar Large Haul | to Ruin Station [50/200/250/500/1000/2000/4000 Rep]", Earnings.DefaultReward2.ToString("N0"), "1", "1", (2 * Earnings.DefaultReward2).ToString("N0") }, Cells(rewards[0]));
        Assert.Equal(new[] { "Total", "", "1", "1", (2 * Earnings.DefaultReward2).ToString("N0") }, Cells(rewards[^1]));
        Assert.Equal("Total", Cells(rewards[^1])[0]);
        Assert.True(rewards[^1].GetProperty("bold").GetBoolean());

        var summary = d.GetProperty("summary").EnumerateArray().ToDictionary(x => x.GetProperty("k").GetString()!, x => x.GetProperty("v").GetString()!);
        Assert.Equal(7, summary.Count);
        Assert.False(string.IsNullOrEmpty(summary["Start Time"]));
        Assert.Equal(Earnings.DefaultReward2.ToString("N0"), summary["Total aUEC Earned"]);   // the first mission is completed
    }

    [Fact]
    public void A_from_Ruin_drop_off_is_a_stop_of_its_own_read_like_the_pickups()
    {
        // a waste contract with two drop-offs: Patch City (a pickup stop too) and Rod's Fuel (nothing to pick up there): a row of its own for each
        // (Frank, 29 Oct 2026): Ruin as the pickup, the count, the cargo with its SCU, its value, the station as the drop-off, in gold
        var w = GameLogWriter.MissionId(61);
        var waste = string.Format(GameLogWriter.WasteContract, 2);
        var (t, _) = PlayedSession.Load(more: x => x.Minutes(1).Accepted(w)
            .Marker(w, waste, "dropoff_w_0", "900000006101", GameLogWriter.PatchCity)
            .Marker(w, waste, "dropoff_w_1", "900000006102", GameLogWriter.RodsFuel));
        var d = JsonDocument.Parse(WebSnapshot.Json(t, new WebSnapshotOptions())).RootElement;
        var rows = Rows(d.GetProperty("pickups"));
        var ruin = PyroDistances.NameOf(PyroDistances.Ruin);
        var drops = rows.Where(r => Cells(r)[1] == ruin).ToList();
        Assert.Equal(2, drops.Count);
        Assert.Equal(new[] { "Patch City", "Rod's Fuel 'N Supplies" }, drops.Select(r => Cells(r)[6]).OrderBy(x => x));
        Assert.All(drops, r => Assert.Equal("1", Cells(r)[2]));
        Assert.All(drops, r => Assert.StartsWith("Waste: ", Cells(r)[4]));   // the boxes; the SCU beside it is a number, or ? when nobody knows it
        Assert.All(drops, r => Assert.True(Cells(r)[3] == "?" || int.TryParse(Cells(r)[3], out _)));
        Assert.All(drops, r => Assert.Equal((Earnings.DefaultReward2 / 2).ToString("N0"), Cells(r)[5]));   // its part of the contract
        Assert.All(drops, r => Assert.Equal(Hex(ViewPalette.Dark.Gold), r.GetProperty("back")[6].GetString()));
        var patch = Cells(rows.Single(r => Cells(r)[1] == "Patch City"));   // the pickup stop there is its own row still, worth its pickups alone
        Assert.Equal(ruin, patch[6]);
        Assert.Equal(t.Earnings.StopValue(t.State.PickupRows.Single(r => r.Zone == PlayedSession.PatchZone)).ToString("N0"), patch[5]);
    }

    [Fact]
    public void Every_column_has_the_same_colour_and_an_empty_session_shows_nothing_rather_than_zeros()
    {
        var d = Snapshot();
        static string[] Backs(JsonElement row) => row.GetProperty("back").EnumerateArray().Select(c => c.GetString()!).ToArray();
        var stop = Backs(Rows(d.GetProperty("pickups"))[0]);
        Assert.Equal(stop[3], stop[1]);   // Pickup Location and Drop-off look like the rest (nothing is typed on the page; Batches is green when all is picked up)
        Assert.Equal(stop[3], stop[6]);
        Assert.Equal(Hex(ViewPalette.Dark.Calc), stop[3]);
        Assert.Single(Backs(Rows(d.GetProperty("rewards"))[0]).Distinct());   // the Mission Reward too

        var live = new LiveTracker(PlayedSession.Data(), null, new LogUploads());   // the session started, nothing uploaded yet
        live.Refresh(DateTime.UtcNow);
        var empty = JsonDocument.Parse(live.Json).RootElement;
        Assert.Empty(Rows(empty.GetProperty("rewards")));   // no Total of zeros
        Assert.Empty(Rows(empty.GetProperty("leaders")));
        var summary = empty.GetProperty("summary").EnumerateArray().ToDictionary(x => x.GetProperty("k").GetString()!, x => x.GetProperty("v").GetString()!);
        Assert.All(new[] { "Batches Delivered", "Active Missions", "Missions Completed", "Total aUEC Earned", "aUEC/Hour" }, k => Assert.Equal("", summary[k]));
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
        Assert.Empty(Rows(b.GetProperty("rewards")));   // no contracts: not even a Total row
        Assert.Equal(2, t.State.Current!.Missions.Count);   // and the session is the whole one again
        Assert.Equal(trips, t.State.Trips.Count);
        Assert.Null(t.State.Focus);

        // a party that hauled in this session and is not playing now (its payouts went apart): it can still be picked
        t.State.Data.Sessions[t.State.Current!.Key].Groups = new() { new() { PlayedSession.Player }, new() { "Pilot_C", "Pilot_D" } };
        list = JsonDocument.Parse(WebSnapshot.Json(t, new WebSnapshotOptions())).RootElement.GetProperty("parties").EnumerateArray().ToArray();
        Assert.Equal(new[] { PlayedSession.Player, "Pilot_B", "Pilot_C's party" }, list.Select(p => p.GetProperty("title").GetString()));
        Assert.Equal(new[] { true, true, false }, list.Select(p => p.GetProperty("playing").GetBoolean()));
        t.State.Data.Sessions[t.State.Current!.Key].Groups = new();

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
        // a run on the way says when it started (UTC), so the page counts its Time up by its own clock (30 Oct 2026); a run that ended says nothing
        Assert.All(d.GetProperty("runsLive").EnumerateArray(), x => Assert.True(x.ValueKind == JsonValueKind.Null || DateTime.TryParse(x.GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out _)));
        var time = d.GetProperty("summary").EnumerateArray().Single(i => i.GetProperty("k").GetString() == "Session Time");
        Assert.Equal(time.GetProperty("live").GetBoolean(), time.GetProperty("since").ValueKind == JsonValueKind.String);   // the same for the session clock
    }

    [Fact]
    public void The_planner_has_the_workers_tables()
    {
        var d = Snapshot();
        Assert.False(d.TryGetProperty("party", out _));   // no Party table since 29 Oct 2026: the Party button of the top bar shows it
        var worker = d.GetProperty("planner").GetProperty("workers").EnumerateArray().Single();
        Assert.Equal(PlayedSession.Player, worker.GetProperty("name").GetString());
        Assert.Equal("PILOT_A", worker.GetProperty("heading").GetString());
        Assert.Equal(PyroDistances.NameOf(PlayedSession.Gaslight), worker.GetProperty("location").GetString());   // the station alone, for the heading's "since" (30 Oct 2026)
        Assert.Equal(JsonValueKind.Object, worker.GetProperty("current").ValueKind);
        Assert.True(d.GetProperty("planner").GetProperty("actionWidth").GetInt32() > 16);
    }

    [Fact]
    public void The_trip_planner_has_the_map_tools_systems_with_its_places_on_them()
    {
        var planner = Snapshot().GetProperty("planner");
        var maps = planner.GetProperty("maps").EnumerateArray().ToList();   // every system as the map engine draws it (StarMap): the same places as the Map tool (stage 2)
        Assert.Equal(new[] { "pyro", "stanton", "nyx" }, maps.Select(m => m.GetProperty("id").GetString()));
        var map = maps[0];
        Assert.Equal(("pyro", "Pyro"), (map.GetProperty("id").GetString(), map.GetProperty("name").GetString()));
        var places = map.GetProperty("places").EnumerateArray().ToList();
        Assert.Contains(places, p => p.GetProperty("key").GetString() == "pyro/pyro" && p.GetProperty("kind").GetString() == "star");
        var ruin = places.Single(p => p.GetProperty("key").GetString() == "pyro/ruin-station");
        Assert.Equal(("station", "terminus", "Ruin Station"), (ruin.GetProperty("kind").GetString(), ruin.GetProperty("parent").GetString(), ruin.GetProperty("short").GetString()));
        Assert.Equal(-52.3368, ruin.GetProperty("x").GetDouble(), 4);
        // the places the routes and the workers touch, by their place on the maps (the routes are drawn between them) and their system
        var stations = planner.GetProperty("places").EnumerateArray().ToDictionary(s => s.GetProperty("id").GetString()!, s => (s.GetProperty("key").GetString(), s.GetProperty("system").GetString()));
        Assert.Equal(("pyro/ruin-station", "pyro"), stations["Ruin"]);
        Assert.All(stations.Values, v => Assert.Equal("pyro", v.Item2));
        Assert.False(planner.TryGetProperty("backdrop", out _));
        Assert.False(planner.TryGetProperty("stations", out _));
        Assert.False(planner.TryGetProperty("map", out _));
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
        Assert.Equal("#0d1117", ViewPalette.Hex(p.Window));
    }
}
