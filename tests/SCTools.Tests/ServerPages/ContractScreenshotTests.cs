using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SCTools.Server;
using SCTools.Server.Data;

namespace SCTools.Tests.ServerPages;

/// <summary>
/// A contract screenshot read into the Mission Library (#437, Frank 31 Oct 2026): only the admin has the paste box, and only for the mission whose 📷 button
/// he pressed (never one guessed by its name, never one added); the server makes values of what his browser read and finds the faction; he saves what he
/// checked.
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
        Assert.Matches("<section class=\"card shot\" id=\"shot\"[^>]* hidden>", page);   // closed until a mission's 📷 opens it
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
        var res = await ServerFactory.Send(admin, "/missions", "/admin/missions/screenshot", ("key", "patrol to prevent possible attack [250 rep] [bp]*"),
            ("title", "Patrol to Prevent Possible Attack [ 250 Rep]\n[BPT*"), ("reward", "235,000"), ("contractor", "Intersec Defense Solutions"),
            ("details", "Complete the patrol and we'll send the main contractor a bonus of\n18 MG Scrip."));
        var d = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal((235_000L, 18L, "Intersec Defense Solutions"), (d.GetProperty("reward").GetInt64(), d.GetProperty("mg").GetInt64(), d.GetProperty("faction").GetString()));
        var mission = d.GetProperty("choices")[0];
        Assert.Equal("patrol to prevent possible attack [250 rep] [bp]*", mission.GetProperty("key").GetString());
        Assert.Equal(JsonValueKind.Null, mission.GetProperty("auec").ValueKind);   // nothing typed in yet
        Assert.True(d.GetProperty("notes").GetArrayLength() == 0, d.GetProperty("notes").ToString());

        // the reflection's "a" and a misread letter: the faction is the known one; a reward not read for sure is a note
        res = await ServerFactory.Send(admin, "/missions", "/admin/missions/screenshot", ("key", "space patrol [150 rep]"), ("title", "Space Patrol [150 Rep]"), ("reward", "2"),
            ("contractor", "a Headtiunters"), ("details", ""));
        d = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Headhunters", d.GetProperty("faction").GetString());
        Assert.Equal(JsonValueKind.Null, d.GetProperty("reward").ValueKind);
        Assert.Contains("type it in", d.GetProperty("notes")[0].GetString());
    }

    [Fact]
    public async Task What_the_admin_checked_is_saved_and_a_mission_is_never_added()
    {
        using var f = Library();
        var admin = await f.Admin();
        var res = await ServerFactory.Send(admin, "/missions", "/admin/missions/screenshot/save",
            ("key", "patrol to prevent possible attack [250 rep] [bp]*"), ("auec", "235000"), ("mg", "18"), ("council", ""), ("giver", "Intersec Defense Solutions"));
        Assert.True(JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("ok").GetBoolean());
        var library = f.Services.GetRequiredService<MissionLibrary>();
        var patrol = library.Entries().Single(e => e.Row.Key == "patrol to prevent possible attack [250 rep] [bp]*").Row;
        Assert.Equal((235_000L, 18L, (long?)null), (patrol.Auec!.Value, patrol.MgScrip!.Value, patrol.CouncilScrip));

        // without a mission chosen by its 📷 nothing is read into the library: not one guessed by its name, not one added (Frank, 31 Oct 2026: a similar
        // name could be another mission; a misread name would be a row no log ever joins)
        res = await ServerFactory.Send(admin, "/missions", "/admin/missions/screenshot",
            ("title", "Space Patrol [150 Rep]"), ("reward", "87,000"), ("contractor", "Headhunters"), ("details", ""));
        Assert.Contains("Choose the mission first", JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetString());
        res = await ServerFactory.Send(admin, "/missions", "/admin/missions/screenshot/save", ("key", "looking for trouble [200 rep]"), ("auec", "158750"));
        Assert.False(JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("ok").GetBoolean());
        Assert.DoesNotContain(library.Entries(), e => e.Row.Key.StartsWith("looking for trouble", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_mission_s_camera_button_opens_the_box_for_that_mission()
    {
        using var f = Library();
        var library = f.Services.GetRequiredService<MissionLibrary>();
        library.Kinds(new[] { new MissionKindRow { Key = "hauling:2 to ruin", Title = "Hauling: 2 To Ruin" } });
        var admin = await f.Admin();

        // every row of the library has its button
        var page = await admin.GetStringAsync("/missions");
        Assert.Contains("data-shot-for=\"space patrol [150 rep]\"", page);

        // from another tool: by its key (the Companion, the Active Contracts), by its name or its contract line (the Completed Contracts)
        Assert.Contains("data-shot-key=\"space patrol [150 rep]\"", await admin.GetStringAsync("/missions?shot=space%20patrol%20%5B150%20rep%5D"));
        Assert.Contains("data-shot-key=\"space patrol [150 rep]\"", await admin.GetStringAsync("/missions?shot=Space%20Patrol%20%5B150%20Rep%5D"));
        Assert.Contains("data-shot-key=\"hauling:2 to ruin\"", await admin.GetStringAsync("/missions?shot=2%20To%20Ruin"));
        Assert.Contains("No mission of the library is", await admin.GetStringAsync("/missions?shot=Nothing%20Like%20It"));

        // for that mission: a screenshot of another one gets a note, and it is saved to this one
        var res = await ServerFactory.Send(admin, "/missions", "/admin/missions/screenshot",
            ("key", "space patrol [150 rep]"), ("title", "Eliminate Vanduul Ship [300 Rep]"), ("reward", "87,000"), ("contractor", "Headhunters"), ("details", ""));
        var d = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("space patrol [150 rep]", Assert.Single(d.GetProperty("choices").EnumerateArray()).GetProperty("key").GetString());
        Assert.Contains("is it really Space Patrol [150 Rep]", d.GetProperty("notes")[0].GetString());
    }
}
