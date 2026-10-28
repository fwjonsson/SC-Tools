namespace SCTools.GameData;

/// <summary>
/// The admin's word on a destination or a start name of the log that the map does not know: it is this place (Key), or it is not a place to show (Key null).
/// A text that ends with * is every text that starts so ("AsteroidCluster_Pyro_*").
/// </summary>
public sealed record PlaceAlias(string Text, string? Key);

/// <summary>
/// The places of the map as the site has them: the game data's (StarMap) with the admin's changes (MapPlaces in the server): his own names for places, the
/// places he added, and what a destination or a start name of the log is when the game data does not say (PlaceAlias). Also which place a quantum destination
/// of the log is and what a name the game says is. A place he renamed is still found by the game's name.
/// </summary>
public sealed class Atlas
{
    /// <summary>The game data alone.</summary>
    public static Atlas Default { get; } = new(StarMap.Places, Array.Empty<PlaceAlias>());

    readonly Dictionary<string, Place> _byKey;
    readonly List<(string Text, bool Prefix, string? Key)> _aliases;

    public Atlas(IEnumerable<Place> places, IEnumerable<PlaceAlias> aliases)
    {
        Places = places.ToList();
        _byKey = new Dictionary<string, Place>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Places) _byKey[p.Key] = p;
        _aliases = aliases
            .Select(a => (Text: a.Text.Trim(), a.Key))
            .Where(a => a.Text.Trim('*').Length > 0)
            .Select(a => (Text: Normal(a.Text.TrimEnd('*')), Prefix: a.Text.EndsWith('*'), a.Key))
            .OrderBy(a => a.Prefix).ThenByDescending(a => a.Text.Length)   // the exact ones first, then the longest beginning
            .ToList();
    }

    static string Normal(string s) => s.Trim().ToLowerInvariant();

    /// <summary>Every place, the game data's and the admin's.</summary>
    public IReadOnlyList<Place> Places { get; }

    /// <summary>A place by its key ("pyro/ruin-station"), or null.</summary>
    public Place? Find(string? key) => key != null && _byKey.TryGetValue(key, out var p) ? p : null;

    /// <summary>The places of a system.</summary>
    public IEnumerable<Place> Of(string system) => Places.Where(p => p.System == system);

    /// <summary>
    /// The admin's word on a text of the log: (true, the place) or (true, null) when it is not a place to show; (false, null) when he said nothing (or the
    /// place he named is gone). A party member's marker is never a place.
    /// </summary>
    public (bool Said, Place? Place) AliasOf(string text)
    {
        var t = Normal(text);
        foreach (var a in _aliases)
        {
            if (!(a.Prefix ? t.StartsWith(a.Text, StringComparison.Ordinal) : t == a.Text)) continue;
            if (a.Key == null) return (true, null);
            return Find(a.Key) is { } p ? (true, p) : (false, null);
        }
        if (t.StartsWith("partymembermarker", StringComparison.Ordinal)) return (true, null);   // flying to a party member
        return (false, null);
    }

    /// <summary>The names of a place to compare with what the game says: its own, and the game data's when the admin renamed it.</summary>
    IEnumerable<string> Names(Place p)
    {
        yield return StarMap.Letters(p.Name);
        yield return StarMap.Letters(p.Short);
        if (StarMap.Find(p.Key) is { } game && (game.Name != p.Name || game.Short != p.Short))
        {
            yield return StarMap.Letters(game.Name);
            yield return StarMap.Letters(game.Short);
        }
    }

    /// <summary>The place of one of the Hauling Planner's Pyro stations (its id in PyroDistances), or null.</summary>
    public Place? OfPyroStation(string? id) => id != null && StarMap.PyroStations.TryGetValue(id, out var p) ? Find("pyro/" + p) : null;

    /// <summary>
    /// The place a quantum destination of the log is ("rs_ext_pyro6_leo", "pyro3", "LOC_RR_S1_L2", "rs_ext_nyx-pyro_jp1", "levski_all-001"), or null (a party
    /// member, an asteroid, an encounter: not a place of the map). The admin's word first. A name that fits places in more than one system: the one in this
    /// system.
    /// </summary>
    public Place? OfQuantumTarget(string target, string? system = null)
    {
        if (string.IsNullOrWhiteSpace(target)) return null;
        var (said, place) = AliasOf(target);
        if (said) return place;
        if (OfPyroStation(PyroDistances.IdOfQuantumTarget(target)) is { } station) return station;
        var m = StarMap.Planet.Match(target);
        if (m.Success)
        {
            var s = m.Groups["s"].Value.ToLowerInvariant();
            var order = StarMap.PlanetOrder[s];
            var n = m.Groups["n"].Value[0] - '1';
            return n >= 0 && n < order.Length ? Find(s + "/" + order[n]) : null;
        }
        m = StarMap.RestStop.Match(target);
        if (m.Success)
        {
            var n = m.Groups["n"].Value[0] - '1';
            var key = n >= 0 && n < StarMap.StantonPrefix.Length
                ? StarMap.Of("stanton").FirstOrDefault(p => p.Short.Equals(StarMap.StantonPrefix[n] + "-L" + m.Groups["l"].Value, StringComparison.OrdinalIgnoreCase))?.Key
                : null;
            if (key != null) return Find(key);
        }
        m = StarMap.Gate.Match(target);
        if (m.Success)
        {
            var b = m.Groups["b"].Value.ToLowerInvariant() switch { "stan" => "stanton", var x => x };
            return Find(m.Groups["a"].Value.ToLowerInvariant() + "/" + b + "-gateway");
        }
        // a place whose name is in it ("levski_all-001" is Levski), the longest name; in this system first
        var t = StarMap.Letters(target);
        return Places
            .Where(p => p.Kind != PlaceKind.Star)
            .Select(p => (Place: p, Name: Names(p).Where(n => n.Length >= 5 && t.Contains(n, StringComparison.Ordinal)).OrderByDescending(n => n.Length).FirstOrDefault()))
            .Where(x => x.Name != null)
            .OrderByDescending(x => x.Place.System == system).ThenByDescending(x => x.Name!.Length)
            .Select(x => x.Place).FirstOrDefault();
    }

    /// <summary>
    /// What a name the game says in plain words is ("Projected Start Location is Aberdeen", "... is Pyro System"): the system and the place (null for the system
    /// alone, or neither). The admin's word first. A name of more than one system ("Nyx Gateway" is in Pyro and Stanton): the one in this system; else, after a
    /// gateway, the system it leads to; else the only one.
    /// </summary>
    public (string? System, Place? Place) OfName(string name, string? system = null, Place? at = null)
    {
        var n = StarMap.Letters(name);
        if (n.Length == 0) return (null, null);
        var (said, place) = AliasOf(name);
        if (said) return place == null ? (null, null) : (place.System, place);
        foreach (var s in StarMap.Systems)
            if (n == StarMap.Letters(s.Name + " system") || n == StarMap.Letters(s.Name)) return (s.Id, null);
        var all = Places.Where(p => p.Kind != PlaceKind.Star && Names(p).Contains(n)).ToList();
        if (all.Count == 0)   // a longer or a shorter name ("Megumi Refueling Station", "Gaslight"): the place it starts with, the longest
            all = Places.Where(p => p.Kind != PlaceKind.Star && n.Length >= 5)
                .Select(p => (Place: p, Len: Names(p).Where(x => x.Length >= 5 && (n.StartsWith(x, StringComparison.Ordinal) || x.StartsWith(n, StringComparison.Ordinal)))
                    .Select(x => x.Length).DefaultIfEmpty(0).Max()))
                .Where(x => x.Len > 0)
                .GroupBy(x => x.Place.System).Select(g => g.OrderByDescending(x => x.Len).First().Place).ToList();
        if (all.Count == 0) return (null, null);
        var here = all.FirstOrDefault(p => p.System == system);
        if (here != null) return (here.System, here);
        if (at?.Kind == PlaceKind.Gateway)
        {
            var to = StarMap.Systems.FirstOrDefault(s => (StarMap.Find(at.Key)?.Name ?? at.Name).StartsWith(s.Name + " ", StringComparison.OrdinalIgnoreCase));
            if (to != null && all.FirstOrDefault(p => p.System == to.Id) is { } there) return (there.System, there);
        }
        return all.Count == 1 ? (all[0].System, all[0]) : (null, null);
    }
}
