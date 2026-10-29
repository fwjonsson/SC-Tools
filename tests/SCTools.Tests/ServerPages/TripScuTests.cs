using System.Text.Json;

namespace SCTools.Tests.ServerPages;

/// <summary>The SCU a hauler types for a drop-off of his own trip on the Trip Planner: sent in the background to /hauling/trips/scu, his own trips only.</summary>
public class TripScuTests
{
    [Fact]
    public async Task The_hauler_types_the_SCU_of_his_own_drop_off_and_the_admin_anybody_s()
    {
        using var f = new ServerFactory();
        var admin = await f.Admin();
        f.Live.Refresh(DateTime.UtcNow);
        var data = JsonDocument.Parse(await admin.GetStringAsync("/data.json")).RootElement;
        Assert.Equal("/hauling/trips/scu", data.GetProperty("planner").GetProperty("tripScu").GetString());   // where it goes

        // the admin types for any drop-off (here one the session does not know: it is kept all the same, for when the contract comes)
        var answer = await Quiet(admin, "m1|dropoff_0", "152");
        Assert.True(answer.GetProperty("ok").GetBoolean());
        Assert.EndsWith("152 SCU.", answer.GetProperty("said").GetString());
        Assert.Equal(152, f.Live.Read(t => t.State.Data.TypedScu.GetValueOrDefault("m1|dropoff_0")));
        Assert.False((await Quiet(admin, "m1|dropoff_0", "lots")).GetProperty("ok").GetBoolean());
        Assert.True((await Quiet(admin, "m1|dropoff_0", "")).GetProperty("ok").GetBoolean());   // empty: forgotten
        Assert.Equal(0, f.Live.Read(t => t.State.Data.TypedScu.GetValueOrDefault("m1|dropoff_0")));

        // a player: only a drop-off on his own Current Route (none here: refused), never somebody else's
        var player = await f.Player(f.AddKey("zzoega"));
        var refused = await Quiet(player, "m1|dropoff_0", "100");
        Assert.False(refused.GetProperty("ok").GetBoolean());
        Assert.Contains("Only the hauler", refused.GetProperty("said").GetString());
        Assert.Equal(0, f.Live.Read(t => t.State.Data.TypedScu.GetValueOrDefault("m1|dropoff_0")));
        Assert.StartsWith("/login", (await f.Browser().PostAsync("/hauling/trips/scu", new FormUrlEncodedContent(Array.Empty<KeyValuePair<string, string>>()))).Headers.Location!.PathOf());
        Assert.Contains("function tripScuFields", await f.Browser().GetStringAsync("/hauling.js"));
    }

    static async Task<JsonElement> Quiet(HttpClient c, string key, string value)
    {
        var page = await c.GetStringAsync("/hauling");
        using var req = new HttpRequestMessage(HttpMethod.Post, "/hauling/trips/scu")
        {
            Content = new FormUrlEncodedContent(new[] { KeyValuePair.Create("key", key), KeyValuePair.Create("value", value),
                KeyValuePair.Create("__RequestVerificationToken", ServerFactory.Token(page)) }),
        };
        req.Headers.Add("X-Autosave", "1");
        return JsonDocument.Parse(await (await c.SendAsync(req)).Content.ReadAsStringAsync()).RootElement;
    }
}
