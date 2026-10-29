using System.Text.Json;

namespace SCTools.Tests.ServerPages;

/// <summary>The admin's Lost Cargo pick in the Cargo runs table of the Hauling Planner's Session tab: sent in the background to /hauling/runs/lost.</summary>
public class RunsLostTests
{
    [Fact]
    public async Task The_admin_picks_the_lost_cargo_of_a_run_in_the_table_and_nobody_else_can()
    {
        using var f = new ServerFactory();
        var admin = await f.Admin();
        f.Live.Refresh(DateTime.UtcNow);
        var data = JsonDocument.Parse(await admin.GetStringAsync("/data.json")).RootElement;
        Assert.Equal("/hauling/runs/lost", data.GetProperty("runsLost").GetString());   // where the pick goes; the keys of the runs beside the table
        Assert.Equal(JsonValueKind.Array, data.GetProperty("runKeys").ValueKind);

        var page = await admin.GetStringAsync("/hauling");
        using var req = new HttpRequestMessage(HttpMethod.Post, "/hauling/runs/lost")
        {
            Content = new FormUrlEncodedContent(new[] { KeyValuePair.Create("key", "no-such-run"), KeyValuePair.Create("level", "All"),
                KeyValuePair.Create("__RequestVerificationToken", ServerFactory.Token(page)) }),
        };
        req.Headers.Add("X-Autosave", "1");
        var answer = JsonDocument.Parse(await (await admin.SendAsync(req)).Content.ReadAsStringAsync()).RootElement;
        Assert.False(answer.GetProperty("ok").GetBoolean());
        Assert.Contains("not in the session", answer.GetProperty("said").GetString());
        var plain = await ServerFactory.Send(admin, "/hauling", "/hauling/runs/lost", ("key", "no-such-run"), ("level", "Little"));
        Assert.Equal("/hauling/session", plain.Headers.Location!.PathOf());   // without the background header: back to the tab

        var player = await f.Player(f.AddKey("zzoega"));   // the pick is the admin's
        Assert.StartsWith("/login/denied", (await ServerFactory.Send(player, "/hauling", "/hauling/runs/lost", ("key", "x"), ("level", "All"))).Headers.Location!.PathOf());
        Assert.StartsWith("/login", (await f.Browser().PostAsync("/hauling/runs/lost", new FormUrlEncodedContent(Array.Empty<KeyValuePair<string, string>>()))).Headers.Location!.PathOf());
    }
}
