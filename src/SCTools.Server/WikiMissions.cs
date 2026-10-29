using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SCTools.Server.Data;

namespace SCTools.Server;

/// <summary>
/// The Star Citizen Wiki API's missions (WikiMissions). The list of every mission of the game (api.star-citizen.wiki/api/missions, 200 to a page) is read
/// once a day (LearnAll): every mission's debug name, title, reward, faction, category, reputation and legality, so the library can show them for every kind
/// it has (also one whose contract id no log showed: by its name). The page of a contract the logs had (api/missions/{uuid}: the uuid is the log's
/// contractDefinitionId, the same whatever names a string mod shows) is asked for too, a few at a time (Learn): it has the hauling orders (the SCU of every
/// stop), which the list does not. A contract is asked for once (one the API does not know, again after a week); when the API cannot be reached, the next time.
/// </summary>
public sealed class WikiMissions
{
    /// <summary>Where a mission of the API is: this, then its uuid.</summary>
    public const string Api = "https://api.star-citizen.wiki/api/missions/";

    /// <summary>The list of every mission, a page at a time (?limit=200&amp;page=n; 200 is the most the API gives to a page).</summary>
    public const string List = "https://api.star-citizen.wiki/api/missions";
    public const int PageSize = 200;

    /// <summary>How often the list of every mission is read again (the Wiki changes with the game's versions).</summary>
    public static readonly TimeSpan ListEvery = TimeSpan.FromDays(1);

    /// <summary>When the list was read last: kept in SiteValues under this name (the ticks of the time, UTC).</summary>
    public const string ListReadKey = "WikiMissionsListUtc";

    static readonly TimeSpan AskAgain = TimeSpan.FromDays(7);
    const int PerRound = 10;
    static readonly HttpClient Http = MakeClient();

    readonly DataStore _store;
    readonly object _lock = new();

    public WikiMissions(DataStore store) => _store = store;

    /// <summary>How a contract is asked for: its uuid, and the API's answer (null: the API does not know it). The tests put their own here.</summary>
    public Func<string, CancellationToken, Task<string?>> Fetch { get; set; } = FetchFromApi;

    /// <summary>How a page of the list is asked for: its number (from 1), and the API's answer. The tests put their own here.</summary>
    public Func<int, CancellationToken, Task<string?>> FetchPage { get; set; } = FetchPageFromApi;

    static HttpClient MakeClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
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

    static async Task<string?> FetchPageFromApi(int page, CancellationToken stop)
    {
        using var r = await Http.GetAsync(List + "?limit=" + PageSize.ToString(CultureInfo.InvariantCulture) + "&page=" + page.ToString(CultureInfo.InvariantCulture), stop);
        r.EnsureSuccessStatusCode();
        return await r.Content.ReadAsStringAsync(stop);
    }

    /// <summary>The missions the API knows, of the ones read so far (the list and the pages asked for), by uuid.</summary>
    public static Dictionary<string, WikiContract> Known(TrackerDb db) =>
        db.WikiMissions.AsNoTracking().Where(r => r.Found).ToList().ToDictionary(r => r.Uuid, ToContract, StringComparer.OrdinalIgnoreCase);

    /// <summary>When the list of every mission was read last, or null (never).</summary>
    public DateTime? ListReadUtc()
    {
        lock (_lock)
        {
            using var db = _store.Open();
            return db.SiteValues.Find(ListReadKey) is { } v ? new DateTime(v.Value, DateTimeKind.Utc) : null;
        }
    }

    /// <summary>Reads the list of every mission when it was not read in the last day (ListEvery). Returns how many missions it had; 0 when it was not due, or the API was not there.</summary>
    public async Task<int> LearnAllIfDue(DateTime nowUtc, CancellationToken stop = default)
    {
        var read = ListReadUtc();
        if (read != null && nowUtc - read.Value < ListEvery) return 0;
        return await LearnAll(nowUtc, stop);
    }

    /// <summary>
    /// Reads the list of every mission of the game, page by page, and keeps what it says about each: the debug name, title, reward, faction, category,
    /// reputation and legality. The list has no hauling orders: a mission read from its own page before keeps the orders it had, and one that is new is
    /// ListOnly (its page is asked for when a log has the contract). Returns how many missions the list had; 0 when the API could not be reached, and the
    /// list is not counted as read then (the next time).
    /// </summary>
    public async Task<int> LearnAll(DateTime nowUtc, CancellationToken stop = default)
    {
        var total = 0;
        var pages = 1;
        for (var page = 1; page <= pages; page++)
        {
            string? json;
            try
            {
                json = await FetchPage(page, stop);
            }
            catch (Exception ex) when ((ex is HttpRequestException || ex is TaskCanceledException) && !stop.IsCancellationRequested)
            {
                return 0;   // the API is not there now: the next time
            }
            if (json == null) return 0;
            var (rows, last) = ParseList(json);
            if (page == 1) pages = Math.Max(1, last);
            if (rows.Count == 0) break;
            lock (_lock)
            {
                using var db = _store.Open();
                foreach (var found in rows)
                {
                    var row = db.WikiMissions.Find(found.Uuid);
                    if (row == null) db.WikiMissions.Add(row = new WikiMissionRow { Uuid = found.Uuid, AskedUtc = nowUtc, ListOnly = true });
                    else if (!row.Found) (row.AskedUtc, row.ListOnly) = (nowUtc, true);   // its page was not there, the list has it: the page is asked once more
                    row.Found = true;
                    (row.DebugName, row.Title) = (found.DebugName, found.Title);
                    (row.RewardMin, row.RewardMax, row.RewardCurrency) = (found.RewardMin, found.RewardMax, found.RewardCurrency);
                    (row.Faction, row.Category, row.Xp, row.Legality) = (found.Faction, found.Category, found.Xp, found.Legality);
                    if (found.Orders != null) row.Orders = found.Orders;
                }
                db.SaveChanges();
            }
            total += rows.Count;
        }
        lock (_lock)
        {
            using var db = _store.Open();
            var v = db.SiteValues.Find(ListReadKey);
            if (v == null) db.SiteValues.Add(v = new SiteValueRow { Key = ListReadKey });
            v.Value = nowUtc.Ticks;
            v.ChangedUtc = nowUtc;
            db.SaveChanges();
        }
        return total;
    }

    /// <summary>
    /// Asks the API about these contracts (uuids) that it was not asked about yet, or that only the list was read for (ListOnly: the hauling orders are on
    /// the mission's own page), a few at a time. Returns how many it knows now that it did not.
    /// </summary>
    public async Task<int> Learn(IEnumerable<string> uuids, DateTime nowUtc, CancellationToken stop = default)
    {
        List<string> ask;
        using (var db = _store.Open())
        {
            var asked = db.WikiMissions.AsNoTracking().ToList().ToDictionary(r => r.Uuid, r => (r.Found, r.AskedUtc, r.ListOnly), StringComparer.OrdinalIgnoreCase);
            ask = uuids.Select(u => u.Trim().ToLowerInvariant()).Where(u => Guid.TryParse(u, out _)).Distinct()
                .Where(u => !asked.TryGetValue(u, out var a) || a.ListOnly || (!a.Found && nowUtc - a.AskedUtc > AskAgain)).Take(PerRound).ToList();
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
                if (found != null)
                {
                    if (!row.Found || row.ListOnly) learned++;
                    row.Found = true;
                    row.ListOnly = false;
                    (row.DebugName, row.Title, row.Orders) = (found.DebugName, found.Title, found.Orders);
                    (row.RewardMin, row.RewardMax, row.RewardCurrency) = (found.RewardMin, found.RewardMax, found.RewardCurrency);
                    (row.Faction, row.Category, row.Xp, row.Legality) = (found.Faction ?? row.Faction, found.Category ?? row.Category, found.Xp ?? row.Xp, found.Legality ?? row.Legality);
                }
                else if (row.ListOnly)
                {
                    row.ListOnly = false;   // the list has it, its own page is not there: what the list said stays, and it is not asked again
                }
                else
                {
                    row.Found = false;
                }
                db.SaveChanges();
            }
        }
        return learned;
    }

    /// <summary>
    /// The API's answer about a mission (its own page) as a row: its debug name, title, hauling orders, "100-150;100-150" (min_scu-max_scu of every order; only
    /// one of them: both are it), reward, faction, category, reputation and legality; null when it is not an answer about a mission.
    /// </summary>
    public static WikiMissionRow? Parse(string uuid, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var d) || d.ValueKind != JsonValueKind.Object) return null;
            return Row(uuid, d);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// A page of the list of every mission as rows (the ones with a uuid), and how many pages the list has (meta.last_page; 0 when the page does not say).
    /// Not an answer about missions: no rows.
    /// </summary>
    public static (List<WikiMissionRow> Rows, int LastPage) ParseList(string json)
    {
        var rows = new List<WikiMissionRow>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var list) || list.ValueKind != JsonValueKind.Array) return (rows, 0);
            foreach (var d in list.EnumerateArray())
            {
                if (d.ValueKind != JsonValueKind.Object || Text(d, "uuid") is not { } uuid || !Guid.TryParse(uuid, out _)) continue;
                rows.Add(Row(uuid.ToLowerInvariant(), d));
            }
            var last = doc.RootElement.TryGetProperty("meta", out var meta) && meta.ValueKind == JsonValueKind.Object ? Number(meta, "last_page") ?? 0 : 0;
            return (rows, last);
        }
        catch (JsonException)
        {
            return (new List<WikiMissionRow>(), 0);
        }
    }

    static string? Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    static int? Number(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;
    static long? Long(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var n) ? (long)Math.Round(n) : null;
    static bool? Flag(JsonElement e, string name) => e.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) ? v.GetBoolean() : null;

    /// <summary>
    /// A mission of the API (its own page, or one of the list) as a row. The faction is the Wiki's faction, else the mission giver; the category is the Wiki's
    /// "type" (reward_scope, as its mission page shows it: Hauling, Bounty Hunter...), else its mission_type; the reputation is reputation_amount; the legality
    /// its label, else Legal or Illegal by the flag.
    /// </summary>
    static WikiMissionRow Row(string uuid, JsonElement d)
    {
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
        var faction = d.TryGetProperty("faction", out var f) && f.ValueKind == JsonValueKind.Object ? Text(f, "name") : null;
        var legality = Text(d, "legality_label") ?? (Flag(d, "illegal") is { } illegal ? illegal ? "Illegal" : "Legal" : null);
        static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        return new WikiMissionRow
        {
            Uuid = uuid, DebugName = Text(d, "debug_name"), Title = Text(d, "title"), Orders = orders.Count > 0 ? string.Join(";", orders) : null, Found = true,
            RewardMin = Long(d, "reward_min"), RewardMax = Long(d, "reward_max"), RewardCurrency = Text(d, "reward_currency"),
            Faction = Clean(faction) ?? Clean(Text(d, "mission_giver")), Category = Clean(Text(d, "reward_scope")) ?? Clean(Text(d, "mission_type")),
            Xp = Long(d, "reputation_amount"), Legality = Clean(legality),
        };
    }

    /// <summary>A row as what the tracker knows about the contract.</summary>
    public static WikiContract ToContract(WikiMissionRow r) =>
        new(r.Uuid, r.DebugName, r.Title, Orders(r.Orders), r.RewardMin, r.RewardMax, r.RewardCurrency, r.Faction, r.Category, r.Xp, r.Legality);

    /// <summary>"100-150;100-150" as the ranges.</summary>
    public static List<ScuRange> Orders(string? text) =>
        (text ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries).Select(o => o.Split('-'))
            .Where(p => p.Length == 2 && int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out _) && int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            .Select(p => new ScuRange(int.Parse(p[0], CultureInfo.InvariantCulture), int.Parse(p[1], CultureInfo.InvariantCulture))).ToList();
}
