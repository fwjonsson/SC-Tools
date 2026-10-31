using System.Net;
using SCTools.Server.Models;
using SCTools.Tests.Support;

namespace SCTools.Tests.ServerPages;

/// <summary>
/// The admin's pages change what the program changes: the session buttons, the rewards, the stops and runs, the party, who has a mission, and the debts. The
/// server has the played session (a run handed in, one on the way) uploaded.
/// </summary>
public class AdminPagesTests
{
    static async Task<(ServerFactory F, HttpClient Admin)> Start(Action<GameLogWriter>? more = null)
    {
        var data = PlayedSession.Data();
        data.Party.Add("Pilot_B");
        var f = new ServerFactory { Data = data };
        var w = PlayedSession.Writer();
        more?.Invoke(w);
        f.Upload(PlayedSession.Player, w.Lines.ToList());
        return (f, await f.Admin());
    }

    static T Read<T>(ServerFactory f, Func<Tracker, T> read) => f.Live.Read(read);

    static async Task<string> Said(HttpClient c, HttpResponseMessage r, string page)
    {
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        return WebUtility.HtmlDecode(await c.GetStringAsync(page));
    }

    [Fact]
    public async Task The_hauling_session_takes_the_admins_start_time_and_lost_cargo_and_the_session_page_is_gone()
    {
        var (f, admin) = await Start();
        using var _ = f;
        // the admin's Session page went (Frank, 31 Oct 2026): Admin opens his Players tab, the start time is a row of the Session table
        var home = await admin.GetAsync("/admin");
        Assert.Equal("/admin/players", home.Headers.Location?.OriginalString);
        Assert.DoesNotContain(">Session</a>", await admin.GetStringAsync("/admin/players"));
        Assert.Equal("/hauling/session/start", System.Text.Json.JsonDocument.Parse(await admin.GetStringAsync("/data.json")).RootElement.GetProperty("startTimeSet").GetString());

        // the rewards are in the Mission Library, by the contract's own name (the game's), typed on Edit Mission Info: the planner pays it for every such contract
        var kind = SCTools.Missions.MissionKinds.TitleKey("Senior | Stellar Large Haul | to Ruin Station [50/200/250/500/1000/2000/4000 Rep]")!;
        await ServerFactory.Send(admin, "/missions", "/admin/missions/set", ("id", kind), ("field", "auec"), ("value", "1 300 000"));
        Assert.Equal(1_300_000m, Read(f, t => t.Earnings.RewardOf(t.State.Current!.Missions[0])));
        Assert.Equal(Earnings.DefaultReward2, Read(f, t => t.State.Inputs!.Reward2));   // the session's table by stops is only the fallback

        const string Tab = "/hauling/session";
        Assert.Contains("The session time runs from 07:05.", await Said(admin, await ServerFactory.Send(admin, "/hauling", Tab + "/start", ("startTime", "7:05")), Tab));
        Assert.Contains("Type the start time as 19:30.", await Said(admin, await ServerFactory.Send(admin, "/hauling", Tab + "/start", ("startTime", "soon")), Tab));
        Assert.Equal("07:05", Read(f, t => t.State.Inputs!.Start));
        Assert.Contains("comes from the logs again", await Said(admin, await ServerFactory.Send(admin, "/hauling", Tab + "/start", ("startTime", "")), Tab));
        Assert.Null(Read(f, t => t.State.Inputs!.Start));

        // the lost cargo of a run: in the Cargo runs table of the Session tab (RunsLost, since 29 Oct 2026)
        var done = Read(f, t => PlayedSession.Runs(t).Done.Key);
        Assert.Contains("lost cargo Little", await Said(admin, await ServerFactory.Send(admin, "/hauling", "/hauling/runs/lost", ("key", done), ("level", "Little")), Tab));
        Assert.Equal("Little", Read(f, t => t.Runs.LostLevel(done)));

        // a player: not his to change
        await ServerFactory.Send(admin, "/admin/tools", "/admin/tools/set", ("id", "hauling"), ("value", "true"));
        var player = await f.Player(f.AddKey("Pilot_B"));
        Assert.StartsWith("/login/denied", (await ServerFactory.Send(player, "/hauling", Tab + "/start", ("startTime", "8:00"))).Headers.Location!.PathOf());
        Assert.Null(Read(f, t => t.State.Inputs!.Start));
    }

    [Fact]
    public async Task A_new_session_pays_the_built_in_table_until_the_library_knows_a_contract()
    {
        using var f = new ServerFactory();   // no data, no logs: no session
        var admin = await f.Admin();
        var library = WebUtility.HtmlDecode(await admin.GetStringAsync("/missions"));
        Assert.DoesNotContain("Hauling: 2 To Ruin", library);   // the planner's contracts are kinds by their game names, like every mission: none until one is seen

        f.Live.Edit(t => SCTools.Server.Editing.Edits.ToggleSession(t, DateTime.UtcNow), DateTime.UtcNow);   // Start Session (by hand: the test server does not follow the logs)
        Assert.Equal(Earnings.DefaultReward2, Read(f, t => t.State.Inputs!.Reward2));   // the built-in table by stops, for a contract the library knows nothing about
        Assert.Equal(Earnings.DefaultReward3, Read(f, t => Earnings.RewardOf(new Mission { Multi = 3, Title = "Nobody knows" }, t.State.Inputs!, t.State.Jobs)));
    }

    [Fact]
    public async Task The_players_page_adds_sets_active_and_removes_members_and_opens_their_my_page()
    {
        var (f, admin) = await Start();
        using var _ = f;
        f.AddKey("Pilot_K");   // registered, not in the party
        var page = WebUtility.HtmlDecode(await admin.GetStringAsync("/admin/players"));
        Assert.Contains("PILOT_B", page);
        Assert.Contains("PILOT_K", page);
        Assert.Contains("href=\"/me?player=Pilot_K\"", page);   // his name opens his My page

        const string P = "/admin/players";
        Assert.Contains("Pilot_C joined the party.", await Said(admin, await ServerFactory.Send(admin, P, P + "/party/add", ("name", "Pilot_C")), P));
        Assert.Contains("Type the name in the game", await Said(admin, await ServerFactory.Send(admin, P, P + "/party/add", ("name", "two words")), P));

        // Active, sent as soon as it changes (no Save button)
        async Task<string> Active(string value) => await Said(admin, await ServerFactory.Send(admin, P, P + "/active", ("name", "Pilot_C"), ("value", value)), P);
        Assert.Contains("Pilot_C left", await Active("out"));
        Assert.False(Read(f, t => t.Crew.IsActiveAt("Pilot_C", DateTime.UtcNow)));
        Assert.Contains("Pilot_C joined", await Active("in"));
        Assert.True(Read(f, t => t.Crew.IsActiveAt("Pilot_C", DateTime.UtcNow)));
        Assert.Contains("data-autosave", await admin.GetStringAsync(P));
        Assert.DoesNotContain(">Save<", await admin.GetStringAsync(P));

        // his My page, opened by the admin: what the admin changes there is his (Frank, 31 Oct 2026: the Party page, where he set them, went)
        var mine = WebUtility.HtmlDecode(await admin.GetStringAsync("/me?player=Pilot_C"));
        Assert.Contains("PILOT_C", mine);
        Assert.Contains("action=\"/me?player=Pilot_C\"", mine);
        Assert.Contains("action=\"/me/fleet/add?player=Pilot_C\"", mine);
        var saved = await ServerFactory.Send(admin, "/me?player=Pilot_C", "/me?player=Pilot_C", ("Location", ""), ("Color", "#00ff00"));
        Assert.Equal("/me?player=Pilot_C", saved.Headers.Location?.OriginalString);
        Assert.Equal(unchecked((int)0xFF00FF00), Read(f, t => t.State.Data.WorkerColors["Pilot_C"]));
        // a player's address names nobody else: his own page
        var player = await f.Player(f.AddKey("Pilot_K"));
        var his = WebUtility.HtmlDecode(await player.GetStringAsync("/me?player=Pilot_C"));
        Assert.Contains("PILOT_K", his);
        Assert.DoesNotContain("PILOT_C", his);

        await ServerFactory.Send(admin, P, P + "/party/remove", ("name", "Pilot_C"));
        Assert.DoesNotContain("Pilot_C", Read(f, t => t.State.Data.Party));
        Assert.Contains("Pilot_K joined the party.", await Said(admin, await ServerFactory.Send(admin, P, P + "/party/add", ("name", "Pilot_K")), P));   // a registered player's button
    }

    [Fact]
    public async Task The_admin_ticks_payments_corrects_balances_and_payouts_on_the_debts_tab()
    {
        // his Debts page went (Frank, 31 Oct 2026): the Hauling Planner's Debts tab has his fields, sent in the background
        var (f, admin) = await Start(w => w.Minutes(1).Sent("Pilot_B", 100_000));
        using var _ = f;
        const string Tab = "/hauling/debts";
        var debts = System.Text.Json.JsonDocument.Parse(await admin.GetStringAsync("/data.json")).RootElement.GetProperty("debts");
        Assert.Equal(System.Text.Json.JsonValueKind.Array, debts.GetProperty("payoutsEdit").ValueKind);   // the admin's: what he changes
        Assert.Equal(System.Text.Json.JsonValueKind.Array, debts.GetProperty("sessionParty").ValueKind);

        var transfer = Read(f, t => t.State.Data.Transfers.Single());
        var key = SCTools.Server.Editing.Edits.TransferKey(transfer);
        var r = await ServerFactory.Send(admin, Tab, Tab + "/payment", ("key", key), ("payment", "true"));   // he did not send it: the admin may still
        Assert.Contains("pays off a debt", await Said(admin, r, Tab));
        Assert.True(Read(f, t => t.State.Data.Transfers.Single().Payment));

        r = await ServerFactory.Send(admin, Tab, Tab + "/balance", ("me", PlayedSession.Player), ("other", "Pilot_B"), ("balance", "+5 000"));
        Assert.Contains("Pilot_B owes " + PlayedSession.Player, await Said(admin, r, Tab));
        Assert.Equal(5_000m, Read(f, t => SCTools.Server.Editing.Edits.Balances(t, PlayedSession.Player).Single(b => b.Other == "Pilot_B").Shown));
        Assert.Contains("Type the balance as a number", await Said(admin, await ServerFactory.Send(admin, Tab, Tab + "/balance", ("me", PlayedSession.Player), ("other", "Pilot_B"), ("balance", "lots")), Tab));

        var payout = Read(f, t => t.State.Data.Payouts.FirstOrDefault()?.MissionId);
        if (payout != null)
        {
            await ServerFactory.Send(admin, Tab, Tab + "/payout", ("id", payout), ("payout", "1000000"));
            Assert.Equal(1_000_000m, Read(f, t => t.State.Data.PayoutOverrides[payout]));
            await ServerFactory.Send(admin, Tab, Tab + "/payout", ("id", payout), ("payout", ""));
            Assert.False(Read(f, t => t.State.Data.PayoutOverrides.ContainsKey(payout)));
        }

        await ServerFactory.Send(admin, Tab, Tab + "/party", ("names", PlayedSession.Player));
        Assert.Equal(new[] { PlayedSession.Player }, Read(f, t => t.State.Inputs!.Party));

        // a player: none of it (his own Payment ticks only), and his data has no admin's fields
        await ServerFactory.Send(admin, "/admin/tools", "/admin/tools/set", ("id", "hauling"), ("value", "true"));
        var player = await f.Player(f.AddKey("Pilot_B"));
        foreach (var (action, fields) in new[] { ("/balance", new[] { ("me", "Pilot_B"), ("other", PlayedSession.Player), ("balance", "0") }), ("/party", new[] { ("names", "Pilot_B") }) })
            Assert.StartsWith("/login/denied", (await ServerFactory.Send(player, Tab, Tab + action, fields)).Headers.Location!.PathOf());
        Assert.Equal(new[] { PlayedSession.Player }, Read(f, t => t.State.Inputs!.Party));
        var his = System.Text.Json.JsonDocument.Parse(await player.GetStringAsync("/data.json")).RootElement;
        if (his.TryGetProperty("debts", out var hisDebts))
        {
            Assert.False(hisDebts.TryGetProperty("payoutsEdit", out var _1));
            Assert.False(hisDebts.TryGetProperty("sessionParty", out var _2));
        }
    }

    [Fact]
    public async Task A_player_cannot_open_the_admin_pages()
    {
        var (f, _) = await Start();
        using var __ = f;
        var player = await f.Player(f.AddKey("Pilot_B"));
        foreach (var url in new[] { "/admin/players", "/admin/map" })
        {
            var r = await player.GetAsync(url);
            Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
            Assert.StartsWith("/login/denied", r.Headers.Location!.PathOf());
        }
    }

    [Theory]
    [InlineData("1268250", 1268250)]
    [InlineData("1 268 250", 1268250)]
    [InlineData("1,268,250", 1268250)]
    [InlineData("+5 000", 5000)]
    [InlineData("−5 000", -5000)]
    [InlineData("-250", -250)]
    public void Amounts_are_read_with_or_without_spaces(string typed, int expected) => Assert.Equal<decimal?>(expected, Amounts.Parse(typed));

    [Fact]
    public void Nothing_or_words_are_no_amount()
    {
        Assert.Null(Amounts.Parse(""));
        Assert.Null(Amounts.Parse("lots"));
        Assert.Equal("+5000", Amounts.Signed(5000.4m));
        Assert.Equal("0", Amounts.Signed(0.2m));
    }
}
