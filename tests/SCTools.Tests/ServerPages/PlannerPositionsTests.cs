using System.Globalization;
using SCTools.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using SCTools.Server;

namespace SCTools.Tests.ServerPages;

/// <summary>The map and the Hauling Planner learn from each other: where the map has a worker reaches the planner's Current location (PlannerPositions).</summary>
public class PlannerPositionsTests
{
    static string Id(DateTime t) => t.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    [Fact]
    public async Task A_crew_members_jump_in_the_pilots_log_places_him_for_the_planner_too()
    {
        using var f = new ServerFactory();
        var now = DateTime.UtcNow;
        // zzoega flies his C2 to Gaslight with zzatvo aboard: only zzoega's log has the destination, both logs have the arrival of the same ship
        var pilot = new GameLogWriter().At(now.AddMinutes(-20)).Login("zzoega").Minutes(2).QuantumSelected("rs_ext_pyro5_l2").Minutes(4).QuantumArrived();
        var crew = new GameLogWriter().At(now.AddMinutes(-20)).Login("zzatvo").Minutes(6).QuantumArrived();
        Assert.True(f.Live.Uploads.Add("zzoega", Id(now.AddMinutes(-20)), 0, 1000, pilot.Lines.ToList(), now).Ok);
        Assert.True(f.Live.Uploads.Add("zzatvo", Id(now.AddMinutes(-20)), 0, 1000, crew.Lines.ToList(), now).Ok);
        f.Live.Refresh(now);
        var positions = f.Services.GetRequiredService<PlannerPositions>();
        positions.Push(now, force: true);   // the server's refresh loop runs in the test host too and may have given them already: either way they are there now
        Assert.Equal(PlayedSession.Gaslight, f.Live.Read(t => t.Whereabouts.AutoLocationOf("zzoega")));
        Assert.Equal(PlayedSession.Gaslight, f.Live.Read(t => t.Whereabouts.AutoLocationOf("zzatvo")));   // the crew member: his own log has no destination
        Assert.Equal(PlayedSession.Gaslight, f.Live.Read(t => t.Plan.GetPlan().Workers.Single(w => w.Name.Equals("zzatvo", StringComparison.OrdinalIgnoreCase)).LocationId));
        Assert.False(positions.Push(now.AddSeconds(10)));   // nothing changed: nothing to give
        f.Live.Refresh(now.AddSeconds(1));
        Assert.Contains("Current location: Gaslight", f.Live.Json);   // the Trip Planner says so
    }
}
