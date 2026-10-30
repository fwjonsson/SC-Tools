using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SCTools.Server.Data;

namespace SCTools.Server;

/// <summary>
/// A destination, a start name or an area of the logs that the map does not know: what it is, how often and when last, by whom, and in which
/// system (when known).
/// </summary>
public sealed record UnknownPlace(string Text, MapService.UnknownKind Kind, int Seen, DateTime LastUtc, IReadOnlyList<string> Players, string? System)
{
    /// <summary>"Quantum destination", "Start name", "Area" (the game's area tag, "RR_P6_LEO", that comes with the inventory lines: the space around a place; Frank, 30 Oct 2026).</summary>
    public string KindText => Kind switch
    {
        MapService.UnknownKind.Start => "Start name",
        MapService.UnknownKind.Location => "Area",
        _ => "Quantum destination",
    };
}

/// <summary>A place on the admin's page: as the site has it, the game data's (null for one he added), and whether he renamed it.</summary>
public sealed record PlaceEntry(Place Place, Place? Game, bool Added)
{
    public bool Renamed => Game != null && (Game.Name != Place.Name || Game.Short != Place.Short);
}

/// <summary>
/// The places of the map as the admin changes them (Admin → Map places): his own name for a place, places he adds, and what a destination or a start name
/// of the logs is when the map does not know it (an alias: a place, or not a place to show). Saved in the database and kept in memory as an Atlas, which the
/// Map tool uses (MapService), so a change shows at once. Also the destinations and start names in the logs that the map does not know yet.
/// </summary>
public sealed class MapPlaces
{
    /// <summary>The longest name of a place.</summary>
    public const int MaxName = 60;

    /// <summary>The longest text of an alias (a destination id of the log can be long).</summary>
    public const int MaxText = 160;

    /// <summary>What a place the admin adds can be.</summary>
    public static readonly PlaceKind[] AddKinds = { PlaceKind.Station, PlaceKind.City, PlaceKind.Moon, PlaceKind.Planet, PlaceKind.Gateway };

    readonly DataStore _store;
    readonly LogUploads _uploads;
    readonly object _lock = new();
    List<MapPlaceRow>? _places;
    List<MapAliasRow>? _aliases;
    volatile Atlas? _atlas;

    public MapPlaces(DataStore store, LogUploads uploads)
    {
        _store = store;
        _uploads = uploads;
    }

    void Load()
    {
        if (_places != null) return;
        using var db = _store.Open();
        _places = db.MapPlaces.AsNoTracking().ToList();
        _aliases = db.MapAliases.AsNoTracking().OrderBy(a => a.Id).ToList();
        Build();
    }

    static Place ToPlace(MapPlaceRow r) =>
        new(r.System, r.Key[(r.Key.IndexOf('/') + 1)..], r.Name, r.Short, Enum.TryParse<PlaceKind>(r.Kind, out var k) ? k : PlaceKind.Station, r.Parent, r.X, r.Y);

    void Build()
    {
        var renamed = _places!.Where(r => !r.Added).ToDictionary(r => r.Key, StringComparer.OrdinalIgnoreCase);
        var places = StarMap.Places.Select(p => renamed.TryGetValue(p.Key, out var r) ? p with { Name = r.Name, Short = r.Short } : p)
            .Concat(_places!.Where(r => r.Added).Select(ToPlace));
        _atlas = new Atlas(places, _aliases!.Select(a => new PlaceAlias(a.Text, a.PlaceKey)));
    }

    /// <summary>The places as the site has them now, without waiting for them to be read (null before the first time): for the tracker, which must not wait here.</summary>
    public Atlas? Current => _atlas;

    /// <summary>The places as the site has them now.</summary>
    public Atlas Atlas
    {
        get
        {
            lock (_lock)
            {
                Load();
                return _atlas!;
            }
        }
    }

    /// <summary>Every place, by system, the way the map has them.</summary>
    public List<PlaceEntry> Entries()
    {
        var atlas = Atlas;
        return atlas.Places.Select(p => new PlaceEntry(p, StarMap.Find(p.Key), StarMap.Find(p.Key) == null)).ToList();
    }

    /// <summary>The aliases, the newest first.</summary>
    public List<MapAliasRow> Aliases()
    {
        lock (_lock)
        {
            Load();
            return _aliases!.OrderByDescending(a => a.Id).ToList();
        }
    }

    static string? NameOf(string? typed) => typed?.Trim() is { Length: > 0 and <= MaxName } name ? name : null;

    /// <summary>Gives a place a name or a short name of his own (empty: the game data's again). The sentence for the page.</summary>
    public string Rename(string key, string field, string? value, DateTime nowUtc)
    {
        lock (_lock)
        {
            Load();
            var place = _atlas!.Find(key);
            if (place == null) return "That place is not on the map.";
            if (field != "name" && field != "short") return "That cannot be changed.";
            var game = StarMap.Find(place.Key);
            var typed = value?.Trim() ?? "";
            if (typed.Length > MaxName) return "A name is at most " + MaxName + " letters.";
            if (typed.Length == 0 && game == null && field == "name") return "A place needs a name.";
            var name = field == "name" ? (typed.Length > 0 ? typed : game!.Name) : place.Name;
            var shortName = field == "short" ? (typed.Length > 0 ? typed : game?.Short ?? name) : place.Short;
            using var db = _store.Open();
            var row = db.MapPlaces.Find(place.Key);
            if (game != null && name == game.Name && shortName == game.Short)
            {
                if (row != null) db.MapPlaces.Remove(row);   // the game data's names again
            }
            else if (row == null)
            {
                db.MapPlaces.Add(new MapPlaceRow
                {
                    Key = place.Key, System = place.System, Name = name, Short = shortName, Kind = place.Kind.ToString(), Parent = place.Parent, X = place.X, Y = place.Y,
                    Added = game == null, ChangedUtc = nowUtc,
                });
            }
            else
            {
                row.Name = name;
                row.Short = shortName;
                row.ChangedUtc = nowUtc;
            }
            db.SaveChanges();
            _places = db.MapPlaces.AsNoTracking().ToList();
            Build();
            return game != null && name == game.Name && shortName == game.Short
                ? place.Key + " has the game's names again: " + name + " (" + shortName + ")."
                : place.Key + " is called " + name + " (" + shortName + ") now.";
        }
    }

    /// <summary>
    /// Adds a place: its system, name, short name (empty: the name), kind, and where it is: at a place of the same system (its position; at a planet or a moon
    /// it is in its orbit) or at a position typed in Gm. The sentence for the page, and its key when it is added.
    /// </summary>
    public (string Said, string? Key) Add(string? system, string? name, string? shortName, string? kind, string? at, string? x, string? y, DateTime nowUtc)
    {
        var sys = StarMap.Systems.FirstOrDefault(s => s.Id == system);
        if (sys == null) return ("Pick the system.", null);
        if (NameOf(name) is not { } n) return ("Type the name (at most " + MaxName + " letters).", null);
        var sh = string.IsNullOrWhiteSpace(shortName) ? n : NameOf(shortName);
        if (sh == null) return ("The short name is at most " + MaxName + " letters.", null);
        if (!Enum.TryParse<PlaceKind>(kind, true, out var k) || !AddKinds.Contains(k)) return ("Pick what it is.", null);
        lock (_lock)
        {
            Load();
            double px, py;
            string? parent = null;
            if (!string.IsNullOrWhiteSpace(at))
            {
                var near = _atlas!.Find(at);
                if (near == null || near.System != sys.Id) return ("Pick a place in " + sys.Name + " for where it is.", null);
                (px, py) = (near.X, near.Y);
                if (near.Kind is PlaceKind.Planet or PlaceKind.Moon) parent = near.Id;
                else parent = near.Parent;
            }
            else if (double.TryParse(x?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out px)
                     && double.TryParse(y?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out py)
                     && Math.Abs(px) < 1000 && Math.Abs(py) < 1000)
            {
            }
            else return ("Pick a place it is at, or type where it is: X and Y in Gm (like -52.3).", null);
            var slug = StarMap.Slug(n);
            if (slug.Length == 0) slug = "place";
            var key = sys.Id + "/" + slug;
            for (var i = 2; _atlas!.Find(key) != null; i++) key = sys.Id + "/" + slug + "-" + i;
            using var db = _store.Open();
            db.MapPlaces.Add(new MapPlaceRow
            {
                Key = key, System = sys.Id, Name = n, Short = sh, Kind = k.ToString(), Parent = parent, X = px, Y = py, Added = true, ChangedUtc = nowUtc,
            });
            db.SaveChanges();
            _places = db.MapPlaces.AsNoTracking().ToList();
            Build();
            return (n + " is on the map of " + sys.Name + " now.", key);
        }
    }

    /// <summary>Takes a place he added off the map (the aliases to it are not known again until he says what they are). The sentence for the page.</summary>
    public string Remove(string key)
    {
        lock (_lock)
        {
            Load();
            using var db = _store.Open();
            var row = db.MapPlaces.Find(key);
            if (row == null || !row.Added) return "Only a place you added can be taken off the map.";
            db.MapPlaces.Remove(row);
            db.SaveChanges();
            _places = db.MapPlaces.AsNoTracking().ToList();
            Build();
            return row.Name + " is off the map.";
        }
    }

    /// <summary>What a text of the logs is not: a place to show (the key for it on the page).</summary>
    public const string NotAPlace = "-";

    /// <summary>Says what a text of the logs is: a place (its key), or not a place to show (NotAPlace). The sentence for the page.</summary>
    public string Alias(string? text, string? key, DateTime nowUtc)
    {
        var t = text?.Trim() ?? "";
        if (t.Trim('*').Length == 0 || t.Length > MaxText) return "Type the text from the log (at most " + MaxText + " letters; * at the end: every one that starts so).";
        if (string.IsNullOrEmpty(key)) return "Pick what " + t + " is: a place, or not a place to show.";
        lock (_lock)
        {
            Load();
            Place? place = null;
            if (key != NotAPlace)
            {
                place = _atlas!.Find(key);
                if (place == null) return "That place is not on the map.";
            }
            using var db = _store.Open();
            var row = db.MapAliases.ToList().FirstOrDefault(a => string.Equals(a.Text, t, StringComparison.OrdinalIgnoreCase));
            if (row == null) db.MapAliases.Add(new MapAliasRow { Text = t, PlaceKey = place?.Key, AddedUtc = nowUtc });
            else
            {
                row.Text = t;
                row.PlaceKey = place?.Key;
                row.AddedUtc = nowUtc;
            }
            db.SaveChanges();
            _aliases = db.MapAliases.AsNoTracking().OrderBy(a => a.Id).ToList();
            Build();
            return place == null ? t + " is not a place to show: the map leaves it out." : t + " is " + place.Name + " (" + StarMap.Systems.First(s => s.Id == place.System).Name + ") now.";
        }
    }

    /// <summary>Forgets what he said about a text. The sentence for the page.</summary>
    public string Unalias(int id)
    {
        lock (_lock)
        {
            Load();
            using var db = _store.Open();
            var row = db.MapAliases.Find(id);
            if (row == null) return "That is gone already.";
            db.MapAliases.Remove(row);
            db.SaveChanges();
            _aliases = db.MapAliases.AsNoTracking().OrderBy(a => a.Id).ToList();
            Build();
            return row.Text + " is not known again.";
        }
    }

    /// <summary>The destinations, start names and areas in the logs (on the server now) that the map does not know, the latest first.</summary>
    public List<UnknownPlace> Unknown()
    {
        var atlas = Atlas;
        var found = new Dictionary<(string, MapService.UnknownKind), Tally>();
        List<(string Player, List<QtEvent> Qt, List<LocationSeen> Seen)> logs;
        lock (_uploads.Sync) logs = _uploads.Logs.Select(l => (l.Player ?? l.Uploader ?? "", l.Qt.ToList(), l.Locations.ToList())).ToList();
        foreach (var (player, qt, seen) in logs)
        {
            MapService.Follow(qt, atlas, (time, text, kind, system) =>
            {
                var k = (text.ToLowerInvariant(), kind);
                if (!found.TryGetValue(k, out var tally)) found[k] = tally = new Tally(text);
                tally.Seen++;
                if (player.Length > 0) tally.Players.Add(player);
                if (time >= tally.Last) (tally.Last, tally.System) = (time, system ?? tally.System);
            }, seen);
        }
        return found.Select(kv => new UnknownPlace(kv.Value.Text, kv.Key.Item2, kv.Value.Seen, kv.Value.Last, kv.Value.Players.OrderBy(p => p).ToList(), kv.Value.System))
            .OrderByDescending(u => u.LastUtc).ToList();
    }

    sealed class Tally
    {
        public Tally(string text) => Text = text;
        public string Text { get; }
        public int Seen { get; set; }
        public DateTime Last { get; set; }
        public HashSet<string> Players { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string? System { get; set; }
    }
}
