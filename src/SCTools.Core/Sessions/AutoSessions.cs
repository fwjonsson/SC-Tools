namespace SCTools.Sessions;

/// <summary>
/// The sessions that start and stop by themselves (the Hauling Planner's, the Gauntlet Tracker's grind sessions): a session starts when a mission of the
/// tool is accepted, and goes on while something happens with its missions (accepted, completed, failed, abandoned; for hauling also the pickups and drop-offs).
/// After an hour with nothing (also when the game crashed or the tracker was off), it ended 5 minutes after the last thing that happened.
/// </summary>
public static class AutoSessions
{
    /// <summary>This long without anything happening ends a session.</summary>
    public static readonly TimeSpan Quiet = TimeSpan.FromHours(1);

    /// <summary>A session that ended by itself ends this long after the last thing that happened in it.</summary>
    public static readonly TimeSpan Tail = TimeSpan.FromMinutes(5);

    /// <summary>A session: when it started, when it stopped (null: it runs), and the last thing that happened in it.</summary>
    public sealed record Window(DateTime Start, DateTime? Stop, DateTime LastActivity);

    /// <summary>The last thing that happened in a session that started then: the activity after it, as long as no hour goes by without any.</summary>
    public static DateTime LastActivity(DateTime start, IReadOnlyList<DateTime> sortedActivity)
    {
        var last = start;
        foreach (var t in sortedActivity)
        {
            if (t < start) continue;
            if (t - last >= Quiet) break;
            if (t > last) last = t;
        }
        return last;
    }

    /// <summary>When a session whose last activity was then stops: 5 minutes after it once an hour has gone by, else it runs (null).</summary>
    public static DateTime? StopOf(DateTime lastActivity, DateTime nowUtc) => nowUtc - lastActivity >= Quiet ? lastActivity + Tail : null;

    /// <summary>
    /// The sessions in these times, oldest first: one starts at a start (a mission accepted) that is not in the one before, and lasts as long as the activity
    /// goes on (LastActivity). Only the last one can be running.
    /// </summary>
    /// <param name="starts">when a mission of the tool was accepted</param>
    /// <param name="activity">everything that happened with the missions of the tool (the starts may be in it or not)</param>
    public static List<Window> Find(IEnumerable<DateTime> starts, IEnumerable<DateTime> activity, DateTime nowUtc)
    {
        var begins = starts.Distinct().Order().ToList();
        var all = activity.Concat(begins).Distinct().Order().ToList();
        var found = new List<Window>();
        foreach (var s in begins)
        {
            if (found.Count > 0 && s <= found[^1].LastActivity) continue;   // in the session before
            var last = LastActivity(s, all);
            var stop = StopOf(last, nowUtc);
            found.Add(new Window(s, stop, last));
            if (stop == null) break;
        }
        return found;
    }

    /// <summary>
    /// The Hauling Planner's sessions (kept in AppData.ManualSessions, so a session keeps its key and what was typed for it): the last one stops when an hour
    /// went by without anything (or goes on again, when a log that came late shows it did not end), and new ones start with the missions accepted after it.
    /// A session stopped by hand stays as it is. True when anything changed.
    /// </summary>
    public static bool Apply(List<ManualSession> sessions, IEnumerable<DateTime> starts, IEnumerable<DateTime> activity, DateTime nowUtc)
    {
        var begins = starts.ToList();
        var all = activity.Concat(begins).Distinct().Order().ToList();
        var changed = false;
        var after = DateTime.MinValue;
        var last = sessions.LastOrDefault();
        if (last != null)
        {
            if (last.StopUtc == null || last.Auto)
            {
                // a session that stopped by itself goes on from its last activity (never less: the logs of its start may be gone from the server by now)
                var from = last.Auto && last.StopUtc is { } had ? had - Tail : last.StartUtc;
                var lastActivity = LastActivity(from, all);
                var stop = StopOf(lastActivity, nowUtc);
                if (last.StopUtc != stop || !last.Auto)
                {
                    last.StopUtc = stop;
                    last.Auto = true;
                    changed = true;
                }
                if (stop == null) return changed;   // it runs: everything new is in it
                after = lastActivity;
            }
            else
            {
                after = last.StopUtc.Value;
            }
        }
        foreach (var w in Find(begins.Where(t => t > after), all.Where(t => t > after), nowUtc))
        {
            sessions.Add(new ManualSession { StartUtc = w.Start, StopUtc = w.Stop, Auto = true });
            changed = true;
        }
        return changed;
    }

    /// <summary>
    /// What happened in the log with the missions the Hauling Planner reads (to and from Ruin): when each was accepted (the starts; a mission shared to you
    /// when you first saw it), and everything (the starts, the pickups and drop-offs, the ends).
    /// </summary>
    public static (List<DateTime> Starts, List<DateTime> Activity) HaulingActivity(HaulingLog log)
    {
        var starts = new List<DateTime>();
        var activity = new List<DateTime>();
        foreach (var m in log.Missions.Values)
        {
            if (!m.IsHauling && !m.IsOut) continue;
            if ((m.Accepted ?? m.FirstSeen) is { } start)
            {
                starts.Add(start);
                activity.Add(start);
            }
            if (m.EndedAt is { } end) activity.Add(end);
            foreach (var list in m.Events.Values)
                foreach (var e in list) activity.Add(e.Time);
        }
        return (starts, activity);
    }
}
