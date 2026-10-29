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
        Assert.Contains("the Star Citizen Wiki", System.Net.WebUtility.HtmlDecode(await admin.GetStringAsync("/hauling/jobs")));

        // the library's kind of the mission is the API's mission (by its title in the logs, whatever a string mod calls it): its reward and SCU are grey there
        var library = f.Services.GetRequiredService<MissionLibrary>();
        await admin.GetStringAsync("/missions");   // the mission comes into the library
        Assert.Equal(1, library.LinkWiki());
        Assert.Equal(0, library.LinkWiki());
        var entry = library.Entries().Single(e => e.Row.Title == "Master Hauler Needed for Large Shipment");
        Assert.Equal((Uuid, "2,000,000–2,200,000"), (entry.Row.WikiUuid, entry.Wiki?.AuecText));
        var page = await admin.GetStringAsync("/missions");
        Assert.Contains("placeholder=\"2,000,000–2,200,000\" class=\"num\"", System.Net.WebUtility.HtmlDecode(page));
        Assert.Contains("placeholder=\"~500\" class=\"num scu\"", page);
    }
}
