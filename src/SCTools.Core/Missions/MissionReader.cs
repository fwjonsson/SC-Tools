using System.Globalization;
using System.Text.RegularExpressions;

namespace SCTools.Missions;

/// <summary>How a mission ended for the player (null while it is going).</summary>
public enum MissionOutcome { Complete, Fail, Abandon }

/// <summary>
/// One mission of one player, as far as his log shows it: the game's ContractId (the offer it came from), its title as the game showed it (with Frank's
/// string mod, his names), whether it was shared to him, when it started and how it ended. Hauling: the Hauling Planner's contract line of a mission it reads
/// ("2 To Ruin", MissionKinds.WithHauling). The Mission Library groups them by their kind (MissionKinds).
/// </summary>
public sealed record MissionSeen(string MissionId, string? ContractId, string? Title, bool Shared, DateTime? StartedUtc, DateTime? EndedUtc, MissionOutcome? Outcome,
    string? Hauling = null);

/// <summary>
/// Reads every mission of a player's Game.log (any kind, not only hauling): "ContractId: [c]. MissionId: m" says which offer it is, the notifications
/// (Contract Accepted / Shared / Complete / Failed) its title and how it went, EndMission how it ended otherwise (CompletionType Abandon or Fail). The first
/// accept or share counts; a mission counts once.
/// </summary>
public sealed class MissionReader
{
    static readonly Regex Contract = new(@"ContractId: \[(?<c>[0-9a-fA-F-]{36})\]\.? MissionId: \[?(?<m>[0-9a-fA-F-]{36})", RegexOptions.Compiled);

    static readonly Regex Note = new(
        "^<(?<ts>[^>]+)> \\[Notice\\] <SHUDEvent_OnNotification> Added notification \"Contract (?<k>Accepted|Shared|Complete|Failed):\\s*(?<t>[^\"]*?)\\s*\" \\[\\d+\\].*?MissionId: \\[(?<m>[0-9a-fA-F-]{36})\\]",
        RegexOptions.Compiled);

    static readonly Regex End = new(
        "^<(?<ts>[^>]+)> \\[Notice\\] <EndMission> Ending mission for player\\. MissionId\\[(?<m>[0-9a-fA-F-]{36})\\].*?CompletionType\\[(?<ct>[A-Za-z]+)\\]",
        RegexOptions.Compiled);

    static readonly Regex Markup = new(@"</?EM\d*>", RegexOptions.Compiled);

    sealed class State
    {
        public string? ContractId;
        public string? Title;
        public bool Shared;
        public DateTime? Started;
        public DateTime? Ended;
        public MissionOutcome? Outcome;
        public int Order;
    }

    readonly Dictionary<string, State> _missions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every mission seen so far, in the order they first showed up.</summary>
    public IReadOnlyList<MissionSeen> Missions =>
        _missions.OrderBy(m => m.Value.Order)
            .Select(m => new MissionSeen(m.Key.ToLowerInvariant(), m.Value.ContractId, m.Value.Title, m.Value.Shared, m.Value.Started, m.Value.Ended, m.Value.Outcome))
            .ToList();

    State Of(string id) => _missions.TryGetValue(id, out var s) ? s : _missions[id] = new State { Order = _missions.Count };

    /// <summary>The title as the mission list shows it: without the game's markup (&lt;EM4&gt;...&lt;/EM4&gt;) and the colon at its end.</summary>
    public static string CleanTitle(string title) => Markup.Replace(title, "").Trim().TrimEnd(':').Trim();

    public void ProcessLine(string line)
    {
        if (line.Length < 30 || line[0] != '<' || !line.Contains("Mission", StringComparison.Ordinal)) return;
        if (line.Contains("ContractId", StringComparison.Ordinal))
        {
            var c = Contract.Match(line);
            if (c.Success) Of(c.Groups["m"].Value).ContractId ??= c.Groups["c"].Value.ToLowerInvariant();
            return;
        }
        if (line.Contains("Added notification \"Contract ", StringComparison.Ordinal))
        {
            var n = Note.Match(line);
            if (!n.Success || Time(n) is not { } at) return;
            var s = Of(n.Groups["m"].Value);
            var title = CleanTitle(n.Groups["t"].Value);
            // the first name counts (a share the game did not name yet has none), unless the game had not filled it in ("at ~mission(Location)") and this one is
            if (title.Length > 0 && !title.StartsWith("~mission(", StringComparison.Ordinal)) s.Title = MissionKinds.Better(s.Title, title);
            switch (n.Groups["k"].Value)
            {
                case "Accepted":
                    s.Started ??= at;
                    break;
                case "Shared":
                    if (s.Started == null) s.Shared = true;
                    s.Started ??= at;
                    break;
                case "Complete":
                    if (s.Outcome == null) (s.Outcome, s.Ended) = (MissionOutcome.Complete, at);
                    break;
                case "Failed":
                    if (s.Outcome == null) (s.Outcome, s.Ended) = (MissionOutcome.Fail, at);
                    break;
            }
            return;
        }
        if (line.Contains("<EndMission>", StringComparison.Ordinal))
        {
            var e = End.Match(line);
            if (!e.Success || Time(e) is not { } at) return;
            var s = Of(e.Groups["m"].Value);
            if (s.Outcome != null) return;
            var type = e.Groups["ct"].Value;
            s.Outcome = type.Equals("Complete", StringComparison.OrdinalIgnoreCase) ? MissionOutcome.Complete
                : type.Equals("Fail", StringComparison.OrdinalIgnoreCase) ? MissionOutcome.Fail : MissionOutcome.Abandon;
            s.Ended = at;
        }
    }

    static DateTime? Time(Match m) =>
        DateTime.TryParse(m.Groups["ts"].Value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t) ? t : null;
}
