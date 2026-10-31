using System.Net;
using SCTools.Tests.Support;

namespace SCTools.Tests.ServerPages;

/// <summary>
/// On the server the logs decide who is in the party (whoever is in an in-game party with a member) and who is active (the in-game party and the trackers);
/// the admin's Players page shows it and can override Active (the Party page until 31 Oct 2026).
/// </summary>
public class AutoPartyPageTests
{
    static string Id(GameLogWriter w) => w.Lines[0][1..24] + "Z";

    static async Task<string> Set(ServerFactory f, HttpClient admin, string name, string value)
    {
        var r = await ServerFactory.Send(admin, "/admin/players", "/admin/players/active", ("name", name), ("value", value));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        return WebUtility.HtmlDecode(await admin.GetStringAsync("/admin/players"));
    }

    [Fact]
    public async Task The_party_page_shows_who_plays_with_whom_and_the_admin_can_override_it()
    {
        using var f = new ServerFactory { Data = new AppData { Party = new List<string> { "zzoega", "bileh123" } }, FollowParty = true };   // zzatvo is not a member yet
        _ = f.Live;   // the server starts (and the logs take over from then)
        var now = DateTime.UtcNow;
        // zzoega's tracker: he made a party half an hour ago and zzatvo (no tracker) joined it; his game is still writing its log
        var log = new GameLogWriter().At(now.AddMinutes(-30)).Login("zzoega").Minutes(1).PartyCreated().Minutes(1).MemberJoined("zzatvo");
        Assert.True(f.Live.Uploads.Add("zzoega", Id(log), 0, 1000, log.Lines.ToList(), now).Ok);
        f.Live.Refresh(now);
        var admin = await f.Admin();

        var page = WebUtility.HtmlDecode(await admin.GetStringAsync("/admin/players"));
        Assert.Contains("zzoega's party: zzatvo, zzoega", page);
        Assert.Equal(new[] { "zzoega", "bileh123", "zzatvo" }, f.Live.Read(t => t.State.Data.Party.ToList()));   // in the party with a member: a member by himself
        Assert.Contains("ZZATVO", page);
        Assert.Contains("Leads his party", page);
        Assert.Contains("In zzoega's party", page);   // zzatvo, seen through zzoega's log
        Assert.Contains("Out of the party by the logs", page);   // bileh123: still a player, out of the party by the logs
        Assert.True(f.Live.Read(t => t.Crew.IsActiveAt("zzatvo", DateTime.UtcNow)));
        Assert.False(f.Live.Read(t => t.Crew.IsActiveAt("bileh123", DateTime.UtcNow)));
        Assert.Equal(new[] { "bileh123" }, f.Live.Read(t => t.State.Data.InactiveParty.ToList()));
        f.Live.Read(t => f.Store.Flush(t.State.Data));   // the server writes what changed every two seconds
        var saved = f.Store.Load();
        Assert.NotNull(saved.AutoPartySince);
        Assert.Contains(saved.PartyChanges, c => c.Name == "zzatvo" && c.Source == PartyChange.BackToLogs && c.Active);

        page = await Set(f, admin, "bileh123", "in");
        Assert.Contains("bileh123 is In until the logs show him leaving.", page);
        Assert.Contains("value=\"in\" selected", page);
        Assert.DoesNotContain("Out of the party by the logs", page);   // In by hand: in the party again
        Assert.True(f.Live.Read(t => t.Crew.IsActiveAt("bileh123", DateTime.UtcNow)));

        page = await Set(f, admin, "bileh123", "auto");
        Assert.Contains("bileh123 follows the logs again.", page);
        Assert.Contains("Out of the party by the logs", page);   // and out of it again
        Assert.False(f.Live.Read(t => t.Crew.IsActiveAt("bileh123", DateTime.UtcNow)));   // the logs: not playing
        Assert.Null(f.Live.Read(t => t.Crew.OverrideOf("bileh123")));
    }
}
