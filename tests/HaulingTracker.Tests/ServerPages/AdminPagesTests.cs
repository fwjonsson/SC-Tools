using System.Net;
using HaulingTracker.Server.Models;
using HaulingTracker.Tests.Support;

namespace HaulingTracker.Tests.ServerPages;

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
    public async Task The_session_page_has_the_buttons_rewards_stops_and_runs()
    {
        var (f, admin) = await Start();
        using var _ = f;
        var page = WebUtility.HtmlDecode(await admin.GetStringAsync("/admin"));
        Assert.Contains("Mission rewards", page);
        Assert.Contains("Patch City", page);
        Assert.Contains("Next Stops: Unlocked", page);
        Assert.Contains("Stop Session", page);   // the played session was started by hand and runs

        Assert.Contains("The mission rewards are saved", await Said(admin, await ServerFactory.Send(admin, "/admin", "/admin/rewards", ("rewards[0]", "1 300 000")), "/admin"));
        Assert.Equal(1_300_000m, Read(f, t => t.State.Inputs!.Reward2));
        Assert.Equal(1_300_000m, Read(f, t => t.State.Data.DefaultRewards["2 To Ruin"]));   // and the next sessions start with it

        Assert.Contains("The session time runs from 07:05.", await Said(admin, await ServerFactory.Send(admin, "/admin", "/admin/start-time", ("startTime", "7:05")), "/admin"));
        Assert.Contains("Type the start time as 19:30.", await Said(admin, await ServerFactory.Send(admin, "/admin", "/admin/start-time", ("startTime", "soon")), "/admin"));
        Assert.Equal("07:05", Read(f, t => t.State.Inputs!.Start));

        await ServerFactory.Send(admin, "/admin", "/admin/stop", ("zone", PlayedSession.PatchZone), ("name", "Patchy"), ("notes", "full of pirates"));
        Assert.Equal("Patchy", Read(f, t => t.Places.LocationName(PlayedSession.PatchZone)));
        Assert.Equal("full of pirates", Read(f, t => t.State.Inputs!.Rows[PlayedSession.PatchZone].Notes));
        await ServerFactory.Send(admin, "/admin", "/admin/stop", ("zone", PlayedSession.PatchZone), ("name", ""), ("notes", "full of pirates"));
        Assert.Equal("Patch City", Read(f, t => t.Places.LocationName(PlayedSession.PatchZone)));   // the name the program knows again

        var done = Read(f, t => PlayedSession.Runs(t).Done.Key);
        Assert.Contains("lost cargo Little", await Said(admin, await ServerFactory.Send(admin, "/admin", "/admin/run", ("key", done), ("level", "Little")), "/admin"));
        Assert.Equal("Little", Read(f, t => t.Runs.LostLevel(done)));

        Assert.Contains("The next stops are locked", await Said(admin, await ServerFactory.Send(admin, "/admin", "/admin/locks"), "/admin"));
        Assert.True(Read(f, t => t.State.Inputs!.NextStopsLocked));
        Assert.Contains("unlocked", await Said(admin, await ServerFactory.Send(admin, "/admin", "/admin/locks"), "/admin"));
        Assert.False(Read(f, t => t.State.Inputs!.NextStopsLocked));

        Assert.Contains("Session stopped.", await Said(admin, await ServerFactory.Send(admin, "/admin", "/admin/session"), "/admin"));
        Assert.Null(Read(f, t => t.State.Data.ManualSessions.LastOrDefault(s => !s.StopUtc.HasValue)));
        Assert.Contains("Session started.", await Said(admin, await ServerFactory.Send(admin, "/admin", "/admin/session"), "/admin"));
        Assert.Equal(2, Read(f, t => t.State.Data.ManualSessions.Count));
    }

    [Fact]
    public async Task The_rewards_can_be_set_without_a_session_and_a_new_session_starts_with_them()
    {
        using var f = new ServerFactory();   // no data, no logs: no session
        var admin = await f.Admin();
        var page = WebUtility.HtmlDecode(await admin.GetStringAsync("/admin"));
        Assert.Contains("No session now", page);
        Assert.Contains("value=\"" + Earnings.DefaultReward2.ToString("0") + "\"", page);   // the built-in reward

        var r = await ServerFactory.Send(admin, "/admin", "/admin/rewards", ("rewards[0]", "1400000"), ("rewards[1]", ""));
        Assert.Contains("the next session starts with them", await Said(admin, r, "/admin"));
        Assert.Contains("value=\"1400000\"", await admin.GetStringAsync("/admin"));

        await ServerFactory.Send(admin, "/admin", "/admin/session");   // Start Session
        Assert.Equal(1_400_000m, Read(f, t => t.State.Inputs!.Reward2));
        Assert.Equal(Earnings.DefaultReward3, Read(f, t => t.State.Inputs!.Reward3));   // nothing typed: the built-in one
    }

    [Fact]
    public async Task The_party_page_adds_changes_and_removes_members()
    {
        var (f, admin) = await Start();
        using var _ = f;
        Assert.Contains("PILOT_B", await admin.GetStringAsync("/admin/party"));

        Assert.Contains("Pilot_C joined the party.", await Said(admin, await ServerFactory.Send(admin, "/admin/party", "/admin/party/add", ("name", "Pilot_C")), "/admin/party"));
        Assert.Contains("Type the name in the game", await Said(admin, await ServerFactory.Send(admin, "/admin/party", "/admin/party/add", ("name", "two words")), "/admin/party"));

        // every field is saved on its own, as soon as it changes (no Save button): the others stay as they are
        async Task<string> Set(string field, string? value)
        {
            var fields = new List<(string, string)> { ("name", "Pilot_C"), ("field", field) };
            if (value != null) fields.Add(("value", value));   // an unticked box sends nothing
            return await Said(admin, await ServerFactory.Send(admin, "/admin/party", "/admin/party/set", fields.ToArray()), "/admin/party");
        }
        var ship = Ships.All[1];
        Assert.Contains("Saved for Pilot_C: ship.", await Set("ship", ship.Label));
        Assert.Equal(ship.Name, Read(f, t => t.State.Data.Ship["Pilot_C"]));
        Assert.Contains("Saved for Pilot_C: batches.", await Set("batches", "5"));
        Assert.Equal(5, Read(f, t => t.Crew.ShipCapacityOf("Pilot_C")));
        Assert.Contains("Saved for Pilot_C: colour.", await Set("color", "#00ff00"));
        Assert.Equal(unchecked((int)0xFF00FF00), Read(f, t => t.State.Data.WorkerColors["Pilot_C"]));
        Assert.Equal(ship.Name, Read(f, t => t.State.Data.Ship["Pilot_C"]));   // the colour did not touch the ship
        Assert.Contains("Pilot_C left", await Set("active", null));
        Assert.False(Read(f, t => t.Crew.IsActiveAt("Pilot_C", DateTime.UtcNow)));
        Assert.Equal(5, Read(f, t => t.Crew.ShipCapacityOf("Pilot_C")));   // nor the batches
        Assert.Contains("Pilot_C joined", await Set("active", "true"));
        Assert.True(Read(f, t => t.Crew.IsActiveAt("Pilot_C", DateTime.UtcNow)));
        Assert.Contains("not one of the choices", await Set("ship", "A ship that does not exist"));
        Assert.Contains("data-autosave", await admin.GetStringAsync("/admin/party"));
        Assert.DoesNotContain(">Save<", await admin.GetStringAsync("/admin/party"));

        await ServerFactory.Send(admin, "/admin/party", "/admin/party/remove", ("name", "Pilot_C"));
        Assert.DoesNotContain("Pilot_C", Read(f, t => t.State.Data.Party));
    }

    [Fact]
    public async Task The_contracts_page_sets_who_has_a_mission_and_marks_it_gone()
    {
        var (f, admin) = await Start();
        using var _ = f;
        Assert.Contains(PlayedSession.M2, await admin.GetStringAsync("/admin/contracts"));

        Assert.True(Read(f, t => t.Contracts.HasMission(t.Contracts.ContractRows().Single(m => m.Id == PlayedSession.M2), PlayedSession.Player)));
        var r = await ServerFactory.Send(admin, "/admin/contracts", "/admin/contracts/holder", ("id", PlayedSession.M2), ("member", "Pilot_B"), ("has", "true"));
        Assert.Contains("Pilot_B has", await Said(admin, r, "/admin/contracts"));
        Assert.True(Read(f, t => t.Contracts.HasMission(t.Contracts.ContractRows().Single(m => m.Id == PlayedSession.M2), "Pilot_B")));

        r = await ServerFactory.Send(admin, "/admin/contracts", "/admin/contracts/gone", ("id", PlayedSession.M2));
        Assert.Contains("is marked as gone", await Said(admin, r, "/admin/contracts"));
        Assert.True(Read(f, t => t.State.Data.DroppedMissions.ContainsKey(PlayedSession.M2)));
        Assert.Contains("Bring back", await admin.GetStringAsync("/admin/contracts"));
        await ServerFactory.Send(admin, "/admin/contracts", "/admin/contracts/gone", ("id", PlayedSession.M2));
        Assert.False(Read(f, t => t.State.Data.DroppedMissions.ContainsKey(PlayedSession.M2)));
    }

    [Fact]
    public async Task The_debts_page_ticks_payments_corrects_balances_and_payouts()
    {
        var (f, admin) = await Start(w => w.Minutes(1).Sent("Pilot_B", 100_000));
        using var _ = f;
        var page = WebUtility.HtmlDecode(await admin.GetStringAsync("/admin/debts?me=" + PlayedSession.Player));
        Assert.Contains("Balances of " + PlayedSession.Player, page);

        var transfer = Read(f, t => t.State.Data.Transfers.Single());
        var key = HaulingTracker.Server.Editing.Edits.TransferKey(transfer);
        var r = await ServerFactory.Send(admin, "/admin/debts", "/admin/debts/payment", ("key", key), ("payment", "true"), ("me", PlayedSession.Player));
        Assert.Contains("pays off a debt", await Said(admin, r, "/admin/debts"));
        Assert.True(Read(f, t => t.State.Data.Transfers.Single().Payment));

        r = await ServerFactory.Send(admin, "/admin/debts", "/admin/debts/balance", ("me", PlayedSession.Player), ("other", "Pilot_B"), ("balance", "+5 000"));
        Assert.Contains("Pilot_B owes " + PlayedSession.Player, await Said(admin, r, "/admin/debts?me=" + PlayedSession.Player));
        Assert.Equal(5_000m, Read(f, t => HaulingTracker.Server.Editing.Edits.Balances(t, PlayedSession.Player).Single(b => b.Other == "Pilot_B").Shown));

        var payout = Read(f, t => t.State.Data.Payouts.FirstOrDefault()?.MissionId);
        if (payout != null)
        {
            await ServerFactory.Send(admin, "/admin/debts", "/admin/debts/payout", ("id", payout), ("payout", "1000000"));
            Assert.Equal(1_000_000m, Read(f, t => t.State.Data.PayoutOverrides[payout]));
            await ServerFactory.Send(admin, "/admin/debts", "/admin/debts/payout", ("id", payout), ("payout", ""));
            Assert.False(Read(f, t => t.State.Data.PayoutOverrides.ContainsKey(payout)));
        }

        await ServerFactory.Send(admin, "/admin/debts", "/admin/debts/party", ("names", PlayedSession.Player));
        Assert.Equal(new[] { PlayedSession.Player }, Read(f, t => t.State.Inputs!.Party));
    }

    [Fact]
    public async Task A_player_cannot_open_the_admin_pages()
    {
        var (f, _) = await Start();
        using var __ = f;
        var player = await f.Player(f.AddKey("Pilot_B"));
        foreach (var url in new[] { "/admin", "/admin/party", "/admin/contracts", "/admin/debts" })
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
