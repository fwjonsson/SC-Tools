using SCTools.Tests.Support;

namespace SCTools.Tests.Tracking;

/// <summary>
/// A party hauling in Stanton alone (stage 3 of docs/planner-any-hauling.md): its hauling job starts the session, and the planner without a hub plans it from the
/// session's hub (the place its parcels go to) when nobody knows where the worker is; the old planner has nothing to plan and keeps everybody at Ruin.
/// </summary>
public class StantonSessionTests
{
    static readonly string Job = GameLogWriter.MissionId(91);
    const string Contract = "HaulCargo_AToB_Stanton_Small_FoodSupplies";
    const string Pickup = "stanton/cru-l4-shallow-fields-station";
    const string Drop = "stanton/port-tressler";

    static Tracker Load()
    {
        var w = new GameLogWriter().Login(PlayedSession.Player).Minutes(1).AcceptedAs(Job, "Local Haul")
            .Marker(Job, Contract, "pickup_sa_0", "860000009101", (-18119.931151, 180393.511132, -232742.694013))
            .Marker(Job, Contract, "dropoff_sb_0", "860000009102", (-18119.931151, 180393.511132, -232742.694013))
            .ObjectivePopup(Job, "pickup_sa_0", "Collect Food Supplies From Shallow Fields Station at Crusader\u0027s L4 Lagrange point")
            .DeliverPopup(Job, "dropoff_sb_0", 60, "Food Supplies", "Port Tressler")
            .Minutes(5).Noise();
        var t = new Tracker(PlayedSession.Data(), _ => { });
        t.Load(new[] { w.Read() });
        t.Select(t.State.Sessions[^1]);   // the session started by hand, with no Red Wind contract in it
        t.LoadStops();
        t.LoadRuns();
        return t;
    }

    [Fact]
    public void The_job_starts_a_session_and_the_new_planner_plans_it_from_the_hub()
    {
        var t = Load();
        var (starts, _, going) = AutoSessions.HaulingActivity(t.State.Log!, GameLogWriter.Start.AddMinutes(10));
        Assert.Single(starts);   // the job alone starts a session
        Assert.True(going);
        Assert.Empty(t.State.OpenRows);   // no Red Wind contract: the Stops board is empty, the job is on the Other hauling board
        Assert.Single(HaulJobs.Open(t.State));

        var old = t.Plan.GetPlan();
        Assert.Equal(0, t.Plan.Core!.Batches);
        Assert.Equal(PyroDistances.Ruin, old.Workers.Single().LocationId);   // the old planner: Ruin, as always

        var plan = t.FreePlan.GetPlan();
        Assert.Equal(1, t.FreePlan.Core!.Batches);
        var w = plan.Workers.Single();
        Assert.Equal(Drop, w.LocationId);   // nobody knows where he is: the session's hub, where its parcel goes
        Assert.False(w.LocationByHand);
        var trip = Assert.Single(t.FreePlan.Core.Trips);
        Assert.Equal(new[] { Drop, Pickup, Drop }, trip.Places());   // from the hub to the pickup and back to the drop-off
        Assert.Equal(StarRoutes.PathGm(trip.Places()), trip.Distance, 3);
        var row = w.Rows.Single(r => !r.Hidden);
        Assert.Equal("Port Tressler  \u2192  CRU-L4 Shallow Fields Station  \u2192  Port Tressler", row.Route);
        Assert.Equal("", plan.Note);
    }
}
