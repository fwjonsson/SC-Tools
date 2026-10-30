using System.Globalization;
using System.Text.RegularExpressions;

namespace SCTools.Missions;

/// <summary>How a mission ended for the player (null while it is going).</summary>
public enum MissionOutcome { Complete, Fail, Abandon }

/// <summary>
/// One mission of one player, as far as his log shows it: the game's ContractId (the offer it came from), its title as the game showed it (with Frank's
/// string mod, his names), whether it was shared to him, when it started and how it ended. Hauling: the Hauling Planner's contract line of a mission it reads
/// ("2 To Ruin", MissionKinds.WithHauling). Definition: the contract's id as its objective markers say (contractDefinitionId: the Star Citizen Wiki API's
/// uuid of the mission). The Mission Library groups them by their kind (MissionKinds).
/// </summary>
public sealed record MissionSeen(string MissionId, string? ContractId, string? Title, bool Shared, DateTime? StartedUtc, DateTime? EndedUtc, MissionOutcome? Outcome,
    string? Hauling = null, string? Definition = null);

/// <summary>
/// Reads every mission of a player's Game.log (any kind, not only hauling): "ContractId: [c]. MissionId: m" says which offer it is, the notifications
/// (Contract Accepted / Shared / Complete / Failed) its title and how it went, EndMission how it ended otherwise (CompletionType Abandon or Fail). The first
/// accept or share counts; a mission counts once.
/// </summary>
public sealed class MissionReader
{
    static readonly Regex Contract = new(@"ContractId: \[(?<c>[0-9a-fA-F-]{36})\]\.? MissionId: \[?(?<m>[0-9a-fA-F-]{36})", RegexOptions.Compiled);

    static readonly Regex Note = new(
        "^<(?<ts>[^>]+)> \\[Notice\\] <SHUDEvent_OnNotification> Added notification \"Contract (?<k>Accepted|Shared|Complete|Failed):\\s*(?<t>.*?)\\s*\" \\[\\d+\\].*?MissionId: \\[(?<m>[0-9a-fA-F-]{36})\\]",
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

    /// <summary>
    /// The shares he got that no accept took yet: the game's "Contract Shared" line has no mission id (all zeros) and often the contract's template for a
    /// name ("~mission(ReputationRank) | ..."), and his "Contract Accepted" with the real mission follows a second to half a minute later (Frank's logs of
    /// Aug-Oct 2026, three accounts). Each share is one accept.
    /// </summary>
    readonly List<(DateTime At, string Title)> _shares = new();

    /// <summary>How long after a share his accept of it comes at the most.</summary>
    static readonly TimeSpan ShareAccepted = TimeSpan.FromSeconds(30);

    const string NoMission = "00000000-0000-0000-0000-000000000000";

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
            var title = CleanTitle(n.Groups["t"].Value);
            if (n.Groups["m"].Value == NoMission)
            {
                if (n.Groups["k"].Value == "Shared") _shares.Add((at, title));   // a share that names no mission: his accept of it says which
                return;
            }
            var s = Of(n.Groups["m"].Value);
            // the first name counts (a share the game did not name yet has none), unless the game had not filled it in ("at ~mission(Location)") and this one is
            if (title.Length > 0 && !title.StartsWith("~mission(", StringComparison.Ordinal)) s.Title = MissionKinds.Better(s.Title, title);
            switch (n.Groups["k"].Value)
            {
                case "Accepted":
                    // shared with him when a share came just before (and it is not one he had already: the game says them all again at a login)
                    if (s.Started == null && TakeShare(at, title)) s.Shared = true;
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

    /// <summary>A share of this contract (its name, or the template) that came in the half minute before this accept: taken by it. True when there was one.</summary>
    bool TakeShare(DateTime at, string title)
    {
        _shares.RemoveAll(s => at - s.At > ShareAccepted);
        var i = _shares.FindIndex(s => s.At <= at && (s.Title.StartsWith("~mission(", StringComparison.Ordinal) || s.Title.Length == 0
            || string.Equals(KindName(s.Title), KindName(title), StringComparison.OrdinalIgnoreCase)));
        if (i < 0) return false;
        _shares.RemoveAt(i);
        return true;
    }

    /// <summary>A name as the kinds compare it (without what a string mod adds).</summary>
    static string KindName(string title) => MissionKinds.KindTitle(title) ?? title;

    static DateTime? Time(Match m) =>
        DateTime.TryParse(m.Groups["ts"].Value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t) ? t : null;
}
