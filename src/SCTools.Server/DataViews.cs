using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SCTools.Server;

/// <summary>
/// The Hauling Planner's data.json as one viewer may see it (Audience): the admin's own view gets all of it; a player his party's session (its stops,
/// contracts, runs, leaderboard and the planner's routes of his party), his own debts and money transfers, the saved sessions he was in, and the all-time
/// leaderboard; a visitor the maps and the all-time leaderboard, nothing about anybody's session. Made from the one data.json LiveTracker makes, once per
/// viewer and version (kept until the next version), with an ETag of its own, so the page's 304 still works.
/// </summary>
public sealed class DataViews
{
    /// <summary>The session's parts of data.json: a player gets his party's (the party's own view when several haul in the session), a visitor none.</summary>
    static readonly string[] SessionKeys = { "session", "summary", "rewards", "pickups", "finished", "leaders", "runs", "runsLive" };

    readonly object _lock = new();
    string _etag = "";
    JsonObject? _full;
    readonly Dictionary<string, (byte[] Bytes, string ETag)> _made = new();

    /// <summary>The data for this viewer (allowed: the players he may see, null for everybody), and its ETag.</summary>
    public (byte[] Bytes, string ETag) For(LiveSnapshot snapshot, Viewer viewer, IReadOnlySet<string>? allowed)
    {
        if (viewer.Everything) return (snapshot.Bytes, snapshot.ETag);
        var key = viewer.Key + "|" + string.Join(",", (allowed ?? new HashSet<string>()).Select(n => n.ToLowerInvariant()).Order(StringComparer.Ordinal));
        lock (_lock)
        {
            if (_etag != snapshot.ETag)
            {
                _etag = snapshot.ETag;
                _full = null;
                _made.Clear();
            }
            if (_made.TryGetValue(key, out var made)) return made;
            _full ??= JsonNode.Parse(snapshot.Json) as JsonObject ?? new JsonObject();
            var view = (JsonObject)_full.DeepClone();
            if (viewer.Name == null) Visitor(view);
            else Player(view, viewer.Name, allowed ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            var bytes = Encoding.UTF8.GetBytes(view.ToJsonString());
            var etag = snapshot.ETag.TrimEnd('"') + "-" + Hash(key) + "\"";
            if (_made.Count > 50) _made.Clear();   // a handful of players: never many
            return _made[key] = (bytes, etag);
        }
    }

    static string Hash(string s)
    {
        uint h = 2166136261;
        foreach (var c in s) h = (h ^ c) * 16777619;
        return h.ToString("x8");
    }

    /// <summary>A visitor: the maps and the all-time leaderboard; nobody's session, debts or saved sessions. The page says to log in.</summary>
    static void Visitor(JsonObject d)
    {
        foreach (var k in SessionKeys.Concat(new[] { "planRuns", "runKeys", "runsLost", "debts", "debtsPay", "parties" })) d.Remove(k);
        d["session"] = "";
        d["visitor"] = true;
        if (d["history"] is JsonObject h) h.Remove("saved");
        if (d["planner"] is JsonObject p)
        {
            p["workers"] = new JsonArray();
            p["summary"] = "";
            p["note"] = "";
        }
    }

    /// <summary>A player: his party's session, his own debts, the saved sessions he was in.</summary>
    static void Player(JsonObject d, string me, IReadOnlySet<string> party)
    {
        bool Mine(string? n) => n != null && party.Contains(n.Trim());
        // the parties of the session: his own, with its view when several haul in it; none of the others
        var parties = d["parties"] as JsonArray ?? new JsonArray();
        var several = parties.Count > 1;
        var own = parties.OfType<JsonObject>().FirstOrDefault(p => (p["names"] as JsonArray ?? new JsonArray()).Any(n => string.Equals(n?.GetValue<string>(), me, StringComparison.OrdinalIgnoreCase)));
        var fullRuns = d["planRuns"]?.DeepClone() ?? d["runs"]?.DeepClone();
        var fullLive = d["runsLive"]?.DeepClone() as JsonArray;
        if (own == null)
        {
            foreach (var k in SessionKeys) d.Remove(k);   // not in the session: none of it
            d["session"] = "";
        }
        else if (several && own["view"] is JsonObject view)
        {
            foreach (var k in SessionKeys) d[k] = view[k]?.DeepClone();
        }
        if (own != null) own.Remove("view");
        d["parties"] = own == null ? new JsonArray() : new JsonArray(own.DeepClone());
        d.Remove("runKeys");    // the admin's Lost Cargo pick
        d.Remove("runsLost");

        // the Cargo Runs under the map: the rows of his party's workers, with when each run on the way started
        if (own != null && fullRuns is JsonObject runs && runs["rows"] is JsonArray rows)
        {
            var keptRows = new JsonArray();
            var keptLive = new JsonArray();
            for (var i = 0; i < rows.Count; i++)
            {
                var worker = (rows[i]?["cells"] as JsonArray)?.ElementAtOrDefault(1)?.GetValue<string>();
                if (!Mine(worker)) continue;
                keptRows.Add(rows[i]!.DeepClone());
                keptLive.Add(fullLive != null && i < fullLive.Count ? fullLive[i]?.DeepClone() : null);
            }
            runs["rows"] = keptRows;
            d["planRuns"] = runs;
            d["planRunsLive"] = keptLive;
        }
        else
        {
            d.Remove("planRuns");
            d["planRunsLive"] = new JsonArray();
        }

        // the planner: the routes and cards of his party's workers
        if (d["planner"] is JsonObject p && p["workers"] is JsonArray workers)
        {
            var kept = new JsonArray(workers.OfType<JsonObject>().Where(w => Mine(w["name"]?.GetValue<string>())).Select(w => (JsonNode)w.DeepClone()).ToArray());
            if (kept.Count != workers.Count) p["summary"] = "";   // the plan's summary is about everybody
            p["workers"] = kept;
        }

        // the debts: his own (the Earnings line of his, his balances, the transfers he sent or got, the payouts he had a share of)
        if (d["debts"] is JsonObject debts)
        {
            Rows(debts["earnings"], cells => Same(Cell(cells, 0), me));
            if (debts["balances"] is JsonArray balances)
                debts["balances"] = new JsonArray(balances.OfType<JsonObject>().Where(b => Same(b["name"]?.GetValue<string>(), me)).Select(b => (JsonNode)b.DeepClone()).ToArray());
            var keptTransfers = Rows(debts["transfers"], cells => Same(Cell(cells, 1), me) || Same(Cell(cells, 2), me));
            if (debts["transfersEdit"] is JsonArray edits)   // the payment ticks go with the transfers' rows, one for one
                debts["transfersEdit"] = new JsonArray(keptTransfers.Where(i => i < edits.Count).Select(i => edits[i]?.DeepClone()).ToArray());
            Rows(debts["payouts"], cells => Names(Cell(cells, 3)).Any(n => Same(n, me)));
        }

        // the saved sessions: the ones he carried in
        if (d["history"] is JsonObject history)
            Rows(history["saved"], cells => Names(Cell(cells, 4)).Any(n => Same(n, me)));
    }

    static bool Same(string? a, string? b) => a != null && b != null && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    static string? Cell(JsonArray? cells, int i) => cells != null && i < cells.Count ? cells[i]?.GetValue<string>() : null;

    /// <summary>The names in a cell like "zzatvo 8,842,521, zzoega 7,644,729" or "zzoega, zzatvo": every word that is not a number.</summary>
    static IEnumerable<string> Names(string? cell) =>
        (cell ?? "").Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries).Where(w => !w.All(c => char.IsDigit(c) || c == '.' || c == '%' || c == '+' || c == '-'));

    /// <summary>Keeps the rows of a table ({ headers, rows }) whose cells pass; returns the indexes of the rows kept (in the table as it was).</summary>
    static List<int> Rows(JsonNode? table, Func<JsonArray?, bool> keep)
    {
        var kept = new List<int>();
        if (table is not JsonObject t || t["rows"] is not JsonArray rows) return kept;
        var left = new JsonArray();
        for (var i = 0; i < rows.Count; i++)
        {
            if (!keep(rows[i]?["cells"] as JsonArray)) continue;
            kept.Add(i);
            left.Add(rows[i]!.DeepClone());
        }
        t["rows"] = left;
        return kept;
    }

    /// <summary>The Map tool's data (map.json) as this viewer may see it: the players of his party, their objectives and their missions; a visitor the places alone.</summary>
    public static JsonNode MapFor(object snapshot, IReadOnlySet<string>? allowed)
    {
        var node = JsonSerializer.SerializeToNode(snapshot, JsonSerializerOptions.Web)!;
        if (allowed == null || node is not JsonObject d) return node;
        bool Mine(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) && allowed.Contains(s);
        if (d["players"] is JsonArray players)
            d["players"] = new JsonArray(players.Where(p => Mine(p?["name"])).Select(p => p!.DeepClone()).ToArray());
        if (d["objectives"] is JsonArray objectives)
            d["objectives"] = new JsonArray(objectives.Where(o => Mine(o?["player"]) || (o?["who"] as JsonArray ?? new JsonArray()).Any(Mine)).Select(o => o!.DeepClone()).ToArray());
        if (d["missions"] is JsonArray missions)
        {
            var kept = new JsonArray();
            foreach (var m in missions.OfType<JsonObject>())
            {
                if (m["who"] is not JsonArray who || !who.Any(Mine)) continue;
                var copy = (JsonObject)m.DeepClone();
                copy["who"] = new JsonArray(who.Where(Mine).Select(w => w!.DeepClone()).ToArray());   // the others who have it are not his to know
                kept.Add(copy);
            }
            d["missions"] = kept;
        }
        if (d["deaths"] is JsonArray deaths)   // the skulls: his party's alone, like the players (#410)
            d["deaths"] = new JsonArray(deaths.Where(x => Mine(x?["player"])).Select(x => x!.DeepClone()).ToArray());
        if (allowed.Count == 0) d["unplaced"] = 0;
        return d;
    }
}
