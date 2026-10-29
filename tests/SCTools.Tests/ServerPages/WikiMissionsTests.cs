using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SCTools.Server;
using SCTools.Tests.Support;

namespace SCTools.Tests.ServerPages;

/// <summary>
/// The Star Citizen Wiki API's missions (WikiMissions): the server asks the API about the contracts of the logs by their uuid (the log's contractDefinitionId)
/// once, keeps their hauling orders, and a hauling job nobody gave the SCU of has the middle of them; it reads the list of every mission once a day, so
/// every kind of the library shows the Wiki's faction, category, reward, XP and legality, also a kind whose contract id no log showed (by its name).
/// </summary>
public class WikiMissionsTests
{
    const string Uuid = "26e80306-6190-447f-a214-634d8ed8ca7d";
    const string Master = "RedWind_Pyro_BulkGrade_Solar_CFP_StationToStation_Potassium_CargoHauling_Multi4ToSingle";

    // the API's answer, as Frank downloaded it on 29 Oct 2026 (the parts the server reads; the API has no reward for this one: this one is made up)
    static readonly string Answer = JsonSerializer.Serialize(new
    {
        data = new
        {
            uuid = Uuid,
            debug_name = Master,
            title = "Master Hauler Needed for Large Shipment",
            mission_type = "Hauling - Stellar",
            faction = new { name = "Red Wind", uuid = "9f3b4d6c-0000-0000-0000-000000000001" },
            reward_scope = "Hauling",
            reputation_amount = 1500,
            illegal = false,
            legality_label = "Legal",
            reward_min = 2_000_000,
            reward_max = 2_200_000,
            reward_currency = "aUEC",
            hauling_orders = Enumerable.Repeat(new { kind = "Resource", name = "Potassium", max_scu = 150, min_scu = 100, max_amount = (int?)null, max_container_size = 32 }, 4),
        },
    });

    [Fact]
    public void The_API_s_answer_is_its_debug_name_title_and_hauling_orders()
    {
        var row = WikiMissions.Parse(Uuid, Answer)!;
        Assert.Equal((Master, "Master Hauler Needed for Large Shipment", "100-150;100-150;100-150;100-150"), (row.DebugName, row.Title, row.Orders));
        Assert.Equal(("Red Wind", "Hauling", 1500L, "Legal"), (row.Faction, row.Category, row.Xp, row.Legality));   // what the Wiki shows about it
        Assert.Equal("5 MG Scrip", new WikiContract(Uuid, null, null, Array.Empty<ScuRange>(), 5, 5, "MG Scrip").RewardText);   // a reward in another currency
        Assert.Equal("2,000,000–2,200,000", WikiMissions.ToContract(row).RewardText);
        Assert.Equal(new ScuRange(400, 600), WikiMissions.ToContract(row).Total);
        Assert.Equal((2_100_000L, "2,000,000–2,200,000"), (WikiMissions.ToContract(row).Auec!.Value, WikiMissions.ToContract(row).AuecText));
        Assert.Null(new WikiContract(Uuid, null, null, Array.Empty<ScuRange>(), 5, 5, "MG Scrip").Auec);   // not aUEC
        var onlyMin = new WikiContract(Uuid, null, null, Array.Empty<ScuRange>(), 394_250, 0, "aUEC");   // "reward_max": 0 there: only the one end is given
        Assert.Equal((394_250L, "394,250"), (onlyMin.Auec!.Value, onlyMin.AuecText));
        Assert.Null(new WikiContract(Uuid, null, null, Array.Empty<ScuRange>(), 0, 0, "aUEC").Auec);
        Assert.Null(WikiMissions.Parse(Uuid, "<html>not found</html>"));
        Assert.Equal(new[] { new ScuRange(2, 2) }, WikiMissions.Orders("2-2;x-3"));
    }

    [Fact]
    public async Task A_contract_is_asked_for_once_and_a_job_of_it_has_the_middle_of_its_hauling_orders()
    {
        using var f = new ServerFactory();
        var wiki = f.Services.GetRequiredService<WikiMissions>();
        var asked = new List<string>();
        wiki.Fetch = (uuid, _) =>
        {
            asked.Add(uuid);
            return Task.FromResult(uuid == Uuid ? Answer : null);
        };
        const string other = "701271ba-964f-4738-b489-1f38d9c40c4a";
        var now = DateTime.UtcNow;
        Assert.Equal(1, await wiki.Learn(new[] { Uuid.ToUpperInvariant(), other, "not a uuid", Uuid }, now));
        Assert.Equal(new[] { Uuid, other }, asked);
        Assert.Equal(0, await wiki.Learn(new[] { Uuid, other }, now.AddDays(1)));   // asked already
        Assert.Equal(2, asked.Count);
        await wiki.Learn(new[] { Uuid, other }, now.AddDays(8));   // the one the API did not know: again after a week
        Assert.Equal(new[] { Uuid, other, other }, asked);

        // a job of the contract: nobody gave its SCU, so it has the middle of the API's orders
        var m = GameLogWriter.MissionId(1);
        var w = new GameLogWriter().Login("zzoega").AcceptedAs(m, "Master Hauler Needed for Large Shipment");
        foreach (var stop in new[] { "a", "b", "c", "d" }) w.Marker(m, Master, "pickup_" + stop + "_0", "86000000000" + stop.Length, (1000, 2000, 3000), Uuid);
        f.Upload("zzoega", w.Marker(m, Master, "dropoff_x_0", "862738706222", (12, -16, -7.75), Uuid).Lines.ToList());
        f.Services.GetRequiredService<MissionLibrary>().PushBook();
        f.Live.Refresh(DateTime.UtcNow);
        var admin = await f.Admin();
        var data = JsonDocument.Parse(await admin.GetStringAsync("/data.json")).RootElement;
        var row = Assert.Single(data.GetProperty("jobs").GetProperty("rows").EnumerateArray()).GetProperty("cells").EnumerateArray().Select(c => c.GetString()).ToArray();
        Assert.Equal(("~500 (wiki 400–600)", "~2,000,000–2,200,000 (wiki)"), (row[4], row[5]));
        Assert.Contains("~125 SCU", row[2]);
        var field = Assert.Single(data.GetProperty("jobsEdit").EnumerateArray());   // its SCU field: grey, the Wiki's
        Assert.Equal(("~500", "wiki 400–600"), (field.GetProperty("hint").GetString(), field.GetProperty("from").GetString()));

        // the library's kind of the mission is the API's mission (by its title in the logs, whatever a string mod calls it): its reward and SCU are grey there
        var library = f.Services.GetRequiredService<MissionLibrary>();
        await admin.GetStringAsync("/missions");   // the mission comes into the library
        Assert.Equal(new[] { Uuid }, library.Contracts());   // what the server asks the API about
        Assert.Equal(1, library.LinkWiki());
        Assert.Equal(0, library.LinkWiki());
        var entry = library.Entries().Single(e => e.Row.Title == "Master Hauler Needed for Large Shipment");
        Assert.Equal((Uuid, "2,000,000–2,200,000"), (entry.Row.WikiUuid, entry.Wiki?.AuecText));
        var page = await admin.GetStringAsync("/admin/missions");
        Assert.Contains("placeholder=\"2,000,000–2,200,000\" class=\"num\"", System.Net.WebUtility.HtmlDecode(page));
        Assert.Contains("placeholder=\"~500\" class=\"num scu\"", page);
        var shown = System.Net.WebUtility.HtmlDecode(await admin.GetStringAsync("/missions"));   // read-only: the Wiki's in grey
        Assert.Contains("<span class=\"muted\" title=\"the Star Citizen Wiki's reward\">2,000,000–2,200,000</span>", shown);
        Assert.Contains("<span class=\"muted\" title=\"the Star Citizen Wiki's hauling orders\">~500</span>", shown);
    }

    /// <summary>A page of the list of every mission, as the API gives it (the parts the server reads).</summary>
    static string ListPage(int page, int lastPage, params object[] missions) =>
        JsonSerializer.Serialize(new { data = missions, meta = new { current_page = page, last_page = lastPage, per_page = 200, total = 3 } });

    static object Listed(string uuid, string title, string? faction, string scope, long rewardMin, long? xp, bool illegal) => new
    {
        uuid, title, debug_name = "Debug_" + title.Replace(" ", ""), faction = faction == null ? null : (object)new { name = faction, uuid = "f-" + uuid },
        mission_giver = faction, reward_scope = scope, reward_min = rewardMin, reward_max = 0, reward_currency = "UEC", reputation_amount = xp, illegal,
        legality_label = illegal ? "Illegal" : "Legal",
    };

    [Fact]
    public async Task The_list_of_every_mission_is_read_once_a_day_and_a_kind_without_a_contract_id_is_linked_by_its_name()
    {
        using var f = new ServerFactory();
        var wiki = f.Services.GetRequiredService<WikiMissions>();
        const string rival = "26b8773e-0b96-40b4-a33a-27a4e9877e75", batch = "091ffb61-7733-4ebe-8cef-66ec15de2320";
        var pages = new List<int>();
        wiki.FetchPage = (page, _) =>
        {
            pages.Add(page);
            return Task.FromResult<string?>(page == 1
                ? ListPage(1, 2, Listed(rival, "Stop Rival Attack at Asteroid Mining Base", "Red Wind", "Security", 25_000, 250, false), Listed(batch, "A Batch from Scratch", "Wallace Klim", "Hauling", 20_000, 1000, true))
                : ListPage(2, 2, Listed(Uuid, "Master Hauler Needed for Large Shipment", null, "Hauling", 2_000_000, null, false)));
        };
        var now = DateTime.UtcNow;
        Assert.Null(wiki.ListReadUtc());
        Assert.Equal(3, await wiki.LearnAll(now));
        Assert.Equal(new[] { 1, 2 }, pages);
        Assert.Equal(now, wiki.ListReadUtc()!.Value, TimeSpan.FromSeconds(1));
        Assert.Equal(0, await wiki.LearnAllIfDue(now.AddHours(20)));   // once a day
        Assert.Equal(3, await wiki.LearnAllIfDue(now.AddHours(25)));
        using (var db = f.Store.Open())
        {
            var row = db.WikiMissions.Single(r => r.Uuid == batch);
            Assert.Equal(("Wallace Klim", "Hauling", 1000L, "Illegal", 20_000L, true, true), (row.Faction, row.Category, row.Xp, row.Legality, row.RewardMin, row.Found, row.ListOnly));
            Assert.Null(row.Orders);   // the list has no hauling orders
        }

        // a contract the logs have is asked for by its uuid once more (its own page has the hauling orders), then not again
        var asked = new List<string>();
        wiki.Fetch = (uuid, _) => { asked.Add(uuid); return Task.FromResult(uuid == Uuid ? Answer : null); };
        Assert.Equal(1, await wiki.Learn(new[] { Uuid, batch }, now));
        Assert.Equal(new[] { Uuid, batch }, asked);
        Assert.Equal(0, await wiki.Learn(new[] { Uuid, batch }, now.AddHours(1)));
        Assert.Equal(2, asked.Count);
        using (var db = f.Store.Open())
        {
            var master = db.WikiMissions.Single(r => r.Uuid == Uuid);
            Assert.Equal(("100-150;100-150;100-150;100-150", false, "Red Wind"), (master.Orders, master.ListOnly, master.Faction));
            Assert.True(db.WikiMissions.Single(r => r.Uuid == batch).Found);   // its own page is not there: what the list said stays
        }

        // a mission whose log never showed its contract id (no marker): its kind is the Wiki's mission with its name, whatever a string mod adds to it
        var admin = await f.Admin();
        f.Upload("zzoega", new GameLogWriter().At(now.AddMinutes(-30)).Login("zzoega").Minutes(1)
            .AcceptedAs(GameLogWriter.MissionId(7), "Stop Rival Attack at Asteroid Mining Base <EM4>[200 Rep] [BP]*</EM4>").Lines.ToList());
        var library = f.Services.GetRequiredService<MissionLibrary>();
        library.Collect();
        Assert.Empty(library.Contracts());
        Assert.Equal(1, library.LinkWiki());
        var entry = library.Entries().Single(e => e.Row.Title.StartsWith("Stop Rival", StringComparison.Ordinal));
        Assert.Equal((rival, "Red Wind", "Security", 250L, "Legal"), (entry.Row.WikiUuid, entry.FactionShown, entry.Category, entry.Xp, entry.Legality));
        var page = System.Net.WebUtility.HtmlDecode(await admin.GetStringAsync("/missions"));
        Assert.Contains("<span class=\"muted\" title=\"the Star Citizen Wiki's faction\">Red Wind</span>", page);
        Assert.Contains("<span class=\"muted\" title=\"the Star Citizen Wiki's category\">Security</span>", page);
        Assert.Contains(">250</span>", page);
        Assert.Contains(">Legal</span>", page);
        Assert.Contains("title=\"the Star Citizen Wiki's reward\">25,000</span>", page);
        Assert.Contains("<option value=\"Red Wind\">Red Wind</option>", page);   // the Wiki's faction to pick
        Assert.Contains("Stop Rival Attack", System.Net.WebUtility.HtmlDecode(await admin.GetStringAsync("/missions?faction=Red%20Wind")));
        Assert.Contains("Stop Rival Attack", System.Net.WebUtility.HtmlDecode(await admin.GetStringAsync("/missions?q=security")));
        Assert.Contains("placeholder=\"Red Wind\" class=\"giver\"", await admin.GetStringAsync("/admin/missions"));   // the Wiki's faction until one is typed in
        Assert.Contains(">Faction", page);
        Assert.Contains(">Reward", page);
        Assert.DoesNotContain(">Giver<", page);
    }

    [Fact]
    public async Task The_old_logs_give_their_missions_contract_ids_and_a_server_that_read_them_before_reads_them_once_more()
    {
        using var f = new ServerFactory();
        const string ling = "701271ba-964f-4738-b489-1f38d9c40c4a";
        var m = GameLogWriter.MissionId(4);
        var lines = new GameLogWriter().At(new DateTime(2026, 9, 1, 7, 0, 0, DateTimeKind.Utc)).Login("zzoega").AcceptedAs(m, "RSI Disc. Month: Ling Small Haul")
            .Marker(m, "Iasi_HaulCargo_AtoB_Small_PartySupplies", "dropoff_a_0", "862738706222", (12, -16, -7.75), ling).Lines.ToArray();
        var tracker = f.Tracker(f.AddKey("zzoega"));
        (await tracker.PostAsJsonAsync("/api/history", new { file = "2026-09-01T07:00:00.000Z", from = 0, lines, done = true })).EnsureSuccessStatusCode();
        var library = f.Services.GetRequiredService<MissionLibrary>();
        Assert.Equal(new[] { ling }, library.Contracts());   // an old log's mission: its contract id too

        // a server that read the old logs before they gave the contract ids: it reads them once more
        using (var db = f.Store.Open())
        {
            foreach (var row in db.Missions) row.Definition = null;
            db.SaveChanges();
        }
        File.WriteAllText(Path.Combine(f.Services.GetRequiredService<ServerPaths>().HistoryDir, MissionLibrary.HistoryMarker), "1");
        Assert.Empty(library.Contracts());
        Assert.Equal(1, library.ReadKeptHistory());
        Assert.Equal(new[] { ling }, library.Contracts());
        Assert.Equal(0, library.ReadKeptHistory());   // once
    }
}
