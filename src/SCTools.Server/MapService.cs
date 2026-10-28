using SCTools.Server.Editing;

namespace SCTools.Server;

/// <summary>
/// The Map tool's live data (map.json): the Pyro system as the Trip Planner draws it, everybody playing where his quantum jumps put him, and the objectives
/// of the missions going on. The logs say when a player picks a quantum destination (and from where) and when he arrives, not when the jump itself starts:
/// a jump is taken to start a few seconds after the destination was picked (JumpDelay), and the page works out where he is and how fast he goes from his
/// drive (crafted as it is), so a player in quantum moves along his route with his speed. When he arrives, the log puts him there.
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

    static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    static string? StationName(string? id) => id == null ? null : PyroDistances.Points.Where(p => p.Id == id).Select(p => p.Name).FirstOrDefault();

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
            lock (_uploads.Sync) qt = log.Qt.OrderBy(e => e.Time).ToList();
            players.Add(Player(name, qt, nowUtc));

            Dictionary<string, MissionSeen> seen;
            lock (_uploads.Sync)
                seen = (titles.GetValueOrDefault(log.Uploader ?? name) ?? Array.Empty<MissionSeen>()).ToDictionary(s => s.MissionId, StringComparer.OrdinalIgnoreCase);
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
                    var station = StationIds.Of(zone);
                    if (station != null)
                    {
                        objectives.Add(new { player = name, title, station, where = StationName(station) });
                        continue;
                    }
                    if (m.ObjPos.TryGetValue(objective, out var pos) && (Math.Abs(pos.X) > 1e9 || Math.Abs(pos.Y) > 1e9))   // in the system's own frame
                    {
                        var (x, y) = PyroDistances.MapOfSystem(pos.X, pos.Y);
                        objectives.Add(new { player = name, title, x, y, where = "in space" });
                        continue;
                    }
                    unplaced++;
                }
            }
        }
        return new
        {
            updated = nowUtc,
            stations = PyroDistances.Points.Select(p => new { id = p.Id, name = p.Name, x = p.X, y = p.Y }).ToList(),
            backdrop = new
            {
                sun = new[] { PyroDistances.Sun.X, PyroDistances.Sun.Y },
                sunRadius = PyroDistances.SunRadius,
                orbits = PyroDistances.OrbitRadii,
                planets = PyroDistances.Planets.Select(q => new[] { q.X, q.Y }).ToList(),
            },
            players,
            objectives,
            unplaced,
        };
    }

    /// <summary>
    /// Where a player is by his quantum jumps: at the station he arrived at; on his way (where from, where to, when the jump started, how far, and his drive,
    /// so the page moves him); or not known.
    /// </summary>
    object Player(string name, List<QtEvent> qt, DateTime nowUtc)
    {
        var (color, drive, driveText) = _live.Read(t => (ViewPalette.Hex(Edits.ColorOf(t, name)), t.Crew.QtOf(name), t.Crew.DriveTextOf(name)));
        var ship = _fleet.Active(name).Text;
        var common = new { name, color, ship, drive = driveText.Length > 0 ? driveText : drive.Label + " (assumed)" };

        var lastSelected = qt.LastOrDefault(e => e.Kind == QtKind.Selected);
        if (lastSelected == null) return new { common.name, common.color, common.ship, common.drive, state = "unknown" };
        var to = PyroDistances.IdOfQuantumTarget(lastSelected.Destination);
        var arrived = qt.FirstOrDefault(e => e.Kind == QtKind.Arrived && e.Time >= lastSelected.Time);
        if (arrived != null)
            return new { common.name, common.color, common.ship, common.drive, state = to == null ? "unknown" : "at", at = to, since = arrived.Time };

        // on his way (or about to be): from where the game said he started, else where he arrived before
        var from = lastSelected.From.Length > 0 ? PyroDistances.IdOfPlace(lastSelected.From) : null;
        if (from == null)
        {
            var before = qt.LastOrDefault(e => e.Kind == QtKind.Arrived && e.Time < lastSelected.Time);
            var picked = before == null ? null : qt.LastOrDefault(e => e.Kind == QtKind.Selected && e.Time <= before.Time);
            from = picked == null ? null : PyroDistances.IdOfQuantumTarget(picked.Destination);
        }
        if (to == null || from == null || from == to)
            return new { common.name, common.color, common.ship, common.drive, state = to == null ? "unknown" : "at", at = to, since = lastSelected.Time };
        var start = lastSelected.Time + JumpDelay;
        var gm = PyroDistances.Between(from, to);
        var seconds = drive.Seconds(gm);
        if (nowUtc > start + TimeSpan.FromSeconds(seconds) + LateArrival)   // long over: he is there
            return new { common.name, common.color, common.ship, common.drive, state = "at", at = (string?)to, since = start + TimeSpan.FromSeconds(seconds) };
        return new
        {
            common.name, common.color, common.ship, common.drive,
            state = "jump",
            from,
            to,
            toName = StationName(to),
            start = new DateTimeOffset(DateTime.SpecifyKind(start, DateTimeKind.Utc)).ToUnixTimeMilliseconds(),
            dist = gm * 1e9,
            seconds,
            motion = new { v = drive.Speed, a1 = drive.Accel1, a2 = drive.Accel2, sw = drive.SwitchSpeed },
        };
    }
}
