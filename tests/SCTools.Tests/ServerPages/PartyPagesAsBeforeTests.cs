using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SCTools.Server;
using SCTools.Tests.Support;

namespace SCTools.Tests.ServerPages;

/// <summary>
/// What the pages that ask about the party showed before the party tracker (recorded on 31 Oct 2026): the Party tool and its panel, who a player may see
/// (the Companion, the Party tool), the Gauntlet Tracker's party and the Hauling Planner's parties in data.json. The party tracker has to show the same.
/// </summary>
public class PartyPagesAsBeforeTests
{
    static string Id(DateTime t) => t.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Every_page_asks_the_same_party()
    {
        using var f = new ServerFactory { Data = new AppData { Party = new List<string> { "zzoega", "zzatvo" } }, FollowParty = true };
        _ = f.Live;
        var now = DateTime.UtcNow;
        // zzoega made a party, zzatvo joined it, Bileh123 (no tracker) joined and zzatvo is its leader now; zzoii plays in a party of his own with Trila
        var zzoega = new GameLogWriter().At(now.AddMinutes(-40)).Login("zzoega").Minutes(1).PartyCreated().Minutes(1).MemberJoined("zzatvo")
            .Minutes(5).MemberJoined("Bileh123").Minutes(10).NewLeader("zzatvo");
        var zzatvo = new GameLogWriter().At(now.AddMinutes(-39.5)).Login("zzatvo").Minutes(1).PartyJoined().Minutes(5).MemberJoined("Bileh123")
            .Minutes(10).NewLeader("zzatvo");
        var zzoii = new GameLogWriter().At(now.AddMinutes(-30)).Login("zzoii").Minutes(1).PartyCreated().Minutes(1).MemberJoined("Trila");
        foreach (var (who, w) in new[] { ("zzoega", zzoega), ("zzatvo", zzatvo), ("zzoii", zzoii) })
            Assert.True(f.Live.Uploads.Add(who, Id(w.Lines.Count > 0 ? DateTime.Parse(w.Lines[0][1..25], null, DateTimeStyles.AdjustToUniversal) : now), 0, 1000,
                w.Lines.ToList(), now).Ok);
        f.Live.Refresh(now);
        var admin = await f.Admin();

        var panel = WebUtility.HtmlDecode(await admin.GetStringAsync("/party/panel"));
        Assert.Contains("5 playing in 2 parties", panel);
        Assert.Contains("zzatvo's party", panel);
        Assert.Contains("zzoii's party", panel);
        var page = WebUtility.HtmlDecode(await admin.GetStringAsync("/party"));
        Assert.Contains("zzatvo's party", page);
        Assert.Contains("href=\"/party/Bileh123\"", page);
        Assert.Contains("href=\"/party/Trila\"", page);

        // who each one may see: his in-game party (the Companion, the Party tool and every page cut to the viewer go by this)
        var audience = f.Services.GetRequiredService<Audience>();
        string Sees(string name) => string.Join(", ", audience.PartyOf(name, DateTime.UtcNow).Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal("Bileh123, zzatvo, zzoega", Sees("zzoega"));
        Assert.Equal("bileh123, zzatvo, zzoega", Sees("bileh123"));   // himself as asked
        Assert.Equal("Trila, zzoii", Sees("zzoii"));
        Assert.Equal("nobody", Sees("nobody"));

        // the Gauntlet Tracker and the Party tool's tick ask PartyService for the parties now
        var parties = f.Services.GetRequiredService<PartyService>().Parties(DateTime.UtcNow);
        Assert.Equal("zzatvo: Bileh123, zzatvo, zzoega | zzoii: Trila, zzoii", string.Join(" | ", parties.Select(p => p.Leader + ": " + string.Join(", ", p.Names))));

        // the Hauling Planner: the party follows the game's parties (Bileh123 became a member), and data.json has the parties with a member in them
        Assert.Equal(new[] { "zzoega", "zzatvo", "Bileh123" }, f.Live.Read(t => t.State.Data.Party.ToList()));
        var json = JsonDocument.Parse(f.Live.Json).RootElement.GetProperty("parties");
        var party = Assert.Single(json.EnumerateArray());
        Assert.Equal("zzatvo's party", party.GetProperty("title").GetString());
        Assert.Equal(new[] { "Bileh123", "zzatvo", "zzoega" }, party.GetProperty("names").EnumerateArray().Select(n => n.GetString()));
        Assert.True(f.Live.Read(t => t.Crew.IsActiveAt("Bileh123", DateTime.UtcNow)));
    }
}
