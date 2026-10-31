using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SCTools.Server;
using SCTools.Tests.Support;

namespace SCTools.Tests.ServerPages;

/// <summary>
/// What a player earned from his completed missions (ContractTracker.Earned: the Session tool and the Companion), by Frank's rules of 30 Oct 2026: the aUEC
/// split evenly among the players who completed the same mission, a Hauling Planner contract as its payout split (the Debts tab's), the scrip only for the
/// player who took the contract; and on the Companion, a player's combined accounts added up over his own game session.
/// </summary>
public class EarningsShareTests
{
    const string Contract = "26b8773e-0b96-40b4-a33a-27a4e9877e75";
    const string Bounty = "71111111-1111-4111-8111-111111111111";
    const string Haul = "72222222-2222-4222-8222-222222222222";

    static string T(DateTime utc) => utc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    static string Note(DateTime t, string what, string title, string mission) =>
        $"<{T(t)}> [Notice] <SHUDEvent_OnNotification> Added notification \"{what}: {title}: \" [84] to queue. New queue size: 1, MissionId: [{mission}], ObjectiveId: [] [Missions][Comms]";

    /// <summary>The offer a mission came from (its ContractId): a shared mission is in the library for the player it was shared with only with it.</summary>
    static string Offer(DateTime t, string mission) =>
        $"<{T(t)}> [Notice] <CSCPlayerMissionLog::MissionStartCommsNotification> MissionStart comms notification will not be sent - This mission has no MissionStart comms setup. ContractId: [{Contract}]. MissionId: {mission} [Missions][Comms]";

    /// <summary>
    /// zzoega took "Stop Rival Attack" and shared it with zzatvo; both completed it. zzoega also completed a hauling contract the Hauling Planner paid out to
    /// zzoega, zzatvo and zzoii (zzoii without it in his own log). Both games run now.
    /// </summary>
    static ServerFactory Played(DateTime now, List<List<string>>? combined = null)
    {
        var data = new AppData
        {
            Payouts = { new PayoutRecord { MissionId = Haul, Time = now.AddMinutes(-10), Contract = "Carbon, 2 stops", Payout = 90_000, Members = { "zzoega", "zzatvo", "zzoii" }, Each = 30_000 } },
        };
        if (combined != null) data.CombinedAccounts = combined;
        var f = new ServerFactory { Data = data };
        var start = now.AddMinutes(-50);
        f.Upload("zzoega", new GameLogWriter().At(start).Login("zzoega").Lines.Concat(new[]
        {
            Note(start.AddMinutes(5), "Contract Accepted", "Stop Rival Attack", Bounty), Offer(start.AddMinutes(5), Bounty),
            Note(start.AddMinutes(20), "Contract Complete", "Stop Rival Attack", Bounty),
            Note(start.AddMinutes(25), "Contract Accepted", "Haul the Carbon", Haul), Offer(start.AddMinutes(25), Haul),
            Note(start.AddMinutes(40), "Contract Complete", "Haul the Carbon", Haul),
            Note(now.AddMinutes(-2), "Contract Accepted", "Something Else", "73333333-3333-4333-8333-333333333333"),
        }).ToList(), T(start));
        f.Upload("zzatvo", new GameLogWriter().At(start).Login("zzatvo").Lines.Concat(new[]
        {
            Note(start.AddMinutes(6), "Contract Shared", "Stop Rival Attack", Bounty), Offer(start.AddMinutes(6), Bounty),
            Note(start.AddMinutes(20), "Contract Complete", "Stop Rival Attack", Bounty),
            Note(now.AddMinutes(-2), "Contract Accepted", "Something Else", "74444444-4444-4444-8444-444444444444"),
        }).ToList(), T(start));
        var library = f.Services.GetRequiredService<MissionLibrary>();
        library.Collect();
        library.Set("stop rival attack", "auec", "100000", "admin", now);
        library.Set("stop rival attack", "mg", "10", "admin", now);
        library.Set("haul the carbon", "auec", "500000", "admin", now);   // the planner's split wins over the library's whole reward
        return f;
    }

    [Fact]
    public void The_aUEC_is_split_among_who_completed_it_the_planner_s_contracts_go_by_its_payouts_and_the_scrip_to_who_took_it()
    {
        var now = DateTime.UtcNow;
        using var f = Played(now);
        var library = f.Services.GetRequiredService<MissionLibrary>();

        var zzoega = f.Services.GetRequiredService<ContractTracker>().Earned("zzoega");
        var rival = Assert.Single(zzoega, m => m.Kind == "stop rival attack");
        Assert.Equal((50_000L, 10L), (rival.Auec!.Value, rival.Mg!.Value));   // half of it (zzatvo completed it too), and the MG scrip: he took it
        Assert.Equal(30_000, Assert.Single(zzoega, m => m.Kind == "haul the carbon").Auec);   // the planner's payout split, as on the Debts tab

        var zzatvo = f.Services.GetRequiredService<ContractTracker>().Earned("zzatvo");
        var shared = Assert.Single(zzatvo, m => m.Kind == "stop rival attack");
        Assert.Equal((50_000L, 0L), (shared.Auec!.Value, shared.Mg!.Value));   // shared with him: his half of the aUEC, no scrip
        Assert.True(shared.HasReward);
        Assert.Equal(30_000, Assert.Single(zzatvo, m => m.Kind == "Carbon, 2 stops").Auec);   // a payout he shared without the contract in his own log

        Assert.Equal(30_000, Assert.Single(f.Services.GetRequiredService<ContractTracker>().Earned("zzoii")).Auec);
    }

    [Fact]
    public async Task The_session_tool_shows_his_part()
    {
        var now = DateTime.UtcNow;
        using var f = Played(now);
        var admin = await f.Admin();
        await ServerFactory.Send(admin, "/admin/tools", "/admin/tools/set", ("id", "session"), ("value", "true"));
        var page = await (await f.Player(f.AddKey("zzatvo"))).GetStringAsync("/history");
        Assert.Contains("80,000", page);   // 50,000 + 30,000, not 100,000 + 500,000
        Assert.DoesNotContain("600,000", page);
    }

    [Fact]
    public async Task The_companion_adds_up_his_combined_accounts_over_his_own_session()
    {
        var now = DateTime.UtcNow;
        using var f = Played(now, new List<List<string>> { new() { "zzoega", "zzatvo" } });
        var admin = await f.Admin();
        await ServerFactory.Send(admin, "/admin/tools", "/admin/tools/set", ("id", "companion"), ("value", "true"));
        f.Services.GetRequiredService<ScripValues>().Set(ScripValues.MgKey, 1_000, now);

        var s = JsonDocument.Parse(await (await f.Player(f.AddKey("zzoega"))).GetStringAsync("/companion/session.json")).RootElement.GetProperty("session");
        // zzoega 50,000 + 30,000 and 10 MG scrip, zzatvo 50,000 + 30,000 and none: together the bounty's whole reward and both payout shares
        Assert.Equal((160_000L, 10L), (s.GetProperty("auec").GetInt64(), s.GetProperty("mg").GetInt64()));
        Assert.Equal(170_000, s.GetProperty("value").GetDouble());   // the MG scrip at the Settings page's 1,000
        Assert.Equal(2, s.GetProperty("completed").GetInt32());   // the missions completed stay his own
        Assert.Equal(new[] { "zzoega", "zzatvo" }, s.GetProperty("accounts").EnumerateArray().Select(a => a.GetString()));

        // without combined accounts: his own part alone
        using var alone = Played(now);
        var admin2 = await alone.Admin();
        await ServerFactory.Send(admin2, "/admin/tools", "/admin/tools/set", ("id", "companion"), ("value", "true"));
        var own = JsonDocument.Parse(await (await alone.Player(alone.AddKey("zzoega"))).GetStringAsync("/companion/session.json")).RootElement.GetProperty("session");
        Assert.Equal(80_000, own.GetProperty("auec").GetInt64());
        Assert.Equal(JsonValueKind.Null, own.GetProperty("accounts").ValueKind);
    }
}
