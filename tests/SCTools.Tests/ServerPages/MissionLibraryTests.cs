using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using SCTools.Server;
using SCTools.Tests.Support;

namespace SCTools.Tests.ServerPages;

/// <summary>
/// The Mission Library: every kind of mission the players took is there by itself (live logs and old ones); a logged in player types in the rewards, the
/// giver and notes, saved one field at a time; the Wiki fills in what it knows.
/// </summary>
public class MissionLibraryTests
{
    const string C1 = "26b8773e-0b96-40b4-a33a-27a4e9877e75";
    const string C2 = "8d8fc20d-61ad-4433-a3f1-948d8e05104a";

    static string[] Mission(string mission, string contract, string title, string day = "2026-10-21") => new[]
    {
        $"<{day}T08:00:00.000Z> [Notice] <SHUDEvent_OnNotification> Added notification \"Contract Accepted: {title}: \" [84] to queue. New queue size: 1, MissionId: [{mission}], ObjectiveId: [] [Missions][Comms]",
        $"<{day}T08:00:00.500Z> [Notice] <CSCPlayerMissionLog::MissionStartCommsNotification> MissionStart comms notification will not be sent - This mission has no MissionStart comms setup. ContractId: [{contract}]. MissionId: {mission} [Missions][Comms]",
        $"<{day}T08:30:00.000Z> [Notice] <SHUDEvent_OnNotification> Added notification \"Contract Complete: {title}: \" [84] to queue. New queue size: 1, MissionId: [{mission}], ObjectiveId: [] [Missions][Comms]",
    };

    sealed class Wiki : IMissionWiki
    {
        public Task<WikiMission?> FindAsync(string contractId, CancellationToken ct) =>
            Task.FromResult(contractId == C1 ? new WikiMission("Stellar Large Haul", "Red Wind Linehaul", 20_000, 30_000, "UEC") : null);
    }

    [Fact]
    public async Task Every_mission_taken_is_in_the_library_and_a_player_fills_in_the_rest()
    {
        using var f = new ServerFactory { Wiki = new Wiki() };
        var admin = await f.Admin();
        f.Upload("zzoega", Mission("11111111-1111-1111-1111-111111111111", C1, "Senior | Stellar Large Haul [BP]")
            .Concat(Mission("22222222-2222-2222-2222-222222222222", C1, "Senior | Stellar Large Haul [BP]")).ToArray());

        var page = await admin.GetStringAsync("/missions");   // only the admin until the tool is shown
        Assert.Contains("value=\"Senior | Stellar Large Haul [BP]\"", page);   // the title from the log, his string mod's name
        Assert.Contains(">2 / 2<", page);                                        // completed of taken
        Assert.Contains("not asked yet", page);

        var player = await f.Player(f.AddKey("bileh123"));
        Assert.Equal(HttpStatusCode.Redirect, (await player.GetAsync("/missions")).StatusCode);
        await ServerFactory.Send(admin, "/admin/tools", "/admin/tools/set", ("id", "missions"), ("value", "true"));

        var saved = await ServerFactory.Send(player, "/missions", "/missions/set", ("id", C1), ("field", "auec"), ("value", "25,000"), ("back", "/missions?show=todo"));
        Assert.Equal("/missions?show=todo#m-" + C1, saved.Headers.Location?.OriginalString);
        await ServerFactory.Send(player, "/missions", "/missions/set", ("id", C1), ("field", "council"), ("value", "12"));
        await ServerFactory.Send(player, "/missions", "/missions/set", ("id", C1), ("field", "giver"), ("value", "Red Wind"));
        await ServerFactory.Send(player, "/missions", "/missions/set", ("id", C1), ("field", "mg"), ("value", "lots"));   // not a number: not saved
        var entry = f.Services.GetRequiredService<MissionLibrary>().Entries().Single();
        Assert.Equal((25_000L, 12L, (long?)null, "Red Wind", "bileh123"), (entry.Row.Auec!.Value, entry.Row.CouncilScrip!.Value, entry.Row.MgScrip, entry.Row.Giver, entry.Row.EditedBy));
        Assert.Contains("Not saved", await player.GetStringAsync("/missions"));
        Assert.DoesNotContain(C1, await player.GetStringAsync("/missions?show=todo"));   // it has rewards now

        Assert.Equal(1, await f.Services.GetRequiredService<MissionLibrary>().FillFromWikiAsync(10, CancellationToken.None));
        Assert.Contains("Red Wind Linehaul", await player.GetStringAsync("/missions"));
        Assert.Equal(0, await f.Services.GetRequiredService<MissionLibrary>().FillFromWikiAsync(10, CancellationToken.None));   // asked once
    }

    [Fact]
    public async Task The_missions_of_the_old_logs_are_in_it_too_also_when_they_came_before_the_library()
    {
        using var f = new ServerFactory();
        var admin = await f.Admin();
        var tracker = f.Tracker(f.AddKey("zzoega"));
        var lines = new GameLogWriter().At(new DateTime(2026, 9, 1, 7, 0, 0, DateTimeKind.Utc)).Login("zzoega").Lines
            .Concat(Mission("33333333-3333-3333-3333-333333333333", C2, "Stop Rival Attack", "2026-09-01")).ToArray();
        var r = await tracker.PostAsJsonAsync("/api/history", new { file = "2026-09-01T07:00:00.000Z", from = 0, lines, done = true });
        r.EnsureSuccessStatusCode();
        Assert.Contains("value=\"Stop Rival Attack\"", await admin.GetStringAsync("/missions"));

        var library = f.Services.GetRequiredService<MissionLibrary>();
        using (var db = f.Store.Open())
        {
            db.Missions.RemoveRange(db.Missions);   // as before the library existed: only the old log on the server
            db.SaveChanges();
        }
        Assert.Equal(1, library.ReadKeptHistory());
        Assert.Equal(1, library.Entries().Single(e => e.Row.ContractId == C2).Taken);
        Assert.Equal(0, library.ReadKeptHistory());   // once
    }
}
