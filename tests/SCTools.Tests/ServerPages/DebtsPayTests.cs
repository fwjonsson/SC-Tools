using System.Net;
using System.Text.Json;
using SCTools.Tests.Support;

namespace SCTools.Tests.ServerPages;

/// <summary>The Payment tick on the Hauling Planner's Debts tab: the player who sent the money ticks it, nobody else (the admin on his own page).</summary>
public class DebtsPayTests
{
    [Fact]
    public async Task The_sender_ticks_a_payment_on_the_live_page_and_the_receiver_cannot()
    {
        using var f = new ServerFactory { Data = PlayedSession.Data() };
        f.Upload(PlayedSession.Player, PlayedSession.Writer().Minutes(1).Sent("Pilot_B", 100_000).Lines.ToList());
        var sender = await f.Player(f.AddKey(PlayedSession.Player));
        var receiver = await f.Player(f.AddKey("Pilot_B"));

        var data = JsonDocument.Parse(await sender.GetStringAsync("/data.json")).RootElement;
        Assert.Equal("/hauling/debts/payment", data.GetProperty("debtsPay").GetString());   // where the tick goes
        var edit = Assert.Single(data.GetProperty("debts").GetProperty("transfersEdit").EnumerateArray());
        var key = edit.GetProperty("key").GetString()!;
        Assert.Equal((PlayedSession.Player, false), (edit.GetProperty("from").GetString(), edit.GetProperty("payment").GetBoolean()));
        Assert.Equal(f.Live.Read(t => t.State.Data.Transfers.Single().Key), key);

        async Task<JsonElement> Tick(HttpClient c, string value)
        {
            var page = await c.GetStringAsync("/hauling");
            using var req = new HttpRequestMessage(HttpMethod.Post, "/hauling/debts/payment")
            {
                Content = new FormUrlEncodedContent(new[] { KeyValuePair.Create("key", key), KeyValuePair.Create("payment", value), KeyValuePair.Create("__RequestVerificationToken", ServerFactory.Token(page)) }),
            };
            req.Headers.Add("X-Autosave", "1");
            var res = await c.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            return JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        }
        var ok = await Tick(sender, "true");
        Assert.True(ok.GetProperty("ok").GetBoolean());
        Assert.Contains("pays off a debt", ok.GetProperty("said").GetString());
        Assert.True(f.Live.Read(t => t.State.Data.Transfers.Single().Payment));
        Assert.Equal("yes", JsonDocument.Parse(await sender.GetStringAsync("/data.json")).RootElement.GetProperty("debts").GetProperty("transfers").GetProperty("rows")[0].GetProperty("cells")[5].GetString());   // Payment, after # (31 Oct 2026)

        var denied = await Tick(receiver, "false");   // the one who got the money cannot say
        Assert.False(denied.GetProperty("ok").GetBoolean());
        Assert.Contains("Only " + PlayedSession.Player, denied.GetProperty("said").GetString());
        Assert.True(f.Live.Read(t => t.State.Data.Transfers.Single().Payment));

        var plain = await ServerFactory.Send(sender, "/hauling", "/hauling/debts/payment", ("key", key), ("payment", "false"));   // without the script: back to the tab
        Assert.Equal((HttpStatusCode.Redirect, "/hauling/debts"), (plain.StatusCode, plain.Headers.Location?.OriginalString));
        Assert.False(f.Live.Read(t => t.State.Data.Transfers.Single().Payment));
        Assert.StartsWith("/login", (await f.Browser().GetAsync("/hauling/debts/payment")).Headers.Location?.PathOf() ?? "/login");   // watching: no
    }
}
