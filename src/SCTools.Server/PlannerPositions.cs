namespace SCTools.Server;

/// <summary>
/// Gives the Hauling Planner where the map has everybody (TrackerState.Positions): every few seconds (RefreshService) the map's places for every player
/// playing, as the planner's stations (MapService.PlannerPositions). The planner takes them where they are newer than its own pickups and deliveries
/// (Whereabouts), so a crew member's jumps (in the pilot's log), a destination or an area the admin named on Map places and a mission's objective place him
/// for the plan too (Frank's list of 29 Oct 2026: the map and the planner learn from each other; the other way, the planner's pickups and deliveries, the
/// map takes in its Snapshot). Only a change reaches the tracker and makes the plan anew, and the next data.json shows it (LiveTracker.Refresh).
/// </summary>
public sealed class PlannerPositions
{
    /// <summary>How often the map's places are looked at.</summary>
    public static readonly TimeSpan Every = TimeSpan.FromSeconds(6);

    readonly LiveTracker _live;
    readonly MapService _map;
    Dictionary<string, WorkerPosition> _last = new(StringComparer.OrdinalIgnoreCase);
    DateTime _at = DateTime.MinValue;

    public PlannerPositions(LiveTracker live, MapService map)
    {
        _live = live;
        _map = map;
    }

    /// <summary>Looks at the map's places (every six seconds, or now with force) and gives them to the planner when they changed. True when they did.</summary>
    public bool Push(DateTime nowUtc, bool force = false)
    {
        if (!force && nowUtc - _at < Every) return false;
        _at = nowUtc;
        var now = _map.PlannerPositions(nowUtc);
        if (now.Count == _last.Count && now.All(kv => _last.TryGetValue(kv.Key, out var had) && had == kv.Value)) return false;
        _last = now;
        lock (_live.Uploads.Sync)
        {
            _live.Tracker.State.Positions = now;
            foreach (var plan in _live.Tracker.Plans) plan.Invalidate();   // the plan starts where the map has him
        }
        return true;
    }
}
