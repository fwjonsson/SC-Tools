using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SCTools.Missions;
using SCTools.Play;

namespace SCTools.Server;

/// <summary>
/// The Companion's data (#380, Frank 30 Oct 2026): a player's own page to keep open while playing. Personal: it is the viewer's (the player the admin looks as
/// with View as, else who is logged in), with his party on the map, in the Players list and in the contracts. The admin's own view (View as: Admin) has
/// everybody on the map, and the rest is his own player's like anybody's (Frank, 30 Oct 2026); View as his own player is a player's Companion.
/// <list type="bullet">
/// <item>Map: the Map tool's map.json cut to the viewer's party (DataViews.MapFor), with who the viewer is (me: the map follows him to another system), and for
/// the admin the Mission Library's fields of every contract (edit), so he fills them in as he plays. For the admin's own view the players and objectives
/// are everybody's, and party names the ones the lists beside the map keep to.</item>
/// <item>Session: his current game session (GameSession: from when the game started until an hour after it was closed; after that the last one, marked
/// over) with the Session tool's figures for it, and the contracts his party ended in it (completed, failed or abandoned, with who had each). No running
/// time in it (the clock rule): the browser counts the game going on from its start. The earnings are his part (the contracts tracker's Earned); a player with
/// combined accounts (AppData.CombinedAccounts) sees what all of them earned in his session together, per hour of his own time played (Frank, 30 Oct 2026).</item>
/// </list>
/// </summary>
public sealed class CompanionService
{
    readonly MapService _map;
    readonly Audience _audience;
    readonly Playtime _playtime;
    readonly MissionLibrary _library;
    readonly ContractTracker _contracts;
    readonly ScripValues _scrip;
    readonly LogUploads _uploads;
    readonly LiveTracker _live;
    readonly SessionStarts _starts;
    readonly object _lock = new();
    readonly Dictionary<string, (byte[] Bytes, string ETag)> _made = new();

    public CompanionService(MapService map, Audience audience, Playtime playtime, MissionLibrary library, ContractTracker contracts, ScripValues scrip, LiveTracker live,
        SessionStarts starts)
    {
        _starts = starts;
        _map = map;
        _audience = audience;
        _playtime = playtime;
        _library = library;
        _contracts = contracts;
        _scrip = scrip;
        _uploads = live.Uploads;
        _live = live;
    }

    /// <summary>This player's accounts whose earnings the Companion adds up: his combined accounts (set on the Account page), else himself alone.</summary>
    List<string> AccountsOf(string me) =>
        _live.Read(t => t.State.Data.CombinedAccounts.FirstOrDefault(g => g.Any(n => PlayerNames.SameName(n, me)))?.ToList()) is { Count: > 1 } group
            ? group.OrderBy(n => !PlayerNames.SameName(n, me)).ThenBy(n => n, StringComparer.OrdinalIgnoreCase).ToList()   // his own first
            : new List<string> { me };

    /// <summary>The players this viewer's Companion shows: himself and his party (Audience.PartyOf); a visitor nobody.</summary>
    HashSet<string> PartyOf(string? me, DateTime nowUtc) =>
        me == null ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) : _audience.PartyOf(me, nowUtc);

    /// <summary>
    /// The map for this viewer (me: his player's name, null for a visitor); editor: the real admin, who gets the library's fields of the contracts;
    /// everybody: the admin's own view, with everybody's players and objectives on the map (party: the ones the Players and Objectives lists show).
    /// </summary>
    public JsonNode Map(string? me, bool editor, DateTime nowUtc, bool everybody = false)
    {
        var snapshot = _map.Snapshot(nowUtc);
        var party = PartyOf(me, nowUtc);
        var node = DataViews.MapFor(snapshot, party);
        if (node is JsonObject d)
        {
            d["me"] = me;
            if (me != null) d["mine"] = JsonSerializer.SerializeToNode(_map.Mine(me), JsonSerializerOptions.Web);   // his ship and drive, for measuring a distance (#408)
            if (everybody && JsonSerializer.SerializeToNode(snapshot, JsonSerializerOptions.Web) is JsonObject all)
            {
                d["players"] = all["players"]?.DeepClone();
                d["objectives"] = all["objectives"]?.DeepClone();
                d["deaths"] = all["deaths"]?.DeepClone();
                d["unplaced"] = all["unplaced"]?.DeepClone();
                d["party"] = new JsonArray(party.Order(StringComparer.OrdinalIgnoreCase).Select(n => (JsonNode)n).ToArray());
            }
            if (editor && d["missions"] is JsonArray missions)
            {
                _library.Collect();   // a contract just taken is in the library (nothing new: nothing done)
                var entries = ContractInfo.ByKey(_library);
                foreach (var m in missions.OfType<JsonObject>())
                    if (m["key"]?.GetValue<string>() is { } key && entries.TryGetValue(key, out var e))
                        m["edit"] = JsonSerializer.SerializeToNode(ContractEdit.Of(e), JsonSerializerOptions.Web);
            }
        }
        return node;
    }

    /// <summary>Every faction in the library so far (typed in, or the Wiki's), for the admin's Faction fields to pick from, as on Edit Mission Info.</summary>
    public List<string> Factions() =>
        _library.Entries().Select(e => e.FactionShown).Where(g => !string.IsNullOrEmpty(g)).Select(g => g!).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// The session part for this viewer, and its ETag: the same data again is the same tag (the page's poll gets a 304). Made again when a piece of a log
    /// came in, the library or the scrip's value changed, or a minute went by (the session can be over by then); never for a clock.
    /// </summary>
    public (byte[] Bytes, string ETag) Session(string? me, bool editor, DateTime nowUtc)
    {
        var party = PartyOf(me, nowUtc);
        var accounts = me == null ? new List<string>() : AccountsOf(me);
        var key = string.Join("|", me?.ToLowerInvariant() ?? "", editor, string.Join(",", party.Select(n => n.ToLowerInvariant()).Order(StringComparer.Ordinal)),
            string.Join(",", accounts).ToLowerInvariant(), _uploads.Pieces, _library.Version, _scrip.Mg, _scrip.Council, _starts.Version, nowUtc.Ticks / TimeSpan.TicksPerMinute);
        lock (_lock)
            if (_made.TryGetValue(key, out var made)) return made;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(SessionOf(me, party, accounts, editor, nowUtc), JsonSerializerOptions.Web);
        var etag = "\"" + Convert.ToHexString(SHA1.HashData(bytes))[..16].ToLowerInvariant() + "\"";
        lock (_lock)
        {
            if (_made.Count > 50) _made.Clear();   // a handful of players: never many
            return _made[key] = (bytes, etag);
        }
    }

    object SessionOf(string? me, IReadOnlyCollection<string> party, IReadOnlyList<string> accounts, bool editor, DateTime nowUtc)
    {
        if (me == null) return new { visitor = true };
        _library.Collect();
        var session = GameSession.Latest(_playtime.Games(me, nowUtc), nowUtc);
        if (session == null) return new { me, session = (object?)null, completed = Array.Empty<object>() };
        // its start: the admin's (SessionStarts, his Start time row; Frank, 31 Oct 2026), else the logs'. The games and the missions count from it
        var from = session.StartUtc;
        var start = _starts.Of(SessionStarts.Companion, me, from) ?? from;
        var playedBefore = session.Games.Where(g => !g.Running).Aggregate(TimeSpan.Zero, (t, g) => t + Since(g, start));
        // the missions of the session: from its start to now, or to its end when it is over
        var to = session.Over ? session.EndUtc!.Value : nowUtc;
        long mg = _scrip.Mg, council = _scrip.Council;
        var own = PlayStats.For(start, to, Array.Empty<PlayedGame>(), _contracts.Earned(me), mg, council);
        // the money: his part, or with combined accounts what all of them earned in his session together (the missions completed stay his own)
        var totals = accounts.Count > 1 ? PlayStats.For(start, to, Array.Empty<PlayedGame>(), _contracts.Earned(accounts), mg, council) : own;
        var running = session.Running;
        return new
        {
            me,
            session = new
            {
                start,
                from,   // the logs' start: the admin's Start time row says which session it is
                startSet = start != from,
                running = running == null ? (DateTime?)null : running.StartUtc > start ? running.StartUtc : start,   // the game going on: the browser counts its time on from here
                lastEnd = running == null ? session.LastEndUtc : (DateTime?)null,
                over = session.Over,
                playedBefore = Math.Round(playedBefore.TotalSeconds),
                crashes = session.Crashes,
                completed = own.Completed,
                withoutReward = totals.WithoutReward,
                accounts = accounts.Count > 1 ? accounts : null,   // the earnings are these accounts' together
                auec = totals.Auec,
                mg = totals.Mg,
                council = totals.Council,
                value = Math.Round(totals.Value),
                mgValue = mg,
                councilValue = council,
            },
            completed = Ended(party, start, to, editor),
        };
    }

    /// <summary>The part of a game after the session's start (all of it when the session starts before it).</summary>
    static TimeSpan Since(PlayedGame g, DateTime start)
    {
        var from = g.StartUtc > start ? g.StartUtc : start;
        return g.EndUtc > from ? g.EndUtc - from : TimeSpan.Zero;
    }

    /// <summary>The contracts the party ended in the session, the newest first: a shared one once, with everybody who had it, and the library's columns.</summary>
    List<object> Ended(IReadOnlyCollection<string> party, DateTime fromUtc, DateTime toUtc, bool editor)
    {
        var entries = ContractInfo.ByKey(_library);
        return _library.Ended(party, fromUtc, toUtc)
            .GroupBy(m => m.MissionId, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var first = g.OrderBy(m => m.EndedUtc).First();
                var e = first.Kind != null && entries.TryGetValue(first.Kind, out var found) ? found : null;
                var i = ContractInfo.Of(e, first.Title ?? "A mission");
                return (Ended: g.Max(m => m.EndedUtc), Row: (object)new
                {
                    title = i.Title,
                    who = g.Select(m => m.Player).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList(),
                    outcome = Said(first.Outcome),
                    ended = g.Max(m => m.EndedUtc),
                    faction = i.Faction, category = i.Category, scu = i.Scu, reward = i.Reward, mg = i.Mg, council = i.Council, xp = i.Xp, legality = i.Legality,
                    key = e?.Row.Key,
                    edit = editor ? i.Edit : null,
                });
            })
            .OrderByDescending(r => r.Ended).Select(r => r.Row).ToList();
    }

    /// <summary>How a contract ended, as the table says it.</summary>
    static string Said(MissionOutcome o) => o switch
    {
        MissionOutcome.Complete => "Completed",
        MissionOutcome.Fail => "Failed",
        _ => "Abandoned",
    };
}
