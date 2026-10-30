using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SCTools.Server;
using SCTools.Tests.Support;

namespace SCTools.Tests.ServerPages;

/// <summary>
/// The old logs on the server: a tracker hears which it does not have and sends them once; they are kept apart from the live logs, their Gauntlet runs go
/// to the database, and a log of another player on the same PC is noted and not read.
/// </summary>
public class HistoryTests
{
    const string Day1 = "2026-09-30T18:00:00.000Z";
    const string Day2 = "2026-10-01T18:00:00.000Z";
    const string Id = "11111111-1111-1111-1111-111111111111";

    static string[] Gauntlet(string player) => new GameLogWriter().At(new DateTime(2026, 9, 30, 18, 0, 0, DateTimeKind.Utc)).Login(player).Lines.Concat(new[]
    {
        $"<2026-09-30T18:00:10.000Z> [Notice] <SHUDEvent_OnNotification> Added notification \"Contract Accepted:  Combat Gauntlet - Scenario #7: \" [12] to queue. New queue size: 1, MissionId: [{Id}], ObjectiveId: [x]",
        $"<2026-09-30T18:05:10.000Z> [Notice] <SHUDEvent_OnNotification> Added notification \"Contract Complete:  Combat Gauntlet - Scenario #7: \" [12] to queue. New queue size: 1, MissionId: [{Id}], ObjectiveId: [x]",
    }).ToArray();

    static async Task<string[]> Missing(HttpClient tracker, params string[] files)
    {
        var r = await tracker.PostAsJsonAsync("/api/history/missing", new { files });
        r.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("missing").EnumerateArray().Select(e => e.GetString()!).ToArray();
    }

    static Task<HttpResponseMessage> Piece(HttpClient tracker, string file, long from, string[] lines, bool done, string? last = null, string? build = null,
        int? version = HistorySender.Version) =>
        tracker.PostAsJsonAsync("/api/history", new { file, from, lines, done, last, build, version });

    [Fact]
    public async Task An_old_log_is_sent_once_and_its_gauntlet_runs_are_kept()
    {
        using var f = new ServerFactory();
        var tracker = f.Tracker(f.AddKey("zzoega"));
        Assert.Equal(new[] { Day1, Day2 }, await Missing(tracker, Day1, Day2));

        var lines = Gauntlet("zzoega");
        Assert.Equal(HttpStatusCode.OK, (await Piece(tracker, Day1, 0, lines[..2], false)).StatusCode);
        var wrong = await Piece(tracker, Day1, 5, lines[2..], true);   // not where the server is: it says where
        Assert.Equal(HttpStatusCode.Conflict, wrong.StatusCode);
        Assert.Equal(2, JsonDocument.Parse(await wrong.Content.ReadAsStringAsync()).RootElement.GetProperty("at").GetInt64());
        Assert.Equal(HttpStatusCode.OK, (await Piece(tracker, Day1, 2, lines[2..], true, "2026-09-30T21:00:00Z", "9123456")).StatusCode);

        Assert.Equal(new[] { Day2 }, await Missing(tracker, Day1, Day2));   // the next time: only the one it does not have
        var row = Assert.Single(f.Services.GetRequiredService<HistoryStore>().Files("zzoega"));
        Assert.Equal((new DateTime(2026, 9, 30, 18, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 30, 21, 0, 0, DateTimeKind.Utc)), (row.StartUtc, row.EndUtc!.Value));
        Assert.Equal(("9123456", lines.Length, (string?)null), (row.Build, row.Lines, row.Note));
        var run = Assert.Single(f.Services.GetRequiredService<GauntletService>().Runs("zzoega"));   // in the database, for the Gauntlet Tracker
        Assert.Equal((7, 300.0), (run.Scenario, run.TimingSec!.Value));
        Assert.Empty(f.Live.Uploads.Logs);   // not a live log: the Hauling Planner never sees it
    }

    [Fact]
    public async Task An_old_log_a_tracker_sent_before_it_updated_is_asked_for_again()
    {
        // the server reads more of the old logs since 31 Oct 2026 (what a mission paid): a tracker that did not update yet says no version, so what it
        // sends is kept and asked for again once it has; an updated one sends it once
        using var f = new ServerFactory();
        var tracker = f.Tracker(f.AddKey("zzoega"));
        var lines = Gauntlet("zzoega");
        Assert.Equal(HttpStatusCode.OK, (await Piece(tracker, Day1, 0, lines, true, version: null)).StatusCode);
        Assert.Equal(2, Assert.Single(f.Services.GetRequiredService<HistoryStore>().Files("zzoega")).Version);
        Assert.Equal(new[] { Day1 }, await Missing(tracker, Day1));
        var paid = lines.Append("<2026-09-30T18:05:10.600Z> [Notice] <SHUDEvent_OnNotification> Added notification \"You've Earned: 16 Rewards").ToArray();
        Assert.Equal(HttpStatusCode.OK, (await Piece(tracker, Day1, 0, paid, true)).StatusCode);   // the updated tracker sends the reward line too
        Assert.Empty(await Missing(tracker, Day1));
        Assert.Single(f.Services.GetRequiredService<GauntletService>().Runs("zzoega"));   // read twice, kept once
        var contract = f.Services.GetRequiredService<ContractTracker>().Of(new[] { Id })[Id];
        Assert.Equal(16, contract.HolderOf("zzoega")!.Rewards);   // the old contract knows what it paid him
    }

    [Fact]
    public async Task A_log_of_another_player_on_the_same_pc_is_noted_and_not_read()
    {
        using var f = new ServerFactory();
        var tracker = f.Tracker(f.AddKey("zzoega"));
        Assert.Equal(HttpStatusCode.OK, (await Piece(tracker, Day1, 0, Gauntlet("zzatvo"), true)).StatusCode);
        Assert.Equal("zzatvo's game", Assert.Single(f.Services.GetRequiredService<HistoryStore>().Files("zzoega")).Note);
        Assert.Empty(f.Services.GetRequiredService<GauntletService>().Runs("zzoega"));
        Assert.Empty(await Missing(tracker, Day1));   // and it is not asked for again
        Assert.Equal(HttpStatusCode.Unauthorized, (await Piece(f.Tracker("not a key"), Day2, 0, Gauntlet("zzoega"), true)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Piece(tracker, "not a time", 0, Gauntlet("zzoega"), true)).StatusCode);
    }
}
