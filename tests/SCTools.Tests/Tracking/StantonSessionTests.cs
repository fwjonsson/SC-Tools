using System.Text.Json;
using SCTools.Missions;
using SCTools.Tests.Support;

namespace SCTools.Tests.Tracking;

/// <summary>
/// A party hauling in Stanton alone (stages 3 and 4 of docs/planner-any-hauling.md): its hauling job starts the session, is on the Stops board with its places
/// and on the Contracts table by its name, the planner without a hub plans it from the session's hub (the place its parcel goes to) when nobody knows where the
/// worker is, its pickup and delivery are a run, and it pays what the Mission Library says. The old planner has nothing to plan and keeps everybody at Ruin.
/// </summary>
public class StantonSessionTests
{
    static readonly string Job = GameLogWriter.MissionId(91);
    const string Contract = "HaulCargo_AToB_Stanton_Small_FoodSupplies";
    const string Title = "Local Haul";
    const string Pickup = "stanton/cru-l4-shallow-fields-station";
    const string Drop = "stanton/port-tressler";
    const string PickupZone = "860000009101";
    const string DropZone = "860000009102";
    static readonly (double, double, double) Far = (-18119.931151, 180393.511132, -232742.694013);

    static GameLogWriter Writer() => new GameLogWriter().Login(PlayedSession.Player).Minutes(1).AcceptedAs(Job, Title)
        .Marker(Job, Contract, "pickup_sa_0", PickupZone, Far)
        .Marker(Job, Contract, "dropoff_sb_0", DropZone, Far)
        .ObjectivePopup(Job, "pickup_sa_0", "Collect Food Supplies From Shallow Fields Station at Crusader's L4 Lagrange point")
        .DeliverPopup(Job, "dropoff_sb_0", 60, "Food Supplies", "Port Tressler")
        .Minutes(5).Noise();

    /// <summary>The tracker with the session shown (started by hand, no Red Wind contract in it); reward: what the Mission Library says the job's kind pays.</summary>
    static Tracker Load(GameLogWriter w, long? reward = null)
    {
        var t = new Tracker(PlayedSession.Data(), _ => { });
        if (reward != null)
            t.State.Jobs = new JobBook(new Dictionary<string, long>(), new Dictionary<string, JobKind> { [MissionKinds.TitleKey(Title)!] = new JobKind(null, null, null, reward) }, () => Atlas.Default);
        t.Load(new[] { w.Read() });
        t.Select(t.State.Sessions[^1]);
        t.LoadStops();
        t.LoadRuns();
        return t;
    }

    [Fact]
    public void The_job_starts_a_session_and_the_new_planner_plans_it_from_the_hub()
    {
        var t = Load(Writer());
        var (starts, _, going) = AutoSessions.HaulingActivity(t.State.Log!, GameLogWriter.Start.AddMinutes(10));
        Assert.Single(starts);   // the job alone starts a session
        Assert.True(going);
        Assert.Single(HaulJobs.Open(t.State));

        var old = t.Plan.GetPlan();
        Assert.Equal(0, t.Plan.Core!.Batches);
        Assert.Equal(PyroDistances.Ruin, old.Workers.Single().LocationId);   // the old planner: Ruin, as always
        Assert.Contains("Not planned here (the drop-off is not Ruin; the Route Planner (new) plans them): Shallow Fields Station at Crusader's L4 Lagrange point ×1 to Port Tressler.", old.Note);

        var plan = t.FreePlan.GetPlan();
        Assert.Equal(1, t.FreePlan.Core!.Batches);   // once: from the job, not from its row on the Stops board too
        var w = plan.Workers.Single();
        Assert.Equal(Drop, w.LocationId);   // nobody knows where he is: the session's hub, where its parcel goes
        Assert.False(w.LocationByHand);
        var trip = Assert.Single(t.FreePlan.Core.Trips);
        Assert.Equal(new[] { Drop, Pickup, Drop }, trip.Places());   // from the hub to the pickup and back to the drop-off
        Assert.Equal(StarRoutes.PathGm(trip.Places()), trip.Distance, 3);
        var row = w.Rows.Single(r => !r.Hidden);
        Assert.Equal("Port Tressler  →  CRU-L4 Shallow Fields Station  →  Port Tressler", row.Route);
        Assert.Equal("", plan.Note);
    }

    [Fact]
    public void The_job_is_in_the_session_on_the_boards_and_in_the_contracts()
    {
        var t = Load(Writer(), reward: 90_000);
        var m = Assert.Single(t.State.Current!.Missions);
        Assert.Equal((Title, 1, false), (m.Title, m.Parcels, m.IsHauling));
        var r = Assert.Single(t.State.OpenRows);
        Assert.Equal((PickupZone, false, DropZone, "Port Tressler", "Shallow Fields Station at Crusader's L4 Lagrange point", true, 1, 0),
            (r.Zone, r.ToRuin, r.DropZone, r.DropName, r.PickupName, r.Job, r.Missions, r.Loaded));
        Assert.Equal(90_000m, t.Earnings.StopValue(r));   // the library's reward for its kind, one parcel

        var d = JsonDocument.Parse(WebSnapshot.Json(t, new WebSnapshotOptions())).RootElement;
        var stops = d.GetProperty("pickups").GetProperty("rows").EnumerateArray().Select(x => x.GetProperty("cells").EnumerateArray().Select(c => c.GetString()!).ToArray()).ToList();
        var line = Assert.Single(stops);
        Assert.Equal(("CRU-L4 Shallow Fields Station", "1/1", "~60", 90_000m.ToString("N0"), "Port Tressler"), (line[1], line[2], line[3], line[5], line[6]));   // the places as the map has them; the SCU a share of the job's
        var rewards = d.GetProperty("rewards").GetProperty("rows").EnumerateArray().Select(x => x.GetProperty("cells").EnumerateArray().Select(c => c.GetString()!).ToArray()).ToList();
        Assert.Contains(rewards, c => c[0] == Title && c[1] == 90_000m.ToString("N0") && c[2] == "1" && c[3] == "0");   // the Contracts table: the job by its name, going
        Assert.Equal("CRU-L4 Shallow Fields Station 60 SCU", t.Contracts.ContractStopsText(m));
        Assert.Single(d.GetProperty("jobs").GetProperty("rows").EnumerateArray());   // the Other hauling board keeps it too (its SCU field) until the boards have one
    }

    [Fact]
    public void A_run_on_the_way_in_Stanton_starts_at_the_hub_and_goes_to_the_jobs_drop_off()
    {
        // stage 4b: picked up at CRU-L4, not delivered: the run goes to Port Tressler, and on the Route Planner (new) it starts at the hub, not at Ruin
        var t = Load(Writer().Minutes(10).Unstow().Seconds(5).Objective(Job, "pickup_sa_0", "COMPLETED"));
        Assert.Equal(new[] { Drop }, t.Runs.LoadedDestinationsOf(PlayedSession.Player));
        var w = t.FreePlan.GetPlan().Workers.Single();
        var current = w.Rows.First(r => !r.Hidden);
        Assert.Equal("active", current.Level);
        Assert.Equal(new[] { Drop, Pickup, Drop }, current.Lines.Select(l => l.Id));   // the hub, the pickup (done), the drop-off
        Assert.Equal("Port Tressler  \u2192  CRU-L4 Shallow Fields Station  \u2192  Port Tressler", current.Route);
        Assert.Contains("Collected", current.Lines[1].ActionText);
        Assert.Contains("Deliver", current.Lines[2].ActionText);
        Assert.Equal(0, t.FreePlan.Core!.Batches);   // nothing left to plan
        Assert.Equal(PyroDistances.Ruin, t.Plan.GetPlan().Workers.Single().Rows.First(r => !r.Hidden).Lines[0].Id);   // the old planner's run still starts at Ruin
    }

    [Fact]
    public void Picking_up_and_delivering_the_job_is_a_run_that_pays_the_librarys_reward()
    {
        var w = Writer()
            .Minutes(10).Unstow().Seconds(5).Objective(Job, "pickup_sa_0", "COMPLETED")
            .Minutes(20).LoadingPlatform().Seconds(5).Objective(Job, "dropoff_sb_0", "COMPLETED").Seconds(1).MissionEnded(Job, "COMPLETED");
        var t = Load(w, reward: 90_000);
        var run = Assert.Single(t.State.Trips);
        Assert.Equal((PlayedSession.Player, 1, true, DropZone), (run.Worker, run.HandedIn, run.End.HasValue, run.EndZone));
        Assert.Equal(PickupZone, Assert.Single(run.Pickups).Zone);
        Assert.Equal(90_000m, t.Earnings.RunValue(run));
        Assert.Equal(90_000m, t.Earnings.Earned());   // the session earned it
        Assert.Empty(t.State.OpenRows);   // the mission is completed: off the Stops and Finished stops boards, like a completed contract (the Cargo runs keep it)
        var done = Assert.Single(t.State.Rows);
        Assert.Equal((1, 1, true), (done.Delivered, done.Missions, done.DeliveredTime.HasValue));
        Assert.Equal(0, t.FreePlan.GetPlan().Workers.Single().Rows.Count(x => x.Level == "plan"));   // nothing left to plan
        var d = JsonDocument.Parse(WebSnapshot.Json(t, new WebSnapshotOptions())).RootElement;
        var runs = Assert.Single(d.GetProperty("runs").GetProperty("rows").EnumerateArray()).GetProperty("cells").EnumerateArray().Select(c => c.GetString()!).ToArray();
        Assert.Equal(PlayedSession.Player, runs[1]);   // the Cargo runs board has the run
        Assert.Contains("CRU-L4 Shallow Fields Station", runs[2]);
    }
}
