using System.Text.RegularExpressions;
using SCTools.Missions;

namespace SCTools.Locations;

/// <summary>
/// The location tracker: where every player is, since when, and whether he is on his way somewhere, from every clue the logs give (#388). It is the one place
/// that knows where the players are: the Map tool, the Hauling Planner (its map and its Current location), the Companion, My page and the Party pages all ask
/// its answer (LocationBook), worked out once per refresh (LiveTracker.Locations).
///
/// The clues, in each player's own log (on the server): when he picks a quantum destination (and, in plain words, where he starts from), when he goes from
/// one area into another (off a pad, out of a station's area into space, into the area of where he jumped to: AreaChange), when he arrives, and the
/// inventory of a place he opened (LocationSeen). A crew member's log has the arrival of the pilot's ship but not the destination: the pilot's log has it
/// (WithPilots, by the ship's id). A jump with no destination picked (at a marker he looked at) goes to a mission's objective going on (MissionPoint), else
/// somewhere the map does not know. The admin's Map places name the places the game data does not know (the Atlas). And the Hauling Planner's pickups,
/// deliveries and hand-ins: where they are newer than what the jumps say, he is at that station (Frank, 29 Oct 2026: the planner knew they were at Ruin when
/// the map did not).
/// A jump starts when he left the area he picked it in and ends when he comes into the next one (JumpTimes); picked out in space, a few seconds after the pick
/// (JumpDelay). With his drive (crafted as it is) the map moves him along his route (Now).
/// </summary>
public static class LocationTracker
{
    /// <summary>
    /// From picking the destination to the jump when he picked it out in space, with no area to leave: aligning, the spool, the calibration (a guess; the
    /// logs cannot measure it, the arrival corrects it).
    /// </summary>
    public static readonly TimeSpan JumpDelay = TimeSpan.FromSeconds(10);

    /// <summary>A jump that should have ended this long ago without the arrival in the log: he is there (the line got lost, or the game ended).</summary>
    public static readonly TimeSpan LateArrival = TimeSpan.FromMinutes(2);

    /// <summary>The missions he can jump to: the ones whose beacon came in the last while.</summary>
    public static readonly TimeSpan MissionAge = TimeSpan.FromHours(12);

    /// <summary>The arrival of the same jump in the logs of everybody aboard (the pilot's and the crew's): this close.</summary>
    public static readonly TimeSpan SameJump = TimeSpan.FromSeconds(10);

    /// <summary>An objective is done when he arrives at it, or a moment before: a jump that arrives this long after it is still to it.</summary>
    public static readonly TimeSpan DoneGrace = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Works out where everybody playing is (a game running: a log that grew in the last fifteen minutes and was not quit, PartyTracker.Quiet), from the logs
    /// (one per tracker): his jumps (a crew member's through the pilot's log, the members of his party asked first), the inventories he opened, the areas he
    /// went into and the mission objectives he can jump to, read with the atlas (the game data and the admin's Map places); then the Hauling Planner's clue
    /// (planner: for a member of its party, the station of his last pickup, delivery or hand-in and when), where it is newer than all of that.
    /// </summary>
    /// <param name="games">every game in each player's logs (LogUploads.Games), by uploader</param>
    /// <param name="seen">every mission in each player's logs with its title (LogUploads.MissionsSeen), by uploader</param>
    public static LocationBook Work(IEnumerable<HaulingLog> logs, IReadOnlyDictionary<string, List<(string File, GameRun Run, bool Quit)>> games,
        IReadOnlyDictionary<string, IReadOnlyList<MissionSeen>> seen, Atlas atlas, DateTime nowUtc, PartyBook? parties = null,
        Func<string, PlannerClue?>? planner = null)
    {
        var book = new LocationBook { Now = nowUtc };
        var playing = new List<(HaulingLog Log, string Name)>();
        foreach (var log in logs)
        {
            var name = log.Player ?? log.Uploader;
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (!(games.TryGetValue(log.Uploader ?? name, out var runs) && runs.Any(r => !r.Quit && nowUtc - r.Run.Last < PartyTracker.Quiet))) continue;
            playing.Add((log, name));
        }
        foreach (var (log, name) in playing)
        {
            var titles = (seen.GetValueOrDefault(log.Uploader ?? name) ?? Array.Empty<MissionSeen>()).ToDictionary(s => s.MissionId, StringComparer.OrdinalIgnoreCase);
            // a crew's jumps go where the pilot picked (another player of the site in the same ship: his party first); a jump to a mission's beacon is to that objective
            var party = parties?.GroupOf(name)?.Names ?? Array.Empty<string>();
            var others = playing.Where(o => !ReferenceEquals(o.Log, log)).OrderBy(o => party.Any(n => SameName(n, o.Name)) ? 0 : 1)
                .Select(o => (IReadOnlyList<QtEvent>)o.Log.Qt).ToList();
            var points = Points(log.Beacons.Where(b => b.Time >= nowUtc - MissionAge), log.Missions, m => TitleOf(m, titles), atlas);
            var where = Follow(WithPilots(log.Qt, others), atlas, null, log.Locations, points, log.Areas);
            // the Hauling Planner saw him pick up, deliver or hand in later than the jumps say anything: he is at that station
            if (planner?.Invoke(name) is { } clue && clue.At > (where.Since ?? DateTime.MinValue) && clue.At > (where.Picked?.Time ?? DateTime.MinValue)
                && atlas.OfPyroStation(clue.Station) is { } there && !Equals(there, where.At))
                where = new PlayerLocation(there.System, there, clue.At, null, null, null, ByPlanner: true);
            book.Players.Add(new LocatedPlayer(name, log, where));
        }
        return book;
    }

    /// <summary>
    /// A mission's title for the map: a hauling contract by its contract line, as the Hauling Planner says it; another by the title the notifications gave it
    /// (seen: the missions of his log by id), else "A mission".
    /// </summary>
    public static string TitleOf(Mission m, IReadOnlyDictionary<string, MissionSeen> seen) =>
        m.IsHauling || m.IsOut ? m.Title ?? "Hauling: " + Contracts.ContractName(m)
            : seen.TryGetValue(m.Id, out var s) && s.Title != null ? s.Title : "A mission";

    /// <summary>Follows a player's quantum jumps with the places of the game data alone.</summary>
    public static PlayerLocation Follow(IReadOnlyList<QtEvent> qt, IReadOnlyList<LocationSeen>? seen = null, IReadOnlyList<MissionPoint>? missions = null,
        IReadOnlyList<AreaChange>? areas = null) =>
        Follow(qt, Atlas.Default, null, seen, missions, areas);

    /// <summary>
    /// Follows a player's quantum jumps in order: a route names where he starts from in plain words ("Projected Start Location is Aberdeen", or "Pyro System"),
    /// the destination picked is a place of the map (Atlas.OfQuantumTarget) or not, and an arrival puts him at the destination. Where he opened the inventory
    /// of a place (seen: "Location[Nyx_Levski]", Atlas.OfLocation) he is there. The system is the one of the last place he was at or set out from. Unknown:
    /// told every start name, destination and location the map does not know (and nobody said is not a place), with when, what it is and the system he is in.
    /// Missions: the objectives he can jump to (MissionPoints). A destination that is one's beacon is that objective; an arrival with no destination picked
    /// (he jumped at a marker he looked at) is at the one of a mission going on then, the latest; else it is somewhere the map does not know. Areas: the areas
    /// he went into since he picked the jump he is on, and whether he was landed when he picked it, go with it (JumpTimes makes the jump's times of them).
    /// </summary>
    public static PlayerLocation Follow(IReadOnlyList<QtEvent> qt, Atlas atlas, Action<DateTime, string, UnknownKind, string?>? unknown = null,
        IReadOnlyList<LocationSeen>? seen = null, IReadOnlyList<MissionPoint>? missions = null, IReadOnlyList<AreaChange>? areas = null)
    {
        string? system = null;
        Place? at = null;
        DateTime? since = null;
        QtEvent? picked = null;
        Place? from = null, to = null;
        MissionPoint? atMission = null, toMission = null;
        var jumped = false;
        Place? lastPing = null;
        bool? landed = null, pickedLanded = null;
        var moves = new List<AreaChange>();
        var steps = qt.Select(e => (e.Time, Qt: (QtEvent?)e, Seen: (LocationSeen?)null, Area: (AreaChange?)null))
            .Concat((seen ?? Array.Empty<LocationSeen>()).Select(s => (s.Time, Qt: (QtEvent?)null, Seen: (LocationSeen?)s, Area: (AreaChange?)null)))
            .Concat((areas ?? Array.Empty<AreaChange>()).Select(a => (a.Time, Qt: (QtEvent?)null, Seen: (LocationSeen?)null, Area: (AreaChange?)a)))
            .OrderBy(s => s.Time);
        foreach (var step in steps)
        {
            if (step.Area is { } area)
            {
                landed = area.Landed;
                if (picked != null) moves.Add(area);   // on his way: when the jump starts and ends (JumpTimes)
                continue;
            }
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
                moves = new List<AreaChange>();
                from = to = null;
                atMission = toMission = null;
                jumped = false;
                lastPing = null;
                continue;
            }
            var e = step.Qt!;
            if (e.Kind == QtKind.Selected)
            {
                // the game writes the pick again and again while he aims at it: the same destination again is the same jump, with the areas he left since
                if (picked == null || !picked.Destination.Equals(e.Destination, StringComparison.OrdinalIgnoreCase))
                {
                    moves = new List<AreaChange>();
                    pickedLanded = landed;
                }
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
                moves = new List<AreaChange>();
            }
        }
        return new PlayerLocation(system, at, since, picked, from, to, atMission, toMission, jumped, lastPing,
            picked != null ? moves : null, picked != null ? pickedLanded : null);
    }

    /// <summary>
    /// A crew's jumps with the pilot's destinations: everybody aboard a ship has its arrival in his log at the same moment (the same ship id), only the pilot
    /// has the destination he picked. An arrival with no destination picked gets the destination of the same ship's arrival in another player's log (others,
    /// the first that has it), picked when he picked it.
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

    /// <summary>
    /// When a jump started and when it got there (null: not yet, as far as the log says), from the areas he went into since he picked it (moves) and how long
    /// his drive takes (seconds). Picked on a pad or in a hangar (landed), it starts when he leaves: off the pad, and out of the station's area a few seconds
    /// later (a leave line soon after the one before moves the start on); until then it has not started. Picked out in space (or a log that does not say),
    /// JumpDelay after the pick, or when he leaves the area he is in soon after. A line well into the jump (three quarters of his drive's time) is the area of
    /// where he jumped to: he is there. Measured on zzoega's logs (31 Oct 2026): from leaving to coming in, a route takes the same time to 2 seconds (Ruin to
    /// Rod's Fuel 281 to 287 s, seven times; from the pick it was 323 to 388 s); the arrival line comes 2 seconds (gateways) to 14 (stations) after coming in.
    /// </summary>
    public static (DateTime? Start, DateTime? There) JumpTimes(QtEvent picked, IReadOnlyList<AreaChange>? moves, bool? landed, double seconds)
    {
        DateTime? start = landed == true ? null : picked.Time + JumpDelay;
        // a leave line this soon after the one before is still leaving (off the pad and out of the area are 2 to 15 seconds apart in the logs); never as
        // late as half the jump, nor so late that a drive slower than his real one takes coming in for leaving
        var soon = TimeSpan.FromSeconds(Math.Clamp(seconds / 2, 10, 30));
        foreach (var m in moves ?? Array.Empty<AreaChange>())
        {
            if (m.Time < picked.Time) continue;
            if (start == null)
            {
                if (!m.Landed) start = m.Time;   // off the pad (one still landed is walking about the station)
                continue;
            }
            if (!m.Landed && m.Time - start.Value <= soon)
            {
                start = m.Time;   // out of the station's area into space
                continue;
            }
            if (m.Landed || m.Time - start.Value >= TimeSpan.FromSeconds(seconds * 0.75)) return (start, m.Time);   // into where he jumped to
        }
        return (start, null);
    }

    /// <summary>
    /// Where a player is now, with his drive (how far and how fast he goes): at the place he arrived at; on his way (where from, where to, when the jump
    /// started: JumpTimes), or still on the pad with the destination picked, or already in the area of where he jumped to, or there when the jump is long
    /// over without the arrival in the log (LateArrival); heading somewhere the jump cannot be drawn to (only where to); at a mission's objective the map
    /// cannot place; or not known (the system, when that is; after a jump somewhere the map does not know, since when).
    /// </summary>
    public static PlayerNow Now(PlayerLocation w, QtDrive drive, DateTime nowUtc)
    {
        static string AtMission(MissionPoint p) => "the mission: " + p.Text;
        // leaving: the destination he picked while he is still on the pad or in the hangar (the jump starts when he is off it)
        PlayerNow At(Place place, DateTime? since, MissionPoint? mission, string? leaving = null) =>
            new(PlayerState.At, place.System)
            {
                At = place, AtName = mission != null ? AtMission(mission) : place.Name, Mission = mission?.Title, Since = since, ByPlanner = w.ByPlanner, Leaving = leaving,
            };

        if (w.Picked is { } picked && (w.To != null || w.ToMission != null))
        {
            var toName = w.ToMission is { } tm ? AtMission(tm) : w.To!.Name;
            if (w.To is { } to && w.From is { } from && from.System == to.System && !Equals(from, to))
            {
                var gm = StarMap.JumpGm(from, to);
                var seconds = drive.Seconds(gm);
                var (left, there) = JumpTimes(picked, w.Moves, w.PickedLanded, seconds);
                if (left is not { } start) return At(from, w.Since, null, toName);   // still on the pad: not gone yet
                if (there != null) return At(to, there, w.ToMission);   // in the area of where he jumped to: there (the arrival line comes a few seconds later)
                if (nowUtc > start + TimeSpan.FromSeconds(seconds) + LateArrival) return At(to, start + TimeSpan.FromSeconds(seconds), w.ToMission);   // long over: he is there
                return new PlayerNow(PlayerState.Jump, to.System) { From = from, To = to, ToName = toName, Start = start, Gm = gm, Seconds = seconds };
            }
            return new PlayerNow(PlayerState.Heading, w.To?.System ?? w.ToMission?.System ?? w.System)
            {
                To = w.To, ToName = toName, Mission = w.ToMission?.Title, Since = picked.Time,
            };
        }
        if (w.At is { } at) return At(at, w.Since, w.AtMission);
        if (w.AtMission is { } am)
            return new PlayerNow(PlayerState.Mission, am.System ?? w.System) { AtName = AtMission(am), Mission = am.Title, Since = w.Since };
        // not known: off the grid after a jump the map cannot follow (with the last place it had him at), or no signal yet
        return new PlayerNow(PlayerState.Unknown, w.System) { Off = w.Jumped, Since = w.Jumped ? w.Since : null, LastPing = w.Jumped ? w.LastPing?.Name : null };
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

    /// <summary>Is this state of an objective the end of it?</summary>
    public static bool IsOver(string state) => state is "COMPLETED" or "FAILED" or "WITHDRAWN";

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
}
