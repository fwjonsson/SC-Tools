using System.Globalization;
using System.Text.RegularExpressions;

namespace SCTools.Tracking;

/// <summary>Where the SCU of a hauling job comes from, the first that has it (see HaulJobs.Of).</summary>
public enum ScuFrom
{
    /// <summary>Not known: the game did not say, and nobody typed it in.</summary>
    NotKnown,

    /// <summary>The game's popups for this mission: "New Objective: Deliver 0/4 SCU of Distilled Spirits to Port Tressler".</summary>
    Game,

    /// <summary>A hauler typed it in for this mission (only this one).</summary>
    Hauler,

    /// <summary>The Mission Library: what was typed in there for this kind of mission, else the last SCU a hauler or the game gave one of them.</summary>
    Library,

    /// <summary>The Star Citizen Wiki API's hauling orders for the contract (by its id): the middle of their SCU ranges.</summary>
    Wiki,
}

/// <summary>A range of SCU (a hauling order of the Star Citizen Wiki API: "100 - 150 SCU").</summary>
public sealed record ScuRange(int Min, int Max)
{
    public double Middle => (Min + Max) / 2.0;
}

/// <summary>
/// What the Star Citizen Wiki API says about a contract (/api/missions/{uuid}, the uuid is the log's contractDefinitionId): its debug name (the log's
/// contract name), its title, and its hauling orders' SCU (one per stop: a "Master" Red Wind contract has 4 of 100 - 150 SCU).
/// </summary>
public sealed record WikiContract(string Uuid, string? DebugName, string? Title, IReadOnlyList<ScuRange> Orders)
{
    /// <summary>The SCU of the whole contract: the orders' ranges added up.</summary>
    public ScuRange Total => new(Orders.Sum(o => o.Min), Orders.Sum(o => o.Max));
}

/// <summary>
/// A stop of a hauling job: the stop's key (its objectives without their number: "pickup_1f934d6b-…"), the zone the game gives it, where it is in words
/// (what its text says, or "near ArcCorp" when only the zone tells), the place of the map (null: not known; Near: only roughly, the planet it is at), its
/// share of the job's SCU (Exact: the game said it for this stop, else it is a share of the whole), and when it was done (null: not yet).
/// </summary>
public sealed record HaulStop(string Key, string? Zone, string? Name, Place? Place, bool Near, double Scu, DateTime? DoneUtc, bool Exact = false)
{
    public bool Done => DoneUtc != null;

    /// <summary>
    /// What a board says for it: the game's words ("Port Tressler", "NB Int. Spaceport", "near ArcCorp"), but the map's name for a station the game names by its
    /// Lagrange point ("Shallow Fields Station at Crusader's L4 Lagrange point" is "CRU-L4 Shallow Fields Station").
    /// </summary>
    public string Shown => Name != null && Place != null && !Near && Name.Contains("Lagrange point", StringComparison.OrdinalIgnoreCase) ? Place.Name
        : Name ?? Place?.Name ?? "a place the game does not name";
}

/// <summary>
/// A hauling mission of any system that is not one of the Hauling Planner's own Pyro contracts (2–4 To Ruin, From Ruin): "RSI Disc. Month: Ling Small
/// Haul" (pick up at one place, deliver to another), a courier run, the cargo of a salvage order. Its title, system, pickups and drop-offs, and its SCU
/// with where that comes from.
/// </summary>
public sealed record HaulJob(Mission Mission, string Title, string? System, IReadOnlyList<HaulStop> Pickups, IReadOnlyList<HaulStop> Drops, double Scu, ScuFrom ScuFrom,
    ScuRange? WikiRange = null)
{
    /// <summary>Every drop-off is done.</summary>
    public bool Delivered => Drops.Count > 0 && Drops.All(d => d.Done);
}

/// <summary>A zone of the game that another objective named: the place, and whether the zone is only the planet's (Near) or the place itself.</summary>
public sealed record ZoneName(string Name, Place? Place, bool Near);

/// <summary>
/// Reads the hauling jobs of a log: every mission with drop-offs ("dropoff_…" objectives) that the Hauling Planner does not read as one of its own
/// contracts. The game names a stop in the popup of its objective ("Deliver 0/4 SCU of Distilled Spirits to Port Tressler", "Collect Food Supplies From
/// Shallow Fields Station at Crusader's L4 Lagrange point"); a pickup it does not name (most "A to B" hauls) is named by its zone, when another objective in
/// the same zone said where it is. The SCU: the game's popups for the mission add up to it; else a hauler's number for this mission; else the Mission
/// Library's. It is split evenly over the pickups, and over the drop-offs the game did not give the SCU of, like the planner's own contracts.
/// </summary>
public static class HaulJobs
{
    /// <summary>A marker this close to the origin of its zone is in the place itself (a station's own zone); farther, the zone is the planet's.</summary>
    const double OwnZone = 10_000;

    /// <summary>Is this mission a hauling job (and not one of the Hauling Planner's own contracts)?</summary>
    public static bool IsJob(Mission m) => !m.IsHauling && !m.IsOut && Objectives(m).Any(o => o.StartsWith("dropoff_", StringComparison.Ordinal));

    /// <summary>Every objective of a mission the log says anything about.</summary>
    static IEnumerable<string> Objectives(Mission m) => m.ObjZone.Keys.Concat(m.ObjText.Keys).Concat(m.Events.Keys).Concat(m.ObjScu.Keys).Distinct(StringComparer.Ordinal);

    /// <summary>The stop an objective is at: its id without the number of the commodity ("pickup_1f934d6b-…_2" is "pickup_1f934d6b-…").</summary>
    public static string StopKey(string objective) => Regex.Replace(objective, @"_\d+$", "");

    static readonly Regex DeliverText = new(@"^Deliver (?:\d+/\d+ SCU of )?.+? [Tt]o (?<p>.+)$", RegexOptions.Compiled);
    static readonly Regex CollectText = new(@"^(?:Collect|Pick ?up|Retrieve) .+? [Ff]rom (?<p>.+)$", RegexOptions.Compiled);

    /// <summary>The place an objective's text names ("Deliver 0/4 SCU of Distilled Spirits to Port Tressler": "Port Tressler"), or null.</summary>
    public static string? PlaceText(string text)
    {
        var m = DeliverText.Match(text.Trim());
        if (!m.Success) m = CollectText.Match(text.Trim());
        return m.Success ? m.Groups["p"].Value.Trim() : null;
    }

    // the cities' spaceports, as the missions name them
    static readonly Dictionary<string, string> Spaceports = new(StringComparer.Ordinal)
    {
        ["nbintspaceport"] = "stanton/new-babbage",
        ["newbabbageinterstellarspaceport"] = "stanton/new-babbage",
        ["teasaspaceport"] = "stanton/lorville",
        ["rikermemorialspaceport"] = "stanton/area18",
        ["augustdunlowspaceport"] = "stanton/orison",
    };

    // "Shallow Fields Station at Crusader's L4 Lagrange point" (CRU-L4); "microTech Logistics Depot S4LD13 on microTech", "wreck site near Hurston"
    static readonly Regex Lagrange = new(@"^(?<n>.+?) at (?<p>\w+)'s (?<l>L\d) Lagrange point$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex OnPlace = new(@"^(?<n>.+?) (?:on|near|above|around) (?<p>.+)$", RegexOptions.Compiled);

    static readonly Dictionary<string, string> PlanetCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Crusader"] = "CRU", ["Hurston"] = "HUR", ["ArcCorp"] = "ARC", ["microTech"] = "MIC",
    };

    /// <summary>
    /// The place of the map a name in a mission's text is: the admin's word first (Map places), the spaceports, a place's name or short name, then a place
    /// whose name is in it (or the other way round); for "X at Crusader's L4 Lagrange point" or "X on microTech" the second part when X is not known.
    /// </summary>
    public static Place? PlaceOf(string name, string? system, Atlas atlas)
    {
        name = name.Trim().Trim('"', '“', '”').Trim();
        string? second = null;
        var lp = Lagrange.Match(name);
        if (lp.Success)
        {
            name = lp.Groups["n"].Value;
            if (PlanetCodes.TryGetValue(lp.Groups["p"].Value, out var code)) second = code + "-" + lp.Groups["l"].Value.ToUpperInvariant();
        }
        else if (OnPlace.Match(name) is { Success: true } on)
        {
            name = on.Groups["n"].Value;
            second = on.Groups["p"].Value;
        }
        return Named(name, system, atlas) ?? (second != null ? Named(second, system, atlas) : null);
    }

    static Place? Named(string name, string? system, Atlas atlas)
    {
        name = name.Trim().Trim('"', '“', '”').Trim();
        var n = StarMap.Letters(name);
        if (n.Length == 0) return null;
        var (said, aliased) = atlas.AliasOf(name);
        if (said) return aliased;
        if (Spaceports.TryGetValue(n, out var port) && atlas.Find(port) is { } city) return city;
        var (_, place) = atlas.OfName(name, system);
        if (place != null) return place;
        if (n.Length < 5) return null;
        return atlas.Places.Where(p => p.Kind != PlaceKind.Star)
            .Select(p => (Place: p, Len: new[] { p.Name, p.Short }.Select(StarMap.Letters).Where(x => x.Length >= 5 && (n.Contains(x, StringComparison.Ordinal) || x.Contains(n, StringComparison.Ordinal)))
                .Select(x => x.Length).DefaultIfEmpty(0).Max()))
            .Where(x => x.Len > 0)
            .OrderByDescending(x => x.Place.System == system).ThenByDescending(x => x.Len)
            .Select(x => x.Place).FirstOrDefault();
    }

    /// <summary>The planet a place is at (itself for a planet; a moon's or a station's planet), or null.</summary>
    public static Place? PlanetOf(Place p, Atlas atlas)
    {
        Place? at = p;
        for (var i = 0; i < 4 && at != null; i++)
        {
            if (at.Kind == PlaceKind.Planet) return at;
            at = at.Parent == null ? null : atlas.Find(at.System + "/" + at.Parent);
        }
        return null;
    }

    /// <summary>
    /// The zones the objectives of these missions name (zone to where it is): a zone a marker is right in (it is the place's own) is that place; one whose
    /// markers are far from its middle is a planet's (a station near it, a city on it), so only the planet is known: "near ArcCorp".
    /// </summary>
    public static Dictionary<string, ZoneName> ZoneNames(IEnumerable<Mission> missions, Atlas atlas)
    {
        var own = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);       // zone: the places it is the own zone of
        var planets = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);   // zone: the planets of the places named in it
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var m in missions)
        {
            foreach (var (objective, zone) in m.ObjZone)
            {
                if (!m.ObjText.TryGetValue(objective, out var text) || PlaceText(text) is not { } said) continue;
                var place = PlaceOf(said, m.System, atlas);
                if (place == null) continue;
                var inside = m.ObjPos.TryGetValue(objective, out var pos) && Math.Sqrt(pos.X * pos.X + pos.Y * pos.Y + pos.Z * pos.Z) < OwnZone;
                if (inside)
                {
                    if (!own.TryGetValue(zone, out var set)) own[zone] = set = new HashSet<string>(StringComparer.Ordinal);
                    set.Add(place.Key);
                    names[zone] = said;
                }
                if (PlanetOf(place, atlas) is { } planet)
                {
                    if (!planets.TryGetValue(zone, out var set)) planets[zone] = set = new HashSet<string>(StringComparer.Ordinal);
                    set.Add(planet.Key);
                }
            }
        }
        var result = new Dictionary<string, ZoneName>(StringComparer.Ordinal);
        foreach (var zone in own.Keys.Concat(planets.Keys).Distinct(StringComparer.Ordinal))
        {
            if (own.TryGetValue(zone, out var places) && places.Count == 1 && atlas.Find(places.First()) is { } exact)
                result[zone] = new ZoneName(names.GetValueOrDefault(zone) ?? exact.Name, exact, false);
            else if (planets.TryGetValue(zone, out var ps) && ps.Count == 1 && atlas.Find(ps.First()) is { } planet)
                result[zone] = new ZoneName("near " + planet.Name, planet, true);
        }
        return result;
    }

    /// <summary>
    /// The hauling jobs of these missions (the merged log's, or a player's), with what the game says. title: a mission's title (null: "A hauling mission");
    /// haulerScu: the SCU a hauler typed in for this mission; libraryScu: the Mission Library's for its kind; wiki: what the Wiki API says of its contract.
    /// </summary>
    public static List<HaulJob> Of(IReadOnlyCollection<Mission> missions, Atlas atlas, Func<Mission, string?>? title = null,
        Func<Mission, long?>? haulerScu = null, Func<Mission, long?>? libraryScu = null, Func<Mission, WikiContract?>? wiki = null)
    {
        var zones = ZoneNames(missions, atlas);
        return missions.Where(IsJob).Select(m => Of(m, zones, atlas, title?.Invoke(m), haulerScu?.Invoke(m), libraryScu?.Invoke(m), wiki?.Invoke(m))).ToList();
    }

    /// <summary>How long after it was taken a job that never ended is still shown.</summary>
    public static readonly TimeSpan Age = TimeSpan.FromHours(12);

    /// <summary>
    /// The hauling jobs of the party going on: the merged log's jobs that a member of the party table (or of names) has, not ended, taken in the last 12
    /// hours of the logs, the first taken first, with the SCU of the site's book (State.Jobs: a hauler's, the library's) and its places. The zones are named
    /// by the missions of the last day.
    /// </summary>
    public static List<HaulJob> Open(TrackerState s, IReadOnlyCollection<string>? names = null)
    {
        var log = s.Log;
        if (log == null) return new List<HaulJob>();
        var book = s.Jobs;
        var atlas = book.Atlas;
        var party = names ?? s.Data.Party;
        var now = log.LastTime;
        static DateTime? Seen(Mission m) => m.Accepted ?? m.FirstSeen;
        var recent = log.Missions.Values.Where(m => Seen(m) is { } t && now - t < TimeSpan.FromDays(1)).ToList();
        var zones = ZoneNames(recent, atlas);
        return recent.Where(m => IsJob(m) && m.EndState == null && !m.Dropped && now - Seen(m)!.Value < Age && m.Holders.Any(h => party.Any(p => SameName(p, h))))
            .OrderBy(m => Seen(m)).ThenBy(m => m.Id, StringComparer.Ordinal)
            .Select(m => Of(m, zones, atlas, null, book.HaulerScu(m), book.KindOf(m)?.ScuToUse, book.WikiOf(m))).ToList();
    }

    static string Scu(double scu) => ((long)Math.Round(scu)).ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>A job's stops for a board: "Port Tressler 8 SCU ✓,  near ArcCorp ~4 SCU" (~: a share of the whole), or "—".</summary>
    public static string StopsText(IReadOnlyList<HaulStop> stops) =>
        stops.Count == 0 ? "—" : string.Join(",  ", stops.Select(s => s.Shown + (s.Scu >= 0.5 ? " " + (s.Exact ? "" : "~") + Scu(s.Scu) + " SCU" : "") + (s.Done ? " ✓" : "")));

    /// <summary>A job's SCU for a board: "8" (the game's), "6 (hauler)", "10 (library)", "~500 (wiki 400–600)", or "?" (nobody knows yet).</summary>
    public static string ScuText(HaulJob j) => j.ScuFrom switch
    {
        ScuFrom.Game => Scu(j.Scu),
        ScuFrom.Hauler => Scu(j.Scu) + " (hauler)",
        ScuFrom.Library => Scu(j.Scu) + " (library)",
        ScuFrom.Wiki => "~" + Scu(j.Scu) + (j.WikiRange is { } r && r.Min != r.Max ? " (wiki " + Scu(r.Min) + "–" + Scu(r.Max) + ")" : " (wiki)"),
        _ => "?",
    };

    /// <summary>One hauling job: its stops, named and placed, and its SCU split over them.</summary>
    public static HaulJob Of(Mission m, IReadOnlyDictionary<string, ZoneName> zones, Atlas atlas, string? title, long? haulerScu, long? libraryScu,
        WikiContract? wiki = null)
    {
        var objectives = Objectives(m).ToList();
        List<(HaulStop Stop, int GameScu)> Stops(string prefix) =>
            objectives.Where(o => o.StartsWith(prefix, StringComparison.Ordinal)).GroupBy(StopKey, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g =>
                {
                    var ids = g.OrderBy(o => o, StringComparer.Ordinal).ToList();
                    var zone = ids.Select(o => m.ObjZone.GetValueOrDefault(o)).FirstOrDefault(z => z != null);
                    var said = ids.Select(o => m.ObjText.GetValueOrDefault(o)).Where(t => t != null).Select(t => PlaceText(t!)).FirstOrDefault(p => p != null);
                    var place = said != null ? PlaceOf(said, m.System, atlas) : null;
                    var near = false;
                    if (place == null && zone != null && zones.TryGetValue(zone, out var zn))
                    {
                        said ??= zn.Name;
                        (place, near) = (zn.Place, zn.Near);
                    }
                    if (place == null && zone != null && atlas.OfPyroStation(StationIds.Of(zone)) is { } station) place = station;   // a Pyro station the planner knows
                    // done when every objective of it the game said anything about is completed
                    var events = ids.Where(o => m.Events.TryGetValue(o, out var e) && e.Count > 0).Select(o => m.Events[o]).ToList();
                    DateTime? done = events.Count > 0 && events.All(e => e[^1].State == "COMPLETED") ? events.Max(e => e[^1].Time) : null;
                    return (new HaulStop(g.Key, zone, said, place, near, 0, done), ids.Sum(o => m.ObjScu.GetValueOrDefault(o)));
                }).ToList();

        var pickups = Stops("pickup_");
        var drops = Stops("dropoff_");
        var game = drops.Sum(d => d.GameScu);
        var range = wiki is { Orders.Count: > 0 } ? wiki.Total : null;
        var (scu, from) = game > 0 ? ((long)game, ScuFrom.Game)
            : haulerScu > 0 ? (haulerScu.Value, ScuFrom.Hauler)
            : libraryScu > 0 ? (libraryScu.Value, ScuFrom.Library)
            : range is { Max: > 0 } ? ((long)Math.Round(range.Middle), ScuFrom.Wiki)
            : (0L, ScuFrom.NotKnown);
        // a drop-off the game gave the SCU of has it; the rest of the SCU goes evenly over the others, and over the pickups
        var unknownDrops = drops.Count(d => d.GameScu == 0);
        var rest = Math.Max(0, scu - drops.Sum(d => d.GameScu));
        var dropStops = drops.Select(d => d.Stop with { Scu = d.GameScu > 0 ? d.GameScu : unknownDrops > 0 ? rest / (double)unknownDrops : 0, Exact = d.GameScu > 0 }).ToList();
        var pickupStops = pickups.Select(p => p.Stop with { Scu = pickups.Count > 0 ? scu / (double)pickups.Count : 0, Exact = from == ScuFrom.Game && pickups.Count == 1 }).ToList();
        var system = m.System ?? dropStops.Concat(pickupStops).Select(s => s.Place?.System).FirstOrDefault(s => s != null);
        return new HaulJob(m, title ?? m.Title ?? "A hauling mission", system, pickupStops, dropStops, scu, from, range);
    }
}
