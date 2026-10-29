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
/// has another ContractId). Everybody looks it up read-only (/missions: a search, a giver, with or without rewards, the ones he played, sorted by a
/// column, with his own plays only); the admin types in the rewards, the SCU, the giver and the name on Edit Mission Info (/admin/missions), saved one
/// field at a time. The Gauntlet Tracker counts with its scenarios' rewards.
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
    public async Task The_missions_with_the_same_name_are_one_row_the_admin_fills_in_the_rest_and_everybody_looks_it_up()
    {
        using var f = new ServerFactory();
        var admin = await f.Admin();
        // the same mission from two places: another ContractId, the same name
        f.Upload("zzoega", Mission("11111111-1111-1111-1111-111111111111", C1, Rival)
            .Concat(Mission("22222222-2222-2222-2222-222222222222", C2, Rival)).ToArray());

        var page = WebUtility.HtmlDecode(await admin.GetStringAsync("/missions"));   // only the admin until the tool is shown
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(page, System.Text.RegularExpressions.Regex.Escape(RivalShown)));
        Assert.DoesNotContain("data-autosave", page);   // read-only, the admin's too
        Assert.Contains("Hauling: 2 To Ruin", page);   // the Hauling Planner's contract lines are there from the start
        Assert.Contains("href=\"/admin/missions\"", page);   // where he changes it
        Assert.DoesNotContain("0 / 2", page);   // zzoega's plays are not the admin's

        var player = await f.Player(f.AddKey("bileh123"));
        var zzoega = await f.Player(f.AddKey("zzoega"));
        Assert.Equal(HttpStatusCode.Redirect, (await player.GetAsync("/missions")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await f.Browser().GetAsync("/missions")).StatusCode);   // a visitor: the login
        await ServerFactory.Send(admin, "/admin/tools", "/admin/tools/set", ("id", "missions"), ("value", "true"));
        var visitor = await f.Browser().GetStringAsync("/missions");   // shown: everybody, as the Hauling Planner's live page
        Assert.Contains(RivalShown, WebUtility.HtmlDecode(visitor));
        Assert.DoesNotContain("Failed /<br>Completed", visitor);   // nobody's plays for a visitor
        Assert.DoesNotContain("Last<br>played", visitor);
        Assert.DoesNotContain("name=\"mine\"", visitor);
        var his = WebUtility.HtmlDecode(await zzoega.GetStringAsync("/missions"));
        Assert.Contains("Failed /<br>Completed", his);
        Assert.Contains("Last<br>played", his);
        Assert.Contains(">0 / 2<", his);   // failed / completed, his own
        Assert.Contains(">21 Oct 2026<", his);   // when he played it last
        var bileh = WebUtility.HtmlDecode(await player.GetStringAsync("/missions"));
        Assert.DoesNotContain("0 / 2", bileh);   // bileh123 never played it
        Assert.DoesNotContain("21 Oct 2026", bileh);

        // a player cannot change it, the admin does on Edit Mission Info
        var denied = await ServerFactory.Send(player, "/missions", "/admin/missions/set", ("id", RivalKey), ("field", "auec"), ("value", "1"));
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await player.GetAsync("/admin/missions")).StatusCode);
        var library = f.Services.GetRequiredService<MissionLibrary>();
        Assert.Null(library.Entries().Single(e => e.Row.Key == RivalKey).Row.Auec);
        var editPage = WebUtility.HtmlDecode(await admin.GetStringAsync("/admin/missions"));
        Assert.Contains("Edit Mission Info", editPage);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(editPage, System.Text.RegularExpressions.Regex.Escape("value=\"" + RivalShown + "\"")));
        Assert.Contains("action=\"/admin/missions/set\"", editPage);
        var saved = await ServerFactory.Send(admin, "/admin/missions", "/admin/missions/set", ("id", RivalKey), ("field", "auec"), ("value", "25,000"), ("back", "/admin/missions?rewards=todo"));
        Assert.Equal("/admin/missions?rewards=todo#" + MissionLibrary.Anchor(RivalKey), saved.Headers.Location?.OriginalString);
        await ServerFactory.Send(admin, "/admin/missions", "/admin/missions/set", ("id", RivalKey), ("field", "council"), ("value", "12"));
        await ServerFactory.Send(admin, "/admin/missions", "/admin/missions/set", ("id", RivalKey), ("field", "giver"), ("value", "Red Wind"));
        await ServerFactory.Send(admin, "/admin/missions", "/admin/missions/set", ("id", RivalKey), ("field", "scu"), ("value", "1,536"));
        await ServerFactory.Send(admin, "/admin/missions", "/admin/missions/set", ("id", RivalKey), ("field", "mg"), ("value", "lots"));   // not a number: not saved
        var entry = library.Entries().Single(e => e.Row.Key == RivalKey);
        Assert.Equal((25_000L, 12L, (long?)null, "Red Wind", ServerFactory.AdminName), (entry.Row.Auec!.Value, entry.Row.CouncilScrip!.Value, entry.Row.MgScrip, entry.Row.Giver, entry.Row.EditedBy));
        Assert.Equal(1536L, entry.Row.Scu);   // its cargo
        var withGiver = await admin.GetStringAsync("/admin/missions");
        Assert.Contains("Not saved", withGiver);
        Assert.Contains("<option value=\"Red Wind\"></option>", withGiver);   // to pick in the other Faction fields
        Assert.Contains("list=\"factions\"", withGiver);
        Assert.Contains("name=\"value\" value=\"1536\" placeholder=\"\" class=\"num scu\"", withGiver);   // in the SCU column
        Assert.DoesNotContain(RivalShown, WebUtility.HtmlDecode(await admin.GetStringAsync("/admin/missions?rewards=todo")));   // it has rewards now

        // everybody sees what he typed, as text, and chooses what to see
        var seen = WebUtility.HtmlDecode(await player.GetStringAsync("/missions"));
        Assert.Contains("<td>Red Wind</td>", seen);
        Assert.Contains(">25,000<", seen);
        Assert.Contains(">1,536<", seen);
        Assert.Contains("<option value=\"Red Wind\" selected=\"selected\">Red Wind</option>", WebUtility.HtmlDecode(await player.GetStringAsync("/missions?faction=Red%20Wind")));
        Assert.DoesNotContain("<tr id=\"" + MissionLibrary.Anchor("hauling:2 to ruin") + "\"", await player.GetStringAsync("/missions?faction=Red%20Wind"));   // no faction: not in it
        Assert.Contains("<option value=\"Red Wind\" selected=\"selected\">Red Wind</option>", WebUtility.HtmlDecode(await player.GetStringAsync("/missions?giver=Red%20Wind")));   // the address as it was
        Assert.DoesNotContain(RivalShown, WebUtility.HtmlDecode(await player.GetStringAsync("/missions?rewards=todo")));
        Assert.Contains(RivalShown, WebUtility.HtmlDecode(await player.GetStringAsync("/missions?rewards=known")));
        Assert.Contains(RivalShown, WebUtility.HtmlDecode(await player.GetStringAsync("/missions?q=rival")));
        Assert.Contains("Nothing here.", await player.GetStringAsync("/missions?mine=1"));   // bileh123 played nothing
        Assert.Contains(RivalShown, WebUtility.HtmlDecode(await zzoega.GetStringAsync("/missions?mine=1")));
        Assert.Contains("href=\"/missions?sort=auec&dir=desc\"", seen);   // a header sorts by its column (seen is decoded: no &amp;)
        Assert.Contains("href=\"/missions?sort=auec&amp;dir=asc\"", await player.GetStringAsync("/missions?sort=auec&dir=desc"));   // again: the other way round
        Assert.Contains("href=\"/missions?sort=last&dir=desc\"", seen);   // the latest he played first

        // a field saved in the background (autosave.js): the answer says what happened, the page is not sent again
        var page2 = await admin.GetStringAsync("/admin/missions");
        Assert.Contains("data-autosave=\"quiet\"", page2);
        async Task<JsonElement> Quiet(string field, string value)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/admin/missions/set")
            {
                Content = new FormUrlEncodedContent(new[] { KeyValuePair.Create("id", RivalKey), KeyValuePair.Create("field", field), KeyValuePair.Create("value", value),
                    KeyValuePair.Create("__RequestVerificationToken", ServerFactory.Token(page2)) }),
            };
            req.Headers.Add(EditMissionsController.AutosaveHeader, "1");
            var res = await admin.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            return JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        }
        var ok = await Quiet("mg", "7");
        Assert.Equal((true, "Saved."), (ok.GetProperty("ok").GetBoolean(), ok.GetProperty("said").GetString()));
        Assert.Equal(7L, library.Entries().Single(e => e.Row.Key == RivalKey).Row.MgScrip);   // the list is made again
        Assert.False((await Quiet("mg", "lots")).GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task A_mission_being_played_is_marked_for_its_player_also_without_its_offer_and_the_list_has_pages()
    {
        using var f = new ServerFactory();
        var admin = await f.Admin();
        await ServerFactory.Send(admin, "/admin/tools", "/admin/tools/set", ("id", "missions"), ("value", "true"));   // shown: the players see it
        var zzoega = await f.Player(f.AddKey("zzoega"));
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

        // the rows of a page: the name of each and its html
        static List<(string Name, string Html)> Rows(string html) => System.Text.RegularExpressions.Regex.Matches(html, "<tr id=.*?</tr>", System.Text.RegularExpressions.RegexOptions.Singleline)
            .Select(m => (System.Text.RegularExpressions.Regex.Match(m.Value, "<td class=\"name\"[^>]*>([^<]+)").Groups[1].Value.Trim(), m.Value)).ToList();
        var page = WebUtility.HtmlDecode(await zzoega.GetStringAsync("/missions"));
        var rows = Rows(page);
        Assert.Equal(MissionsController.PageSize, rows.Count);   // 20 to a page, by name: the Hauling rows, then Kill..., Old Mission 1, 10, 11...
        var names = rows.Select(r => r.Name).ToList();
        Assert.Equal(names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase), names);
        Assert.True(names.IndexOf("Kill the Outlaw at Bloom") < names.IndexOf("Old Mission 1"));
        var row = rows.Single(r => r.Name == "Kill the Outlaw at Bloom").Html;   // being played by him: marked
        Assert.Contains(" running\"", row);   // its class
        Assert.Contains("<span class=\"now\">playing now</span>", row);
        Assert.Contains(">0 / 0<", row);   // failed / completed: nothing yet
        Assert.Contains(">0 / 1<", rows.Single(r => r.Name == "Old Mission 1").Html);
        Assert.Contains(">21 Oct 2026<", rows.Single(r => r.Name == "Old Mission 1").Html);
        Assert.DoesNotContain("running", rows.Single(r => r.Name == "Old Mission 1").Html);
        Assert.DoesNotContain("<span class=\"now\">", await admin.GetStringAsync("/missions"));   // not the admin's play
        Assert.DoesNotContain("Somebody Else", page);
        Assert.Contains("Page 1 of ", page);
        Assert.Contains("href=\"/missions?page=2\"", page);
        Assert.Contains("Next »", page);
        Assert.Contains("missions.js", page);
        Assert.Contains("data-version-url=\"/missions/version\"", page);
        Assert.Contains("data-version-url=\"/admin/missions/version\"", await admin.GetStringAsync("/admin/missions"));
        Assert.Equal(library.Version, JsonDocument.Parse(await admin.GetStringAsync("/admin/missions/version")).RootElement.GetProperty("version").GetInt32());
        var second = WebUtility.HtmlDecode(await admin.GetStringAsync("/missions?page=2"));
        Assert.Contains("Page 2 of ", second);
        Assert.InRange(Rows(second).Count, 1, MissionsController.PageSize);
        Assert.Contains("Page 1 of ", WebUtility.HtmlDecode(await admin.GetStringAsync("/missions?page=0")));   // out of range: the nearest one
        // sorted by what he completed: the completed ones first, the one he is playing (0 completed) after them, and the kinds he never played last
        var last = Rows(WebUtility.HtmlDecode(await zzoega.GetStringAsync("/missions?sort=played&dir=desc&page=2"))).Select(r => r.Name).ToList();
        Assert.True(last.IndexOf("Kill the Outlaw at Bloom") < last.IndexOf("Hauling: 2 To Ruin"), string.Join(", ", last));
        Assert.Contains("Nothing here.", await admin.GetStringAsync("/missions?mine=1"));   // the admin played nothing
        // the latest he played first: the one he plays now, then the ones of 21 Oct, then the kinds he never played
        var latest = Rows(WebUtility.HtmlDecode(await zzoega.GetStringAsync("/missions?sort=last&dir=desc"))).Select(r => r.Name).ToList();
        Assert.Equal("Kill the Outlaw at Bloom", latest[0]);
        Assert.StartsWith("Old Mission", latest[1]);
        Assert.Contains("/missions?sort=last&dir=asc", WebUtility.HtmlDecode(await zzoega.GetStringAsync("/missions?sort=last&dir=desc")));
    }

    [Fact]
    public void Bounties_are_one_kind_by_their_kind_and_risk_and_the_old_kinds_by_target_are_put_together()
    {
        using var f = new ServerFactory();
        f.Services.GetRequiredService<MissionLibrary>().Entries();   // the server's own library is ready before the old rows go in
        using (var db = f.Store.Open())
        {
            // as before: one kind per target, one of them with its reward typed in
            db.MissionKinds.Add(new MissionKindRow { Key = "reclusive bounty: craig clayden | extreme risk [300 rep]", Title = "Reclusive Bounty: Craig Clayden | Extreme Risk [300 Rep]",
                Auec = 120_000, Giver = "Bounty Hunters Guild", EditedBy = "zzoega", EditedUtc = new DateTime(2026, 10, 28, 13, 30, 0, DateTimeKind.Utc) });
            db.Missions.Add(new MissionRow { Player = "zzoega", MissionId = "b264817b-8471-4ddc-9e31-36496ff9a46f", ContractId = "", Title = "Reclusive Bounty: Craig Clayden | Extreme Risk [300 Rep]",
                Kind = "reclusive bounty: craig clayden | extreme risk [300 rep]", StartedUtc = new DateTime(2026, 10, 28, 13, 18, 34, DateTimeKind.Utc) });
            db.SaveChanges();
        }
        var library = new MissionLibrary(f.Store, f.Live.Uploads, f.Services.GetRequiredService<ServerPaths>(), f.Live);   // as when the server starts
        // another target of the same risk, from the log
        var bounty = "<2026-10-28T14:00:00.000Z> [Notice] <SHUDEvent_OnNotification> Added notification \"Contract Accepted:  Reclusive Bounty: Kafu \"Vaco\" Dudley | Extreme Risk <EM4>[300 Rep]</EM4>: \" [85] to queue. New queue size: 1, MissionId: [f4dd25aa-c7d4-409f-8f3e-6c1a2b3c4d5e], ObjectiveId: [] [Missions][Comms]";
        f.Upload("zzatvo", new[] { bounty });
        library.Collect();
        var entries = library.Entries();
        var kind = Assert.Single(entries, e => e.Row.Title.StartsWith("Reclusive Bounty", StringComparison.Ordinal));
        Assert.Equal(("Reclusive Bounty | Extreme Risk [300 Rep]", 2, 120_000L, "Bounty Hunters Guild"), (kind.Row.Title, kind.Taken, kind.Row.Auec!.Value, kind.Row.Giver));
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
        Assert.Contains("value=\"Stop Rival Attack\"", await admin.GetStringAsync("/admin/missions"));

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
        await ServerFactory.Send(admin, "/admin/missions", "/admin/missions/set", ("id", key), ("field", "auec"), ("value", "310000"));
        Assert.Equal(310_000, gauntlet.Rewards()[7].Auec);
        var page = WebUtility.HtmlDecode(await admin.GetStringAsync("/admin/gauntlet"));
        Assert.Contains("310,000", page);
        Assert.Contains("/admin/missions#" + MissionLibrary.Anchor(key), page);

        await ServerFactory.Send(admin, "/admin/missions", "/admin/missions/set", ("id", key), ("field", "auec"), ("value", ""));
        Assert.Equal(0, gauntlet.Rewards()[7].Auec);   // emptied by somebody: not known, and the old value does not come back
    }

    [Fact]
    public async Task The_kinds_of_the_Hauling_Planner_are_named_as_the_game_names_their_missions()
    {
        using var f = new ServerFactory();
        var admin = await f.Admin();
        var library = f.Services.GetRequiredService<MissionLibrary>();
        const string key = "hauling:2 to ruin";
        const string game = "Senior | Stellar Large Haul | to Ruin Station [50/200/250/500/1000/2000/4000 Rep]";
        Assert.Equal("Hauling: 2 To Ruin", library.Entries().Single(e => e.Row.Key == key).Row.Title);   // no mission of it yet: its contract line

        f.Upload(PlayedSession.Player, PlayedSession.Writer().Lines.ToList());   // two of them, as the game names them
        library.Collect();
        var kind = library.Entries().Single(e => e.Row.Key == key);
        Assert.Equal((game, 2), (kind.Row.Title, kind.Taken));
        var page = WebUtility.HtmlDecode(await admin.GetStringAsync("/missions"));
        Assert.Contains(game, page);
        Assert.DoesNotContain("Hauling: 2 To Ruin", page[page.IndexOf("<table", StringComparison.Ordinal)..]);
        Assert.DoesNotContain("the Hauling Planner pays its aUEC", page);   // nothing under the names

        // the kinds named by their contract line before get the game's name once (the one most of their missions have)
        using (var db = f.Store.Open())
        {
            db.MissionKinds.Single(k => k.Key == key).Title = "Hauling: 2 To Ruin";
            db.SaveChanges();
        }
        var again = new MissionLibrary(f.Store, f.Live.Uploads, f.Services.GetRequiredService<ServerPaths>(), f.Live);   // as when the server starts
        Assert.Equal(game, again.Entries().Single(e => e.Row.Key == key).Row.Title);
    }
}
