using System.Text.RegularExpressions;

namespace SCTools.Missions;

/// <summary>
/// What the Mission Library groups the missions by (their kind). The same mission at another place has another ContractId in the log, but the same name, so a
/// mission is grouped by its name as the game shows it. The missions the Hauling Planner reads are grouped by its contract line instead ("2 To Ruin",
/// "Direct From Ruin"): that is what decides what they pay (a direct one is named after where it goes).
/// </summary>
public static class MissionKinds
{
    public const string HaulingPrefix = "hauling:";

    /// <summary>The key of a mission's kind: its contract line when the Hauling Planner reads it, else its name; null while it has no name.</summary>
    public static string? KeyOf(string? title, string? hauling) => hauling != null ? HaulingKey(hauling) : TitleKey(title);

    public static string HaulingKey(string contract) => HaulingPrefix + contract.Trim().ToLowerInvariant();

    static readonly Regex Bounty = new(@"^(?<kind>[^:|]*\bBounty):\s*(?<target>.*?)(?:\s*\|\s*(?<risk>.+))?$", RegexOptions.Compiled);

    /// <summary>
    /// The name of the kind a mission's name is: the name itself, but a bounty by its kind and risk, not by its target, because that is what it pays by
    /// ("Reclusive Bounty: Kafu "Vaco" Dudley | Extreme Risk [300 Rep]" is "Reclusive Bounty | Extreme Risk [300 Rep]", and so is the one whose target the
    /// game had not filled in yet). Null while it has no name.
    /// </summary>
    public static string? KindTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        var m = Bounty.Match(title.Trim());
        if (!m.Success) return title.Trim();
        return m.Groups["risk"].Success ? m.Groups["kind"].Value.Trim() + " | " + m.Groups["risk"].Value.Trim() : m.Groups["kind"].Value.Trim();
    }

    /// <summary>A name as a key: the name of its kind (KindTitle), the same letters in any case, any run of spaces as one.</summary>
    public static string? TitleKey(string? title)
    {
        title = KindTitle(title);
        if (string.IsNullOrWhiteSpace(title)) return null;
        return string.Join(' ', title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
    }

    public static bool IsHauling(string key) => key.StartsWith(HaulingPrefix, StringComparison.Ordinal);

    /// <summary>The name of a kind of the Hauling Planner: "Hauling: 2 To Ruin".</summary>
    public static string HaulingTitle(string contract) => "Hauling: " + contract;

    /// <summary>The contract line of a mission the Hauling Planner reads (a hauling contract to Ruin, a from-Ruin one), or null for any other mission.</summary>
    public static string? HaulingLine(Mission m) => (m.IsHauling && m.Multi > 0) || m.IsOut ? Contracts.ContractName(m) : null;

    /// <summary>The missions, those the Hauling Planner reads with their contract line (from the same log, read by the HaulingLog).</summary>
    public static IReadOnlyList<MissionSeen> WithHauling(IReadOnlyList<MissionSeen> missions, HaulingLog log)
    {
        if (log.Missions.Count == 0) return missions;
        var byId = new Dictionary<string, Mission>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in log.Missions) byId[kv.Key] = kv.Value;
        return missions.Select(m => byId.TryGetValue(m.MissionId, out var h) && HaulingLine(h) is { } line ? m with { Hauling = line } : m).ToList();
    }

    /// <summary>A name the game had not filled in yet ("Stop Rival Attack at ~mission(Location)").</summary>
    public static bool Unfilled(string? title) => title != null && title.Contains("~mission(", StringComparison.Ordinal);

    /// <summary>Of two names of one mission: the first, unless only the other one is filled in.</summary>
    public static string? Better(string? first, string? other) => first == null || (Unfilled(first) && other != null && !Unfilled(other)) ? other ?? first : first;
}
