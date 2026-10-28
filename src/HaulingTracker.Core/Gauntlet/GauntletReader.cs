using System.Globalization;
using System.Text.RegularExpressions;

namespace HaulingTracker.Gauntlet;

/// <summary>
/// Reads the lines of one player's Game.log for Gilly's Combat Gauntlet missions and makes a GauntletRun of each one that ends. The first accept of a mission
/// counts (the game sends the notification again, for instance after a respawn). A mission that is completed is read from the "Contract Complete" notification;
/// one that is abandoned or failed from the EndMission line, but only when it was accepted here (a mission of the other account leaves nothing). Every mission
/// counts once.
/// </summary>
public sealed class GauntletReader
{
    static readonly Regex Accept = new(
        "^<(?<ts>[^>]+)> \\[Notice\\] <SHUDEvent_OnNotification> Added notification \"Contract Accepted:\\s+Combat Gauntlet - Scenario #(?<n>\\d+)[^\"]*\".*?MissionId: \\[(?<id>[0-9a-fA-F-]+)\\]",
        RegexOptions.Compiled);

    static readonly Regex Done = new(
        "^<(?<ts>[^>]+)> \\[Notice\\] <SHUDEvent_OnNotification> Added notification \"Contract Complete:\\s+Combat Gauntlet - Scenario #(?<n>\\d+)[^\"]*\".*?MissionId: \\[(?<id>[0-9a-fA-F-]+)\\]",
        RegexOptions.Compiled);

    static readonly Regex End = new(
        "^<(?<ts>[^>]+)> \\[Notice\\] <EndMission> Ending mission for player\\. MissionId\\[(?<id>[0-9a-fA-F-]+)\\].*?CompletionType\\[(?<ct>[A-Za-z]+)\\]",
        RegexOptions.Compiled);

    readonly Dictionary<string, (int Scenario, DateTime At)> _pending = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _done = new(StringComparer.OrdinalIgnoreCase);
    readonly List<GauntletRun> _runs = new();

    /// <summary>The missions that ended, in the order of the log.</summary>
    public IReadOnlyList<GauntletRun> Runs => _runs;

    public void ProcessLine(string line)
    {
        if (line.Length < 30 || line[0] != '<') return;
        if (line.Contains("Combat Gauntlet", StringComparison.Ordinal))
        {
            var a = Accept.Match(line);
            if (a.Success)
            {
                var id = a.Groups["id"].Value;
                if (!_done.Contains(id) && Time(a) is { } at) _pending.TryAdd(id, (int.Parse(a.Groups["n"].Value, CultureInfo.InvariantCulture), at));
                return;
            }
            var d = Done.Match(line);
            if (d.Success)
            {
                var id = d.Groups["id"].Value;
                if (Time(d) is not { } at || !_done.Add(id)) return;
                DateTime? accepted = null;
                var scenario = int.Parse(d.Groups["n"].Value, CultureInfo.InvariantCulture);
                if (_pending.Remove(id, out var p)) accepted = p.At;
                _runs.Add(new GauntletRun(id, scenario, accepted, at, GauntletOutcome.Complete));
            }
            return;
        }
        if (!line.Contains("<EndMission>", StringComparison.Ordinal)) return;
        var e = End.Match(line);
        if (!e.Success) return;
        var type = e.Groups["ct"].Value;
        var endId = e.Groups["id"].Value;
        if (type.Equals("Complete", StringComparison.OrdinalIgnoreCase) || _done.Contains(endId)) return;
        if (!_pending.Remove(endId, out var pending) || Time(e) is not { } end) return;
        var outcome = type.Equals("Fail", StringComparison.OrdinalIgnoreCase) ? GauntletOutcome.Fail : GauntletOutcome.Abandon;
        _done.Add(endId);
        _runs.Add(new GauntletRun(endId, pending.Scenario, pending.At, end, outcome));
    }

    static DateTime? Time(Match m) =>
        DateTime.TryParse(m.Groups["ts"].Value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t) ? t : null;
}
