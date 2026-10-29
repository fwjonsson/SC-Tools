using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SCTools.Server;
using SCTools.Tests.Support;

namespace SCTools.Tests.ServerPages;

/// <summary>
/// The Star Citizen Wiki API's missions (WikiMissions): the server asks the API about the contracts of the logs by their uuid (the log's contractDefinitionId)
/// once, keeps their hauling orders, and a hauling job nobody gave the SCU of has the middle of them.
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
