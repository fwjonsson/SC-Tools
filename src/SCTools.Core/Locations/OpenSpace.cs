namespace SCTools.Locations;

/// <summary>
/// Which of the game's areas (the numbers of the area lines, AreaChange) are a system's open space: the area a ship flies its quantum jumps in. A jump
/// picked inside any other area (a station's hangar or area, a mission's site, the area around a planet) starts when he leaves into open space, however
/// long he lingered after picking (Frank, 31 Oct 2026: the map started his jump from an asteroid base ten seconds after the pick, before he had left it),
/// and the line out of open space into an area is coming in where he jumped to. In the three accounts' logs of 4 Aug to 31 Oct 2026 a quantum arrival
/// followed a line out of these within a minute 25 to 46% of the time; out of any other area with a hundred lines or more 18% at most (the areas around
/// Stanton's planets). Learned the same way from the logs on the server (at least 22% of at least a hundred lines out of it, and not a station's own area:
/// a gateway's area and a mission's encounter site the route passes through come close with fewer lines), so a new system (Castra, Magnus) or the Nyx
/// update of late 2026 is picked up. The numbers are the same in every account's log.
/// </summary>
public static class OpenSpace
{
    /// <summary>The open space of each system the logs show.</summary>
    public static readonly IReadOnlySet<long> Known = new HashSet<long>
    {
        3184339241,   // Pyro: 648 of 1,411 lines out of it followed by the arrival within a minute
        1902223495,   // Stanton: 494 of 2,010
        2627827560,   // Nyx: 133 of 457
    };

    /// <summary>A quantum arrival this soon after a line out of an area: he flew the jump in that area.</summary>
    public static readonly TimeSpan Arriving = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The open spaces: the known ones, and the ones these logs show by the same rule (left shortly before a quantum arrival, often enough), never one of
    /// the station areas (StationAreas.Learn).
    /// </summary>
    public static HashSet<long> Learn(IEnumerable<HaulingLog> logs, IReadOnlySet<long>? stations = null)
    {
        var space = new HashSet<long>(Known);
        var arrived = new Dictionary<long, int>();
        var all = new Dictionary<long, int>();
        foreach (var log in logs)
        {
            var steps = log.Areas.Where(a => !a.Landed).Select(a => (a.Time, Area: (AreaChange?)a))
                .Concat(log.Qt.Where(e => e.Kind == QtKind.Arrived).Select(e => (e.Time, Area: (AreaChange?)null)))
                .OrderBy(s => s.Time).ToList();
            for (var i = 0; i < steps.Count; i++)
            {
                if (steps[i].Area is not { } a) continue;
                all[a.From] = all.GetValueOrDefault(a.From) + 1;
                if (i + 1 < steps.Count && steps[i + 1].Area == null && steps[i + 1].Time - a.Time <= Arriving)
                    arrived[a.From] = arrived.GetValueOrDefault(a.From) + 1;
            }
        }
        foreach (var (area, n) in all)
            if (n >= 100 && arrived.GetValueOrDefault(area) * 100 >= n * 22 && !(stations ?? StationAreas.Known).Contains(area)) space.Add(area);
        return space;
    }
}
