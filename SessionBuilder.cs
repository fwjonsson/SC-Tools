using System.Text.RegularExpressions;

namespace HaulingTracker;

/// <summary>A group of hauling missions that belong to the same hauling session.</summary>
public sealed class SessionGroup
{
    public List<Mission> Missions = new();
    public List<Mission> Failed = new();   // failed or abandoned hauling missions of the same time: not part of the session, but their cargo can be in a run
    public DateTime Start;      // UTC
    public DateTime End;        // UTC, used for grouping only
    public bool Manual;         // started with Start Session: one session from the start to the stop, however long
    public DateTime? Stop;      // UTC, when a manual session was stopped
    public string? CustomKey;   // the key of a manual session: its start time

    /// <summary>Stable id for saving your own notes: the id of the earliest mission (a manual session: its start time).</summary>
    public string Key => CustomKey ?? (Missions.Count > 0 ? Missions[0].Id : "");
}

public sealed class SessionSummary
{
    public DateTime Start;      // UTC
    public DateTime? End;       // UTC, time of the last completed mission
    public int Completed;
    public int Active;          // missions accepted but not completed yet
    public int Total;
}

/// <summary>One pickup location with every mission that has cargo there.</summary>
public sealed class LocationRow
{
    public string Zone = "";
    public int Missions;        // missions with a stop here
    public int Stops2;          // of those, stops that belong to 2 stop missions
    public int Stops3;          // of those, stops that belong to 3 stop missions
    public int Stops4;          // of those, stops that belong to 4 stop missions
    public int Loaded;          // stops fully picked up
    public int Partial;         // stops where only part is picked up so far
    public int Delivered;       // stops whose cargo has been handed in at Ruin
    public DateTime? LoadedTime;     // latest pickup event here (UTC)
    public DateTime? DeliveredTime;  // set when every stop here has been handed in (UTC)
}

public static class SessionBuilder
{
    /// <summary>The session clock stops while nothing has happened for this long. It never ends a session, the time is just not counted.</summary>
    public static readonly TimeSpan ClockIdleLimit = TimeSpan.FromHours(1);
    static readonly Regex StopId = new(@"_\d+$", RegexOptions.Compiled);

    static bool IsStop(string objective) => objective.StartsWith("pickup_") && StopId.IsMatch(objective);
    static bool IsDrop(string objective) => objective.StartsWith("dropoff_");

    static DateTime MissionStart(Mission m) => m.Accepted ?? m.FirstSeen ?? DateTime.MinValue;

    /// <summary>
    /// A mission the log never saw end (the game crashed, the other account stopped playing) must not live for ever:
    /// with no sign of life for this long before the reference time it is treated as old and left out of the sessions.
    /// </summary>
    public static readonly TimeSpan StaleMissionLimit = TimeSpan.FromHours(3);

    static bool IsStale(Mission m, DateTime reference)
    {
        var last = MissionStart(m);
        foreach (var list in m.Events.Values)
        {
            foreach (var e in list) if (e.Time > last) last = e.Time;
        }
        return last != DateTime.MinValue && last < reference - StaleMissionLimit;
    }

    static DateTime MissionEnd(Mission m, DateTime lastLogTime)
    {
        if (m.EndedAt.HasValue) return m.EndedAt.Value;
        var times = m.Events.Values.SelectMany(l => l).Select(e => e.Time).ToList();
        return times.Count > 0 ? times.Max() : lastLogTime;
    }

    /// <summary>
    /// The sessions are the ones you start and stop with the button: a mission belongs to the one that was running when it ended.
    /// A mission that is still going belongs to the session that is running now, also when you accepted it before you pressed Start
    /// (with no session running it belongs to the one that was running at the last log line). Missions outside every session are not shown.
    /// </summary>
    public static List<SessionGroup> Build(HaulingLog log, IReadOnlyList<ManualSession>? manual = null)
    {
        var claimed = new HashSet<string>();
        var groups = new List<SessionGroup>();
        var windows = (manual ?? Array.Empty<ManualSession>()).OrderBy(x => x.StartUtc).ToList();
        var running = windows.LastOrDefault(x => !x.StopUtc.HasValue);
        foreach (var w in windows)
        {
            var stop = w.StopUtc ?? DateTime.MaxValue;
            var g = new SessionGroup
            {
                Start = w.StartUtc,
                End = w.StopUtc ?? (log.LastTime > w.StartUtc ? log.LastTime : w.StartUtc),
                Manual = true,
                Stop = w.StopUtc,
                CustomKey = "manual-" + w.StartUtc.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture),
            };
            foreach (var m in log.Missions.Values.Where(x => x.IsHauling && !x.Dead).OrderBy(MissionStart))
            {
                if (claimed.Contains(m.Id)) continue;
                if (!m.EndedAt.HasValue && running != null)
                {
                    if (!ReferenceEquals(w, running)) continue;   // a mission that is still going belongs to the running session
                    if (IsStale(m, w.StartUtc)) continue;         // but one from long before the session that never ended is old
                }
                else
                {
                    if (!m.EndedAt.HasValue && IsStale(m, log.LastTime)) continue;
                    var t = m.EndedAt ?? log.LastTime;
                    if (t < w.StartUtc || t > stop) continue;
                }
                g.Missions.Add(m);
                claimed.Add(m.Id);
            }
            foreach (var m in log.Missions.Values.Where(x => x.IsHauling && x.Dead))
            {
                if (claimed.Contains(m.Id)) continue;
                var t = m.EndedAt ?? MissionStart(m);
                if (t == DateTime.MinValue || t < w.StartUtc || t > stop) continue;
                g.Failed.Add(m);
                claimed.Add(m.Id);
            }
            if (g.Missions.Count > 0 || !w.StopUtc.HasValue) groups.Add(g);   // a running session is shown from the start, before its first mission
        }
        return groups.OrderBy(g => g.Start).ToList();
    }

    /// <summary>
    /// The time a session really took: the stretches between activity, but not the long breaks (more than an hour with nothing happening).
    /// So a manual session that goes on over several days does not count the nights. runningUntil = now while the session is running.
    /// </summary>
    public static TimeSpan ActiveTime(SessionGroup g, DateTime? runningUntil)
    {
        var points = new List<DateTime> { g.Start };
        void See(DateTime? t)
        {
            if (t.HasValue && t.Value >= g.Start) points.Add(t.Value);
        }
        foreach (var m in g.Missions)
        {
            See(m.Accepted);
            See(m.FirstSeen);
            See(m.EndedAt);
            foreach (var list in m.Events.Values)
            {
                foreach (var e in list) See(e.Time);
            }
        }
        if (g.Stop.HasValue) points.Add(g.Stop.Value);
        else if (runningUntil.HasValue) points.Add(runningUntil.Value);
        points.Sort();

        var total = TimeSpan.Zero;
        for (var i = 1; i < points.Count; i++)
        {
            var gap = points[i] - points[i - 1];
            if (gap <= ClockIdleLimit) total += gap;
        }
        return total;
    }

    /// <summary>
    /// The latest thing that happened in the session: a pickup, a hand-in, a finished or newly accepted mission.
    /// It keeps moving while you keep hauling, also when the missions you accepted last are not finished yet.
    /// </summary>
    static DateTime? LastActivity(SessionGroup g)
    {
        DateTime? last = null;
        void See(DateTime? t)
        {
            if (t.HasValue && (!last.HasValue || t.Value > last.Value)) last = t;
        }
        foreach (var m in g.Missions)
        {
            See(m.Accepted);
            See(m.FirstSeen);
            See(m.EndedAt);
            foreach (var list in m.Events.Values)
            {
                foreach (var e in list) See(e.Time);
            }
        }
        return last;
    }

    public static SessionSummary Summarize(SessionGroup g)
    {
        var done = g.Missions.Where(m => m.Completed && m.EndedAt.HasValue).ToList();
        return new SessionSummary
        {
            Start = g.Start,
            End = g.Stop ?? LastActivity(g),
            Completed = done.Count,
            Active = g.Missions.Count(m => !m.Completed),
            Total = g.Missions.Count,
        };
    }

    /// <summary>Times the drop-off objective of a mission was updated (hand-ins at Ruin Station).</summary>
    static List<DateTime> HandIns(Mission m)
    {
        return m.Events
            .Where(kv => IsDrop(kv.Key))
            .SelectMany(kv => kv.Value)
            .Select(e => e.Time)
            .OrderBy(t => t)
            .ToList();
    }

    /// <summary>
    /// state: loaded (log says completed), loaded* (mission finished so it must be aboard),
    /// partial (some cargo picked up), pending (nothing logged yet).
    /// </summary>
    static (string State, DateTime? Last, DateTime? Delivered) StopInfo(Mission m, string objective)
    {
        m.Events.TryGetValue(objective, out var events);
        events ??= new List<ObjEvent>();

        var hasComplete = events.Any(e => e.State == "COMPLETED");
        var hasProgress = events.Any(e => e.State == "INPROGRESS");
        DateTime? last = events.Count > 0 ? events.Max(e => e.Time) : (DateTime?)null;

        // The game's "partial" signal is not reliable, so any pickup signal counts the stop as picked up.
        string state;
        if (hasComplete || hasProgress) state = "loaded";
        else if (m.Completed) state = "loaded*";
        else state = "pending";

        // The cargo was handed in at the first Ruin hand-in after the last pickup here.
        DateTime? delivered = null;
        if (last.HasValue)
        {
            foreach (var h in HandIns(m))
            {
                if (h > last.Value)
                {
                    delivered = h;
                    break;
                }
            }
        }
        if (delivered == null && m.Completed) delivered = m.EndedAt;

        return (state, last, delivered);
    }

    /// <summary>The pickup locations of the session. openOnly leaves out the missions that are already completed.</summary>
    public static List<LocationRow> Locations(SessionGroup g, bool openOnly = false)
    {
        var zones = new Dictionary<string, List<(Mission Mission, string Objective)>>();
        foreach (var m in g.Missions.Where(m => !openOnly || !m.Completed))
        {
            foreach (var kv in m.ObjZone)
            {
                if (!IsStop(kv.Key)) continue;
                if (!zones.TryGetValue(kv.Value, out var list))
                {
                    list = new List<(Mission, string)>();
                    zones[kv.Value] = list;
                }
                list.Add((m, kv.Key));
            }
        }

        var rows = new List<LocationRow>();
        foreach (var kv in zones)
        {
            var infos = kv.Value.Select(i => StopInfo(i.Mission, i.Objective)).ToList();
            var lasts = infos.Where(i => i.Last.HasValue).Select(i => i.Last!.Value).ToList();
            var delivered = infos.Where(i => i.Delivered.HasValue).Select(i => i.Delivered!.Value).ToList();

            rows.Add(new LocationRow
            {
                Zone = kv.Key,
                Missions = infos.Count,
                Stops2 = kv.Value.Count(i => i.Mission.Multi == 2),
                Stops3 = kv.Value.Count(i => i.Mission.Multi == 3),
                Stops4 = kv.Value.Count(i => i.Mission.Multi == 4),
                Loaded = infos.Count(i => i.State.StartsWith("loaded")),
                Partial = infos.Count(i => i.State == "partial"),
                Delivered = delivered.Count,
                LoadedTime = lasts.Count > 0 ? lasts.Max() : (DateTime?)null,
                DeliveredTime = delivered.Count == infos.Count && delivered.Count > 0 ? delivered.Max() : (DateTime?)null,
            });
        }

        return rows
            .OrderByDescending(r => r.Missions)
            .ThenBy(r => r.Zone, StringComparer.Ordinal)
            .ToList();
    }
}
