using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SCTools.Server;
using SCTools.Tests.Support;

namespace SCTools.Tests.ServerPages;

/// <summary>
/// The hauling jobs of every system on the Hauling Planner (30 Oct 2026: on the Stops board, since the Other hauling board went): a job with its places is a
/// row of the Stops board and planned, and the hauler types the SCU of a job's drop-off in the SCU column of his Current Route, as for a load from Ruin; the
/// job has it, the boxes follow, and the Mission Library gets it as the suggestion for the kind, like a number the game said.
/// </summary>
public class HaulJobsPageTests
{
    const string Haul = "HaulCargo_AToB_Stanton_Small_MedicalSupplies";
    const string Title = "Local Medical Haul";
    static readonly (double, double, double) Far = (-18119.931151, 180393.511132, -232742.694013);   // a marker far from every Pyro station

    static GameLogWriter TheHaul(GameLogWriter w, string m) =>
        w.AcceptedAs(m, Title).Marker(m, Haul, "pickup_f1_0", "862737938329", Far).Marker(m, Haul, "dropoff_d1_0", "862738093314", Far)
            .ObjectivePopup(m, "pickup_f1_0", "Collect Medical Supplies From Shallow Fields Station at Crusader's L4 Lagrange point")
            .ObjectivePopup(m, "dropoff_d1_0", "Deliver Medical Supplies To Port Tressler");

    static async Task<List<string?[]>> Stops(ServerFactory f, HttpClient c)
    {
        f.Live.Refresh(DateTime.UtcNow);
        var data = JsonDocument.Parse(await c.GetStringAsync("/data.json")).RootElement;
        Assert.False(data.TryGetProperty("jobs", out _));   // no Other hauling board any more
        return data.GetProperty("pickups").GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("cells").EnumerateArray().Select(c => c.GetString()).ToArray()).ToList();
    }

    [Fact]
    public async Task A_job_is_on_the_Stops_board_and_the_hauler_types_the_SCU_of_its_drop_off_on_his_route()
    {
        using var f = new ServerFactory { FollowSessions = true };   // the job starts the session
        var m = GameLogWriter.MissionId(1);
        var w = new GameLogWriter().At(DateTime.UtcNow.AddMinutes(-10)).Login("zzoega");
        TheHaul(w, m);
        f.Upload("zzoega", w.Lines.ToList());
        f.Live.Refresh(DateTime.UtcNow);
        var admin = await f.Admin();

        // the Stops board: the job's pickup as the map names it, its drop-off, no SCU known yet (the planner guesses a third of a mission)
        var rows = await Stops(f, admin);
        var row = Assert.Single(rows);
        Assert.Equal(("CRU-L4 Shallow Fields Station", "1/1", "Port Tressler"), (row[1], row[2], row[6]));
        Assert.StartsWith("~", row[3]!);

        // zzoega's Current Route ends at Port Tressler: that line carries the drop-off, typable, with no SCU yet
        var plan = f.Live.Read(t => t.Plan.GetPlan());
        var worker = plan.Workers.Single(x => x.Name.Equals("zzoega", StringComparison.OrdinalIgnoreCase));   // the party has more members
        var current = worker.Rows.First(r => !r.Hidden);
        var deliver = current.Lines.Last();
        Assert.Equal(("stanton/port-tressler", true, m + "|dropoff_d1_0", "?"), (deliver.Id, deliver.Typable, Assert.Single(deliver.Keys), deliver.ScuText));
        Assert.Contains("Deliver", deliver.ActionText);

        // he types 60 SCU for it in the SCU column (the Hauling Planner's page has the form token, in its top bar)
        var player = await f.Player(f.AddKey("zzoega"));
        Assert.Contains("__RequestVerificationToken", await player.GetStringAsync("/hauling"));
        var saved = await ServerFactory.Send(player, "/hauling", "/hauling/trips/scu", ("key", m + "|dropoff_d1_0"), ("value", "60"));
        Assert.Equal((System.Net.HttpStatusCode.Redirect, "/hauling/planner"), (saved.StatusCode, saved.Headers.Location?.OriginalString));
        f.Live.Refresh(DateTime.UtcNow);
        plan = f.Live.Read(t => t.Plan.GetPlan());
        deliver = plan.Workers.Single(x => x.Name.Equals("zzoega", StringComparison.OrdinalIgnoreCase)).Rows.First(r => !r.Hidden).Lines.Last();
        Assert.Equal(("60", "32 + 24 + 4"), (deliver.ScuText, deliver.BoxesText));   // the job has it: its SCU and the boxes
        row = Assert.Single(await Stops(f, admin));
        Assert.Equal("~60", row[3]);   // the Stops board too

        // the number is kept for that drop-off, and the library's suggestion for the kind comes from it, like a number the game said
        Assert.Equal(60, f.Live.Read(t => t.State.Data.TypedScu[m + "|dropoff_d1_0"]));
        var library = f.Services.GetRequiredService<MissionLibrary>();
        Assert.Equal(1, library.SuggestFromGame(DateTime.UtcNow));
        Assert.Equal(60L, library.Entries().Single(e => e.Row.Title == Title).Row.ScuSuggested);

        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await player.GetAsync("/hauling/jobs")).StatusCode);   // the old board's address is gone (Frank, 30 Oct 2026)
    }
}
