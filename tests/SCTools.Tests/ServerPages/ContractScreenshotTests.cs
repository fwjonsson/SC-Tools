using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SCTools.Server;
using SCTools.Server.Data;

namespace SCTools.Tests.ServerPages;

/// <summary>
/// A contract screenshot read into the Mission Library (#437, Frank 31 Oct 2026): only the admin, from a mission row's 📷 button (a little box beside the
/// row: the Mission Library, the Companion, the Hauling Planner's Session tab), for that mission alone (never one guessed by its name, never one added); the
/// server makes values of what his browser read and finds the faction in his list; he saves what he checked. And his lists of factions and categories the
/// drop-downs pick from, and the Category, XP and Legality he fills in.
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
            new MissionKindRow { Key = "hauling:2 to ruin", Title = "Hauling: 2 To Ruin" },
        });
        library.Set("patrol to prevent possible attack [250 rep] [bp]*", "giver", "Intersec Defense Solutions", "admin", DateTime.UtcNow);
        library.Set("space patrol [150 rep]", "giver", "Headhunters", "admin", DateTime.UtcNow);
        return f;
    }

    static async Task<JsonElement> Post(HttpClient admin, string action, params (string, string)[] fields) =>
        JsonDocument.Parse(await (await ServerFactory.Send(admin, "/missions", action, fields)).Content.ReadAsStringAsync()).RootElement;

    [Fact]
    public async Task Only_the_admin_has_the_camera_buttons()
    {
        using var f = Library();
        var admin = await f.Admin();
        await ServerFactory.Send(admin, "/admin/tools", "/admin/tools/set", ("id", "missions"), ("value", "true"));
        var page = await admin.GetStringAsync("/missions");
        Assert.Contains("data-shot-for=\"space patrol [150 rep]\"", page);   // every row has its 📷
        Assert.Contains("/screenshot.js", page);
        Assert.DoesNotContain("id=\"shot\"", page);   // no paste box of its own

        var player = await f.Player(f.AddKey("zzatvo"));
        var seen = await player.GetStringAsync("/missions");
        Assert.DoesNotContain("data-shot-for", seen);
        Assert.DoesNotContain("screenshot.js", seen);
        var tried = await player.PostAsync("/admin/missions/screenshot", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["key"] = "space patrol [150 rep]", ["__RequestVerificationToken"] = ServerFactory.Token(seen),
        }));
        Assert.NotEqual(HttpStatusCode.OK, tried.StatusCode);   // not his
    }

    [Fact]
    public async Task What_was_read_becomes_values_for_the_mission_of_the_button()
    {
        using var f = Library();
        var admin = await f.Admin();
        var d = await Post(admin, "/admin/missions/screenshot", ("key", "patrol to prevent possible attack [250 rep] [bp]*"),
            ("title", "Patrol to Prevent Possible Attack [ 250 Rep]\n[BPT*"), ("reward", "235,000"), ("contractor", "i Intersec Defense Solutions"),
            ("details", "Complete the patrol and we'll send the main contractor a bonus of\n18 MG Scrip.\nReputation Awarded: 250"));
        Assert.Equal((235_000L, 18L, 250L, "Intersec Defense Solutions"),
            (d.GetProperty("reward").GetInt64(), d.GetProperty("mg").GetInt64(), d.GetProperty("xp").GetInt64(), d.GetProperty("faction").GetString()));
        Assert.Equal("patrol to prevent possible attack [250 rep] [bp]*", d.GetProperty("mission").GetProperty("key").GetString());
        Assert.True(d.GetProperty("notes").GetArrayLength() == 0, d.GetProperty("notes").ToString());

        // a reward not read for sure is a note; a misread faction is the one of the list nearest to it
        d = await Post(admin, "/admin/missions/screenshot", ("key", "space patrol [150 rep]"), ("title", "Space Patrol [150 Rep]"), ("reward", "2"),
            ("contractor", "a Headtiunters"), ("details", ""));
        Assert.Equal("Headhunters", d.GetProperty("faction").GetString());
        Assert.Equal(JsonValueKind.Null, d.GetProperty("reward").ValueKind);
        Assert.Contains("type it in", d.GetProperty("notes")[0].GetString());
        Assert.Equal(150, d.GetProperty("xp").GetInt64());   // no "Reputation Awarded": the [150 Rep] of its name

        // the Hauling Planner's Completed Contracts give a mission by its name, or its contract line: the same mission, not a similar one
        d = await Post(admin, "/admin/missions/screenshot", ("key", "Space Patrol [150 Rep]"), ("title", "Eliminate Vanduul Ship [300 Rep]"), ("reward", "87,000"),
            ("contractor", "Headhunters"), ("details", ""));
        Assert.Equal("space patrol [150 rep]", d.GetProperty("mission").GetProperty("key").GetString());
        Assert.Contains("is it really Space Patrol [150 Rep]", d.GetProperty("notes")[0].GetString());   // the wrong row's 📷?
        d = await Post(admin, "/admin/missions/screenshot", ("key", "2 To Ruin"), ("title", ""), ("reward", "1,268,250"), ("contractor", ""), ("details", ""));
        Assert.Equal("hauling:2 to ruin", d.GetProperty("mission").GetProperty("key").GetString());
    }

    [Fact]
    public async Task Nothing_is_read_without_a_mission_and_a_mission_is_never_added()
    {
        using var f = Library();
        var admin = await f.Admin();
        var d = await Post(admin, "/admin/missions/screenshot", ("title", "Space Patrol [150 Rep]"), ("reward", "87,000"), ("contractor", "Headhunters"), ("details", ""));
        Assert.Contains("Choose the mission first", d.GetProperty("error").GetString());
        d = await Post(admin, "/admin/missions/screenshot", ("key", "Looking For Trouble [200 Rep]"), ("title", "Looking For Trouble [200 Rep]"), ("reward", "158,750"),
            ("contractor", "Headhunters"), ("details", ""));
        Assert.Contains("not in the Mission Library", d.GetProperty("error").GetString());
        d = await Post(admin, "/admin/missions/screenshot/save", ("key", "looking for trouble [200 rep]"), ("auec", "158750"));
        Assert.False(d.GetProperty("ok").GetBoolean());
        var library = f.Services.GetRequiredService<MissionLibrary>();
        Assert.DoesNotContain(library.Entries(), e => e.Row.Key.StartsWith("looking for trouble", StringComparison.Ordinal));

        // what he checked is saved to the mission of the button
        d = await Post(admin, "/admin/missions/screenshot/save", ("key", "patrol to prevent possible attack [250 rep] [bp]*"), ("auec", "235000"), ("mg", "18"),
            ("council", ""), ("giver", "Intersec Defense Solutions"), ("xp", "250"));
        Assert.True(d.GetProperty("ok").GetBoolean());
        var patrol = library.Entries().Single(e => e.Row.Key == "patrol to prevent possible attack [250 rep] [bp]*");
        Assert.Equal((235_000L, 18L, (long?)null, 250L), (patrol.Row.Auec!.Value, patrol.Row.MgScrip!.Value, patrol.Row.CouncilScrip, patrol.Xp!.Value));
    }

    [Fact]
    public async Task The_admin_keeps_his_lists_of_factions_and_categories()
    {
        using var f = Library();
        var admin = await f.Admin();
        var library = f.Services.GetRequiredService<MissionLibrary>();
        // the list starts with what the library has
        Assert.Equal(new[] { "Headhunters", "Intersec Defense Solutions" }, library.Choices(MissionLibrary.FactionList));

        await ServerFactory.Send(admin, "/missions", "/admin/missions/choices/add", ("list", "faction"), ("value", "Foxwell Enforcement"));
        await ServerFactory.Send(admin, "/missions", "/admin/missions/choices/add", ("list", "category"), ("value", "Mercenary"));
        await ServerFactory.Send(admin, "/missions", "/admin/missions/choices/remove", ("list", "faction"), ("value", "Headhunters"));
        Assert.Equal(new[] { "Foxwell Enforcement", "Intersec Defense Solutions" }, library.Choices(MissionLibrary.FactionList));
        Assert.Equal(new[] { "Mercenary" }, library.Choices(MissionLibrary.CategoryList));

        var page = System.Net.WebUtility.HtmlDecode(await admin.GetStringAsync("/missions"));
        Assert.Contains("id=\"choices\"", page);   // the box of the lists
        // a mission that has a faction taken out of the list keeps it: its drop-down still offers it
        Assert.Contains("<option value=\"Headhunters\" selected=\"selected\">Headhunters</option>", page);
        Assert.Contains("<option value=\"Foxwell Enforcement\">Foxwell Enforcement</option>", page);
        Assert.Contains("<option value=\"Mercenary\">Mercenary</option>", page);   // the Category drop-down
        Assert.Contains("<option value=\"Illegal\">Illegal</option>", page);   // Legality: Legal or Illegal
    }

    [Fact]
    public async Task The_admin_fills_in_the_category_the_xp_and_the_legality()
    {
        using var f = Library();
        var admin = await f.Admin();
        foreach (var (field, value) in new[] { ("category", "Mercenary"), ("xp", "250"), ("legality", "Illegal") })
            await ServerFactory.Send(admin, "/missions", "/admin/missions/set", ("id", "space patrol [150 rep]"), ("field", field), ("value", value));
        var e = f.Services.GetRequiredService<MissionLibrary>().Entries().Single(x => x.Row.Key == "space patrol [150 rep]");
        Assert.Equal(("Mercenary", 250L, "Illegal"), (e.Category, e.Xp!.Value, e.Legality));   // one typed in wins over the Wiki's

        // everybody sees them (the admin's own as text, not in grey)
        await ServerFactory.Send(admin, "/admin/tools", "/admin/tools/set", ("id", "missions"), ("value", "true"));
        var page = System.Net.WebUtility.HtmlDecode(await (await f.Player(f.AddKey("zzatvo"))).GetStringAsync("/missions"));
        Assert.Contains("<td>Mercenary</td>", page);
        Assert.Contains("<td>Illegal</td>", page);
    }
}
