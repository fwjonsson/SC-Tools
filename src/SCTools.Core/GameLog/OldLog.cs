using SCTools.Gauntlet;
using SCTools.Missions;

namespace SCTools.GameLog;

/// <summary>
/// What the server reads from an old log a tracker sent (HistorySender): whose game it was, and what the tools that keep everything want from it. Read on
/// its own, apart from the live logs.
/// </summary>
public sealed record OldLog(string? Player, IReadOnlyList<GauntletRun> GauntletRuns, IReadOnlyList<MissionSeen> Missions, DateTime? FirstLine, DateTime? LastLine)
{
    public static OldLog Read(IEnumerable<string> lines)
    {
        var log = new HaulingLog();
        var gauntlet = new GauntletReader();
        var missions = new MissionReader();
        DateTime? first = null, last = null;
        foreach (var line in lines)
        {
            log.ProcessLine(line);
            gauntlet.ProcessLine(line);
            missions.ProcessLine(line);
            if (HaulingLog.TimeOf(line) is { } t)
            {
                first ??= t;
                last = t;
            }
        }
        return new OldLog(log.Player, gauntlet.Runs.ToList(), missions.Missions, first, last);
    }
}
