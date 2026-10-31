using System.Text.Json.Serialization;
using SCTools.Missions;
using SCTools.Sessions;
using SCTools.Server.Editing;

namespace SCTools.Server;

/// <summary>
/// The Map tool's live data (map.json): the star systems from above (StarMap: Pyro, Stanton, Nyx), everybody playing where the location tracker has him
/// (LiveTracker.Locations: his quantum jumps, the areas he went into, the inventories he opened, a pilot's destination for his crew, the mission objectives,
/// the Hauling Planner's pickups and deliveries), and the objectives of the missions going on. The page works out where he is and how fast he goes from his
/// drive (crafted as it is), so a player in quantum moves along his route with his speed (LocationTracker.Now). Under the map, the missions going on with
/// what the Mission Library says about them. The Map tool, the Hauling Planner's Route Planner and the Companion all draw this.
/// </summary>
public sealed class MapService
{
    readonly LogUploads _uploads;
    readonly LiveTracker _live;
    readonly FleetService _fleet;
    readonly MapPlaces _places;
    readonly MissionLibrary? _library;

    public MapService(LogUploads uploads, LiveTracker live, FleetService fleet, MapPlaces places, MissionLibrary? library = null)
    {
        _uploads = uploads;
        _live = live;
        _fleet = fleet;
        _places = places;
        _library = library;
    }

    /// <summary>
    /// An objective on the map: the player whose log placed it, the mission's title, the system, and either the place it is at (Place: a key of the map) or
    /// where it is in the system's own frame (X, Y in Gm), or neither (only in the list); Where: the place's name or what the objective says. Who: everybody
    /// who has the mission (a shared mission is placed once), so the Trip Planner can show one party's objectives.
    /// </summary>
    public sealed record MapObjective(string Player, string Title,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? System,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Place,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? X,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Y,
        string Where)
    {
        public List<string> Who { get; } = new();
    }

    /// <summary>
    /// Is an objective done in this log, so it is off the map: its last state is over, or it is a pickup of one of the Hauling Planner's contracts that the
    /// planner counts as picked up (SessionBuilder.PickedUp: a COMPLETED or an INPROGRESS event). So the diamond goes when the planner credits the pickup, not later.
    /// </summary>
    static bool Done(Mission m, string objective) =>
        (m.Events.TryGetValue(objective, out var events) && events.Count > 0 && LocationTracker.IsOver(events[^1].State)) || (m.IsHauling && SessionBuilder.PickedUp(m, objective));

    /// <summary>Everything the page draws, now: the systems, the players (where the location tracker has them), the objectives, the missions.</summary>
    public object Snapshot(DateTime nowUtc)
    {
        var titles = _uploads.MissionsSeen();
        var atlas = _places.Atlas;
        var players = new List<object>();
        var objectives = new List<MapObjective>();
        var placed = new Dictionary<(string, string), MapObjective>();
        var unplaced = 0;
        var going = new Dictionary<string, GoingMission>(StringComparer.OrdinalIgnoreCase);   // the missions going on, by id (shared: once), for the table under the map
        // everybody playing, where the location tracker has him, with a copy of his missions (the tracker adds to them meanwhile)
        var book = _live.Locations(nowUtc);
        List<(string Name, HaulingLog Log, PlayerLocation Where, Dictionary<string, Mission> Missions)> playing;
        // the beacons placed by a route's fuel estimate (anybody's: a shared mission's beacon is the same), and each objective's beacon, by mission and
        // objective; then the zone of every placed objective is that spot too (the game's id of the cluster it is in, the same for every objective there
        // on that server: a later objective in it, or another mission there, needs no route of its own)
        var byBeacon = new Dictionary<string, Place>(StringComparer.OrdinalIgnoreCase);
        var beaconOf = new Dictionary<(string, string), string>();
        var byZone = new Dictionary<string, Place>();
        lock (_uploads.Sync)
        {
            playing = book.Players.Select(p => (p.Name, p.Log, p.Where, new Dictionary<string, Mission>(p.Log.Missions))).ToList();
            foreach (var p in playing)
            {
                foreach (var (beacon, spot) in p.Where.Ranged ?? new Dictionary<string, Place>()) byBeacon.TryAdd(beacon, spot);
                foreach (var b in p.Log.Beacons) beaconOf[(b.MissionId.ToLowerInvariant(), b.Objective)] = b.NavPoint;
            }
            foreach (var p in playing)
                foreach (var b in p.Log.Beacons)
                    if (byBeacon.TryGetValue(b.NavPoint, out var spot) && p.Missions.TryGetValue(b.MissionId, out var bm) && bm.ObjZone.TryGetValue(b.Objective, out var z))
                        byZone.TryAdd(z, spot);
        }
        // the hidden spots somebody is at or flies to (a mission's beacon placed by its fuel estimate), or died at: the map needs where they are, nothing more
        var spots = playing.SelectMany(p => new[] { p.Where.At, p.Where.From, p.Where.To, p.Where.LastPing })
            .Concat(book.Deaths.SelectMany(d => new[] { d.Where.At, d.Where.From, d.Where.To, d.Where.LastPing }))
            .Where(x => x is { Hidden: true }).Select(x => x!.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // the deaths of the last while, each where it happened: a skull on the map (#410; Frank, 31 Oct 2026)
        var deaths = book.Deaths.Select(Death).Where(d => d != null).ToList();

        // an objective one holder's log says is done is done for everybody with the mission (a shared contract's pickup is credited once; the Hauling Planner
        // reads the logs merged the same way), whichever log the map happens to place it from
        var done = new HashSet<(string, string)>();
        foreach (var p in playing)
            foreach (var m in p.Missions.Values)
                foreach (var objective in m.ObjZone.Keys)
                    if (Done(m, objective)) done.Add((m.Id.ToLowerInvariant(), objective));

        foreach (var p in playing)
        {
            var (log, name, where) = (p.Log, p.Name, p.Where);
            var seen = (titles.GetValueOrDefault(log.Uploader ?? name) ?? Array.Empty<MissionSeen>()).ToDictionary(s => s.MissionId, StringComparer.OrdinalIgnoreCase);
            string Title(Mission m) => LocationTracker.TitleOf(m, seen);
            players.Add(Player(name, where, nowUtc));
            foreach (var m in p.Missions.Values)
            {
                if (m.EndState != null || m.Dropped || (m.FirstSeen ?? m.Accepted ?? DateTime.MinValue) < nowUtc - LocationTracker.MissionAge) continue;
                if (seen.TryGetValue(m.Id, out var s2) && s2.Outcome != null) continue;
                var open = m.ObjZone.Keys.Count(o => !done.Contains((m.Id.ToLowerInvariant(), o)));
                if (!going.TryGetValue(m.Id, out var row)) going[m.Id] = row = new GoingMission(Title(m), new List<string>(), open);
                if (!row.Who.Contains(name)) row.Who.Add(name);
            }

            foreach (var m in p.Missions.Values)
            {
                if (m.EndState != null || m.Dropped || (m.FirstSeen ?? m.Accepted ?? DateTime.MinValue) < nowUtc - LocationTracker.MissionAge) continue;
                seen.TryGetValue(m.Id, out var s);
                if (s?.Outcome != null) continue;   // it ended (the notifications say so)
                var title = Title(m);
                foreach (var (objective, zone) in m.ObjZone)
                {
                    var id = (m.Id.ToLowerInvariant(), objective);
                    if (done.Contains(id)) continue;
                    if (placed.TryGetValue(id, out var shared))   // shared: once, with everybody who has it
                    {
                        if (!shared.Who.Contains(name)) shared.Who.Add(name);
                        continue;
                    }
                    var text = m.ObjText.GetValueOrDefault(objective);
                    MapObjective o;
                    if (atlas.OfPyroStation(StationIds.Of(zone)) is { } station)   // at one of the Pyro stations the Hauling Planner knows
                        o = new MapObjective(name, title, station.System, station.Key, null, null, station.Name);
                    else if (where.System != null && m.ObjPos.TryGetValue(objective, out var pos) && (Math.Abs(pos.X) > 1e9 || Math.Abs(pos.Y) > 1e9))
                        o = new MapObjective(name, title, where.System, null, pos.X / 1e9, pos.Y / 1e9, text ?? "in space");   // in the system's own frame (metres): where it is in the system he is in
                    else if (text != null && LocationTracker.NamedIn(text, m.System, atlas) is { } named)   // "Go to a debris field above Daymar": at Daymar
                        o = new MapObjective(name, title, named.System, named.Key, null, null, text);
                    else if ((beaconOf.TryGetValue(id, out var nav) && byBeacon.TryGetValue(nav, out var ranged) ? ranged : byZone.GetValueOrDefault(zone)) is { } spot)
                    {
                        // its beacon was placed by somebody's route to it (or another objective in the same cluster's was): at that hidden spot
                        o = new MapObjective(name, title, spot.System, spot.Key, null, null, text ?? spot.Name);
                        spots.Add(spot.Key);
                    }
                    else
                    {
                        // the game does not say where (a bounty's marker is at 0, 0, 0): in the list, by what it says and the contract's system
                        o = new MapObjective(name, title, m.System, null, null, null, text ?? "the game does not say where");
                        unplaced++;
                    }
                    o.Who.Add(name);
                    placed[id] = o;
                    objectives.Add(o);
                }
            }
        }
        return new
        {
            updated = nowUtc,
            systems = StarMap.Systems.Select(s => atlas.SystemData(s, spots)).ToList(),
            players,
            objectives,
            unplaced,
            missions = Missions(going),
            deaths,
        };
    }

    /// <summary>
    /// The viewer's own ship and drive, for the map's own tools (measuring a distance and its flight time with his ship, #408): his fleet's active ship and the
    /// drive he flies (Crew.QtOf, crafted as it is), also while he is not playing, so the Map tool's feed can say so in mine.
    /// </summary>
    public object Mine(string name)
    {
        var (drive, driveText) = _live.Read(t => (t.Crew.QtOf(name), t.Crew.DriveTextOf(name)));
        return new { ship = _fleet.Active(name).Text, drive = driveText.Length > 0 ? driveText : drive.Label + " (assumed)", motion = Motion(drive) };
    }

    /// <summary>A drive's motion for the page (the same curve as QtDrive.Seconds): its top speed, its two accelerations and the speed it switches between them.</summary>
    static object Motion(QtDrive drive) => new { v = drive.Speed, a1 = drive.Accel1, a2 = drive.Accel2, sw = drive.SwitchSpeed };

    /// <summary>
    /// A death for the page: who, when (time, Unix milliseconds) and where he was then (LocationTracker.Now for that moment): at a place (at), on a jump (from
    /// and to with when it started, how far and his drive's motion: the page puts the skull where he was on the route), or at his last ping; null when the map
    /// cannot place it.
    /// </summary>
    object? Death(PlayerDeath d)
    {
        var drive = _live.Read(t => t.Crew.QtOf(d.Name));
        var w = LocationTracker.Now(d.Where, drive, d.Time);
        var time = new DateTimeOffset(DateTime.SpecifyKind(d.Time, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
        return w.State switch
        {
            PlayerState.At or PlayerState.Mission when w.At != null => new { player = d.Name, time, system = w.System, at = w.At.Key, atName = w.AtName },
            PlayerState.Jump when w.From != null && w.To != null => new
            {
                player = d.Name, time, system = w.System, from = w.From.Key, to = w.To.Key, atName = "on the way to " + w.ToName,
                start = new DateTimeOffset(DateTime.SpecifyKind(w.Start, DateTimeKind.Utc)).ToUnixTimeMilliseconds(), dist = w.Gm * 1e9, motion = Motion(drive),
            },
            _ when w.LastPingAt != null => (object)new { player = d.Name, time, system = w.System, at = w.LastPingAt.Key, atName = w.LastPingAt.Name },
            _ => null,
        };
    }

    /// <summary>A mission going on: its name, who has it, and how many of its objectives are still open.</summary>
    sealed record GoingMission(string Title, List<string> Who, int Open);

    /// <summary>
    /// The table under the map: every mission going on, with who has it, its open objectives, and what the Mission Library says about its kind (the
    /// library's columns: faction, category, SCU, reward, scrip, XP, legality), by name.
    /// </summary>
    List<object> Missions(Dictionary<string, GoingMission> going)
    {
        var entries = ContractInfo.ByKey(_library);
        return going.Values.OrderBy(g => g.Title, StringComparer.OrdinalIgnoreCase).Select(g =>
        {
            var e = MissionKinds.TitleKey(g.Title) is { } key && entries.TryGetValue(key, out var found) ? found : null;
            var i = ContractInfo.Of(e, g.Title);
            return (object)new
            {
                title = i.Title, who = g.Who.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(), open = g.Open,
                faction = i.Faction, category = i.Category, scu = i.Scu, reward = i.Reward, mg = i.Mg, council = i.Council, xp = i.Xp, legality = i.Legality,
                key = e?.Row.Key,   // its kind in the library: the Companion gives the admin its fields by it
            };
        }).ToList();
    }

    /// <summary>
    /// A player for the page: where he is or where he flies (LocationTracker.Now: at a place; on his way, with where from, where to, when the jump started, how
    /// far, and his drive's motion, so the page moves him; heading somewhere the jump cannot be drawn to; at a mission's objective the map cannot place; or not
    /// known), his colour, ship and drive, the drive's top speed (top, m/s: the meter shows it while he stands still too, Frank 30 Oct 2026) and its motion
    /// (in every state since 31 Oct 2026: the map measures a distance's flight time with the viewer's own drive, #408).
    /// </summary>
    object Player(string name, PlayerLocation where, DateTime nowUtc)
    {
        var (color, drive, driveText) = _live.Read(t => (ViewPalette.Hex(Edits.ColorOf(t, name)), t.Crew.QtOf(name), t.Crew.DriveTextOf(name)));
        var ship = _fleet.Active(name).Text;
        var shown = driveText.Length > 0 ? driveText : drive.Label + " (assumed)";
        var motion = Motion(drive);
        var w = LocationTracker.Now(where, drive, nowUtc);
        return w.State switch
        {
            PlayerState.At => new { name, color, ship, drive = shown, top = drive.Speed, motion, system = w.System, state = "at", at = w.At!.Key, atName = w.AtName,
                mission = w.Mission, since = w.Since, via = w.ByPlanner ? "the Hauling Planner" : null, leaving = w.Leaving,
                hangar = w.Hangar switch { HangarState.Landed => "landed", HangarState.TakingOff => "takeoff", _ => null } },   // in the station's hangar, or out of it in the station's area
            PlayerState.Jump => new
            {
                name, color, ship, drive = shown, top = drive.Speed,
                system = w.System,
                state = "jump",
                from = w.From!.Key,
                fromName = w.From.Name,
                to = w.To!.Key,
                toName = w.ToName,
                start = new DateTimeOffset(DateTime.SpecifyKind(w.Start, DateTimeKind.Utc)).ToUnixTimeMilliseconds(),   // the moving marker counts from it
                dist = w.Gm * 1e9,
                seconds = w.Seconds,
                motion,
            },
            PlayerState.Heading => new { name, color, ship, drive = shown, top = drive.Speed, motion, system = w.System, state = "heading", to = w.To?.Key, toName = w.ToName,
                mission = w.Mission, since = w.Since },
            PlayerState.Mission => new { name, color, ship, drive = shown, top = drive.Speed, motion, system = w.System, state = "mission", atName = w.AtName, mission = w.Mission, since = w.Since },
            // not known: off the grid after a jump the map cannot follow (with the last place it had him at, where the page keeps his marker, faded: Frank,
            // 31 Oct 2026, "I don't like that the marker fully disappears"), or no signal yet
            _ => (object)new { name, color, ship, drive = shown, top = drive.Speed, motion, system = w.System, state = "unknown", off = w.Off, since = w.Since, lastPing = w.LastPing,
                lastPingAt = w.LastPingAt?.Key },
        };
    }
}
