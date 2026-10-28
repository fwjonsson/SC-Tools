using System.Text.RegularExpressions;

namespace SCTools.Runs;

public static class TripBuilder
{
    static readonly TimeSpan HandInGap = TimeSpan.FromMinutes(3);
    static readonly Regex StopId = new(@"_\d+$", RegexOptions.Compiled);

    /// <summary>Every hand-in at Ruin ends a run, also a partial one.</summary>
    public static List<Trip> Build(SessionGroup g)
    {
        // all pickup events (zone, mission, time) and all hand-in events (mission, time)
        var pickups = new List<(string Zone, string Mission, DateTime Time)>();
        var drops = new List<(string Mission, DateTime Time)>();
        foreach (var m in g.Missions)
        {
            foreach (var kv in m.Events)
            {
                if (kv.Key.StartsWith("dropoff_"))
                {
                    foreach (var e in kv.Value) drops.Add((m.Id, e.Time));
                }
                else if (kv.Key.StartsWith("pickup_") && StopId.IsMatch(kv.Key) && m.ObjZone.TryGetValue(kv.Key, out var zone))
                {
                    foreach (var e in kv.Value) pickups.Add((zone, m.Id, e.Time));
                }
            }
        }
        drops.Sort((a, b) => a.Time.CompareTo(b.Time));

        // hand-ins within a few minutes of each other (several missions in a row) are one hand-in
        var ends = new List<(DateTime Time, HashSet<string> Missions)>();
        foreach (var d in drops)
        {
            if (ends.Count > 0 && d.Time - ends[^1].Time <= HandInGap)
            {
                ends[^1].Missions.Add(d.Mission);
                ends[^1] = (d.Time, ends[^1].Missions);
            }
            else
            {
                ends.Add((d.Time, new HashSet<string> { d.Mission }));
            }
        }

        var trips = new List<Trip>();
        var start = g.Missions.Select(m => m.Accepted ?? m.FirstSeen ?? g.Start).DefaultIfEmpty(g.Start).Min();
        for (var i = 0; i <= ends.Count; i++)
        {
            DateTime? end = i < ends.Count ? ends[i].Time : (DateTime?)null;
            var trip = new Trip { Number = i + 1, Start = start, End = end, HandedIn = i < ends.Count ? ends[i].Missions.Count : 0 };
            var inRun = pickups.Where(p => p.Time > start && (end == null || p.Time <= end.Value));
            foreach (var grp in inRun.GroupBy(p => p.Zone))
            {
                trip.Pickups.Add(new TripPickup
                {
                    Zone = grp.Key,
                    Missions = grp.Select(p => p.Mission).Distinct().Count(),
                    Time = grp.Max(p => p.Time),
                });
            }
            trip.Pickups.Sort((a, b) => a.Time.CompareTo(b.Time));

            if (end == null && trip.Pickups.Count == 0) break;   // nothing going on after the last hand-in
            trips.Add(trip);
            if (end.HasValue) start = end.Value;
        }
        return trips;
    }
}
