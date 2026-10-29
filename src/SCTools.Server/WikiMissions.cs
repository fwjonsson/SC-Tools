using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SCTools.Server.Data;

namespace SCTools.Server;

/// <summary>
/// The Star Citizen Wiki API's missions for the contracts the logs had (api.star-citizen.wiki/api/missions/{uuid}: the uuid is the log's
/// contractDefinitionId, the same whatever names a string mod shows): their debug name, title, hauling orders (the SCU of every stop) and reward, kept in the
/// database (WikiMissions). A contract is asked for once (one the API does not know, again after a week), a few at a time; when the API cannot be reached,
/// the next time.
/// </summary>
public sealed class WikiMissions
{
    /// <summary>Where a mission of the API is: this, then its uuid.</summary>
    public const string Api = "https://api.star-citizen.wiki/api/missions/";

    static readonly TimeSpan AskAgain = TimeSpan.FromDays(7);
    const int PerRound = 10;
    static readonly HttpClient Http = MakeClient();

    readonly DataStore _store;
    readonly object _lock = new();

    public WikiMissions(DataStore store) => _store = store;

    /// <summary>How a contract is asked for: its uuid, and the API's answer (null: the API does not know it). The tests put their own here.</summary>
    public Func<string, CancellationToken, Task<string?>> Fetch { get; set; } = FetchFromApi;

    static HttpClient MakeClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("SCTools/1.0 (+https://sc-tools.fwjonsson.dev)");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return c;
    }

    static async Task<string?> FetchFromApi(string uuid, CancellationToken stop)
    {
        using var r = await Http.GetAsync(Api + Uri.EscapeDataString(uuid), stop);
        if (r.StatusCode == HttpStatusCode.NotFound) return null;
        r.EnsureSuccessStatusCode();
        return await r.Content.ReadAsStringAsync(stop);
    }

    /// <summary>The contracts the API knows, of the ones asked for so far, by uuid.</summary>
    public static Dictionary<string, WikiContract> Known(TrackerDb db) =>
        db.WikiMissions.AsNoTracking().Where(r => r.Found).ToList().ToDictionary(r => r.Uuid, ToContract, StringComparer.OrdinalIgnoreCase);

    /// <summary>Asks the API about these contracts (uuids) that it was not asked about yet, a few at a time. Returns how many it knows now that it did not.</summary>
    public async Task<int> Learn(IEnumerable<string> uuids, DateTime nowUtc, CancellationToken stop = default)
    {
        List<string> ask;
        using (var db = _store.Open())
        {
            var asked = db.WikiMissions.AsNoTracking().ToList().ToDictionary(r => r.Uuid, r => (r.Found, r.AskedUtc), StringComparer.OrdinalIgnoreCase);
            ask = uuids.Select(u => u.Trim().ToLowerInvariant()).Where(u => Guid.TryParse(u, out _)).Distinct()
                .Where(u => !asked.TryGetValue(u, out var a) || (!a.Found && nowUtc - a.AskedUtc > AskAgain)).Take(PerRound).ToList();
        }
        var learned = 0;
        foreach (var uuid in ask)
        {
            string? json;
            try
            {
                json = await Fetch(uuid, stop);
            }
            catch (Exception ex) when ((ex is HttpRequestException || ex is TaskCanceledException) && !stop.IsCancellationRequested)
            {
                continue;   // the API is not there now (or says too many): the next time
            }
            var found = json == null ? null : Parse(uuid, json);
            lock (_lock)
            {
                using var db = _store.Open();
                var row = db.WikiMissions.Find(uuid);
                if (row == null) db.WikiMissions.Add(row = new WikiMissionRow { Uuid = uuid });
                row.AskedUtc = nowUtc;
                row.Found = found != null;
                if (found != null)
                {
                    (row.DebugName, row.Title, row.Orders) = (found.DebugName, found.Title, found.Orders);
                    (row.RewardMin, row.RewardMax, row.RewardCurrency) = (found.RewardMin, found.RewardMax, found.RewardCurrency);
                }
                db.SaveChanges();
            }
            if (found != null) learned++;
        }
        return learned;
    }

    /// <summary>
    /// The API's answer about a mission as a row: its debug name, title and hauling orders, "100-150;100-150" (min_scu-max_scu of every order; only one of
    /// them: both are it); null when it is not an answer about a mission.
    /// </summary>
    public static WikiMissionRow? Parse(string uuid, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var d) || d.ValueKind != JsonValueKind.Object) return null;
            static string? Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            static int? Number(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;
            static long? Long(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var n) ? (long)Math.Round(n) : null;
            var orders = new List<string>();
            if (d.TryGetProperty("hauling_orders", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var o in list.EnumerateArray())
                {
                    var (min, max) = (Number(o, "min_scu"), Number(o, "max_scu"));
                    if (min == null && max == null) continue;
                    orders.Add((min ?? max)!.Value.ToString(CultureInfo.InvariantCulture) + "-" + (max ?? min)!.Value.ToString(CultureInfo.InvariantCulture));
                }
            }
            return new WikiMissionRow
            {
                Uuid = uuid, DebugName = Text(d, "debug_name"), Title = Text(d, "title"), Orders = orders.Count > 0 ? string.Join(";", orders) : null, Found = true,
                RewardMin = Long(d, "reward_min"), RewardMax = Long(d, "reward_max"), RewardCurrency = Text(d, "reward_currency"),
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>A row as what the tracker knows about the contract.</summary>
    public static WikiContract ToContract(WikiMissionRow r) => new(r.Uuid, r.DebugName, r.Title, Orders(r.Orders), r.RewardMin, r.RewardMax, r.RewardCurrency);

    /// <summary>"100-150;100-150" as the ranges.</summary>
    public static List<ScuRange> Orders(string? text) =>
        (text ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries).Select(o => o.Split('-'))
            .Where(p => p.Length == 2 && int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out _) && int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            .Select(p => new ScuRange(int.Parse(p[0], CultureInfo.InvariantCulture), int.Parse(p[1], CultureInfo.InvariantCulture))).ToList();
}
