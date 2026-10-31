using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SCTools.Server;
using SCTools.Server.Data;

namespace SCTools.Tests.ServerPages;

/// <summary>
/// A contract screenshot read into the Mission Library (#437, Frank 31 Oct 2026): only the admin has the paste box; the server makes values of what his
/// browser read and finds the mission and the faction; he saves what he checked (a mission nobody played yet is added).
/// </summary>
public class ContractScreenshotTests
{
    static ServerFactory Library()
    {
        var f = new ServerFactory();
        var library = f.Services.GetRequiredService<MissionLibrary>();
        library.Kinds(new[]
        {
            new MissionKindRow { Key = "patrol to prevent possible attack [250 rep] [bp]*", Title = "Patrol to Prevent Possible Attack [250 Rep] [BP]*" },
            new MissionKindRow { Key = "space patrol [150 rep]", Title = "Space Patrol [150 Rep]" },
        });
        library.Set("patrol to prevent possible attack [250 rep] [bp]*", "giver", "Intersec Defense Solutions", "admin", DateTime.UtcNow);
        library.Set("space patrol [150 rep]", "giver", "Headhunters", "admin", DateTime.UtcNow);
        return f;
    }

    [Fact]
    public async Task Only_the_admin_has_the_paste_box()
    {
        using var f = Library();
        var admin = await f.Admin();
        await ServerFactory.Send(admin, "/admin/tools", "/admin/tools/set", ("id", "missions"), ("value", "true"));
        var page = await admin.GetStringAsync("/missions");
        Assert.Contains("id=\"shot\"", page);
        Assert.Contains("/screenshot.js", page);

        var player = await f.Player(f.AddKey("zzatvo"));
        var seen = await player.GetStringAsync("/missions");
        Assert.DoesNotContain("id=\"shot\"", seen);
        Assert.DoesNotContain("screenshot.js", seen);
        var token = ServerFactory.Token(seen);
        var tried = await player.PostAsync("/admin/missions/screenshot", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["title"] = "Space Patrol", ["__RequestVerificationToken"] = token,
        }));
        Assert.NotEqual(HttpStatusCode.OK, tried.StatusCode);   // not his
    }

    [Fact]
    public async Task What_was_read_becomes_values_with_the_mission_and_the_faction_it_is()
    {
        using var f = Library();
        var admin = await f.Admin();
        var res = await ServerFactory.Send(admin, "/missions", "/admin/missions/screenshot",
            ("title", "Patrol to Prevent Possible Attack [ 250 Rep]\n[BPT*"), ("reward", "235,000"), ("contractor", "Intersec Defense Solutions"),
            ("details", "Complete the patrol and we'll send the main contractor a bonus of\n18 MG Scrip."));
        var d = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal((235_000L, 18L, "Intersec Defense Solutions"), (d.GetProperty("reward").GetInt64(), d.GetProperty("mg").GetInt64(), d.GetProperty("faction").GetString()));
        var mission = d.GetProperty("choices")[0];
        Assert.Equal("patrol to prevent possible attack [250 rep] [bp]*", mission.GetProperty("key").GetString());
        Assert.Equal(JsonValueKind.Null, mission.GetProperty("auec").ValueKind);   // nothing typed in yet
        Assert.True(d.GetProperty("notes").GetArrayLength() == 0, d.GetProperty("notes").ToString());

        // the reflection's "a" and a misread letter: the faction is the known one; a reward not read for sure is a note
        res = await ServerFactory.Send(admin, "/missions", "/admin/missions/screenshot", ("title", "Space Patrol [150 Rep]"), ("reward", "2"), ("contractor", "a Headtiunters"), ("details", ""));
        d = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Headhunters", d.GetProperty("faction").GetString());
        Assert.Equal(JsonValueKind.Null, d.GetProperty("reward").ValueKind);
        Assert.Contains("type it in", d.GetProperty("notes")[0].GetString());
    }

    [Fact]
    public async Task What_the_admin_checked_is_saved_and_a_new_mission_is_added()
    {
        using var f = Library();
        var admin = await f.Admin();
        var res = await ServerFactory.Send(admin, "/missions", "/admin/missions/screenshot/save",
            ("key", "patrol to prevent possible attack [250 rep] [bp]*"), ("auec", "235000"), ("mg", "18"), ("council", ""), ("giver", "Intersec Defense Solutions"));
        Assert.True(JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("ok").GetBoolean());
        // an offer nobody took yet: added by its name, with what was read
        res = await ServerFactory.Send(admin, "/missions", "/admin/missions/screenshot/save",
            ("key", ""), ("title", "Looking For Trouble [200 Rep]"), ("auec", "158750"), ("council", "14"), ("giver", "Headhunters"));
        Assert.True(JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("ok").GetBoolean());

        var entries = f.Services.GetRequiredService<MissionLibrary>().Entries().ToDictionary(e => e.Row.Key);
        var patrol = entries["patrol to prevent possible attack [250 rep] [bp]*"].Row;
        Assert.Equal((235_000L, 18L, (long?)null), (patrol.Auec!.Value, patrol.MgScrip!.Value, patrol.CouncilScrip));
        var trouble = entries["looking for trouble [200 rep]"].Row;
        Assert.Equal(("Looking For Trouble [200 Rep]", 158_750L, 14L, "Headhunters"), (trouble.Title, trouble.Auec!.Value, trouble.CouncilScrip!.Value, trouble.Giver));
    }
}
