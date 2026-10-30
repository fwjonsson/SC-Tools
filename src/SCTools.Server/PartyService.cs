using SCTools.Server.Data;

namespace SCTools.Server;

/// <summary>A player as the Party tool shows him: playing or not and when he was last seen, his in-game party, the ship he flies and his fleet, where the
/// Hauling Planner has him (null when he is not in its party), and his tracker.</summary>
/// <param name="NoTracker">he runs no SC Tools Tracker: only the others' logs show him (PartyService.WithoutTracker)</param>
public sealed record PartyMember(
    string Name, bool Playing, DateTime? LastSeenUtc, PartyGroup? Party, ActiveShip Ship, IReadOnlyList<FleetShip> Fleet, string? Location, TrackerSeen? Tracker,
    bool NoTracker = false);

/// <summary>
/// The players of the site, for every tool: who the players are (the tracker keys, the Hauling Planner's party and the fleets), who is playing, and what
/// there is to know about a player (the ship he flies and his fleet, FleetService; his tracker, TrackerVersions). The parties are the party tracker's
/// (PartyTracker, worked out once per refresh by LiveTracker.Parties): this asks it.
/// </summary>
public sealed class PartyService
{
    readonly DataStore _store;
    readonly LogUploads _uploads;
    readonly LiveTracker _live;
    readonly FleetService _fleet;
    readonly TrackerVersions _versions;

    readonly TrackerKeys _keys;

    public PartyService(DataStore store, LogUploads uploads, LiveTracker live, FleetService fleet, TrackerVersions versions, TrackerKeys keys)
    {
        _keys = keys;
        _store = store;
        _uploads = uploads;
        _live = live;
        _fleet = fleet;
        _versions = versions;
    }

    static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Everybody the site knows, as the Hauling Planner's party writes them, then the others: the tracker keys, the fleets, the logs.</summary>
    public List<string> Players()
    {
        var names = new List<string>(_live.Read(t => t.State.Data.Party.ToList()));
        void Add(string? n)
        {
            if (!string.IsNullOrWhiteSpace(n) && !names.Any(x => Same(x, n))) names.Add(n.Trim());
        }
        foreach (var p in _keys.Players) Add(p);   // in memory (the admin's Keys page reloads them): this runs for every page the admin opens
        foreach (var p in _fleet.Players()) Add(p);
        lock (_uploads.Sync)
            foreach (var log in _uploads.Logs) Add(log.Player ?? log.Uploader);
        return names;
    }

    /// <summary>The in-game parties now, everybody playing (a player alone is a party of one): the party tracker's.</summary>
    public List<PartyGroup> Parties(DateTime nowUtc) => _live.Parties(nowUtc).Groups;

    /// <summary>When each player's game was last seen (the latest line any of his logs has), and whether it still runs (it grew in the last minutes).</summary>
    Dictionary<string, (bool Playing, DateTime Last)> Seen(DateTime nowUtc) =>
        _uploads.Games().Where(g => g.Value.Count > 0).ToDictionary(
            g => g.Key,
            g =>
            {
                var last = g.Value.Max(r => r.Run.Last);
                return (g.Value.Any(r => !r.Quit && nowUtc - r.Run.Last < PartyTracker.Quiet), last);
            },
            StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Has this player no SC Tools Tracker: no log of his own on the server and no tracker key. He is only known from the party lines of the others' logs
    /// (in the party, joined, left, leads, his game went out), not his ship, where he is or his own missions (Frank, 31 Oct 2026: marked "no tracker").
    /// </summary>
    public bool WithoutTracker(string name, PartyBook book) => !book.HasTracker(name) && !_keys.Players.Any(p => Same(p, name));

    /// <summary>Everybody, the ones playing first: name, playing, last seen (a player without a tracker: the last time the others' logs showed him).</summary>
    public List<(string Name, bool Playing, DateTime? LastSeenUtc)> Roster(DateTime nowUtc)
    {
        var seen = Seen(nowUtc);
        var book = _live.Parties(nowUtc);
        var list = new List<(string Name, bool Playing, DateTime? LastSeenUtc)>();
        foreach (var n in Players())
            list.Add(seen.TryGetValue(n, out var s) ? (n, s.Playing, (DateTime?)s.Last)
                : book.LastSeen.TryGetValue(n, out var t) ? (n, false, (DateTime?)t) : (n, false, (DateTime?)null));
        return list.OrderByDescending(p => p.Playing).ThenByDescending(p => p.LastSeenUtc ?? DateTime.MinValue).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>What there is to know about a player, or null when the site does not know him.</summary>
    public PartyMember? Member(string name, DateTime nowUtc)
    {
        var known = Players().FirstOrDefault(n => Same(n, name));
        if (known == null) return null;
        var seen = Seen(nowUtc);
        var book = _live.Parties(nowUtc);
        var party = book.GroupOf(known);
        // a player without a log of his own plays while he is in the party of somebody who plays
        var playing = seen.TryGetValue(known, out var s) ? s.Playing : party != null && party.Names.Count > 1;
        DateTime? last = seen.TryGetValue(known, out var t2) ? t2.Last : book.LastSeen.TryGetValue(known, out var t3) ? t3 : null;
        var location = _live.Read(t =>
        {
            if (!t.State.Data.Party.Any(n => Same(n, known))) return null;   // only the Hauling Planner's party has a place there
            var id = t.Whereabouts.LocationOf(known).Id;
            return PyroDistances.Points.Where(p => p.Id == id).Select(p => p.Name).FirstOrDefault();
        });
        return new PartyMember(known, playing, last, party, _fleet.Active(known), _fleet.Fleet(known), location, _versions.Of(known), WithoutTracker(known, book));
    }
}
