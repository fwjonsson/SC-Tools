using System.Text.Json;
using SCTools.Tests.Support;

namespace SCTools.Tests.Tracking;

/// <summary>
/// A hauling job with its ends in two systems (stage 2 of docs/planner-any-hauling.md; Frank's Covalex master mission: a pickup in Stanton, several drop-offs in
/// Pyro) is planned by the planner without a hub (the Route Planner (new) tab) with the Pyro contracts, through the gateways; the old planner leaves it to the board.
/// </summary>
public class InterstellarJobTests
{
    static readonly string Job = GameLogWriter.MissionId(81);
    static readonly string Unplaced = GameLogWriter.MissionId(82);
    const string Contract = "HaulCargo_SingleToMulti2_Interstellar_Large_FoodSupplies";
    const string PickupZone = "860000008101";
    const string OrbituaryZone = "860000008102";
    static readonly (double X, double Y, double Z) InStanton = (-18119.931151, 180393.511132, -232742.694013);   // a marker far from every Pyro station
    static readonly (double X, double Y, double Z) Orbituary = (379850, -718724, 226044);   // the station's own position (StationIds)
    const string Pickup = "stanton/cru-l4-shallow-fields-station";

    /// <summary>The played session (a run on the way with a batch left at Patch City), and the job: 100 SCU from CRU-L4 to Orbituary and Gaslight, 50 each.</summary>
    static (Tracker Tracker, SaveCounter Saves) Load(bool unplaced = false) => PlayedSession.Load(null, w =>
    {
        w.Minutes(1).AcceptedAs(Job, "Master Hauler Needed for Large Shipment")
            .Marker(Job, Contract, "pickup_ja_0", PickupZone, InStanton)
            .Marker(Job, Contract, "dropoff_jb_0", OrbituaryZone, Orbituary)
            .Marker(Job, Contract, "dropoff_jc_0", PlayedSession.GaslightZone, GameLogWriter.Gaslight)
            .ObjectivePopup(Job, "pickup_ja_0", "Collect Food Supplies From Shallow Fields Station at Crusader's L4 Lagrange point")
            .DeliverPopup(Job, "dropoff_jb_0", 50, "Food Supplies", "Orbituary")
            .DeliverPopup(Job, "dropoff_jc_0", 50, "Food Supplies", "Gaslight");
        if (unplaced)
            w.Minutes(1).AcceptedAs(Unplaced, "Covalex Haul")   // a pickup the game does not name anywhere: no place of the map
                .Marker(Unplaced, Contract, "pickup_ua_0", "860000008201", InStanton)
                .Marker(Unplaced, Contract, "dropoff_ub_0", OrbituaryZone, Orbituary)
                .DeliverPopup(Unplaced, "dropoff_ub_0", 40, "Food Supplies", "Orbituary");
    });

    [Fact]
    public void The_job_is_on_the_board_with_its_places_and_the_new_planner_plans_it_through_the_gateway()
    {
        var (t, _) = Load();
        var job = Assert.Single(HaulJobs.Open(t.State));
        Assert.Equal((Pickup, 100.0, ScuFrom.Game), (job.Pickups.Single().Place?.Key, job.Scu, job.ScuFrom));
        Assert.Equal(new[] { "pyro/gaslight", "pyro/orbituary" }, job.Drops.Select(d => d.Place?.Key).OrderBy(x => x));

        // the old planner (the Route Planner tab) plans the batch to Ruin alone
        t.Plan.GetPlan();
        Assert.Equal(1, t.Plan.Core!.Batches);
        Assert.Contains("Not planned here (the drop-off is not Ruin; the Route Planner (new) plans them): Shallow Fields Station at Crusader's L4 Lagrange point ×1 to Orbituary, Gaslight.", t.Plan.GetPlan().Note);   // the job is in the session since stage 4: the old planner says so

        // the planner without a hub plans the two parcels too: from CRU-L4 to Orbituary and to Gaslight, through the Stanton and Pyro gateways
        var plan = t.FreePlan.GetPlan();
        Assert.Equal(3, t.FreePlan.Core!.Batches);
        Assert.Equal("", plan.Note);
        var picks = t.FreePlan.Core.Trips.SelectMany(x => x.Picks).Where(p => p.Id == Pickup).ToList();
        Assert.Equal(new[] { PlayedSession.Gaslight, "73041" }, picks.Select(p => p.To).OrderBy(x => x));
        Assert.All(picks, p => Assert.Equal(1, p.Batches));
        var trip = t.FreePlan.Core.Trips.Single(x => x.Picks.Any(p => p.Id == Pickup));
        Assert.Contains(Pickup, trip.Places());
        Assert.Equal(StarRoutes.PathGm(trip.Places()), trip.Distance, 3);   // the legs through the gateways, the crossing's time in the seconds
        Assert.Equal(StarRoutes.PathSeconds(t.Crew.QtOf(PlayedSession.Player), trip.Places()), trip.Seconds, 3);
        var row = plan.Workers.Single().Rows.Single(r => r.Trip == trip);
        Assert.Contains("⇢", row.Route);   // a dashed arrow for the leg through a gateway
        Assert.Contains("CRU-L4 Shallow Fields Station", row.Route);
        var into = row.Lines.First(l => l.Id == Pickup);
        Assert.True(into.Gate);
        Assert.Contains("Gm + jump", string.Join("|", PlanText.LineCells(row.Lines).Single(c => c[1] == "CRU-L4 Shallow Fields Station")));   // the Distance cell: the jumps' Gm and the crossing
        Assert.True(into.Seconds > StarRoutes.GateSeconds);

        // the page's data: every system's map, and the places of the routes with their system
        var d = JsonDocument.Parse(WebSnapshot.Json(t, new WebSnapshotOptions())).RootElement.GetProperty("freePlanner");
        Assert.Equal(new[] { "pyro", "stanton", "nyx" }, d.GetProperty("maps").EnumerateArray().Select(m => m.GetProperty("id").GetString()));
        var place = d.GetProperty("places").EnumerateArray().Single(p => p.GetProperty("id").GetString() == Pickup);
        Assert.Equal((Pickup, "CRU-L4 Shallow Fields Station", "stanton"), (place.GetProperty("key").GetString(), place.GetProperty("name").GetString(), place.GetProperty("system").GetString()));
        Assert.Contains(d.GetProperty("places").EnumerateArray(), p => p.GetProperty("id").GetString() == PyroDistances.Ruin && p.GetProperty("key").GetString() == "pyro/ruin-station");
    }

    [Fact]
    public void A_job_with_a_stop_the_map_has_no_place_for_stays_out_with_a_note()
    {
        var (t, _) = Load(unplaced: true);
        var plan = t.FreePlan.GetPlan();
        Assert.Equal(3, t.FreePlan.Core!.Batches);   // the placed job is planned, the other is not
        Assert.Contains("Not planned (the map has no place for a stop): Covalex Haul (", plan.Note);
        Assert.DoesNotContain("Master Hauler", plan.Note);
    }
}
