using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SCTools.Gauntlet;
using SCTools.Missions;
using SCTools.Server;
using SCTools.Server.Controllers;
using SCTools.Server.Data;
using SCTools.Tests.Support;

namespace SCTools.Tests.ServerPages;

/// <summary>
/// The Mission Library: every mission the players took is there by itself (live logs and old ones), one row per name (the same mission at another place
/// has another ContractId); a logged in player types in the rewards, the giver and notes, saved one field at a time. The Gauntlet Tracker counts with its
/// scenarios' rewards.
/// </summary>
public class MissionLibraryTests
{
    const string C1 = "26b8773e-0b96-40b4-a33a-27a4e9877e75";
    const string C2 = "8d8fc20d-61ad-4433-a3f1-948d8e05104a";
    const string Rival = "Stop Rival Attack at Asteroid Mining Base <EM4>[200 Rep] [BP]*</EM4>";
    const string RivalShown = "Stop Rival Attack at Asteroid Mining Base [200 Rep] [BP]*";
    static readonly string RivalKey = MissionKinds.TitleKey(RivalShown)!;

    static string[] Mission(string mission, string contract, string title, string day = "2026-10-21") => new[]
    {
        $"<{day}T08:00:00.000Z> [Notice] <SHUDEvent_OnNotification> Added notification \"Contract Accepted: {title}: \" [84] to queue. New queue size: 1, MissionId: [{mission}], ObjectiveId: [] [Missions][Comms]",
        $"<{day}T08:00:00.500Z> [Notice] <CSCPlayerMissionLog::MissionStartCommsNotification> MissionStart comms notification will not be sent - This mission has no MissionStart comms setup. ContractId: [{contract}]. MissionId: {mission} [Missions][Comms]",
        $"<{day}T08:30:00.000Z> [Notice] <SHUDEvent_OnNotification> Added notification \"Contract Complete: {title}: \" [84] to queue. New queue size: 1, MissionId: [{mission}], ObjectiveId: [] [Missions][Comms]",
    };

    [Fact]
    public async Task The_missions_with_the_same_name_are_one_row_and_a_player_fills_in_the_rest()
    {
        using var f = new ServerFactory();
        var admin = await f.Admin();
        // the same mission from two places: another ContractId, the same name
        f.Upload("zzoega", Mission("11111111-1111-1111-1111-111111111111", C1, Rival)
            .Concat(Mission("22222222-2222-2222-2222-222222222222", C2, Rival)).ToArray());

        var page = WebUtility.HtmlDecode(await admin.GetStringAsync("/missions"));   // only the admin until the tool is shown
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(page, System.Text.RegularExpressions.Regex.Escape("value=\"" + RivalShown + "\"")));
        Assert.Contains(">2 / 2<", page);   // completed of taken
        Assert.Contains("Hauling: 2 To Ruin", page);   // the Hauling Planner's contract lines are there from the start

        var player = await f.Player(f.AddKey("bileh123"));
        Assert.Equal(HttpStatusCode.Redirect, (await player.GetAsync("/missions")).StatusCode);
        await ServerFactory.Send(admin, "/admin/tools", "/admin/tools/set", ("id", "missions"), ("value", "true"));

        var saved = await ServerFactory.Send(player, "/missions", "/missions/set", ("id", RivalKey), ("field", "auec"), ("value", "25,000"), ("back", "/missions?show=todo"));
        Assert.Equal("/missions?show=todo#" + MissionLibrary.Anchor(RivalKey), saved.Headers.Location?.OriginalString);
        await ServerFactory.Send(player, "/missions", "/missions/set", ("id", RivalKey), ("field", "council"), ("value", "12"));
        await ServerFactory.Send(player, "/missions", "/missions/set", ("id", RivalKey), ("field", "giver"), ("value", "Red Wind"));
        await ServerFactory.Send(player, "/missions", "/missions/set", ("id", RivalKey), ("field", "mg"), ("value", "lots"));   // not a number: not saved
        var entry = f.Services.GetRequiredService<MissionLibrary>().Entries().Single(e => e.Row.Key == RivalKey);
        Assert.Equal((25_000L, 12L, (long?)null, "Red Wind", "bileh123"), (entry.Row.Auec!.Value, entry.Row.CouncilScrip!.Value, entry.Row.MgScrip, entry.Row.Giver, entry.Row.EditedBy));
        var withGiver = await player.GetStringAsync("/missions");
        Assert.Contains("Not saved", withGiver);
        Assert.Contains("<option value=\"Red Wind\"></option>", withGiver);   // to pick in the other Giver fields
        Assert.Contains("list=\"givers\"", withGiver);
        Assert.DoesNotContain(RivalShown, WebUtility.HtmlDecode(await player.GetStringAsync("/missions?show=todo")));   // it has rewards now

        // a field saved in the background (autosave.js): the answer says what happened, the page is not sent again
        var page2 = await player.GetStringAsync("/missions");
        Assert.Contains("data-autosave=\"quiet\"", page2);
        async Task<JsonElement> Quiet(string field, string value)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/missions/set")
            {
                Content = new FormUrlEncodedContent(new[] { KeyValuePair.Create("id", RivalKey), KeyValuePair.Create("field", field), KeyValuePair.Create("value", value),
                    KeyValuePair.Create("__RequestVerificationToken", ServerFactory.Token(page2)) }),
            };
            req.Headers.Add(MissionsController.AutosaveHeader, "1");
            var res = await player.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            return JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        }
        var ok = await Quiet("mg", "7");
        Assert.Equal((true, "Saved."), (ok.GetProperty("ok").GetBoolean(), ok.GetProperty("said").GetString()));
        Assert.Equal(7L, f.Services.GetRequiredService<MissionLibrary>().Entries().Single(e => e.Row.Key == RivalKey).Row.MgScrip);   // the list is made again
        Assert.False((await Quiet("mg", "lots")).GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task A_mission_just_taken_is_on_top_while_it_is_played_also_without_its_offer_and_the_list_has_pages()
    {
        using var f = new ServerFactory();
        var admin = await f.Admin();
        var library = f.Services.GetRequiredService<MissionLibrary>();
        // 25 kinds played on 21 Oct, then a bounty taken a minute ago: the log never says which offer it is (most missions with comms do not), and one
        // shared with him by the other account (its offer never showed in this log)
        var old = Enumerable.Range(1, 25).SelectMany(i => Mission($"{i:00000000}-1111-1111-1111-111111111111", C1, "Old Mission " + i));
        var now = DateTime.UtcNow.AddMinutes(-1).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture);
        var bounty = $"<{now}> [Notice] <SHUDEvent_OnNotification> Added notification \"Contract Accepted: Kill the Outlaw at Bloom: \" [84] to queue. New queue size: 1, MissionId: [99999999-9999-9999-9999-999999999999], ObjectiveId: [] [Missions][Comms]";
        var shared = $"<{now}> [Notice] <SHUDEvent_OnNotification> Added notification \"Contract Shared: Somebody Else's Job: \" [85] to queue. New queue size: 1, MissionId: [88888888-8888-8888-8888-888888888888], ObjectiveId: [] [Missions][Comms]";
        var before = library.Version;
        f.Upload("zzoega", old.Append(bounty).Append(shared).ToArray());
        library.Collect();
        Assert.True(library.Version > before);
        Assert.Equal(library.Version, JsonDocument.Parse(await admin.GetStringAsync("/missions/version")).RootElement.GetProperty("version").GetInt32());

        static int Rows(string html) => System.Text.RegularExpressions.Regex.Matches(html, "<tr id=").Count;
        var page = WebUtility.HtmlDecode(await admin.GetStringAsync("/missions"));
        Assert.Equal(MissionsController.PageSize, Rows(page));   // 20 to a page
        var top = page[page.IndexOf("<tr id=", StringComparison.Ordinal)..];
        top = top[..top.IndexOf("</tr>", StringComparison.Ordinal)];
        Assert.Contains("value=\"Kill the Outlaw at Bloom\"", top);   // on top, being played
        Assert.Contains("running", top);
        Assert.Contains("Playing now", top);
        Assert.DoesNotContain("Somebody Else", page);
        Assert.Contains("Page 1 of ", page);
        Assert.Contains("href=\"/missions?page=2\"", page);
        Assert.Contains("missions.js", page);
        var second = WebUtility.HtmlDecode(await admin.GetStringAsync("/missions?page=2"));
        Assert.Contains("Page 2 of ", second);
        Assert.InRange(Rows(second), 1, MissionsController.PageSize);
        Assert.Contains("Page 1 of ", WebUtility.HtmlDecode(await admin.GetStringAsync("/missions?page=0")));   // out of range: the nearest one
    }

    [Fact]
    public async Task The_missions_of_the_old_logs_are_in_it_too_also_when_they_came_before_the_library()
    {
        using var f = new ServerFactory();
        var admin = await f.Admin();
        var tracker = f.Tracker(f.AddKey("zzoega"));
        var lines = new GameLogWriter().At(new DateTime(2026, 9, 1, 7, 0, 0, DateTimeKind.Utc)).Login("zzoega").Lines
            .Concat(Mission("33333333-3333-3333-3333-333333333333", C2, "Stop Rival Attack", "2026-09-01")).ToArray();
        var r = await tracker.PostAsJsonAsync("/api/history", new { file = "2026-09-01T07:00:00.000Z", from = 0, lines, done = true });
        r.EnsureSuccessStatusCode();
        Assert.Contains("value=\"Stop Rival Attack\"", await admin.GetStringAsync("/missions"));

        var library = f.Services.GetRequiredService<MissionLibrary>();
        using (var db = f.Store.Open())
        {
            db.Missions.RemoveRange(db.Missions);   // as before the library existed: only the old log on the server
            db.SaveChanges();
        }
        Assert.Equal(1, library.ReadKeptHistory());
        Assert.Equal(1, library.Entries().Single(e => e.Row.Key == "stop rival attack").Taken);
        Assert.Equal(0, library.ReadKeptHistory());   // once
    }

    [Fact]
    public void What_was_typed_by_ContractId_goes_to_the_name_of_its_missions()
    {
        using var f = new ServerFactory();
        f.Services.GetRequiredService<MissionLibrary>().Entries();   // the server's own library is ready before the old rows go in (one at a time on the database)
        using (var db = f.Store.Open())
        {
            // the library as it was: by ContractId, and the missions without a kind
            foreach (var (id, contract) in new[] { ("11111111-1111-1111-1111-111111111111", C1), ("22222222-2222-2222-2222-222222222222", C2) })
                db.Missions.Add(new MissionRow { Player = "zzoega", MissionId = id, ContractId = contract, Title = "Combat Gauntlet - Scenario #6", Outcome = "Complete",
                    EndedUtc = new DateTime(2026, 10, 20, 20, 0, 0, DateTimeKind.Utc) });
            db.Library.Add(new LibraryRow { ContractId = C1, Auec = 171_250, MgScrip = 14, EditedBy = "zzoega" });
            db.Library.Add(new LibraryRow { ContractId = C2, Giver = "Gilly" });
            db.Library.Add(new LibraryRow { ContractId = "00000000-0000-0000-0000-00000000dead", Notes = "no mission of it" });
            db.SaveChanges();
        }
        var library = new MissionLibrary(f.Store, f.Live.Uploads, f.Services.GetRequiredService<ServerPaths>(), f.Live);   // as when the server starts

        var six = library.Entries().Single(e => e.Row.Key == "combat gauntlet - scenario #6");
        Assert.Equal((2, 171_250L, 14L, "Gilly"), (six.Taken, six.Row.Auec!.Value, six.Row.MgScrip!.Value, six.Row.Giver));
        using (var db = f.Store.Open())
            Assert.Equal(new[] { "00000000-0000-0000-0000-00000000dead" }, db.Library.Select(l => l.ContractId).ToArray());   // carried over, except what has no mission
        Assert.Equal(171_250, f.Services.GetRequiredService<GauntletService>().Rewards()[6].Auec);   // the Gauntlet Tracker counts with the library
    }

    [Fact]
    public async Task The_Gauntlet_rewards_are_in_the_library_starting_with_the_ones_the_admin_had()
    {
        using var f = new ServerFactory();
        var admin = await f.Admin();
        var gauntlet = f.Services.GetRequiredService<GauntletService>();
        var settings = gauntlet.Settings();
        settings.RewardAuec[7] = 300_000;   // what the admin's Gauntlet page had
        gauntlet.SaveSettings(settings);

        Assert.Equal((300_000L, 16L), (gauntlet.Rewards()[7].Auec, gauntlet.Rewards()[7].Scrip));
        Assert.Equal(GauntletRewards.Defaults[1].Auec, gauntlet.Rewards()[1].Auec);
        var key = GauntletService.LibraryKey(7);
        await ServerFactory.Send(admin, "/missions", "/missions/set", ("id", key), ("field", "auec"), ("value", "310000"));
        Assert.Equal(310_000, gauntlet.Rewards()[7].Auec);
        var page = WebUtility.HtmlDecode(await admin.GetStringAsync("/admin/gauntlet"));
        Assert.Contains("310,000", page);
        Assert.Contains("/missions#" + MissionLibrary.Anchor(key), page);
        Assert.Contains("the Gauntlet Tracker counts with it", await admin.GetStringAsync("/missions"));

        await ServerFactory.Send(admin, "/missions", "/missions/set", ("id", key), ("field", "auec"), ("value", ""));
        Assert.Equal(0, gauntlet.Rewards()[7].Auec);   // emptied by somebody: not known, and the old value does not come back
    }
}
