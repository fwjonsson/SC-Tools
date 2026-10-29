using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SCTools.Server;
using SCTools.Tests.Support;

namespace SCTools.Tests.ServerPages;

/// <summary>
/// The hauling jobs of every system on the Hauling Planner (the Other hauling board of data.json), where a logged in hauler types
/// in the SCU of a mission the game did not give: the mission has it, and the Mission Library gets it as its suggestion; what is typed in the library wins
/// over the suggestion, and the game's SCU becomes the suggestion too.
/// </summary>
public class HaulJobsPageTests
{
    const string AtoB = "Iasi_HaulCargo_AtoB_Small_PartySupplies";
    const string Courier = "Iasi_2H_Box_Courier_0";
    const string CourierTitle = "RSI Disc. Month: Courier Run (Exp. E)";
    static readonly (double, double, double) InStation = (12.000012, -15.999995, -7.75);
    static readonly (double, double, double) OnPlanet = (-18119.931151, 180393.511132, -232742.694013);

    static GameLogWriter CourierRun(GameLogWriter w, string m) =>
        w.AcceptedAs(m, CourierTitle).Marker(m, Courier, "pickup_f1_0", "862737938329", (-34.999981, 1, 2)).Marker(m, Courier, "dropoff_d1_0", "862738093314", (265321.16624, 1, 2))
            .ObjectivePopup(m, "pickup_f1_0", "Collect Medical Supplies From Shallow Fields Station at Crusader's L4 Lagrange point")
            .ObjectivePopup(m, "dropoff_d1_0", "Deliver Medical Supplies To RSI Event Platform");

    static async Task<List<string?[]>> Board(ServerFactory f, HttpClient c) => (await BoardAndFields(f, c)).Rows;

    /// <summary>The Other hauling board of data.json: its rows, and what the SCU field of every row needs.</summary>
    static async Task<(List<string?[]> Rows, List<JsonElement> Fields)> BoardAndFields(ServerFactory f, HttpClient c)
    {
        f.Live.Refresh(DateTime.UtcNow);
        var data = JsonDocument.Parse(await c.GetStringAsync("/data.json")).RootElement;
        Assert.Equal("/hauling/jobs/scu", data.GetProperty("jobsScu").GetString());   // where the field sends what is typed in
        var rows = data.GetProperty("jobs").GetProperty("rows").EnumerateArray()
            .Select(r => r.GetProperty("cells").EnumerateArray().Select(c => c.GetString()).ToArray()).ToList();
        var fields = data.GetProperty("jobsEdit").EnumerateArray().ToList();
        Assert.Equal(rows.Count, fields.Count);
        return (rows, fields);
    }

    static (string? Id, bool Game, long? Value, string? Hint, string? From) Field(JsonElement e) =>
        (e.GetProperty("id").GetString(), e.GetProperty("game").GetBoolean(), e.GetProperty("value").ValueKind == JsonValueKind.Null ? null : e.GetProperty("value").GetInt64(),
            e.GetProperty("hint").GetString(), e.GetProperty("from").GetString());

    [Fact]
    public async Task A_job_of_any_system_is_on_the_board_and_a_hauler_s_SCU_counts_for_it_and_is_the_library_s_suggestion()
    {
        using var f = new ServerFactory();
        var (haul, courier, second) = (GameLogWriter.MissionId(1), GameLogWriter.MissionId(2), GameLogWriter.MissionId(3));
        var w = new GameLogWriter().Login("zzoega")
            .AcceptedAs(haul, "RSI Disc. Month: Ling Small Haul").Marker(haul, AtoB, "dropoff_aaaa_0", "862738706222", InStation).Marker(haul, AtoB, "pickup_aaaa_0", "862737932760", OnPlanet)
            .ObjectivePopup(haul, "dropoff_aaaa_0", "Deliver 0/4 SCU of Distilled Spirits to Port Tressler")
            .ObjectivePopup(haul, "dropoff_aaaa_1", "Deliver 0/4 SCU of Souvenirs to Port Tressler")
            .Minutes(1);
        CourierRun(w, courier);
        f.Upload("zzoega", w.Lines.ToList());
        var admin = await f.Admin();

        var (rows, fields) = await BoardAndFields(f, admin);
        Assert.Equal(new[] { "RSI Disc. Month: Ling Small Haul", "zzoega", "a place the game does not name 8 SCU", "Port Tressler 8 SCU", "8", "" }, rows[0]);
        Assert.Equal(new[] { CourierTitle, "zzoega", "CRU-L4 Shallow Fields Station", "RSI Event Platform", "?", "" }, rows[1]);
        Assert.True(Field(fields[0]).Game);   // the game said its SCU: no field
        Assert.Equal((courier, false, (long?)null, "", ""), Field(fields[1]));

        // zzoega types in the SCU of his courier run in the board (the Hauling Planner's page has the form token, in its top bar)
        var player = await f.Player(f.AddKey("zzoega"));
        Assert.Contains("__RequestVerificationToken", await player.GetStringAsync("/hauling"));
        var saved = await ServerFactory.Send(player, "/hauling", "/hauling/jobs/scu", ("id", courier), ("title", CourierTitle), ("value", "6"));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        Assert.Equal("/hauling", saved.Headers.Location?.OriginalString);
        (rows, fields) = await BoardAndFields(f, admin);
        Assert.Equal(new[] { CourierTitle, "zzoega", "CRU-L4 Shallow Fields Station ~6 SCU", "RSI Event Platform ~6 SCU", "6 (hauler)", "" }, rows[1]);
        Assert.Equal((courier, false, (long?)6, "6", "suggested by zzoega"), Field(fields[1]));   // his number, and the library's suggestion is his too
        var old = await player.GetAsync("/hauling/jobs");   // the page it was once: the Hauling Planner now
        Assert.Equal((HttpStatusCode.Redirect, "/hauling"), (old.StatusCode, old.Headers.Location?.OriginalString));
        var library = f.Services.GetRequiredService<MissionLibrary>();
        var kind = library.Entries().Single(e => e.Row.Title == CourierTitle).Row;
        Assert.Equal((6L, "zzoega", (long?)null), (kind.ScuSuggested!.Value, kind.ScuSuggestedBy, kind.Scu));
        Assert.Contains("placeholder=\"6\" class=\"num scu\"", await admin.GetStringAsync("/admin/missions"));   // grey in the library: the suggestion
        Assert.Contains("<span class=\"muted\" title=\"suggested by zzoega\">6</span>", await admin.GetStringAsync("/missions"));

        // another courier run of the kind: the library's suggestion; Frank types the kind's SCU in the library: it wins over the suggestion, not over the hauler's
        f.Upload("zzoega", CourierRun(new GameLogWriter().At(w.Now.AddMinutes(1)).Login("zzoega"), second).Lines.ToList(), "second.log");
        Assert.Equal("6 (library)", (await Board(f, admin))[2][4]);
        await ServerFactory.Send(admin, "/admin/missions", "/admin/missions/set", ("id", kind.Key), ("field", "scu"), ("value", "10"));
        (rows, fields) = await BoardAndFields(f, admin);
        Assert.Equal(("6 (hauler)", "10 (library)"), (rows[1][4], rows[2][4]));
        Assert.Equal(((long?)null, "10", "library"), (Field(fields[2]).Value, Field(fields[2]).Hint, Field(fields[2]).From));
        await ServerFactory.Send(player, "/hauling", "/hauling/jobs/scu", ("id", courier), ("title", CourierTitle), ("value", ""));   // his number taken back
        Assert.Equal("10 (library)", (await Board(f, admin))[1][4]);
        Assert.False(JsonDocument.Parse(await (await QuietSave(player, courier, "lots")).Content.ReadAsStringAsync()).RootElement.GetProperty("ok").GetBoolean());

        // what the game said for the haul is the suggestion for its kind
        Assert.Equal(1, library.SuggestFromGame(DateTime.UtcNow));
        var haulKind = library.Entries().Single(e => e.Row.Title == "RSI Disc. Month: Ling Small Haul").Row;
        Assert.Equal((8L, MissionLibrary.GameSaid), (haulKind.ScuSuggested!.Value, haulKind.ScuSuggestedBy));
        Assert.Equal(0, library.SuggestFromGame(DateTime.UtcNow));   // nothing new
    }

    static async Task<HttpResponseMessage> QuietSave(HttpClient c, string id, string value)
    {
        var page = await c.GetStringAsync("/hauling");
        using var req = new HttpRequestMessage(HttpMethod.Post, "/hauling/jobs/scu")
        {
            Content = new FormUrlEncodedContent(new[] { KeyValuePair.Create("id", id), KeyValuePair.Create("value", value),
                KeyValuePair.Create("__RequestVerificationToken", ServerFactory.Token(page)) }),
        };
        req.Headers.Add("X-Autosave", "1");
        return await c.SendAsync(req);
    }
}
