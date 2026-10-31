namespace SCTools.Server;

/// <summary>
/// The admin's notice of places the map does not know (#414, Frank 31 Oct 2026), so he can name them on Map places while he is there. From the sightings of
/// the logs (MapPlaces.Sightings):
/// - a notice (Alert, the toast on every page he has open): somebody else meets an unknown place, the first time anybody does (their later visits stay
///   quiet: he would not know where it is); he himself (any of his combined accounts) comes to one that is still unknown, once per visit (a visit: his
///   sightings of it less than Visit apart);
/// - a card on his Companion (Here) for every unknown place one of his accounts was at in the last half hour, until he names it or presses Later; Later
///   lasts until his next sighting of it.
/// Named on Map places (or "not a place"), it is not unknown any more and nothing is said of it. Sightings from before the server started are not news: the
/// logs are read again at the start, and a new version of the site must not announce every unknown place once more. Kept in memory: a restart forgets the
/// Laters too (the cards of the last half hour come back once).
/// </summary>
public sealed class UnknownPlaceAlerts
{
    /// <summary>His sightings of one place this close together are one visit.</summary>
    public static readonly TimeSpan Visit = TimeSpan.FromMinutes(10);

    /// <summary>A card stays on his Companion this long after his last sighting of the place.</summary>
    public static readonly TimeSpan Recent = TimeSpan.FromMinutes(30);

    /// <summary>The notices kept for the pages to fetch (the newest).</summary>
    const int Kept = 50;

    readonly MapPlaces _places;
    readonly LiveTracker _live;
    readonly DateTime _start;
    readonly object _lock = new();

    // the sightings as worked out last, and what they were worked out from (the logs' version, the places as the admin has them)
    List<UnknownSighting> _sightings = new();
    int _version = -1;
    Atlas? _atlas;

    readonly List<UnknownAlert> _alerts = new();
    int _seq;
    readonly HashSet<string> _found = new();   // places somebody met: the first one was said
    readonly Dictionary<string, DateTime> _his = new();   // his last sighting of a place that was looked at (a visit begins after a gap of Visit)
    readonly Dictionary<string, DateTime> _later = new();   // his Later on a place's card: until his next sighting

    public UnknownPlaceAlerts(MapPlaces places, LiveTracker live)
    {
        _places = places;
        _live = live;
        _start = DateTime.UtcNow;
    }

    /// <summary>This player's accounts: his combined accounts (the Account page), else himself.</summary>
    public HashSet<string> AccountsOf(string player)
    {
        var group = _live.Read(t => t.State.Data.CombinedAccounts.FirstOrDefault(g => g.Any(n => PlayerNames.SameName(n, player)))?.ToList());
        var all = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { player };
        foreach (var n in group ?? new List<string>()) all.Add(n);
        return all;
    }

    /// <summary>
    /// What the admin's pages show now: the number of the newest notice (a page asks with it the next time, after), the notices after the one given (none
    /// the first time a page asks: what was said before it opened is not news to it), and the cards of the unknown places his accounts were at lately.
    /// </summary>
    public UnknownNews News(string admin, int? after, DateTime nowUtc)
    {
        var mine = AccountsOf(admin);
        lock (_lock)
        {
            Update(mine);
            var news = after is { } a ? _alerts.Where(x => x.Seq > a).ToList() : new List<UnknownAlert>();
            var here = _sightings.Where(s => s.Here && mine.Contains(s.Player) && s.TimeUtc >= nowUtc - Recent)
                .GroupBy(s => s.Key)
                .Select(g => g.OrderBy(s => s.TimeUtc).Last())
                .Where(s => !(_later.TryGetValue(s.Key, out var l) && l >= s.TimeUtc))
                .OrderByDescending(s => s.TimeUtc)
                .ToList();
            return new UnknownNews(_seq, news, here);
        }
    }

    /// <summary>Later on a card: it goes until his next sighting of the place (it stays on Map places).</summary>
    public void Later(string admin, string text, UnknownKind kind)
    {
        var mine = AccountsOf(admin);
        lock (_lock)
        {
            Update(mine);
            var key = text.ToLowerInvariant() + "|" + kind;
            var last = _sightings.Where(s => s.Key == key && s.Here && mine.Contains(s.Player)).Select(s => (DateTime?)s.TimeUtc).Max();
            if (last != null) _later[key] = last.Value;
        }
    }

    /// <summary>Works the sightings out again when the logs or the places changed, and makes the notices of the new ones (under the lock).</summary>
    void Update(HashSet<string> mine)
    {
        var version = _live.Uploads.Version;
        var atlas = _places.Atlas;
        if (version != _version || !ReferenceEquals(atlas, _atlas))
        {
            _sightings = _places.Sightings();
            (_version, _atlas) = (version, atlas);
        }
        foreach (var s in _sightings)
        {
            var old = s.TimeUtc < _start;
            if (mine.Contains(s.Player))
            {
                _found.Add(s.Key);   // he met it himself: nobody else's sighting of it is news
                if (!s.Here) continue;
                var seen = _his.TryGetValue(s.Key, out var last);
                if (seen && s.TimeUtc <= last) continue;   // looked at already
                _his[s.Key] = s.TimeUtc;
                if (!old && (!seen || s.TimeUtc - last > Visit)) Say(s, true);   // a visit begins
            }
            else if (_found.Add(s.Key) && !old)
            {
                Say(s, false);   // the first time anybody met it
            }
        }
    }

    void Say(UnknownSighting s, bool his)
    {
        _alerts.Add(new UnknownAlert(++_seq, s.Text, s.Kind, s.KindText, s.Player, his, s.TimeUtc, s.System));
        if (_alerts.Count > Kept) _alerts.RemoveAt(0);
    }
}
