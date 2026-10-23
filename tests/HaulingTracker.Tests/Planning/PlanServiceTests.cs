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
    public void A_first_stop_with_only_a_drop_off_has_nothing_to_lock()
    {
        Assert.True(PlanService.NothingToLock(new PlannedTrip()));
        Assert.True(PlanService.NothingToLock(new PlannedTrip { Stops = { ("70120", 2) }, Drops = { ("70273", 1) } }));
        Assert.False(PlanService.NothingToLock(new PlannedTrip { Stops = { ("70120", 2) }, Drops = { ("70120", 1) } }));
        Assert.False(PlanService.NothingToLock(new PlannedTrip { Stops = { ("70120", 2) } }));
    }
}
