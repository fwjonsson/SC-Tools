using System.Text.RegularExpressions;

namespace HaulingTracker;

/// <summary>A group of hauling missions that belong to the same hauling session.</summary>
public sealed class SessionGroup
{
    public List<Mission> Missions = new();
    public DateTime Start;      // UTC
    public DateTime End;        // UTC, used for grouping only

    /// <summary>Stable id for saving your own notes: the id of the earliest mission.</summary>
    public string Key => Missions.Count > 0 ? Missions[0].Id : "";
}

public sealed class SessionSummary
{
    public DateTime Start;      // UTC
    public DateTime? End;       // UTC, time of the last completed mission
    public int Completed;
    public int Total;
}

/// <summary>One pickup location with every mission that has cargo there.</summary>
public sealed class LocationRow
{
    public string Zone = "";
    public int Missions;        // missions with a stop here
    public int Stops2;          // of those, stops that belong to 2 stop missions
    public int Stops3;          // of those, stops that belong to 3 stop missions
    public int Loaded;          // stops fully picked up
    public int Partial;         // stops where only part is picked up so far
    public int Delivered;       // stops whose cargo has been handed in at Ruin
    public DateTime? LoadedTime;     // latest pickup event here (UTC)
    public DateTime? DeliveredTime;  // set when every stop here has been handed in (UTC)
}

public static class SessionBuilder
{
    static readonly TimeSpan Gap = TimeSpan.FromMinutes(30);
    static readonly Regex StopId = new(@"_\d+$", RegexOptions.Compiled);

    static bool IsStop(string objective) => objective.StartsWith("pickup_") && StopId.IsMatch(objective);
    static bool IsDrop(string objective) => objective.StartsWith("dropoff_");

    static DateTime MissionStart(Mission m) => m.Accepted ?? m.FirstSeen ?? DateTime.MinValue;

    static DateTime MissionEnd(Mission m, DateTime lastLogTime)
    {
        if (m.EndedAt.HasValue) return m.EndedAt.Value;
        var times = m.Events.Values.SelectMany(l => l).Select(e => e.Time).ToList();
        return times.Count > 0 ? times.Max() : lastLogTime;
    }

    /// <summary>Missions that overlap in time (with a 30 minute gap allowed) form one session.</summary>
    public static List<SessionGroup> Build(HaulingLog log)
    {
        var groups = new List<SessionGroup>();
        var missions = log.Missions.Values
            .Where(m => m.IsHauling && !m.Dead)
            .OrderBy(MissionStart)
            .ToList();

        foreach (var m in missions)
        {
            var start = MissionStart(m);
            var end = MissionEnd(m, log.LastTime);
            if (groups.Count > 0 && start <= groups[^1].End + Gap)
            {
                var g = groups[^1];
                g.Missions.Add(m);
                if (end > g.End) g.End = end;
            }
            else
            {
                var g = new SessionGroup { Start = start, End = end };
                g.Missions.Add(m);
                groups.Add(g);
            }
        }
        return groups;
    }

    public static SessionSummary Summarize(SessionGroup g)
    {
        var done = g.Missions.Where(m => m.Completed && m.EndedAt.HasValue).ToList();
        return new SessionSummary
        {
            Start = g.Start,
            End = done.Count > 0 ? done.Max(m => m.EndedAt!.Value) : (DateTime?)null,
            Completed = done.Count,
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

    public static List<LocationRow> Locations(SessionGroup g)
    {
        var zones = new Dictionary<string, List<(Mission Mission, string Objective)>>();
        foreach (var m in g.Missions)
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
