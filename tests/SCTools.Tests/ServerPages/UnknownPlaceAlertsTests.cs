using System.Net;
using System.Text.Json;
using SCTools.Tests.Support;

namespace SCTools.Tests.ServerPages;

/// <summary>
/// The admin's notice of places the map does not know (#414, Frank 31 Oct 2026): a toast on every page he has open when somebody else meets one for the
/// first time, and when one of his accounts comes to one that is still unknown, once per visit; a card on his Companion with Map places' form and Later.
/// RR_JP_NyxCastra is the Castra gateway in Nyx, a system the map does not have: an area it does not know.
/// </summary>
public class UnknownPlaceAlertsTests
{
    const string Castra = "RR_JP_NyxCastra";

    static async Task<JsonElement> News(HttpClient admin, int? after = null) =>
        JsonDocument.Parse(await admin.GetStringAsync("/admin/places/unknown.json" + (after is { } a ? "?after=" + a : ""))).RootElement;

    static int Seq(JsonElement d) => d.GetProperty("seq").GetInt32();

    // a sighting of the area in this player's log, at this time (after the server started: what was before it is not news)
    static void Seen(ServerFactory f, string player, DateTime at, string file) =>
        f.Upload(player, new GameLogWriter().At(at).Login(player).Inventory(player, Castra).Lines.ToList(), file);

    [Fact]
    public async Task Somebody_elses_first_sighting_is_said_once_and_his_own_once_per_visit_with_a_card_until_later()
    {
        using var f = new ServerFactory();
        var admin = await f.Admin();
        var now = DateTime.UtcNow.AddSeconds(5);

        var d = await News(admin);   // the first ask of a page: no notices, only where they stand
        Assert.Empty(d.GetProperty("alerts").EnumerateArray());
        var seq = Seq(d);

        // somebody else meets it: one notice; his next visit: none
        Seen(f, "bileh123", now, "b1.log");
        d = await News(admin, seq);
        var a = Assert.Single(d.GetProperty("alerts").EnumerateArray());
        Assert.Equal((Castra, "Area", "bileh123", false), (a.GetProperty("text").GetString(), a.GetProperty("kind").GetString(), a.GetProperty("player").GetString(), a.GetProperty("his").GetBoolean()));
        Assert.Empty(d.GetProperty("here").EnumerateArray());   // not his: no card
        seq = Seq(d);
        Seen(f, "bileh123", now.AddMinutes(20), "b2.log");
        Assert.Empty((await News(admin, seq)).GetProperty("alerts").EnumerateArray());

        // he comes there himself (the admin's own player): a notice again, and a card with every place to pick
        Seen(f, ServerFactory.AdminName, now.AddMinutes(1), "a1.log");
        d = await News(admin, seq);
        a = Assert.Single(d.GetProperty("alerts").EnumerateArray());
        Assert.True(a.GetProperty("his").GetBoolean());
        var card = Assert.Single(d.GetProperty("here").EnumerateArray());
        Assert.Equal((Castra, "Location"), (card.GetProperty("text").GetString(), card.GetProperty("kindKey").GetString()));
        Assert.Contains(d.GetProperty("places").EnumerateArray().SelectMany(g => g.GetProperty("places").EnumerateArray()), p => p.GetProperty("key").GetString() == "nyx/levski");
        seq = Seq(d);

        // still there two minutes later: the same visit, no notice
        Seen(f, ServerFactory.AdminName, now.AddMinutes(3), "a2.log");
        Assert.Empty((await News(admin, seq)).GetProperty("alerts").EnumerateArray());

        // Later: the card goes (the place stays on Map places)
        var res = await ServerFactory.Send(admin, "/companion", "/admin/places/later", ("text", Castra), ("kind", "Location"));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Empty((await News(admin, seq)).GetProperty("here").EnumerateArray());
        Assert.Contains(Castra, await admin.GetStringAsync("/admin/map"));

        // back there after more than ten minutes: a new visit, a notice and the card again
        Seen(f, ServerFactory.AdminName, now.AddMinutes(15), "a3.log");
        d = await News(admin, seq);
        Assert.True(Assert.Single(d.GetProperty("alerts").EnumerateArray()).GetProperty("his").GetBoolean());
        Assert.Single(d.GetProperty("here").EnumerateArray());
        seq = Seq(d);

        // named from the card, in the background (he stays on the Companion; Frank, 31 Oct 2026): the sentence back, and it is not unknown any more
        var html = await admin.GetStringAsync("/companion");
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["text"] = Castra, ["key"] = "nyx/stanton-gateway", ["back"] = "/companion", ["__RequestVerificationToken"] = ServerFactory.Token(html),
        });
        var post = new HttpRequestMessage(HttpMethod.Post, "/admin/places/alias") { Content = form };
        post.Headers.Add("X-Autosave", "1");
        res = await admin.SendAsync(post);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains(Castra, JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("said").GetString());
        Seen(f, ServerFactory.AdminName, now.AddMinutes(40), "a4.log");
        d = await News(admin, seq);
        Assert.Empty(d.GetProperty("alerts").EnumerateArray());
        Assert.Empty(d.GetProperty("here").EnumerateArray());
    }

    [Fact]
    public async Task Only_the_admin_gets_the_notices_and_the_script_and_the_cards_place()
    {
        using var f = new ServerFactory();
        var admin = await f.Admin();
        Assert.Contains("alerts.js", await admin.GetStringAsync("/history"));   // on every page he has open
        Assert.Contains("id=\"unknownHere\"", await admin.GetStringAsync("/companion"));
        await ServerFactory.Send(admin, "/admin/tools", "/admin/tools/set", ("id", "companion"), ("value", "true"));
        var player = await f.Player(f.AddKey("bileh123"));
        Assert.DoesNotContain("alerts.js", await player.GetStringAsync("/hauling"));
        Assert.DoesNotContain("unknownHere", await player.GetStringAsync("/companion"));
        Assert.StartsWith("/login/denied", (await player.GetAsync("/admin/places/unknown.json")).Headers.Location!.PathOf());
        Assert.StartsWith("/login", (await f.Browser().GetAsync("/admin/places/unknown.json")).Headers.Location!.PathOf());
    }
}
