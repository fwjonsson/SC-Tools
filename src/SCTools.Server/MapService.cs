using SCTools.Missions;
using SCTools.Server.Editing;

namespace SCTools.Server;

/// <summary>
/// The Map tool's live data (map.json): the star systems from above (StarMap: Pyro, Stanton, Nyx), everybody playing where his quantum jumps put him, and the
/// objectives of the missions going on. The log says when a player picks a quantum destination (and, in plain words, where he starts from) and when he
/// arrives, not when the jump itself starts: a jump is taken to start a few seconds after the destination was picked (JumpDelay), and the page works out
/// where he is and how fast he goes from his drive (crafted as it is), so a player in quantum moves along his route with his speed. When he arrives, the log
/// puts him there.
/// </summary>
public sealed class MapService
{
    /// <summary>From picking the destination to the jump: aligning, the spool, the calibration (a guess; the arrival corrects it).</summary>
    public static readonly TimeSpan JumpDelay = TimeSpan.FromSeconds(10);

    /// <summary>A jump that should have ended this long ago without the arrival in the log: he is there (the line got lost, or the game ended).</summary>
    static readonly TimeSpan LateArrival = TimeSpan.FromMinutes(2);

    /// <summary>The missions on the map: the ones that started in the last while and did not end.</summary>
    static readonly TimeSpan MissionAge = TimeSpan.FromHours(12);

    readonly LogUploads _uploads;
    readonly LiveTracker _live;
    readonly FleetService _fleet;

    public MapService(LogUploads uploads, LiveTracker live, FleetService fleet)
    {
        _uploads = uploads;
        _live = live;
        _fleet = fleet;
    }

    /// <summary>Where a player's quantum jumps put him: his system, the place he is at (or null), since when; and the jump he is on, if any.</summary>
    public sealed record Whereabouts(string? System, Place? At, DateTime? Since, QtEvent? Picked, Place? From, Place? To);

    /// <summary>
    /// Follows a player's quantum jumps in order: a route names where he starts from in plain words ("Projected Start Location is Aberdeen", or "Pyro System"),
    /// the destination picked is a place of the map (StarMap.OfQuantumTarget) or not, and an arrival puts him at the destination. The system is the one of the
    /// last place he was at or set out from.
    /// </summary>
    public static Whereabouts Follow(IReadOnlyList<QtEvent> qt)
    {
        string? system = null;
        Place? at = null;
        DateTime? since = null;
        QtEvent? picked = null;
        Place? from = null, to = null;
        foreach (var e in qt.OrderBy(e => e.Time))
        {
            if (e.Kind == QtKind.Selected)
            {
                if (e.From.Length > 0)
                {
                    var (sys, place) = StarMap.OfName(e.From, system, at);
                    if (sys != null) system = sys;
                    if (place != null && !Equals(place, at)) (at, since) = (place, e.Time);
                    else if (place == null && sys != null) at = null;   // "Pyro System": in space somewhere
                }
                picked = e;
                from = at;
                to = StarMap.OfQuantumTarget(e.Destination, system);
                if (to != null && system == null) system = to.System;
            }
            else if (e.Kind == QtKind.Arrived && picked != null)
            {
                (at, since) = (to, e.Time);   // null: somewhere the map does not know (a party member, an asteroid)
                if (to != null) system = to.System;
                picked = null;
                from = to = null;
            }
        }
        return new Whereabouts(system, at, since, picked, from, to);
    }

    /// <summary>Everything the page draws, now.</summary>
    public object Snapshot(DateTime nowUtc)
    {
        var games = _uploads.Games();
        var titles = _uploads.MissionsSeen();
        var players = new List<object>();
        var objectives = new List<object>();
        var placed = new HashSet<(string, string)>();
        var unplaced = 0;
        List<HaulingLog> logs;
        lock (_uploads.Sync) logs = _uploads.Logs.ToList();
        foreach (var log in logs)
        {
            var name = log.Player ?? log.Uploader;
            if (string.IsNullOrWhiteSpace(name)) continue;
            var playing = games.TryGetValue(log.Uploader ?? name, out var runs) && runs.Any(r => !r.Quit && nowUtc - r.Run.Last < AutoParty.Quiet);
            if (!playing) continue;
            List<QtEvent> qt;
            lock (_uploads.Sync) qt = log.Qt.ToList();
            var where = Follow(qt);
            players.Add(Player(name, where, nowUtc));

            var seen = (titles.GetValueOrDefault(log.Uploader ?? name) ?? Array.Empty<MissionSeen>()).ToDictionary(s => s.MissionId, StringComparer.OrdinalIgnoreCase);
            List<Mission> missions;
            lock (_uploads.Sync) missions = log.Missions.Values.ToList();
            foreach (var m in missions)
            {
                if (m.EndState != null || m.Dropped || (m.FirstSeen ?? m.Accepted ?? DateTime.MinValue) < nowUtc - MissionAge) continue;
                seen.TryGetValue(m.Id, out var s);
                if (s?.Outcome != null) continue;   // it ended (the notifications say so)
                var title = m.IsHauling || m.IsOut ? "Hauling: " + Contracts.ContractName(m) : s?.Title ?? "A mission";   // hauling by its contract line, as the planner says
                foreach (var (objective, zone) in m.ObjZone)
                {
                    if (m.Events.TryGetValue(objective, out var events) && events.Count > 0 && events[^1].State is "COMPLETED" or "FAILED") continue;
                    if (!placed.Add((m.Id.ToLowerInvariant(), objective))) continue;   // shared: once
                    if (StarMap.OfPyroStation(StationIds.Of(zone)) is { } station)   // at one of the Pyro stations the Hauling Planner knows
                    {
                        objectives.Add(new { player = name, title, system = station.System, place = station.Key, where = station.Name });
                        continue;
                    }
                    if (where.System != null && m.ObjPos.TryGetValue(objective, out var pos) && (Math.Abs(pos.X) > 1e9 || Math.Abs(pos.Y) > 1e9))
                    {
                        // in the system's own frame (metres): where it is in the system he is in
                        objectives.Add(new { player = name, title, system = where.System, x = pos.X / 1e9, y = pos.Y / 1e9, where = "in space" });
                        continue;
                    }
                    unplaced++;
                }
            }
        }
        return new
        {
            updated = nowUtc,
            systems = StarMap.Systems.Select(s => new
            {
                id = s.Id,
                name = s.Name,
                places = StarMap.Of(s.Id).Select(p => new { key = p.Key, name = p.Name, @short = p.Short, kind = p.Kind.ToString().ToLowerInvariant(), parent = p.Parent, x = p.X, y = p.Y }).ToList(),
            }).ToList(),
            players,
            objectives,
            unplaced,
        };
    }

    /// <summary>
    /// A player on the map: at the place he arrived at; on his way (where from, where to, when the jump started, how far, and his drive, so the page moves him);
    /// heading somewhere the jump cannot be drawn to (only where to); or not known (the system, when that is).
    /// </summary>
    object Player(string name, Whereabouts w, DateTime nowUtc)
    {
        var (color, drive, driveText) = _live.Read(t => (ViewPalette.Hex(Edits.ColorOf(t, name)), t.Crew.QtOf(name), t.Crew.DriveTextOf(name)));
        var ship = _fleet.Active(name).Text;
        var shown = driveText.Length > 0 ? driveText : drive.Label + " (assumed)";
        object At(Place place, DateTime? since) =>
            new { name, color, ship, drive = shown, system = place.System, state = "at", at = place.Key, atName = place.Name, since };

        if (w.Picked is { } picked && w.To is { } to)
        {
            if (w.From is { } from && from.System == to.System && !Equals(from, to))
            {
                var start = picked.Time + JumpDelay;
                var gm = StarMap.JumpGm(from, to);
                var seconds = drive.Seconds(gm);
                if (nowUtc > start + TimeSpan.FromSeconds(seconds) + LateArrival) return At(to, start + TimeSpan.FromSeconds(seconds));   // long over: he is there
                return new
                {
                    name, color, ship, drive = shown,
                    system = to.System,
                    state = "jump",
                    from = from.Key,
                    fromName = from.Name,
                    to = to.Key,
                    toName = to.Name,
                    start = new DateTimeOffset(DateTime.SpecifyKind(start, DateTimeKind.Utc)).ToUnixTimeMilliseconds(),
                    dist = gm * 1e9,
                    seconds,
                    motion = new { v = drive.Speed, a1 = drive.Accel1, a2 = drive.Accel2, sw = drive.SwitchSpeed },
                };
            }
            return new { name, color, ship, drive = shown, system = to.System, state = "heading", to = to.Key, toName = to.Name, since = (DateTime?)picked.Time };
        }
        if (w.At is { } at) return At(at, w.Since);
        return new { name, color, ship, drive = shown, system = w.System, state = "unknown" };
    }
}
