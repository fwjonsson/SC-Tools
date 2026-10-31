using System.Globalization;
using System.Text.RegularExpressions;

namespace SCTools.Missions;

/// <summary>How a mission ended for the player (null while it is going).</summary>
public enum MissionOutcome { Complete, Fail, Abandon }

/// <summary>
/// One mission of one player, as far as his log shows it: the game's ContractId (the offer it came from), its title as the game showed it (with Frank's
/// string mod, his names), whether it was shared to him, when it started and how it ended. Hauling: the Hauling Planner's contract line of a mission it reads
/// ("2 To Ruin", MissionKinds.WithHauling). Definition: the contract's id as its objective markers say (contractDefinitionId: the Star Citizen Wiki API's
/// uuid of the mission). The Mission Library groups them by their kind (MissionKinds). For the contracts tracker: PlayerId, the player's own id in the game
/// (his login line's geid, the markers that name him, his "Ending mission" lines); Members, what his log said about the other players of the mission (joined,
/// left); Rewards, the game's
/// "You've Earned: N Rewards" right after he completed it (the MG or Council scrip it paid him, or items).
/// </summary>
public sealed record MissionSeen(string MissionId, string? ContractId, string? Title, bool Shared, DateTime? StartedUtc, DateTime? EndedUtc, MissionOutcome? Outcome,
    string? Hauling = null, string? Definition = null, string? PlayerId = null, IReadOnlyList<MissionMember>? Members = null, int? Rewards = null)
{
    /// <summary>The same as another, its members too (a record compares a list by reference).</summary>
    public bool Same(MissionSeen? other) =>
        other != null && this with { Members = null } == other with { Members = null }
        && (Members ?? Array.Empty<MissionMember>()).SequenceEqual(other.Members ?? Array.Empty<MissionMember>());
}

/// <summary>
/// Reads every mission of a player's Game.log (any kind, not only hauling): "ContractId: [c]. MissionId: m" says which offer it is, the notifications
/// (Contract Accepted / Shared / Complete / Failed) its title and how it went, EndMission how it ended otherwise (CompletionType Abandon or Fail), and the
/// game's PlayerLeft for the player himself with no EndMission that it took the mission from him (an exit to the menu; LeftFor). The first accept or share
/// counts; a mission counts once.
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

    /// <summary>The player of an "Ending mission" line: always the log's own player (Frank's logs of Aug-Oct 2026, three accounts).</summary>
    static readonly Regex Who = new(@"Player\[[^\]]+\] PlayerId\[(?<id>\d+)\]", RegexOptions.Compiled);

    /// <summary>The player of the log with his id in the game, at the login: "Character: createdAt ... - geid 493008374737 - accountId ... - name zzoega - state STATE_CURRENT".</summary>
    static readonly Regex Character = new(@"<AccountLoginCharacterStatus_Character> Character: (?:.*? - geid (?<id>\d+))?.* - name (?<n>\S+) - state", RegexOptions.Compiled);

    /// <summary>A line that puts an id to a name ("Added to DataBank of Player: zzoega[493008374737]", with every objective marker): his own id when it is his name.</summary>
    static readonly Regex Named = new(@"Player: (?<n>[^\s\[\]]+)\[(?<id>\d{8,})\]", RegexOptions.Compiled);

    static readonly Regex Member = new(
        @"^<(?<ts>[^>]+)> \[Notice\] <Player(?<k>Joined|Left)> Received Player\k<k> push message for: mission_id (?<m>[0-9a-fA-F-]{36}) - player_id (?<p>\d+)",
        RegexOptions.Compiled);

    /// <summary>
    /// The game's "You've Earned: 16 Rewards" (its next line: "Access Them at Your Primary Residence's Inventory"), half a second after the completion of a
    /// mission that pays scrip, in the log of the player it paid (Frank's logs: the MG scrip of a Combat Gauntlet scenario, also when it went on to a share).
    /// </summary>
    static readonly Regex Earned = new(@"^<(?<ts>[^>]+)> \[Notice\] <SHUDEvent_OnNotification> Added notification ""You've Earned: (?<n>\d+) Rewards", RegexOptions.Compiled);

    /// <summary>How long after a completion the game says what it paid at the most.</summary>
    static readonly TimeSpan EarnedAfter = TimeSpan.FromSeconds(10);

    /// <summary>The line kind of Earned for the trackers (HaulingLog.LineKinds): its first line has no "Mission" in it.</summary>
    public const string EarnedLine = "Added notification \"You've Earned: ";

    sealed class State
    {
        public string? ContractId;
        public string? Title;
        public bool Shared;
        public DateTime? Started;
        public DateTime? Ended;
        public MissionOutcome? Outcome;
        public int Order;
        public List<MissionMember> Members = new();
        public int? Rewards;
        public DateTime? LeftAt;   // the game's PlayerLeft for himself while it had not ended (see LeftFor)
    }

    string? _player;     // the log's own player, by name (the login line)
    string? _playerId;   // his id in the game (the login line's geid, the id the markers put to his name, his "Ending mission" lines)
    DateTime _last;      // when the latest line of the log was written (for LeftFor)
    (string Id, DateTime At)? _lastDone;   // the mission he completed last, for what the game says it paid

    /// <summary>
    /// How long the game's PlayerLeft for his own id with no EndMission stands before it ends the mission: the game took the mission from him (a second after
    /// an exit to the menu: zzoega, 31 Oct 2026, #399), unless it says "Contract Accepted" for it again (an event mission he left and got back two seconds
    /// later, 28 Aug 2026). The normal abandon has the EndMission two milliseconds after the PlayerLeft, which says how it ended. HaulingLog ends it at once
    /// and lets an accept revive it; what this reader says is written down for good (the Mission Library), so it waits for the log to go on past the accept.
    /// </summary>
    static readonly TimeSpan LeftFor = TimeSpan.FromSeconds(10);

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
            .Select(m =>
            {
                var (outcome, ended) = Outcome(m.Value);
                return new MissionSeen(m.Key.ToLowerInvariant(), m.Value.ContractId, m.Value.Title, m.Value.Shared, m.Value.Started, ended, outcome,
                    PlayerId: _playerId, Members: Others(m.Value.Members), Rewards: m.Value.Rewards);
            })
            .ToList();

    /// <summary>How and when a mission ended for him: as the log said, else abandoned when the game's PlayerLeft for him stood for LeftFor of the log without an accept.</summary>
    (MissionOutcome? Outcome, DateTime? Ended) Outcome(State s) =>
        s.Outcome != null ? (s.Outcome, s.Ended) : s.LeftAt is { } left && _last - left >= LeftFor ? (MissionOutcome.Abandon, left) : (null, null);

    /// <summary>What the log said about the other players (not about himself: his own leaving is his "Ending mission", or LeftFor), or null.</summary>
    IReadOnlyList<MissionMember>? Others(List<MissionMember> members)
    {
        var others = members.Where(e => e.PlayerId != _playerId).ToList();
        return others.Count > 0 ? others : null;
    }

    State Of(string id) => _missions.TryGetValue(id, out var s) ? s : _missions[id] = new State { Order = _missions.Count };

    /// <summary>The title as the mission list shows it: without the game's markup (&lt;EM4&gt;...&lt;/EM4&gt;) and the colon at its end.</summary>
    public static string CleanTitle(string title) => Markup.Replace(title, "").Trim().TrimEnd(':').Trim();

    public void ProcessLine(string line)
    {
        if (line.Length < 30 || line[0] != '<') return;
        if (GameLog.HaulingLog.TimeOf(line) is { } written && written > _last) _last = written;
        if (_player == null && line.Contains("AccountLoginCharacterStatus_Character", StringComparison.Ordinal))
        {
            var c = Character.Match(line);
            if (!c.Success) return;
            _player = c.Groups["n"].Value;
            if (c.Groups["id"].Success) _playerId ??= c.Groups["id"].Value;
            return;
        }
        if (line.Contains(EarnedLine, StringComparison.Ordinal))
        {
            var g = Earned.Match(line);
            if (g.Success && Time(g) is { } paid && _lastDone is { } done && paid >= done.At && paid - done.At <= EarnedAfter
                && int.TryParse(g.Groups["n"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
                Of(done.Id).Rewards ??= n;
            return;
        }
        if (!line.Contains("Mission", StringComparison.Ordinal)) return;
        if (_playerId == null && _player != null && line.Contains("Player: ", StringComparison.Ordinal))
        {
            var nm = Named.Match(line);
            if (nm.Success && string.Equals(nm.Groups["n"].Value, _player, StringComparison.OrdinalIgnoreCase)) _playerId = nm.Groups["id"].Value;
        }
        if (line.Contains(" push message for: mission_id ", StringComparison.Ordinal))
        {
            var p = Member.Match(line);
            if (!p.Success || Time(p) is not { } when) return;
            var e = new MissionMember(p.Groups["p"].Value, p.Groups["k"].Value == "Left", when);
            var s = Of(p.Groups["m"].Value);
            if (!s.Members.Contains(e)) s.Members.Add(e);
            if (e.Left && _playerId != null && e.PlayerId == _playerId && s.Outcome == null) s.LeftAt ??= when;   // the game took it from him, unless an accept or the EndMission follows (LeftFor)
            return;
        }
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
                    s.LeftAt = null;   // he has it (again): the game gave it back after its PlayerLeft for him, or it was shared with him again
                    break;
                case "Shared":
                    if (s.Started == null) s.Shared = true;
                    s.Started ??= at;
                    break;
                case "Complete":
                    if (s.Outcome == null) (s.Outcome, s.Ended) = (MissionOutcome.Complete, at);
                    _lastDone = (n.Groups["m"].Value, at);
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
            if (Who.Match(line) is { Success: true } who) _playerId ??= who.Groups["id"].Value;
            var s = Of(e.Groups["m"].Value);
            var type = e.Groups["ct"].Value;
            if (type.Equals("Complete", StringComparison.OrdinalIgnoreCase)) _lastDone = (e.Groups["m"].Value, at);
            if (s.Outcome != null) return;
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
