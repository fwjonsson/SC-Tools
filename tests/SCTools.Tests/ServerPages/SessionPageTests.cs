using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using SCTools.Server;
using SCTools.Tests.Support;

namespace SCTools.Tests.ServerPages;

/// <summary>
/// The Session tool: your games (live and from the old logs) and what your completed missions earned, as the Mission Library says they pay, worked out
/// again every time; the clock of the game you play now.
/// </summary>
public class SessionPageTests
{
    const string Contract = "26b8773e-0b96-40b4-a33a-27a4e9877e75";

    static string T(DateTime utc) => utc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    static string[] Mission(string mission, DateTime accepted, DateTime done) => new[]
    {
        $"<{T(accepted)}> [Notice] <SHUDEvent_OnNotification> Added notification \"Contract Accepted: Stop Rival Attack: \" [84] to queue. New queue size: 1, MissionId: [{mission}], ObjectiveId: [] [Missions][Comms]",
        $"<{T(accepted)}> [Notice] <CSCPlayerMissionLog::MissionStartCommsNotification> MissionStart comms notification will not be sent - This mission has no MissionStart comms setup. ContractId: [{Contract}]. MissionId: {mission} [Missions][Comms]",
        $"<{T(done)}> [Notice] <SHUDEvent_OnNotification> Added notification \"Contract Complete: Stop Rival Attack: \" [84] to queue. New queue size: 1, MissionId: [{mission}], ObjectiveId: [] [Missions][Comms]",
    };

    [Fact]
    public async Task Your_games_and_what_your_missions_earned_also_for_rewards_typed_in_later()
    {
        using var f = new ServerFactory();
        var admin = await f.Admin();
        await ServerFactory.Send(admin, "/admin/tools", "/admin/tools/set", ("id", "session"), ("value", "true"));
        var now = DateTime.UtcNow;
        var start = now.AddMinutes(-50);
        f.Upload("zzoega", new GameLogWriter().At(start).Login("zzoega").Lines
            .Concat(Mission("11111111-1111-1111-1111-111111111111", start.AddMinutes(5), start.AddMinutes(20)))
            .Concat(new[] { $"<{T(now.AddMinutes(-2))}> [Notice] <SHUDEvent_OnNotification> Added notification \"Contract Shared: x: \" [84] to queue. New queue size: 1, MissionId: [22222222-2222-2222-2222-222222222222]" })
            .ToArray());

        // an old game from the old logs: it was quit; another one crashed
        var tracker = f.Tracker(f.AddKey("zzoega"));
        var old = new DateTime(2026, 9, 1, 18, 0, 0, DateTimeKind.Utc);
        var quitLines = new GameLogWriter().At(old).Login("zzoega").Lines.Append($"<{T(old.AddHours(2))}> [Notice] <SystemQuit> CSystem::Quit invoked").ToArray();
        (await tracker.PostAsJsonAsync("/api/history", new { file = T(old), from = 0, lines = quitLines, done = true })).EnsureSuccessStatusCode();
        var crash = old.AddDays(1);
        (await tracker.PostAsJsonAsync("/api/history", new { file = T(crash), from = 0, lines = new GameLogWriter().At(crash).Login("zzoega").Lines, done = true, last = T(crash.AddHours(1)) }))
            .EnsureSuccessStatusCode();

        var player = await f.Player(f.AddKey("zzoega"));
        var page = await player.GetStringAsync("/session");
        Assert.Contains("Playing now for", page);   // its log grew two minutes ago
        Assert.Contains("1 completed mission has no reward in the library", page);
        Assert.Contains("<td>Quit</td>", page);
        Assert.Contains("<td>Crashed</td>", page);
        Assert.Contains("<td>Playing</td>", page);
        Assert.Contains("<td class=\"right\">2:00:00</td>", page);   // the old games' lengths
        Assert.Contains("<td class=\"right\">1:00:00</td>", page);
        Assert.Contains("3.8 h", page);   // all time: 2 h + 1 h, and the 50 minutes of the game now

        // the reward typed in afterwards counts for the past
        var library = f.Services.GetRequiredService<MissionLibrary>();
        library.Set(Contract, "auec", "120000", "zzoega", now);
        library.Set(Contract, "council", "10", "zzoega", now);
        f.Services.GetRequiredService<ScripValues>().Set(ScripValues.CouncilKey, 2_000, now);
        page = await player.GetStringAsync("/session");
        Assert.DoesNotContain("no reward in the library", page);
        Assert.Contains("140,000", page);   // 120,000 aUEC + 10 Council scrip x 2,000
        Assert.Contains("Playing for", await player.GetStringAsync("/"));   // the Session card on the landing page

        var games = f.Services.GetRequiredService<Playtime>().Games("zzoega", DateTime.UtcNow);
        Assert.Equal(3, games.Count);
        Assert.True(games[0].Running);
        Assert.Equal((true, false), (games[1].Quit!.Value, games[2].Quit!.Value));
    }

    [Fact]
    public async Task The_session_tool_is_only_for_the_admin_until_it_is_shown_and_never_for_somebody_not_logged_in()
    {
        using var f = new ServerFactory();
        Assert.StartsWith("/login", (await f.Browser().GetAsync("/session")).Headers.Location!.PathOf());
        var player = await f.Player(f.AddKey("zzoega"));
        Assert.StartsWith("/login/denied", (await player.GetAsync("/session")).Headers.Location!.PathOf());
        Assert.Equal(HttpStatusCode.OK, (await (await f.Admin()).GetAsync("/session")).StatusCode);
    }
}
