using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SCTools.Missions;
using SCTools.Sessions;
using SCTools.Server.Editing;

namespace SCTools.Server;

/// <summary>
/// The Map tool's live data (map.json): the star systems from above (StarMap: Pyro, Stanton, Nyx), everybody playing where his quantum jumps put him, and the
/// objectives of the missions going on. The log says when a player picks a quantum destination (and, in plain words, where he starts from) and when he
/// arrives, not when the jump itself starts: a jump is taken to start a few seconds after the destination was picked (JumpDelay), and the page works out
/// where he is and how fast he goes from his drive (crafted as it is), so a player in quantum moves along his route with his speed. When he arrives, the log
/// puts him there. A jump with no destination picked (at a marker he looked at, or as crew) is one too: to a mission's objective going on, to where the
/// pilot picked, or somewhere the map does not know. The Hauling Planner knows where its party is from the pickups, deliveries and hand-ins too (Whereabouts):
/// where that is newer than what the jumps say, the map takes it (Frank, 29 Oct 2026: the planner knew they were at Ruin when the map did not). Under the
/// map, the missions going on with what the Mission Library says about them.
/// </summary>
public sealed class MapService
{
    /// <summary>From picking the destination to the jump: aligning, the spool, the calibration (a guess; the arrival corrects it).</summary>
    public static readonly TimeSpan JumpDelay = TimeSpan.FromSeconds(10);

    /// <summary>A jump that should have ended this long ago without the arrival in the log: he is there (the line got lost, or the game ended).</summary>
    static readonly TimeSpan LateArrival = TimeSpan.FromMinutes(2);

    /// <summary>The missions on the map: the ones that started in the last while and did not end.</summary>
    static readonly TimeSpan MissionAge = TimeSpan.FromHours(12);

    /// <summary>The arrival of the same jump in the logs of everybody aboard (the pilot's and the crew's): this close.</summary>
    public static readonly TimeSpan SameJump = TimeSpan.FromSeconds(10);

    /// <summary>An objective is done when he arrives at it, or a moment before: a jump that arrives this long after it is still to it.</summary>
    static readonly TimeSpan DoneGrace = TimeSpan.FromMinutes(1);

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
    /// Where a player's quantum jumps put him: his system, the place he is at (or null), since when; and the jump he is on, if any. At or to a mission's
    /// objective (AtMission, ToMission; its place, when its text names one). Jumped: the last jump took him somewhere the map does not know ("off the
    /// grid"); LastPing: the last place the map knew him at before that, if any.
    /// </summary>
    public sealed record Whereabouts(string? System, Place? At, DateTime? Since, QtEvent? Picked, Place? From, Place? To,
        MissionPoint? AtMission = null, MissionPoint? ToMission = null, bool Jumped = false, Place? LastPing = null);

    /// <summary>
    /// An objective of a mission he can quantum jump to: its beacon (MissionBeacon: the destination's id, and its marker's entity id), the mission's title,
    /// what the objective says, the contract's system (or null), the place its text names (or null), and from when until the objective or the mission was
    /// over (null: going on).
    /// </summary>
    public sealed record MissionPoint(string NavPoint, string Marker, string Title, string Text, string? System, Place? Place, DateTime From, DateTime? Until)
    {
        /// <summary>Is this destination of the log this objective (its beacon, or its marker)?</summary>
        public bool Is(string destination) =>
            destination.Equals(NavPoint, StringComparison.OrdinalIgnoreCase)
            || (Marker.Length > 0 && destination.Equals("MissionObjectiveMarker_" + Marker, StringComparison.OrdinalIgnoreCase));

        /// <summary>Was the objective there to jump to at this time?</summary>
        public bool On(DateTime t) => From <= t && (Until == null || Until >= t - DoneGrace);
    }

    /// <summary>What a text of the log the map does not know is.</summary>
    public enum UnknownKind { Destination, Start, Location }

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

    /// <summary>Follows a player's quantum jumps with the places of the game data alone.</summary>
    public static Whereabouts Follow(IReadOnlyList<QtEvent> qt, IReadOnlyList<LocationSeen>? seen = null, IReadOnlyList<MissionPoint>? missions = null) =>
        Follow(qt, Atlas.Default, null, seen, missions);

    /// <summary>
    /// Follows a player's quantum jumps in order: a route names where he starts from in plain words ("Projected Start Location is Aberdeen", or "Pyro System"),
    /// the destination picked is a place of the map (Atlas.OfQuantumTarget) or not, and an arrival puts him at the destination. Where he opened the inventory
    /// of a place (seen: "Location[Nyx_Levski]", Atlas.OfLocation) he is there. The system is the one of the last place he was at or set out from. Unknown:
    /// told every start name, destination and location the map does not know (and nobody said is not a place), with when, what it is and the system he is in.
    /// Missions: the objectives he can jump to (MissionPoints). A destination that is one's beacon is that objective; an arrival with no destination picked
    /// (he jumped at a marker he looked at) is at the one of a mission going on then, the latest; else it is somewhere the map does not know.
    /// </summary>
    public static Whereabouts Follow(IReadOnlyList<QtEvent> qt, Atlas atlas, Action<DateTime, string, UnknownKind, string?>? unknown = null,
        IReadOnlyList<LocationSeen>? seen = null, IReadOnlyList<MissionPoint>? missions = null)
    {
        string? system = null;
        Place? at = null;
        DateTime? since = null;
        QtEvent? picked = null;
        Place? from = null, to = null;
        MissionPoint? atMission = null, toMission = null;
        var jumped = false;
        Place? lastPing = null;
        var steps = qt.Select(e => (e.Time, Qt: (QtEvent?)e, Seen: (LocationSeen?)null))
            .Concat((seen ?? Array.Empty<LocationSeen>()).Select(s => (s.Time, Qt: (QtEvent?)null, Seen: (LocationSeen?)s)))
            .OrderBy(s => s.Time);
        foreach (var step in steps)
        {
            if (step.Seen is { } s)
            {
                // he opened the inventory of a place: he is there (and not in quantum, unless it is where the jump he picked starts). A place the map does not
                // know (an inventory at a mission's spot, an outpost) does not take away where it had him: he is still there, or near it
                var (sys, place) = atlas.OfLocation(s.Location);
                if (place == null && unknown != null && !atlas.AliasOf(s.Location).Said) unknown(s.Time, s.Location, UnknownKind.Location, sys ?? system);
                if (sys != null) system = sys;
                if (picked != null && place != null && Equals(place, from)) continue;
                if (place == null && picked == null) continue;
                if (!Equals(place, at) || since == null || atMission != null || jumped) (at, since) = (place ?? at, s.Time);
                picked = null;
                from = to = null;
                atMission = toMission = null;
                jumped = false;
                lastPing = null;
                continue;
            }
            var e = step.Qt!;
            if (e.Kind == QtKind.Selected)
            {
                if (e.From.Length > 0)
                {
                    var (sys, place) = atlas.OfName(e.From, system, at);
                    if (sys == null && unknown != null && !atlas.AliasOf(e.From).Said) unknown(e.Time, e.From, UnknownKind.Start, system);
                    if (sys != null) system = sys;
                    if (place != null && (!Equals(place, at) || atMission != null || jumped))
                    {
                        (at, since) = (place, e.Time);
                        atMission = null;
                        jumped = false;
                        lastPing = null;
                    }
                    else if (place == null && sys != null) at = null;   // "Pyro System": in space somewhere
                }
                picked = e;
                from = at;
                toMission = missions?.LastOrDefault(p => p.Is(e.Destination));
                to = toMission != null ? toMission.Place : atlas.OfQuantumTarget(e.Destination, system);
                if (to == null && toMission == null && unknown != null && e.Destination.Length > 0 && !atlas.AliasOf(e.Destination).Said)
                    unknown(e.Time, e.Destination, UnknownKind.Destination, system);
                system ??= to?.System ?? toMission?.System;
            }
            else if (e.Kind == QtKind.Arrived)
            {
                if (picked == null)
                {
                    // no destination picked: he jumped at a marker he looked at (a mission's objective going on, the latest), or somewhere the map does not know
                    toMission = missions?.Where(p => p.On(e.Time)).OrderBy(p => p.From).LastOrDefault();
                    to = toMission?.Place;
                }
                var left = at;
                (at, since) = (to, e.Time);   // null: somewhere the map does not know (a party member, an asteroid), or a mission's objective
                atMission = toMission;
                jumped = to == null && toMission == null;
                lastPing = jumped ? left ?? lastPing : null;   // off the grid: where he was before (or before that, jump after jump)
                if (to != null) system = to.System;
                else if (toMission?.System != null) system = toMission.System;
                picked = null;
                from = to = null;
                toMission = null;
            }
        }
        return new Whereabouts(system, at, since, picked, from, to, atMission, toMission, jumped, lastPing);
    }

    /// <summary>
    /// A crew's jumps with the pilot's destinations: everybody aboard a ship has its arrival in his log at the same moment (the same ship id), only the pilot
    /// has the destination he picked. An arrival with no destination picked gets the destination of the same ship's arrival in another player's log (others),
    /// picked when he picked it.
    /// </summary>
    public static List<QtEvent> WithPilots(IReadOnlyList<QtEvent> qt, IReadOnlyList<IReadOnlyList<QtEvent>> others)
    {
        var all = new List<QtEvent>(qt.Count);
        var picked = false;
        foreach (var e in qt)
        {
            if (e.Kind == QtKind.Selected)
            {
                picked = true;
                all.Add(e);
                continue;
            }
            if (!picked && e.ShipId.Length > 0 && others.Select(o => PilotPick(o, e)).FirstOrDefault(p => p != null) is { } pick)
                all.Add(new QtEvent { Time = pick.Time, Kind = QtKind.Selected, Destination = pick.Destination, Ship = e.Ship, ShipId = e.ShipId });
            picked = false;
            all.Add(e);
        }
        return all;
    }

    /// <summary>The destination picked in this log for the arrival of the same ship as this arrival, at the same moment (or null).</summary>
    static QtEvent? PilotPick(IReadOnlyList<QtEvent> qt, QtEvent arrival)
    {
        for (var i = 0; i < qt.Count; i++)
        {
            var a = qt[i];
            if (a.Kind != QtKind.Arrived || a.ShipId != arrival.ShipId || (a.Time - arrival.Time).Duration() > SameJump) continue;
            return i > 0 && qt[i - 1].Kind == QtKind.Selected ? qt[i - 1] : null;
        }
        return null;
    }

    /// <summary>The objectives of the missions he can jump to: the ones the game gave a beacon (MissionBeacon), with their mission, text, system and place.</summary>
    public static List<MissionPoint> Points(IEnumerable<MissionBeacon> beacons, IReadOnlyDictionary<string, Mission> missions, Func<Mission, string> title, Atlas atlas)
    {
        var points = new List<MissionPoint>();
        foreach (var b in beacons)
        {
            if (!missions.TryGetValue(b.MissionId, out var m) || m.Dropped) continue;
            var until = m.EndedAt;
            if (m.Events.TryGetValue(b.Objective, out var events) && events.FirstOrDefault(x => IsOver(x.State)) is { } over && (until == null || over.Time < until))
                until = over.Time;
            var text = m.ObjText.GetValueOrDefault(b.Objective) ?? "an objective";
            points.Add(new MissionPoint(b.NavPoint, b.Marker, title(m), text, m.System, NamedIn(text, m.System, atlas), b.Time, until));
        }
        return points;
    }

    static bool IsOver(string state) => state is "COMPLETED" or "FAILED" or "WITHDRAWN";

    /// <summary>
    /// Is an objective done in this log, so it is off the map: its last state is over, or it is a pickup of one of the Hauling Planner's contracts that the
    /// planner counts as picked up (SessionBuilder.PickedUp: a COMPLETED or an INPROGRESS event). So the diamond goes when the planner credits the pickup, not later.
    /// </summary>
    static bool Done(Mission m, string objective) =>
        (m.Events.TryGetValue(objective, out var events) && events.Count > 0 && IsOver(events[^1].State)) || (m.IsHauling && SessionBuilder.PickedUp(m, objective));

    // "Go to a debris field above Daymar", "Go to the asteroids surrounding Yela": the place at the end
    static readonly Regex Near = new(@"\b(?:above|surrounding|near|around|orbiting|over)\s+(?<p>[A-Z][\w'.-]*(?:\s+[A-Z0-9][\w'.-]*)*)\s*$", RegexOptions.Compiled);

    /// <summary>The place an objective's text names at its end ("Go to a debris field above Daymar": Daymar), in the contract's system when it says one; or null.</summary>
    public static Place? NamedIn(string text, string? system, Atlas atlas)
    {
        var m = Near.Match(text);
        if (!m.Success) return null;
        var (_, place) = atlas.OfName(m.Groups["p"].Value, system);
        return place != null && (system == null || place.System == system) ? place : null;
    }

    /// <summary>Everybody playing (a game running, by the trackers' logs), with copies of his lists (the tracker adds to them meanwhile).</summary>
    List<(HaulingLog Log, string Name, List<QtEvent> Qt, List<LocationSeen> Seen, List<MissionBeacon> Beacons, Dictionary<string, Mission> Missions)> Playing(DateTime nowUtc)
    {
        var games = _uploads.Games();
        List<HaulingLog> logs;
        lock (_uploads.Sync) logs = _uploads.Logs.ToList();
        var playing = new List<(HaulingLog Log, string Name, List<QtEvent> Qt, List<LocationSeen> Seen, List<MissionBeacon> Beacons, Dictionary<string, Mission> Missions)>();
        foreach (var log in logs)
        {
            var name = log.Player ?? log.Uploader;
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (!(games.TryGetValue(log.Uploader ?? name, out var runs) && runs.Any(r => !r.Quit && nowUtc - r.Run.Last < AutoParty.Quiet))) continue;
            lock (_uploads.Sync) playing.Add((log, name, log.Qt.ToList(), log.Locations.ToList(), log.Beacons.ToList(), new Dictionary<string, Mission>(log.Missions)));
        }
        return playing;
    }

    /// <summary>
    /// Where the map has everybody playing, for the Hauling Planner (TrackerState.Positions, pushed by PlannerPositions): the place he is at and since when, and
    /// the one he is flying to and since when he picked it, as the planner's stations (null for a place that is not one of them). From the jumps alone (a crew
    /// member's through the pilot's log, WithPilots), the areas and the mission objectives: not from the planner's own pickups and deliveries, which it knows
    /// itself (and which the map takes from it in Snapshot).
    /// </summary>
    public Dictionary<string, WorkerPosition> PlannerPositions(DateTime nowUtc)
    {
        var atlas = _places.Atlas;
        var playing = Playing(nowUtc);
        var positions = new Dictionary<string, WorkerPosition>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in playing)
        {
            var others = playing.Where(o => !ReferenceEquals(o.Log, p.Log)).Select(o => (IReadOnlyList<QtEvent>)o.Qt).ToList();
            var points = Points(p.Beacons.Where(b => b.Time >= nowUtc - MissionAge), p.Missions, m => m.Title ?? "A mission", atlas);
            var w = Follow(WithPilots(p.Qt, others), atlas, null, p.Seen, points);
            positions[p.Name] = new WorkerPosition(StarMap.PyroStationIdOf(w.At), w.At != null ? w.Since : null, StarMap.PyroStationIdOf(w.To), w.To != null ? w.Picked?.Time : null);
        }
        return positions;
    }

    /// <summary>Everything the page draws, now: the systems (one of them when system is given: the Trip Planner's Pyro), the players, the objectives, the missions.</summary>
    public object Snapshot(DateTime nowUtc, string? system = null)
    {
        var games = _uploads.Games();
        var titles = _uploads.MissionsSeen();
        var atlas = _places.Atlas;
        var players = new List<object>();
        var objectives = new List<MapObjective>();
        var placed = new Dictionary<(string, string), MapObjective>();
        var unplaced = 0;
        var going = new Dictionary<string, GoingMission>(StringComparer.OrdinalIgnoreCase);   // the missions going on, by id (shared: once), for the table under the map
        var playing = Playing(nowUtc);

        // an objective one holder's log says is done is done for everybody with the mission (a shared contract's pickup is credited once; the Hauling Planner
        // reads the logs merged the same way), whichever log the map happens to place it from
        var done = new HashSet<(string, string)>();
        foreach (var p in playing)
            foreach (var m in p.Missions.Values)
                foreach (var objective in m.ObjZone.Keys)
                    if (Done(m, objective)) done.Add((m.Id.ToLowerInvariant(), objective));

        foreach (var p in playing)
        {
            var (log, name) = (p.Log, p.Name);
            var seen = (titles.GetValueOrDefault(log.Uploader ?? name) ?? Array.Empty<MissionSeen>()).ToDictionary(s => s.MissionId, StringComparer.OrdinalIgnoreCase);
            string Title(Mission m) => m.IsHauling || m.IsOut ? m.Title ?? "Hauling: " + Contracts.ContractName(m)   // hauling by its contract line, as the planner says
                : seen.TryGetValue(m.Id, out var s) && s.Title != null ? s.Title : "A mission";

            // a crew's jumps go where the pilot picked (another player of the site in the same ship); a jump to a mission's beacon is to that objective
            var others = playing.Where(o => !ReferenceEquals(o.Log, log)).Select(o => (IReadOnlyList<QtEvent>)o.Qt).ToList();
            var points = Points(p.Beacons.Where(b => b.Time >= nowUtc - MissionAge), p.Missions, Title, atlas);
            var where = Follow(WithPilots(p.Qt, others), atlas, null, p.Seen, points);
            // the Hauling Planner saw him pick up, deliver or hand in later than the jumps say anything: he is at that station
            var planner = _live.Read(t => t.State.Data.Party.Any(n => PlayerNames.SameName(n, name)) ? (Station: t.Whereabouts.AutoLocationOf(name), At: t.Whereabouts.LastMoveOf(name)) : (null, DateTime.MinValue));
            var byPlanner = false;
            if (planner.Station != null && planner.At > (where.Since ?? DateTime.MinValue) && planner.At > (where.Picked?.Time ?? DateTime.MinValue)
                && atlas.OfPyroStation(planner.Station) is { } there && !Equals(there, where.At))
            {
                where = new Whereabouts(there.System, there, planner.At, null, null, null);
                byPlanner = true;
            }
            players.Add(Player(name, where, nowUtc, byPlanner));
            foreach (var m in p.Missions.Values)
            {
                if (m.EndState != null || m.Dropped || (m.FirstSeen ?? m.Accepted ?? DateTime.MinValue) < nowUtc - MissionAge) continue;
                if (seen.TryGetValue(m.Id, out var s2) && s2.Outcome != null) continue;
                var open = m.ObjZone.Keys.Count(o => !done.Contains((m.Id.ToLowerInvariant(), o)));
                if (!going.TryGetValue(m.Id, out var row)) going[m.Id] = row = new GoingMission(Title(m), new List<string>(), open);
                if (!row.Who.Contains(name)) row.Who.Add(name);
            }

            foreach (var m in p.Missions.Values)
            {
                if (m.EndState != null || m.Dropped || (m.FirstSeen ?? m.Accepted ?? DateTime.MinValue) < nowUtc - MissionAge) continue;
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
                    else if (text != null && NamedIn(text, m.System, atlas) is { } named)   // "Go to a debris field above Daymar": at Daymar
                        o = new MapObjective(name, title, named.System, named.Key, null, null, text);
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
            systems = StarMap.Systems.Where(s => system == null || s.Id == system).Select(atlas.SystemData).ToList(),
            players,
            objectives,
            unplaced,
            missions = Missions(going),
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
        var entries = _library == null ? new Dictionary<string, LibraryEntry>(StringComparer.OrdinalIgnoreCase)
            : _library.Entries().GroupBy(e => e.Row.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        static string N(long? v) => v?.ToString("N0", CultureInfo.InvariantCulture) ?? "";
        return going.Values.OrderBy(g => g.Title, StringComparer.OrdinalIgnoreCase).Select(g =>
        {
            var e = MissionKinds.TitleKey(g.Title) is { } key && entries.TryGetValue(key, out var found) ? found : null;
            return (object)new
            {
                title = e?.Shown ?? g.Title, who = g.Who.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(), open = g.Open,
                faction = e?.FactionShown ?? "", category = e?.Category ?? "", scu = N(e?.ScuShown), reward = e == null ? "" : e.Row.Auec != null ? N(e.Row.Auec) : e.Wiki?.RewardText ?? "",
                mg = N(e?.Row.MgScrip), council = N(e?.Row.CouncilScrip), xp = N(e?.Xp), legality = e?.Legality ?? "",
            };
        }).ToList();
    }

    /// <summary>
    /// A player on the map: at the place he arrived at; on his way (where from, where to, when the jump started, how far, and his drive, so the page moves him);
    /// heading somewhere the jump cannot be drawn to (only where to); at a mission's objective the map cannot place; or not known (the system, when that is;
    /// after a jump somewhere the map does not know, since when).
    /// </summary>
    object Player(string name, Whereabouts w, DateTime nowUtc, bool byPlanner = false)
    {
        var (color, drive, driveText) = _live.Read(t => (ViewPalette.Hex(Edits.ColorOf(t, name)), t.Crew.QtOf(name), t.Crew.DriveTextOf(name)));
        var ship = _fleet.Active(name).Text;
        var shown = driveText.Length > 0 ? driveText : drive.Label + " (assumed)";
        static string AtMission(MissionPoint p) => "the mission: " + p.Text;
        object At(Place place, DateTime? since, MissionPoint? mission) =>
            new { name, color, ship, drive = shown, system = place.System, state = "at", at = place.Key, atName = mission != null ? AtMission(mission) : place.Name,
                mission = mission?.Title, since, via = byPlanner ? "the Hauling Planner" : null };

        if (w.Picked is { } picked && (w.To != null || w.ToMission != null))
        {
            var toName = w.ToMission is { } tm ? AtMission(tm) : w.To!.Name;
            if (w.To is { } to && w.From is { } from && from.System == to.System && !Equals(from, to))
            {
                var start = picked.Time + JumpDelay;
                var gm = StarMap.JumpGm(from, to);
                var seconds = drive.Seconds(gm);
                if (nowUtc > start + TimeSpan.FromSeconds(seconds) + LateArrival) return At(to, start + TimeSpan.FromSeconds(seconds), w.ToMission);   // long over: he is there
                return new
                {
                    name, color, ship, drive = shown,
                    system = to.System,
                    state = "jump",
                    from = from.Key,
                    fromName = from.Name,
                    to = to.Key,
                    toName,
                    start = new DateTimeOffset(DateTime.SpecifyKind(start, DateTimeKind.Utc)).ToUnixTimeMilliseconds(),
                    dist = gm * 1e9,
                    seconds,
                    motion = new { v = drive.Speed, a1 = drive.Accel1, a2 = drive.Accel2, sw = drive.SwitchSpeed },
                };
            }
            return new { name, color, ship, drive = shown, system = w.To?.System ?? w.ToMission?.System ?? w.System, state = "heading", to = w.To?.Key, toName,
                mission = w.ToMission?.Title, since = (DateTime?)picked.Time };
        }
        if (w.At is { } at) return At(at, w.Since, w.AtMission);
        if (w.AtMission is { } am)
            return new { name, color, ship, drive = shown, system = am.System ?? w.System, state = "mission", atName = AtMission(am), mission = am.Title, since = w.Since };
        // not known: off the grid after a jump the map cannot follow (with the last place it had him at), or no signal yet
        return new { name, color, ship, drive = shown, system = w.System, state = "unknown", off = w.Jumped, since = w.Jumped ? w.Since : null,
            lastPing = w.Jumped ? w.LastPing?.Name : null };
    }
}
