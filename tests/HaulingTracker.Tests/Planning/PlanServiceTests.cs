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

    [Fact]
    public void The_most_valuable_trip_goes_first_after_the_one_he_is_flying()
    {
        PlannedTrip Trip(string worker, bool special = false, bool waste = false)
        {
            var t = new PlannedTrip { Worker = worker, Special = special, Stops = { (PlayedSession.PatchCity, 1) } };
            if (waste) t.Drops.Add((PlayedSession.PatchCity, 1));
            return t;
        }
        var (a1, a2, a3) = (Trip("A"), Trip("A"), Trip("A", special: true));
        var (b1, b2) = (Trip("B"), Trip("B"));
        var (c1, c2) = (Trip("C"), Trip("C", waste: true));
        var value = new Dictionary<PlannedTrip, decimal> { [a1] = 1, [a2] = 3, [a3] = 0.5m, [b1] = 2, [b2] = 2, [c1] = 5, [c2] = 1 };

        var queue = PlanService.Queue(new[] { a1, b1, a3, a2, c1, b2, c2 }, t => value[t], n => n == "C");
        // A: the run he is flying, then the most valuable. B: worth the same, the planner's order. C has waste aboard: the trip that takes it out first
        Assert.Equal(new[] { a3, a2, a1, b1, b2, c2, c1 }, queue);
        Assert.Equal(new[] { a2, a1 }, PlanService.Queue(new[] { a1, a2 }, t => value[t]));
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
