using SCTools.Server.Data;

namespace SCTools.Server;

/// <summary>A player as the Party tool shows him: playing or not and when he was last seen, his in-game party, the ship he flies and his fleet, where the
/// Hauling Planner has him (null when he is not in its party), and his tracker.</summary>
public sealed record PartyMember(
    string Name, bool Playing, DateTime? LastSeenUtc, PartyGroup? Party, ActiveShip Ship, IReadOnlyList<FleetShip> Fleet, string? Location, TrackerSeen? Tracker);

/// <summary>
/// The party of the site, in one place for every tool: who the players are (the tracker keys, the Hauling Planner's party and the fleets), who is playing,
/// the in-game parties now (AutoParty, from the party lines of everybody's log: anybody playing, not only the Hauling Planner's party), and what there is
/// to know about a player (the ship he flies and his fleet, FleetService; his tracker, TrackerVersions).
/// </summary>
public sealed class PartyService
{
    readonly DataStore _store;
    readonly LogUploads _uploads;
    readonly LiveTracker _live;
    readonly FleetService _fleet;
    readonly TrackerVersions _versions;

    public PartyService(DataStore store, LogUploads uploads, LiveTracker live, FleetService fleet, TrackerVersions versions)
    {
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
        foreach (var k in _store.TrackerKeys()) Add(k.Player);
        foreach (var p in _fleet.Players()) Add(p);
        lock (_uploads.Sync)
            foreach (var log in _uploads.Logs) Add(log.Player ?? log.Uploader);
        return names;
    }

    /// <summary>The in-game parties now, everybody playing (a player alone is a party of one), from everybody's log.</summary>
    public List<PartyGroup> Parties(DateTime nowUtc)
    {
        var players = Players();
        lock (_uploads.Sync) return AutoParty.Work(_uploads.Logs, players, nowUtc).Groups;
    }

    /// <summary>When each player's game was last seen (the latest line any of his logs has), and whether it still runs (it grew in the last minutes).</summary>
    Dictionary<string, (bool Playing, DateTime Last)> Seen(DateTime nowUtc) =>
        _uploads.Games().Where(g => g.Value.Count > 0).ToDictionary(
            g => g.Key,
            g =>
            {
                var last = g.Value.Max(r => r.Run.Last);
                return (g.Value.Any(r => !r.Quit && nowUtc - r.Run.Last < AutoParty.Quiet), last);
            },
            StringComparer.OrdinalIgnoreCase);

    /// <summary>Everybody, the ones playing first: name, playing, last seen.</summary>
    public List<(string Name, bool Playing, DateTime? LastSeenUtc)> Roster(DateTime nowUtc)
    {
        var seen = Seen(nowUtc);
        var list = new List<(string Name, bool Playing, DateTime? LastSeenUtc)>();
        foreach (var n in Players())
            list.Add(seen.TryGetValue(n, out var s) ? (n, s.Playing, (DateTime?)s.Last) : (n, false, (DateTime?)null));
        return list.OrderByDescending(p => p.Playing).ThenByDescending(p => p.LastSeenUtc ?? DateTime.MinValue).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>What there is to know about a player, or null when the site does not know him.</summary>
    public PartyMember? Member(string name, DateTime nowUtc)
    {
        var known = Players().FirstOrDefault(n => Same(n, name));
        if (known == null) return null;
        var seen = Seen(nowUtc);
        var playing = seen.TryGetValue(known, out var s) && s.Playing;
        var party = Parties(nowUtc).FirstOrDefault(g => g.Names.Any(n => Same(n, known)));
        var location = _live.Read(t =>
        {
            if (!t.State.Data.Party.Any(n => Same(n, known))) return null;   // only the Hauling Planner's party has a place there
            var id = t.Whereabouts.LocationOf(known).Id;
            return PyroDistances.Points.Where(p => p.Id == id).Select(p => p.Name).FirstOrDefault();
        });
        return new PartyMember(known, playing, seen.TryGetValue(known, out var t2) ? t2.Last : null, party, _fleet.Active(known), _fleet.Fleet(known), location,
            _versions.Of(known));
    }
}
