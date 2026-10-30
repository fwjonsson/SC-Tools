using System.Text.Json;
using SCTools.Tests.Support;

namespace SCTools.Tests.Tracking;

/// <summary>
/// A Red Wind contract from 2 to 4 stations to a station other than Ruin (stage 1a of docs/planner-any-hauling.md; zzoega's "Master | Stellar Large Haul |
/// to Stanton Gateway" of 26 Oct 2026): the planner's own contract, in the session, on the Stops board with its station as the drop-off, in the Contracts
/// tab and the runs, and planned by the Route Planner with the to-Ruin contracts: a trip with its batches ends at its station (stage 1b).
/// </summary>
public class StationContractTests
{
    static readonly string ToRuin = GameLogWriter.MissionId(71);
    static readonly string ToStation = GameLogWriter.MissionId(72);
    const string GaslightZone = "900000007101";
    const string PatchZone = "900000007102";
    const string RuinZone = "900000007103";
    const string RodsZone = "900000007104";
    const string Title = "Master | Stellar Large Haul | to Rod's Fuel 'N Supplies";

    /// <summary>One player with a 2-stop contract to Ruin and a 2-stop contract to Rod's Fuel, both picking up at Gaslight and Patch City, nothing picked up yet.</summary>
    static GameLogWriter Writer()
    {
        var ruin = string.Format(GameLogWriter.HaulingContract, 2);
        var station = string.Format(GameLogWriter.StationContract, 2);
        return new GameLogWriter().Login(PlayedSession.Player)
            .Minutes(1).Accepted(ToRuin)
            .Marker(ToRuin, ruin, "pickup_r_0", GaslightZone, GameLogWriter.Gaslight)
            .Marker(ToRuin, ruin, "pickup_r_1", PatchZone, GameLogWriter.PatchCity)
            .Marker(ToRuin, ruin, "dropoff_r_0", RuinZone, GameLogWriter.RuinStation)
            .Minutes(1).AcceptedAs(ToStation, Title)
            .Marker(ToStation, station, "pickup_s_0", GaslightZone, GameLogWriter.Gaslight)
            .Marker(ToStation, station, "pickup_s_1", PatchZone, GameLogWriter.PatchCity)
            .Marker(ToStation, station, "dropoff_s_0", RodsZone, GameLogWriter.RodsFuel)
            .DeliverPopup(ToStation, "dropoff_s_0", 520, "Waste", "Rod's Fuel 'N Supplies");
    }

    static Tracker Load(GameLogWriter w)
    {
        var t = new Tracker(PlayedSession.Data(), _ => { });
        t.Load(new[] { w.Read() });
        t.Select(t.State.Sessions[^1]);
        t.LoadStops();
        t.LoadRuns();
        return t;
    }

    [Fact]
    public void A_contract_to_another_station_is_a_hauling_contract_with_its_cargo_stops_and_drop_off()
    {
        var log = HaulingLog.Merge(new[] { Writer().Read() });   // merged, as the tracker reads it: the flag survives the merge
        var m = log.Missions[ToStation];
        Assert.True(m.IsHauling);
        Assert.True(m.ToStation);
        Assert.False(m.ToRuin);
        Assert.False(m.IsOut);
        Assert.Equal(("Waste", 2, Title), (m.Cargo, m.Multi, m.Title));
        Assert.Equal(520, m.ObjScu["dropoff_s_0"]);
        Assert.Equal((RodsZone, "Rod's Fuel 'N Supplies"), (SessionBuilder.DropZoneOf(m), SessionBuilder.DropNameOf(m)));
        Assert.Equal("2 To Station", Contracts.ContractName(m));
        var r = log.Missions[ToRuin];
        Assert.True(r.ToRuin);
        Assert.Null(SessionBuilder.DropNameOf(r));   // no popup for it in this log
        Assert.Equal("2 To Ruin", Contracts.ContractName(r));
    }

    [Fact]
    public void Its_stops_are_rows_of_their_own_with_the_station_as_the_drop_off_and_the_planner_takes_its_batches_to_the_station()
    {
        var t = Load(Writer());
        Assert.Equal(new[] { ToRuin, ToStation }, t.State.Current!.Missions.Select(m => m.Id).OrderBy(x => x));   // both are in the session
        var rows = t.State.PickupRows;
        Assert.Equal(4, rows.Count);   // Gaslight and Patch City, each once to Ruin and once to Rod's Fuel
        var gaslight = rows.Where(r => r.Zone == GaslightZone).OrderBy(r => r.ToRuin ? 0 : 1).ToList();
        Assert.Equal(new[] { true, false }, gaslight.Select(r => r.ToRuin));
        Assert.Equal((RodsZone, "Rod's Fuel 'N Supplies", 1), (gaslight[1].DropZone, gaslight[1].DropName, gaslight[1].Missions));
        Assert.Null(gaslight[0].DropZone);

        // the Stops board: the station in the Drop-off Location column; the Other hauling board does not list the contract (it is the planner's own)
        var d = JsonDocument.Parse(WebSnapshot.Json(t, new WebSnapshotOptions())).RootElement;
        var cells = d.GetProperty("pickups").GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("cells").EnumerateArray().Select(c => c.GetString()!).ToArray()).ToList();
        Assert.Equal(2, cells.Count(c => c[1] == "Gaslight"));
        Assert.Equal(new[] { "Rod's Fuel 'N Supplies", PyroDistances.NameOf(PyroDistances.Ruin) }, cells.Where(c => c[1] == "Gaslight").Select(c => c[6]).OrderBy(x => x));
        Assert.Empty(d.GetProperty("jobs").GetProperty("rows").EnumerateArray());
        // the Contracts tab has it by its name, paying what a 2-stop contract pays (no library here)
        var rewards = d.GetProperty("rewards").GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("cells")[0].GetString()).ToList();
        Assert.Contains(Title, rewards);

        // the old planner (the Route Planner tab) plans the two batches for Ruin and says so of the other two
        var oldPlan = t.Plan.GetPlan();
        Assert.Equal(2, t.Plan.Core!.Batches);
        Assert.Contains("Not planned here (the drop-off is not Ruin; the Route Planner (new) plans them): ", oldPlan.Note);
        Assert.Contains("Gaslight \u00d71 to Rod's Fuel 'N Supplies", oldPlan.Note);
        Assert.Contains("Patch City \u00d71 to Rod's Fuel 'N Supplies", oldPlan.Note);
        Assert.All(t.Plan.Core.Trips.SelectMany(x => x.Picks), p => Assert.Equal(PyroDistances.Ruin, p.To));
        // the planner without a hub (the Route Planner (new) tab) plans all four: the two for Rod's Fuel in a trip that ends there, the two for Ruin in one that ends at Ruin (stage 1b)
        var plan = t.FreePlan.GetPlan();
        Assert.Equal(4, t.FreePlan.Core!.Batches);
        Assert.DoesNotContain("Not planned", plan.Note);
        var trips = t.FreePlan.Core.Trips;
        Assert.NotEmpty(trips.Where(x => x.Ends.Contains(PlayedSession.RodsFuel)));
        Assert.Equal(2, trips.Sum(x => x.Picks.Where(p => p.To == PlayedSession.RodsFuel).Sum(p => p.Batches)));
        Assert.Equal(2, trips.Sum(x => x.Picks.Where(p => p.To == PyroDistances.Ruin).Sum(p => p.Batches)));
        Assert.All(trips, x => Assert.Equal(x.Picks.Select(p => p.To).Distinct().OrderBy(s => s), x.Ends.OrderBy(s => s)));   // a trip ends where its cargo goes
        Assert.All(trips, x => Assert.Equal(x.Picks.Sum(p => p.Batches), x.Batches));
        var worker = Assert.Single(plan.Workers);
        var shown = worker.Rows.Where(r => !r.Hidden).ToList();
        Assert.Contains(shown, r => r.Route.Contains("Rod's Fuel 'N Supplies", StringComparison.Ordinal));
        Assert.All(shown, r => Assert.Equal(r.Places[^1], r.Lines[^1].Id));   // the last line of the Current Route is where the trip ends
        var station = shown.First(r => r.Trip != null && r.Trip.Ends.Contains(PlayedSession.RodsFuel));
        Assert.Contains("Deliver", station.Lines.Last(l => l.Id == PlayedSession.RodsFuel).Actions);
        Assert.Equal("Gaslight,  Patch City", t.Contracts.ContractStopsText(t.State.Log!.Missions[ToStation]));
    }

    [Fact]
    public void Picking_up_and_handing_in_at_the_station_is_a_run_and_finishes_the_contract()
    {
        var w = Writer()
            .Minutes(19).Unstow().Seconds(5).Objective(ToStation, "pickup_s_0", "COMPLETED")
            .Minutes(15).Unstow().Seconds(5).Objective(ToStation, "pickup_s_1", "COMPLETED")
            .Minutes(15).LoadingPlatform().Seconds(5).Objective(ToStation, "dropoff_s_0", "COMPLETED").Seconds(1).MissionEnded(ToStation, "COMPLETED");
        var t = Load(w);
        var run = Assert.Single(t.State.Trips);
        Assert.Equal((PlayedSession.Player, 1, true, RodsZone), (run.Worker, run.HandedIn, run.End.HasValue, run.EndZone));   // handed in at the station
        Assert.Equal(new[] { GaslightZone, PatchZone }, run.Pickups.OrderBy(p => p.Time).Select(p => p.Zone));
        Assert.Equal(PlayedSession.RodsFuel, t.Whereabouts.AutoLocationOf(PlayedSession.Player));   // and he is there now: his next trip starts there
        Assert.Equal(RoutePlanner.PathGm(new[] { PyroDistances.Ruin, PlayedSession.Gaslight, PlayedSession.PatchCity, PlayedSession.RodsFuel }), CargoRuns.RunDistance(run, t.Places.StationOf), 3);
        Assert.Equal(2, t.State.PickupRows.Count);   // the completed contract left the boards; the to-Ruin stops are still waiting
        Assert.All(t.State.PickupRows, r => Assert.True(r.ToRuin));
        Assert.Equal(Earnings.DefaultReward2, t.Earnings.Earned());   // the contract paid like a 2-stop contract (the library would say otherwise by its name)
    }
}
