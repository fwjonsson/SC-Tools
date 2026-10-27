using System.Net;
using HaulingTracker.Tests.Support;

namespace HaulingTracker.Tests.ServerPages;

/// <summary>On the server the logs decide who is active (the in-game party and the trackers); the admin's Party page shows it and can override it.</summary>
public class AutoPartyPageTests
{
    static string Id(GameLogWriter w) => w.Lines[0][1..24] + "Z";

    static async Task<string> Set(ServerFactory f, HttpClient admin, string name, string value)
    {
        var r = await ServerFactory.Send(admin, "/admin/party", "/admin/party/set", ("name", name), ("field", "active"), ("value", value));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        return WebUtility.HtmlDecode(await admin.GetStringAsync("/admin/party"));
    }

    [Fact]
    public async Task The_party_page_shows_who_plays_with_whom_and_the_admin_can_override_it()
    {
        using var f = new ServerFactory { Data = new AppData { Party = new List<string> { "zzoega", "zzatvo", "bileh123" } }, FollowParty = true };
        var now = DateTime.UtcNow;
        // zzoega's tracker: he made a party half an hour ago and zzatvo (no tracker) joined it; his game is still writing its log
        var log = new GameLogWriter().At(now.AddMinutes(-30)).Login("zzoega").Minutes(1).PartyCreated().Minutes(1).MemberJoined("zzatvo");
        Assert.True(f.Live.Uploads.Add("zzoega", Id(log), 0, 1000, log.Lines.ToList(), now).Ok);
        f.Live.Refresh(now);
        var admin = await f.Admin();

        var page = WebUtility.HtmlDecode(await admin.GetStringAsync("/admin/party"));
        Assert.Contains("zzoega's party: zzatvo, zzoega", page);
        Assert.Contains("Leads his party", page);
        Assert.Contains("In zzoega's party", page);   // zzatvo, seen through zzoega's log
        Assert.Contains("Not playing", page);         // bileh123
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
        Assert.True(f.Live.Read(t => t.Crew.IsActiveAt("bileh123", DateTime.UtcNow)));

        page = await Set(f, admin, "bileh123", "auto");
        Assert.Contains("bileh123 follows the logs again.", page);
        Assert.False(f.Live.Read(t => t.Crew.IsActiveAt("bileh123", DateTime.UtcNow)));   // the logs: not playing
        Assert.Null(f.Live.Read(t => t.Crew.OverrideOf("bileh123")));
    }
}
