namespace SCTools.Locations;

/// <summary>
/// Which of the game's areas (the numbers of the area lines, AreaChange) are a station's own area: the space around a station where its pads let out, which a
/// player leaves a few seconds later into open space. At most stations taking off is two lines: off the pad into the station's area, then out of it into
/// space; only the second is leaving (Frank, 31 Oct 2026: "no one really flies out from a station's area without quantum jumping"). At some places (Levski,
/// Orbituary, the Pyro Gateway, Everus Harbor) the pad lets out straight into open space: that one line is leaving. The numbers name nothing, so they are
/// learned: an area a pad lets out into and that the player leaves again within half a minute, in at least three of every ten take-offs, is a station's
/// own area. Learned from the three accounts' logs of 4 Aug to 31 Oct 2026 (the numbers are the same in every account's log) and from the logs on the
/// server. The big Nyx update of late 2026 may change the areas (Frank).
/// </summary>
public static class StationAreas
{
    /// <summary>A player who leaves the area a pad let him out into this soon after it was in a station's area.</summary>
    public static readonly TimeSpan Leaving = TimeSpan.FromSeconds(30);

    /// <summary>The station areas of the three accounts' logs (4 Aug to 31 Oct 2026): the area, and in how many of the take-offs into it he left it soon.</summary>
    public static readonly IReadOnlySet<long> Known = new HashSet<long>
    {
        1299866756,   // Pyro III's station in low orbit (RR_P3_LEO), 179 of 233
        2537229937,   // CRU-L1 Ambitious Dream Station (RR_CRU_L1), 144 of 155
        694522860,    // the Pyro Gateway in Stanton (RR_JP_StantonPyro), 103 of 219
        48919369,     // the Pyro Gateway in Nyx (RR_JP_NyxPyro), 79 of 138
        685899972,    // Ruin Station (RR_P6_LEO), 74 of 104
        3273347588,   // the Stanton Gateway in Pyro (RR_JP_PyroStanton), 64 of 150
        1037384677,   // the Castra Gateway in Nyx (RR_JP_NyxCastra), 62 of 103
        3688917516,   // RR_P5_L5, 45 of 48
        1022018936,   // the Magnus Gateway in Stanton (RR_JP_StantonMagnus), 41 of 124
        125532322,    // the Nyx Gateway in Pyro (RR_JP_PyroNyx), 40 of 113
        2790741709,   // RR_P3_L1, 37 of 39
        3336767377,   // RR_P2_L4, 27 of 28
        2963822188,   // MIC-L2 (RR_MIC_L2), 23 of 26
        1719129565,   // Gaslight (RR_P5_L2), 19 of 20
        1041448922,   // Baijini Point or Area18 (RR_ARC_LEO), 19 of 39
        2975175238,   // Patch City (RR_P3_L3), 19 of 19
        1459050291,   // HUR-L2 (RR_HUR_L2), 17 of 21
        765500909,    // ARC-L1 (RR_ARC_L1), 17 of 17
        3377612392,   // MIC-L1 (RR_MIC_L1), 16 of 24
        1793578189,   // RR_P6_L3, 16 of 17
        42202597,     // RR_P6_L4, 14 of 14
        3618949553,   // Megumi Refueling (RR_P6_L5), 14 of 14
        4084189103,   // RR_P5_L4, 12 of 13
        2872784941,   // ARC-L2 (RR_ARC_L2), 8 of 8
        3869838109,   // CRU-L1 (RR_CRU_L1), 4 of 11
        293099511,    // ARC-L5 (RR_ARC_L5), 3 of 3
        3092374405,   // CRU-L4 (RR_CRU_L4), 3 of 4
    };

    /// <summary>Is this area line taking off: off the pad (the area he leaves is the pad he was on)?</summary>
    public static bool OffPad(AreaChange a) => !a.Landed && a.From == a.LandingFrom;

    /// <summary>The station areas: the known ones, and the ones these logs show by the same rule (an area a pad lets out into, left again soon).</summary>
    public static HashSet<long> Learn(IEnumerable<HaulingLog> logs)
    {
        var areas = new HashSet<long>(Known);
        var soon = new Dictionary<long, int>();
        var all = new Dictionary<long, int>();
        foreach (var log in logs)
        {
            var lines = log.Areas.OrderBy(a => a.Time).ToList();
            for (var i = 0; i < lines.Count; i++)
            {
                if (!OffPad(lines[i])) continue;
                var into = lines[i].To;
                all[into] = all.GetValueOrDefault(into) + 1;
                if (i + 1 < lines.Count && lines[i + 1] is { Landed: false } next && next.From == into && next.Time - lines[i].Time <= Leaving)
                    soon[into] = soon.GetValueOrDefault(into) + 1;
            }
        }
        foreach (var (area, n) in soon)
            if (n >= 3 && n * 10 >= all[area] * 3) areas.Add(area);
        return areas;
    }
}
