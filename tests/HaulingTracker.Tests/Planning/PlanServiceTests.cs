using HaulingTracker.Tests.Support;

namespace HaulingTracker.Tests.Planning;

/// <summary>The plan of a played session: the run in progress with what the plan adds to it, locking the next stops, and where the worker colour is.</summary>
public class PlanServiceTests
{
    [Fact]
    public void The_run_in_progress_is_the_current_route_and_every_open_batch_is_planned()
    {
        var (t, _) = PlayedSession.Load();
        var plan = t.Plan.GetPlan();
        var w = Assert.Single(plan.Workers);
        Assert.Equal(PlayedSession.Player, w.Name);
        Assert.Equal(PlayedSession.Gaslight, w.LocationId);   // where his last pickup was
        Assert.False(w.LocationByHand);

        var current = w.Rows.First(r => !r.Hidden);
        Assert.Equal("Current", current.Label);
        Assert.Equal("active", current.Level);
        Assert.Equal("Ruin", current.Lines[0].Name);
        var gaslight = current.Lines.First(l => l.Name == "Gaslight");
        Assert.Contains("Collected ✓", gaslight.Actions);   // picked up already
        Assert.True(gaslight.Done);
        Assert.Equal("Ruin", current.Lines[^1].Name);
        Assert.True(current.Here > 0);   // he is past the start: on his way to the next step

        // the batch still waiting at Patch City is in a trip of his
        Assert.Contains(w.Rows.SelectMany(r => r.Lines), l => l.Name == "Patch City" && l.Batches == 1);
        Assert.False(string.IsNullOrEmpty(plan.Signature));
    }

    [Fact]
    public void The_plan_is_worked_out_once_until_something_changes()
    {
        var (t, _) = PlayedSession.Load();
        t.Plan.GetPlan();
        var core = t.Plan.Core;
        Assert.NotNull(core);
        t.Plan.GetPlan();
        Assert.Same(core, t.Plan.Core);   // the same batches, crew and locks: the same trips
        t.Plan.Invalidate();
        Assert.Null(t.Plan.Core);
        Assert.Equal(t.Plan.GetPlan().Signature, t.Plan.GetPlan().Signature);
    }

    [Fact]
    public void Locked_next_stops_are_kept_for_the_worker_until_they_are_unlocked()
    {
        var (t, saves) = PlayedSession.Load();
        var inp = t.State.Inputs!;
        var before = saves.Count;
        t.Plan.LockNextStops();
        Assert.True(inp.NextStopsLocked);
        Assert.True(saves.Count > before);
        Assert.Contains(inp.LockedTrips.Keys, k => k == PlayedSession.Player);   // he is flying a run: the next stop of its continuation gets locked when the plan is shown

        t.Plan.Unlock();
        Assert.False(inp.NextStopsLocked);
        Assert.Empty(inp.LockedTrips);
    }

    [Fact]
    public void The_worker_colour_is_on_the_step_he_is_doing()
    {
        StopLine Done(string id, bool busy = false) => new() { Id = id, Name = id, Done = true, Busy = busy };
        StopLine Next(string id) => new() { Id = id, Name = id };

        var lines = new List<StopLine> { Done("Ruin"), Done("A"), Next("B"), Next("Ruin") };
        Assert.Equal(2, PlanService.HereLine(lines, "A", false));   // A is done: he is on his way to B
        lines[1].Busy = true;
        Assert.Equal(1, PlanService.HereLine(lines, "A", false));   // still loading at A
        Assert.Equal(0, PlanService.HereLine(new List<StopLine> { Next("Ruin"), Next("A") }, "Ruin", false));   // nothing done yet
        Assert.Equal(2, PlanService.HereLine(new List<StopLine> { Done("Ruin"), Next("A"), Next("B"), Next("Ruin") }, "B", true));   // set by hand: at B
        Assert.Equal(-1, PlanService.HereLine(new List<StopLine>(), "Ruin", false));
    }

    const string Endgame = "70273";

    static PlannedTrip Trip(string worker, string[] stops, string? waste = null, bool special = false, double seconds = 0)
    {
        var t = new PlannedTrip { Worker = worker, Special = special, Seconds = seconds };
        foreach (var id in stops) t.Stops.Add((id, 2));
        if (waste != null) t.Drops.Add((waste, 1));
        return t;
    }

    [Fact]
    public void One_stop_trips_go_first_and_of_those_the_one_that_takes_waste_out()
    {
        var (patch, gas) = (PlayedSession.PatchCity, PlayedSession.Gaslight);
        var flying = Trip("A", new[] { gas }, special: true);
        var oneStop = Trip("A", new[] { patch });
        var oneStopWaste = Trip("A", new[] { patch }, waste: patch);     // waste and pickup at the same station: one landing
        var twoStops = Trip("A", new[] { patch, gas });
        var twoStopsWaste = Trip("A", new[] { gas }, waste: Endgame);    // the waste goes to another station: two landings
        var value = new Dictionary<PlannedTrip, decimal> { [flying] = 1, [oneStop] = 2, [oneStopWaste] = 1.5m, [twoStops] = 9, [twoStopsWaste] = 3 };

        var queue = PlanService.Queue(new[] { twoStops, oneStop, twoStopsWaste, flying, oneStopWaste }, t => value[t]);
        Assert.Equal(new[] { flying, oneStopWaste, oneStop, twoStopsWaste, twoStops }, queue);   // a lot more value does not put two stops ahead of one
    }

    [Fact]
    public void Otherwise_the_trip_that_earns_the_most_per_hour_goes_first()
    {
        var close = Trip("A", new[] { PlayedSession.PatchCity }, seconds: 300);    // 2.0 M for 5 min of flying and two landings: 2.0 M per 35 min
        var far = Trip("A", new[] { PlayedSession.Gaslight }, seconds: 1500);      // 2.2 M for 25 min of flying and two landings: 2.2 M per 55 min
        var value = new Dictionary<PlannedTrip, decimal> { [close] = 2_000_000m, [far] = 2_200_000m };
        Assert.Equal(new[] { close, far }, PlanService.Queue(new[] { far, close }, t => value[t]));
        Assert.Equal(2_000_000.0 * 60 / 35, PlanService.PerHour(close, 2_000_000m), 3);

        // the same: the planner's order. Waste he has taken out already: that trip first, whatever it is
        var (b1, b2) = (Trip("B", new[] { PlayedSession.PatchCity }), Trip("B", new[] { PlayedSession.PatchCity }));
        var (c1, c2) = (Trip("C", new[] { PlayedSession.PatchCity }), Trip("C", new[] { PlayedSession.PatchCity, PlayedSession.Gaslight }, waste: Endgame));
        Assert.Equal(new[] { b1, b2, c2, c1 }, PlanService.Queue(new[] { b1, c1, b2, c2 }, _ => 1m, n => n == "C"));
    }

    [Fact]
    public void A_first_stop_with_only_a_drop_off_has_nothing_to_lock()
    {
        Assert.True(PlanService.NothingToLock(new PlannedTrip()));
        Assert.True(PlanService.NothingToLock(new PlannedTrip { Stops = { ("70120", 2) }, Drops = { ("70273", 1) } }));
        Assert.False(PlanService.NothingToLock(new PlannedTrip { Stops = { ("70120", 2) }, Drops = { ("70120", 1) } }));
        Assert.False(PlanService.NothingToLock(new PlannedTrip { Stops = { ("70120", 2) } }));
    }
}
