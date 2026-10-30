using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SCTools.Gauntlet;
using SCTools.Missions;
using SCTools.Server;
using SCTools.Tests.ServerPages;
using SCTools.Tests.Support;

namespace SCTools.Tests.ContractTracking;

/// <summary>
/// What every tool shows of the contract money for one evening of three players, so a change to how it is worked out shows where the numbers move. zzoega
/// took a bounty and shared it with zzatvo (both completed it), took a bounty by himself, took another one that zzoii accepted and abandoned, took a gauntlet
/// scenario and shared it with zzatvo, ran another one alone, and hauled a contract the Hauling Planner paid out to all three. zzatvo also took his own
/// copy of the same bounty.
/// </summary>
public class ContractMoneyTests
{
    const string Offer = "26b8773e-0b96-40b4-a33a-27a4e9877e75";
    const string RivalShared = "71111111-1111-4111-8111-111111111111";    // zzoega's, shared with zzatvo
    const string PirateOwn = "72222222-2222-4222-8222-222222222222";      // zzoega's alone
    const string PirateCopy = "73333333-3333-4333-8333-333333333333";     // zzatvo's own copy of the same bounty
    const string RivalLeft = "74444444-4444-4444-8444-444444444444";      // zzoega's, zzoii accepted it and abandoned it
    const string GauntletShared = "75555555-5555-4555-8555-555555555555"; // scenario 3, zzoega's, shared with zzatvo
    const string GauntletAlone = "76666666-6666-4666-8666-666666666666";  // scenario 7, zzoega alone
    const string Haul = "77777777-7777-4777-8777-777777777777";           // the planner paid it out to the three

    static string T(DateTime utc) => utc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    static string Note(DateTime t, string what, string title, string mission) =>
        $"<{T(t)}> [Notice] <SHUDEvent_OnNotification> Added notification \"{what}: {title}: \" [84] to queue. New queue size: 1, MissionId: [{mission}], ObjectiveId: [] [Missions][Comms]";

    /// <summary>The offer a mission came from (ContractId): some missions have it in the log, most do not (one of ten in zzatvo's log of 29-30 Oct 2026).</summary>
    static string OfferOf(DateTime t, string mission) =>
        $"<{T(t)}> [Notice] <CSCPlayerMissionLog::MissionStartCommsNotification> MissionStart comms notification will not be sent - This mission has no MissionStart comms setup. ContractId: [{Offer}]. MissionId: {mission} [Missions][Comms]";

    static string Abandon(DateTime t, string mission, string player) =>
        $"<{T(t)}> [Notice] <EndMission> Ending mission for player. MissionId[{mission}] Player[{player}] PlayerId[100000000001] CompletionType[Abandon] Reason[Player left] [Team_MissionFeatures][Missions]";

    static string[] Took(DateTime at, double done, string title, string mission, bool offer = true) =>
        new[] { Note(at, "Contract Accepted", title, mission) }.Concat(offer ? new[] { OfferOf(at, mission) } : Array.Empty<string>())
            .Append(Note(at.AddMinutes(done), "Contract Complete", title, mission)).ToArray();

    /// <summary>
    /// A contract shared with him, as the game writes it (Frank's logs of 29-30 Oct 2026): "Contract Shared" with no mission id (all zeros) and the
    /// contract's template for a name, then "Contract Accepted" with the real mission a few seconds later, as if he took it himself.
    /// </summary>
    static string[] SharedWith(DateTime at, double done, string title, string mission, bool offer = true) =>
        new[] { SharedNote(at) }.Concat(Took(at.AddSeconds(5), done, title, mission, offer)).ToArray();

    static string SharedNote(DateTime at) =>
        Note(at, "Contract Shared", "~mission(ReputationRank) | ~mission(CargoRouteToken) ~mission(CargoGradeToken) Haul", "00000000-0000-0000-0000-000000000000");

    const string Gauntlet3 = "Combat Gauntlet - Scenario #3";
    const string Gauntlet7 = "Combat Gauntlet - Scenario #7";

    static ServerFactory Played(DateTime now, List<List<string>>? combined = null)
    {
        var data = new AppData
        {
            Payouts = { new PayoutRecord { MissionId = Haul, Time = now.AddMinutes(-8), Contract = "Carbon, 2 stops", Payout = 90_000, Members = { "zzoega", "zzatvo", "zzoii" }, Each = 30_000 } },
        };
        if (combined != null) data.CombinedAccounts = combined;
        var f = new ServerFactory { Data = data };
        var start = now.AddMinutes(-50);
        f.Upload("zzoega", new GameLogWriter().At(start).Login("zzoega").Lines
            .Concat(Took(start.AddMinutes(5), 15, "Stop Rival Attack", RivalShared))
            .Concat(Took(start.AddMinutes(6), 15, "Kill the Pirate", PirateOwn))
            .Concat(Took(start.AddMinutes(8), 14, "Stop Rival Attack", RivalLeft))
            .Concat(Took(start.AddMinutes(25), 5, Gauntlet3, GauntletShared, offer: false))
            .Concat(Took(start.AddMinutes(31), 5, Gauntlet7, GauntletAlone, offer: false))
            .Concat(Took(start.AddMinutes(37), 5, "Haul the Carbon", Haul))
            .Append(Note(now.AddMinutes(-2), "Contract Accepted", "Something Else", "78888888-8888-4888-8888-888888888888")).ToList(), T(start));
        f.Upload("zzatvo", new GameLogWriter().At(start).Login("zzatvo").Lines
            .Concat(SharedWith(start.AddMinutes(6), 14, "Stop Rival Attack", RivalShared))
            .Concat(Took(start.AddMinutes(7), 16, "Kill the Pirate", PirateCopy))
            .Concat(SharedWith(start.AddMinutes(25), 5, Gauntlet3, GauntletShared, offer: false))
            .Append(Note(now.AddMinutes(-2), "Contract Accepted", "Something Else", "79999999-9999-4999-8999-999999999999")).ToList(), T(start));
        f.Upload("zzoii", new GameLogWriter().At(start).Login("zzoii").Lines
            .Append(SharedNote(start.AddMinutes(9))).Append(Note(start.AddMinutes(9).AddSeconds(5), "Contract Accepted", "Stop Rival Attack", RivalLeft))
            .Append(Abandon(start.AddMinutes(12), RivalLeft, "zzoii"))
            .Append(Note(now.AddMinutes(-2), "Contract Accepted", "Something Else", "7aaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa")).ToList(), T(start));
        var library = f.Services.GetRequiredService<MissionLibrary>();
        library.Collect();
        f.Services.GetRequiredService<GauntletService>().Collect();
        f.Services.GetRequiredService<GauntletService>().Rewards();   // the scenarios' rewards are in the library (#3: 55,500 and 5 MG scrip, #7: 227,750 and 16)
        library.Set("stop rival attack", "auec", "100000", "admin", now);
        library.Set("stop rival attack", "mg", "10", "admin", now);
        library.Set("kill the pirate", "auec", "40000", "admin", now);
        library.Set("kill the pirate", "council", "5", "admin", now);
        library.Set("haul the carbon", "auec", "500000", "admin", now);   // the planner's split wins over the library's whole reward
        return f;
    }

    static async Task<JsonElement> Companion(ServerFactory f, string player)
    {
        var admin = await f.Admin();
        await ServerFactory.Send(admin, "/admin/tools", "/admin/tools/set", ("id", "companion"), ("value", "true"));
        var json = await (await f.Player(f.AddKey(player))).GetStringAsync("/companion/session.json");
        return JsonDocument.Parse(json).RootElement.GetProperty("session");
    }

    static (long Auec, long Mg, long Council, int Completed) Money(JsonElement s) =>
        (s.GetProperty("auec").GetInt64(), s.GetProperty("mg").GetInt64(), s.GetProperty("council").GetInt64(), s.GetProperty("completed").GetInt32());

    [Fact]
    public async Task The_companion_and_the_session_tool_show_each_player_his_part()
    {
        var now = DateTime.UtcNow;
        using var f = Played(now);
        // zzoega: half the shared bounty (50,000) and its 10 MG scrip, his own bounty (40,000, 5 Council scrip), the one zzoii abandoned in full (100,000, 10 MG),
        // half the shared scenario 3 (27,750) and its 5 MG scrip, scenario 7 alone (227,750, 16 MG), his planner share (30,000)
        Assert.Equal((475_500L, 41L, 5L, 6), Money(await Companion(f, "zzoega")));
        // zzatvo: half the shared bounty without scrip, his own copy of the other bounty in full (40,000, 5 Council scrip), half scenario 3 without scrip, his
        // planner share
        Assert.Equal((147_750L, 0L, 5L, 4), Money(await Companion(f, "zzatvo")));
        // zzoii: his planner share; the bounty he abandoned pays him nothing
        Assert.Equal((30_000L, 0L, 0L, 1), Money(await Companion(f, "zzoii")));

        var admin = await f.Admin();
        await ServerFactory.Send(admin, "/admin/tools", "/admin/tools/set", ("id", "session"), ("value", "true"));
        Assert.Contains("147,750", await (await f.Player(f.AddKey("zzatvo"))).GetStringAsync("/session"));
    }

    [Fact]
    public async Task Combined_accounts_add_up_to_what_they_were_paid_together()
    {
        var now = DateTime.UtcNow;
        using var f = Played(now, new List<List<string>> { new() { "zzoega", "zzatvo" } });
        // the shared bounty and the shared scenario once (their halves), not twice
        Assert.Equal((623_250L, 41L, 10L), (Money(await Companion(f, "zzoega")) is var m ? (m.Auec, m.Mg, m.Council) : default));
    }

    [Fact]
    public void The_gauntlet_tracker_counts_each_player_his_part_of_a_shared_run()
    {
        var now = DateTime.UtcNow;
        using var f = Played(now, new List<List<string>> { new() { "zzoega", "zzatvo" } });
        var gauntlet = f.Services.GetRequiredService<GauntletService>();
        var rewards = gauntlet.Rewards();
        var zzoega = GauntletStats.Totals(gauntlet.Runs("zzoega"), rewards, 1_000);
        Assert.Equal((2, 255_500L, 21L), (zzoega.Completed, zzoega.Auec, zzoega.Scrip));   // half of scenario 3 and its scrip (he took it), scenario 7 in full
        var zzatvo = GauntletStats.Totals(gauntlet.Runs("zzatvo"), rewards, 1_000);
        Assert.Equal((1, 27_750L, 0L), (zzatvo.Completed, zzatvo.Auec, zzatvo.Scrip));     // half of scenario 3, no scrip: it was shared with him

        var board = GauntletLeaderboard.Make(gauntlet.AllRuns(), rewards, 1_000, new List<IReadOnlyList<string>> { new[] { "zzoega", "zzatvo" } });
        var both = Assert.Single(board, l => l.Combined);
        Assert.Equal((283_250L, 21L), (both.Auec, both.Scrip));   // scenario 3 once, scenario 7
        Assert.Equal(55_500, GauntletRewards.Of(3, rewards).Auec);   // what a scenario pays stays its whole reward (the Stats tab's per hour)

        // a party's page: the shared run counts once in the party's earnings (both halves)
        var party = gauntlet.RunsOf(new[] { "zzoega", "zzatvo" }).Select(r => r.Run).ToList();
        Assert.Equal(283_250, GauntletStats.Totals(party, rewards, 1_000).Auec);
    }

    [Fact]
    public void The_tracker_knows_every_contract_who_had_it_and_how_it_ended()
    {
        var now = DateTime.UtcNow;
        using var f = Played(now);
        var tracker = f.Services.GetRequiredService<ContractTracker>();
        var all = tracker.All().ToDictionary(c => c.MissionId, StringComparer.OrdinalIgnoreCase);

        var left = all[RivalLeft];
        Assert.Equal(("stop rival attack", "zzoega", (SCTools.Missions.MissionOutcome?)SCTools.Missions.MissionOutcome.Complete), (left.Kind, left.Taker, left.Outcome));
        Assert.Equal(new[] { ("zzoega", (bool?)true, true), ("zzoii", (bool?)false, false) },
            left.Holders.OrderBy(h => h.Player).Select(h => (h.Player, h.Took, h.Completed)));   // zzoii accepted the share and abandoned it
        Assert.Equal(new[] { "zzoega" }, left.Payees);
        Assert.Equal(new ContractReward(100_000, 10, null), left.Reward);

        var gauntlet = all[GauntletShared];   // in the Mission Library for zzoega, in the Gauntlet Tracker for both
        Assert.Equal(("combat gauntlet - scenario #3", "zzoega"), (gauntlet.Kind, gauntlet.Taker));
        Assert.Equal(new[] { "zzoega", "zzatvo" }, gauntlet.Payees.Order(StringComparer.OrdinalIgnoreCase).Reverse());

        var haul = all[Haul];
        Assert.Equal(new[] { "zzoega", "zzatvo", "zzoii" }, haul.Payees);   // the planner's payout
        Assert.Equal(new ContractReward(30_000, null, null), haul.PartOf("zzoii"));
        Assert.DoesNotContain(all.Values, c => c.MissionId.StartsWith("7aaaaaaa", StringComparison.Ordinal) && c.Outcome != null);   // taken, not ended yet
    }

    [Fact]
    public void Who_took_a_shared_contract_is_who_accepted_it_first_also_after_logging_in_again()
    {
        var now = DateTime.UtcNow;
        using var f = Played(now);
        var tracker = f.Services.GetRequiredService<ContractTracker>();
        Assert.Equal("zzoega", tracker.Of(new[] { RivalShared })[RivalShared].Taker);   // zzatvo's accept came seconds after the share

        // zzoega logs in again: the game says "Contract Accepted" once more for every contract he has (his log of 30 Oct 2026, 02:57)
        var relog = now.AddMinutes(-1);
        f.Upload("zzoega", new GameLogWriter().At(relog).Login("zzoega").Lines.Append(Note(relog.AddSeconds(30), "Contract Accepted", "Stop Rival Attack", RivalShared)).ToList(),
            T(relog));
        f.Services.GetRequiredService<MissionLibrary>().Collect();
        var c = tracker.Of(new[] { RivalShared })[RivalShared];
        Assert.Equal("zzoega", c.Taker);
        Assert.Equal(new ContractReward(50_000, 0, null), c.PartOf("zzatvo"));   // half the aUEC, no scrip
    }

    [Fact]
    public void The_first_accept_counts_also_when_the_older_log_comes_in_last()
    {
        // zzoega's live log (logged in again at 03:00, the game says his contract again) is read before his old log of 20:00, when he took it; zzatvo
        // accepted the share at 20:01
        using var f = new ServerFactory();
        var library = f.Services.GetRequiredService<MissionLibrary>();
        var took = new DateTime(2026, 10, 29, 20, 0, 0, DateTimeKind.Utc);
        MissionSeen Seen(DateTime started) => new(RivalShared, null, "Stop Rival Attack", false, started, took.AddHours(8), SCTools.Missions.MissionOutcome.Complete);
        library.Add(new Dictionary<string, IReadOnlyList<MissionSeen>> { ["zzoega"] = new[] { Seen(took.AddHours(7)) }, ["zzatvo"] = new[] { Seen(took.AddMinutes(1)) } });
        library.Add(new Dictionary<string, IReadOnlyList<MissionSeen>> { ["zzoega"] = new[] { Seen(took) } });
        Assert.Equal("zzoega", f.Services.GetRequiredService<ContractTracker>().Of(new[] { RivalShared })[RivalShared].Taker);
    }

    [Fact]
    public void When_the_taker_abandons_a_shared_contract_its_scrip_goes_to_who_accepted_the_share_first()
    {
        // zzoega takes a bounty, shares it, zzatvo accepts the share before zzoii, zzoega abandons it, the two complete it (the logs as the game writes them)
        var now = DateTime.UtcNow;
        var at = now.AddMinutes(-40);
        const string Bounty = "7bbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb";
        using var f = new ServerFactory();
        f.Upload("zzoega", new GameLogWriter().At(at).Login("zzoega").Lines
            .Append(Note(at.AddMinutes(1), "Contract Accepted", "Stop Rival Attack", Bounty)).Append(Abandon(at.AddMinutes(5), Bounty, "zzoega")).ToList(), T(at));
        f.Upload("zzatvo", new GameLogWriter().At(at).Login("zzatvo").Lines.Concat(SharedWith(at.AddMinutes(2), 10, "Stop Rival Attack", Bounty, offer: false)).ToList(), T(at));
        f.Upload("zzoii", new GameLogWriter().At(at).Login("zzoii").Lines.Concat(SharedWith(at.AddMinutes(3), 10, "Stop Rival Attack", Bounty, offer: false)).ToList(), T(at));
        var library = f.Services.GetRequiredService<MissionLibrary>();
        library.Collect();
        library.Set("stop rival attack", "auec", "100000", "admin", now);
        library.Set("stop rival attack", "mg", "10", "admin", now);

        var c = f.Services.GetRequiredService<ContractTracker>().Of(new[] { Bounty })[Bounty];
        Assert.Equal("zzoega", c.Taker);
        Assert.Null(c.PartOf("zzoega"));                                      // he abandoned it: nothing
        Assert.Equal(new ContractReward(50_000, 10, null), c.PartOf("zzatvo"));  // half the aUEC, and the MG scrip: his share was accepted first
        Assert.Equal(new ContractReward(50_000, 0, null), c.PartOf("zzoii"));
    }

    [Fact]
    public void A_payout_typed_on_the_debts_tab_is_what_the_contract_paid()
    {
        var now = DateTime.UtcNow;
        using var f = Played(now);
        f.Live.Edit(t => t.Payouts.SetPayoutOverride(RivalShared, 60_000m), now);
        var earned = f.Services.GetRequiredService<ContractTracker>().Earned("zzatvo");
        Assert.Equal(30_000, Assert.Single(earned, m => m.Kind == "stop rival attack").Auec);   // half of what it really paid
    }
}
