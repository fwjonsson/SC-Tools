using System.Globalization;
using System.Net;
using SCTools.Tests.Support;

namespace SCTools.Tests.ServerPages;

/// <summary>The Party tool: who is playing with whom (the game's own party, from everybody's log), everybody else, and a player's own page.</summary>
public class PartyToolTests
{
    static string Id(DateTime t) => t.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    [Fact]
    public async Task The_party_tool_shows_who_plays_with_whom_and_what_there_is_to_know_about_a_player()
    {
        using var f = new ServerFactory();
        Assert.StartsWith("/login", (await f.Browser().GetAsync("/party")).Headers.Location!.PathOf());   // never for somebody not logged in
        var now = DateTime.UtcNow;
        // zzoega made a party and zzatvo (no tracker) joined it, then he jumped in his C2; bileh123 plays alone; pilot_x has a key and has not played
        var mine = new GameLogWriter().At(now.AddMinutes(-20)).Login("zzoega").Minutes(1).PartyCreated().Minutes(1).MemberJoined("zzatvo").Minutes(15).QuantumSelected("Gaslight");
        Assert.True(f.Live.Uploads.Add("zzoega", Id(now.AddMinutes(-20)), 0, 1000, mine.Lines.ToList(), now).Ok);
        var his = new GameLogWriter().At(now.AddMinutes(-10)).Login("bileh123").Minutes(5);
        Assert.True(f.Live.Uploads.Add("bileh123", Id(now.AddMinutes(-10)), 0, 1000, his.Lines.ToList(), now).Ok);
        f.AddKey("pilot_x");
        var admin = await f.Admin();

        var page = WebUtility.HtmlDecode(await admin.GetStringAsync("/party"));   // only the admin until it is shown
        Assert.Contains("zzoega's party", page);
        Assert.Contains("href=\"/party/zzatvo\"", page);
        Assert.Contains("CRUS Starlifter C2", page);   // the ship of his last quantum jump (not in a fleet: as the logs call it)
        Assert.Contains("href=\"/party/bileh123\"", page);
        Assert.Contains("Not playing", page);
        Assert.Contains("href=\"/party/pilot_x\"", page);
        Assert.Contains("3 playing in 2 parties", WebUtility.HtmlDecode(await admin.GetStringAsync("/")));   // its card

        var zzoega = WebUtility.HtmlDecode(await admin.GetStringAsync("/party/zzoega"));
        Assert.Contains("<td>Playing</td>", zzoega);
        Assert.Contains("zzoega's party: zzatvo, zzoega", zzoega);
        Assert.Contains("(his last quantum jump", zzoega);
        Assert.Contains("No ships on his page yet.", zzoega);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/party/nobody")).StatusCode);

        var player = await f.Player(f.AddKey("bileh123"));
        Assert.StartsWith("/login/denied", (await player.GetAsync("/party")).Headers.Location!.PathOf());
        await ServerFactory.Send(admin, "/admin/tools", "/admin/tools/set", ("id", "party"), ("value", "true"));
        Assert.Equal(HttpStatusCode.OK, (await player.GetAsync("/party/zzoega")).StatusCode);
    }
}
