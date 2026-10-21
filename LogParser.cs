using System.Globalization;
using System.Text.RegularExpressions;

namespace HaulingTracker;

/// <summary>One state change of a mission objective (INPROGRESS or COMPLETED).</summary>
public sealed class ObjEvent
{
    public DateTime Time;
    public string State = "";
}

/// <summary>A "Deliver 519/551 SCU of Potassium" popup seen in the log.</summary>
public sealed class DeliverNote
{
    public DateTime Time;
    public int Delivered;
    public int Total;
}

/// <summary>Money sent between two players. An empty From or To means "the player whose log this is".</summary>
public sealed class Transfer
{
    public DateTime Time;
    public string From = "";
    public string To = "";
    public decimal Amount;
}

public sealed class MemberEvent
{
    public string Mission = "";
    public string Player = "";   // the player id
    public DateTime Time;
    public bool Left;
}

public sealed class Mission
{
    public string Id = "";
    public string? Cargo;                 // Potassium, Carbon... (only set for Station -> Ruin hauling contracts)
    public int Multi;                     // number of pickup stops (2 or 3)
    public DateTime? Accepted;
    public DateTime? FirstSeen;
    public DateTime? EndedAt;
    public string? EndState;              // COMPLETED, ABANDON, ...
    public Dictionary<string, string> ObjZone = new();                 // objective id -> zone host id
    public Dictionary<string, List<ObjEvent>> Events = new();          // objective id -> state changes
    public List<DeliverNote> Notes = new();

    /// <summary>The players (characters of the logs) who have this mission running: the logs that know it and have not ended it. Only set on the missions of the merged log.</summary>
    public HashSet<string> Holders = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The player who took the contract: the first log that shows it accepted (the others got it shared later). Only set on the missions of the merged log.</summary>
    public string? Taker;

    public bool IsHauling => Cargo != null;
    public bool Completed => EndState == "COMPLETED";
    public bool Dead => EndState != null && EndState != "COMPLETED";
}

/// <summary>
/// Reads Star Citizen Game.log lines one at a time and collects the hauling missions.
/// Only Station -> Ruin hauling contracts are kept. Ruin -> Station contracts are ignored.
/// All times are UTC.
/// </summary>
public sealed class HaulingLog
{
    static readonly Regex Ts = new(@"^<(\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\.\d+)Z> ", RegexOptions.Compiled);

    static readonly Regex Marker = new(
        @"Creating objective marker: missionId \[(?<m>[0-9a-f-]{36})\], generator name \[[^\]]*\], contract \[(?<c>[^\]]*)\], contractDefinitionId\[[^\]]*\], objectiveId \[(?<o>[^\]]*)\], markerEntityId \[\d+\], zoneHostId \[(?<z>\d+)\](?:, position \[x: (?<px>-?[\d.]+), y: (?<py>-?[\d.]+), z: (?<pz>-?[\d.]+)\])?",
        RegexOptions.Compiled);

    static readonly Regex Upsert = new(
        @"<ObjectiveUpserted> Received ObjectiveUpserted push message for: mission_id (?<m>[0-9a-f-]{36}) - objective_id (?<o>\S+) - state MISSION_OBJECTIVE_STATE_(?<s>\w+)",
        RegexOptions.Compiled);

    static readonly Regex Ended = new(
        @"<MissionEnded> Received MissionEnded push message for: mission_id (?<m>[0-9a-f-]{36}) - mission_state MISSION_STATE_(?<s>\w+)",
        RegexOptions.Compiled);

    static readonly Regex EndMission = new(
        @"<EndMission> Ending mission for player\. MissionId\[(?<m>[0-9a-f-]{36})\].*CompletionType\[(?<t>\w+)\]",
        RegexOptions.Compiled);

    static readonly Regex Accepted = new(
        @"Added notification ""Contract Accepted:.*"" \[\d+\] to queue.*MissionId: \[(?<m>[0-9a-f-]{36})\]",
        RegexOptions.Compiled);

    static readonly Regex Deliver = new(
        @"Added notification ""New Objective: Deliver (?<x>\d+)/(?<n>\d+) SCU of (?<c>\w+) to [^""]*"" \[\d+\] to queue.*MissionId: \[(?<m>[0-9a-f-]{36})\]",
        RegexOptions.Compiled);

    static readonly Regex Member = new(
        @"<Player(?<k>Joined|Left)> Received Player\k<k> push message for: mission_id (?<m>[0-9a-f-]{36}) - player_id (?<p>\d+)",
        RegexOptions.Compiled);

    static readonly Regex PlayerIdA = new(@"Player\[(?<n>[^\]]+)\] PlayerId\[(?<id>\d+)\]", RegexOptions.Compiled);
    static readonly Regex PlayerIdB = new(@"Player: (?<n>[^\s\[\]]+)\[(?<id>\d{8,})\]", RegexOptions.Compiled);

    static readonly Regex Character = new(
        @"<AccountLoginCharacterStatus_Character> Character: .* - name (?<n>\S+) - state",
        RegexOptions.Compiled);

    static readonly Regex SentOut = new(@"Added notification ""You sent (?<n>[^:""]+):\s*$", RegexOptions.Compiled);
    static readonly Regex SentIn = new(@"Added notification ""(?<n>[^\s:""]+) has sent you:\s*$", RegexOptions.Compiled);
    static readonly Regex AmountLine = new(@"^<[^>]*> (?<a>[\d,]+) aUEC\s*$", RegexOptions.Compiled);

    static readonly Regex HaulingContract = new(
        @"_StationToRuin_(?<cargo>[A-Za-z]+)_CargoHauling_Multi(?<n>\d)ToSingle",
        RegexOptions.Compiled);

    public Dictionary<string, Mission> Missions { get; } = new();

    /// <summary>Somebody joined a mission (it was shared with him, or he took it) or left it, as the game told this player. The player is an id, see IdNames.</summary>
    public List<MemberEvent> MemberEvents { get; } = new();

    /// <summary>Player id to the name of the character, as far as the logs show it.</summary>
    public Dictionary<string, string> IdNames { get; } = new();

    /// <summary>The characters of the logs that were read (a party member without a log is not in it).</summary>
    public HashSet<string> Players { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Money transfers seen in this log (sent by this player or received by him).</summary>
    public List<Transfer> Transfers { get; } = new();

    // a transfer notification is spread over several lines: who, then the amount on the next line
    bool _pendingOut;
    string? _pendingOther;
    DateTime _pendingTime;

    /// <summary>
    /// When this player used a freight elevator to take cargo out (a pickup). Only the player who does it has these lines.
    /// </summary>
    public List<DateTime> Unstows { get; } = new();

    /// <summary>When this player used a loading platform (a hand-in at Ruin shows up here, also only for that player).</summary>
    public List<DateTime> Platforms { get; } = new();

    /// <summary>The same two lists for every account, set on a merged log.</summary>
    public Dictionary<string, List<DateTime>> UnstowByPlayer { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<DateTime>> PlatformByPlayer { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Character name of the player whose log this is.</summary>
    public string? Player { get; private set; }

    public static string? FirstPlayer(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(fs);
            string? line;
            for (var i = 0; i < 3000 && (line = sr.ReadLine()) != null; i++)
            {
                if (!line.Contains("AccountLoginCharacterStatus_Character")) continue;
                var m = Character.Match(line);
                if (m.Success) return m.Groups["n"].Value;
            }
        }
        catch (IOException) { }
        return null;
    }

    /// <summary>Goes up every time something relevant changed, so the screen knows when to redraw.</summary>
    public int Version { get; private set; }

    /// <summary>Time of the most recent mission related line (UTC).</summary>
    public DateTime LastTime { get; private set; } = DateTime.MinValue;

    /// <summary>Combines the logs of several accounts. A mission shared in a party appears once.</summary>
    public static HaulingLog Merge(IEnumerable<HaulingLog> logs)
    {
        var res = new HaulingLog();
        var holders = new Dictionary<string, HashSet<string>>();
        var takers = new Dictionary<string, (DateTime At, string Who)>();
        var ends = new Dictionary<string, MissionEnds>();
        foreach (var l in logs)
        {
            if (l.LastTime > res.LastTime) res.LastTime = l.LastTime;

            var who = l.Player ?? "Unknown player";
            if (l.Player != null) res.Players.Add(l.Player);
            foreach (var kv in l.IdNames) res.IdNames[kv.Key] = kv.Value;
            foreach (var e in l.MemberEvents)
            {
                if (!res.MemberEvents.Any(x => x.Mission == e.Mission && x.Player == e.Player && x.Left == e.Left && Math.Abs((x.Time - e.Time).TotalSeconds) < 5))
                    res.MemberEvents.Add(e);
            }
            if (!res.UnstowByPlayer.TryGetValue(who, out var us)) res.UnstowByPlayer[who] = us = new List<DateTime>();
            us.AddRange(l.Unstows);
            if (!res.PlatformByPlayer.TryGetValue(who, out var ps)) res.PlatformByPlayer[who] = ps = new List<DateTime>();
            ps.AddRange(l.Platforms);

            foreach (var tr in l.Transfers)
            {
                var from = tr.From.Length == 0 ? who : tr.From;
                var to = tr.To.Length == 0 ? who : tr.To;
                var dup = res.Transfers.FirstOrDefault(x => x.Amount == tr.Amount
                    && string.Equals(x.From, from, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x.To, to, StringComparison.OrdinalIgnoreCase)
                    && Math.Abs((x.Time - tr.Time).TotalSeconds) <= 10);
                if (dup == null) res.Transfers.Add(new Transfer { Time = tr.Time, From = from, To = to, Amount = tr.Amount });
                else if (tr.Time < dup.Time) dup.Time = tr.Time;
            }

            foreach (var m in l.Missions.Values)
            {
                if (!res.Missions.TryGetValue(m.Id, out var t))
                {
                    t = new Mission { Id = m.Id };
                    res.Missions[m.Id] = t;
                }
                t.Cargo ??= m.Cargo;
                if (t.Multi == 0) t.Multi = m.Multi;
                if (m.Accepted.HasValue && (!t.Accepted.HasValue || m.Accepted < t.Accepted)) t.Accepted = m.Accepted;
                if (m.FirstSeen.HasValue && (!t.FirstSeen.HasValue || m.FirstSeen < t.FirstSeen)) t.FirstSeen = m.FirstSeen;
                // how the mission ended is decided after all logs are read (see below)
                var st = ends.TryGetValue(m.Id, out var old) ? old : default;
                if (m.EndState == null || m.EndState == "COMPLETED")
                {
                    if (!holders.TryGetValue(m.Id, out var hs)) holders[m.Id] = hs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    hs.Add(who);   // has it running, or finished it with the party
                }
                if (m.Accepted.HasValue && (!takers.TryGetValue(m.Id, out var tk) || m.Accepted.Value < tk.At)) takers[m.Id] = (m.Accepted.Value, who);
                if (m.EndState == null) st.Active = true;
                else if (m.EndState == "COMPLETED")
                {
                    if (!st.Completed || m.EndedAt < st.CompletedAt) st.CompletedAt = m.EndedAt;
                    st.Completed = true;
                }
                else if (st.EndState == null || m.EndedAt < st.EndedAt)
                {
                    st.EndedAt = m.EndedAt;
                    st.EndState = m.EndState;
                }
                ends[m.Id] = st;
                foreach (var kv in m.ObjZone) t.ObjZone[kv.Key] = kv.Value;
                foreach (var kv in m.Events)
                {
                    if (!t.Events.TryGetValue(kv.Key, out var list))
                    {
                        list = new List<ObjEvent>();
                        t.Events[kv.Key] = list;
                    }
                    foreach (var e in kv.Value)
                    {
                        if (!list.Any(x => x.Time == e.Time && x.State == e.State))
                            list.Add(new ObjEvent { Time = e.Time, State = e.State });
                    }
                }
                t.Notes.AddRange(m.Notes);
            }
        }

        foreach (var kv in holders)
        {
            if (res.Missions.TryGetValue(kv.Key, out var hm)) hm.Holders = kv.Value;
        }
        foreach (var kv in takers)
        {
            if (res.Missions.TryGetValue(kv.Key, out var tm)) tm.Taker = kv.Value.Who;
        }

        // A mission shared in a party is only abandoned or failed when every log that knows it ended it that way.
        // If one player abandons it while another still has it (or completes it), it lives on.
        foreach (var kv in ends)
        {
            var st = kv.Value;
            var t = res.Missions[kv.Key];
            if (st.Completed)
            {
                t.EndedAt = st.CompletedAt;
                t.EndState = "COMPLETED";
            }
            else if (st.Active || st.EndState == null)
            {
                t.EndedAt = null;
                t.EndState = null;
            }
            else
            {
                t.EndedAt = st.EndedAt;
                t.EndState = st.EndState;
            }
        }
        return res;
    }

    struct MissionEnds
    {
        public bool Active;            // some log still has the mission running
        public bool Completed;         // some log completed it
        public DateTime? CompletedAt;
        public string? EndState;       // the earliest abandon / fail, when no log completed it
        public DateTime? EndedAt;
    }

    Mission Get(string id)
    {
        if (!Missions.TryGetValue(id, out var m))
        {
            m = new Mission { Id = id };
            Missions[id] = m;
        }
        return m;
    }

    public static DateTime? FirstTimestamp(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(fs);
            for (var i = 0; i < 20; i++)
            {
                var line = sr.ReadLine();
                if (line == null) break;
                var tm = Ts.Match(line);
                if (tm.Success) return ParseTime(tm.Groups[1].Value);
            }
        }
        catch (IOException) { }
        return null;
    }

    static DateTime ParseTime(string text) =>
        DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    public void ProcessLine(string line)
    {
        if (line.Length < 30 || line[0] != '<') return;

        if (_pendingOther != null)
        {
            var am = AmountLine.Match(line);
            var other = _pendingOther;
            _pendingOther = null;
            if (am.Success && decimal.TryParse(am.Groups["a"].Value.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
            {
                Transfers.Add(new Transfer
                {
                    Time = _pendingTime,
                    From = _pendingOut ? "" : other,
                    To = _pendingOut ? other : "",
                    Amount = amount,
                });
                Version++;
                return;
            }
        }
        if (line.Contains("sent you:") || line.Contains("You sent "))
        {
            var sm = SentOut.Match(line);
            var outgoing = sm.Success;
            if (!outgoing) sm = SentIn.Match(line);
            if (sm.Success)
            {
                var tt = Ts.Match(line);
                if (tt.Success)
                {
                    _pendingOut = outgoing;
                    _pendingOther = sm.Groups["n"].Value.Trim();
                    _pendingTime = ParseTime(tt.Groups[1].Value);
                }
                return;
            }
        }
        if (Player == null && line.Contains("AccountLoginCharacterStatus_Character"))
        {
            var cm = Character.Match(line);
            if (cm.Success)
            {
                Player = cm.Groups["n"].Value;
                Version++;
            }
            return;
        }
        if (line.Contains("FreightElevatorUIProvider::FillUnstowRequest"))
        {
            var um = Ts.Match(line);
            if (um.Success) Unstows.Add(ParseTime(um.Groups[1].Value));
            return;
        }
        if (line.Contains("CSCLoadingPlatformManager::OnLoadingPlatformStateChanged"))
        {
            var pm = Ts.Match(line);
            if (pm.Success) Platforms.Add(ParseTime(pm.Groups[1].Value));
            return;
        }
        if (!line.Contains("Mission")) return;
        var tm = Ts.Match(line);
        if (!tm.Success) return;
        var t = ParseTime(tm.Groups[1].Value);

        if (line.Contains("PlayerId["))
        {
            var pa = PlayerIdA.Match(line);
            if (pa.Success) IdNames[pa.Groups["id"].Value] = pa.Groups["n"].Value;
        }
        else if (line.Contains("Player: "))
        {
            var pb = PlayerIdB.Match(line);
            if (pb.Success) IdNames[pb.Groups["id"].Value] = pb.Groups["n"].Value;
        }

        var x = Member.Match(line);
        if (x.Success)
        {
            MemberEvents.Add(new MemberEvent { Mission = x.Groups["m"].Value, Player = x.Groups["p"].Value, Time = t, Left = x.Groups["k"].Value == "Left" });
            Touch(t);
            return;
        }

        x = Marker.Match(line);
        if (x.Success)
        {
            var m = Get(x.Groups["m"].Value);
            var c = HaulingContract.Match(x.Groups["c"].Value);
            if (c.Success)
            {
                m.Cargo = c.Groups["cargo"].Value;
                m.Multi = int.Parse(c.Groups["n"].Value, CultureInfo.InvariantCulture);
            }
            m.ObjZone[x.Groups["o"].Value] = x.Groups["z"].Value;
            if (x.Groups["px"].Success
                && double.TryParse(x.Groups["px"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var px)
                && double.TryParse(x.Groups["py"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var py)
                && double.TryParse(x.Groups["pz"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var pz))
                StationIds.Register(x.Groups["z"].Value, px, py, pz);   // the same station has another id on every server, but the same position
            m.FirstSeen ??= t;
            Touch(t);
            return;
        }

        x = Upsert.Match(line);
        if (x.Success)
        {
            var m = Get(x.Groups["m"].Value);
            var obj = x.Groups["o"].Value;
            if (!m.Events.TryGetValue(obj, out var list))
            {
                list = new List<ObjEvent>();
                m.Events[obj] = list;
            }
            list.Add(new ObjEvent { Time = t, State = x.Groups["s"].Value });
            Touch(t);
            return;
        }

        x = Ended.Match(line);
        if (x.Success)
        {
            var m = Get(x.Groups["m"].Value);
            m.EndedAt = t;
            m.EndState = x.Groups["s"].Value;
            Touch(t);
            return;
        }

        x = EndMission.Match(line);
        if (x.Success)
        {
            var type = x.Groups["t"].Value;
            if (type != "Complete")
            {
                var m = Get(x.Groups["m"].Value);
                if (m.EndState == null)
                {
                    m.EndedAt = t;
                    m.EndState = type.ToUpperInvariant();
                    Touch(t);
                }
            }
            return;
        }

        x = Accepted.Match(line);
        if (x.Success)
        {
            var m = Get(x.Groups["m"].Value);
            m.Accepted ??= t;
            Touch(t);
            return;
        }

        x = Deliver.Match(line);
        if (x.Success)
        {
            var m = Get(x.Groups["m"].Value);
            m.Notes.Add(new DeliverNote
            {
                Time = t,
                Delivered = int.Parse(x.Groups["x"].Value, CultureInfo.InvariantCulture),
                Total = int.Parse(x.Groups["n"].Value, CultureInfo.InvariantCulture),
            });
            Touch(t);
        }
    }

    void Touch(DateTime t)
    {
        Version++;
        if (t > LastTime) LastTime = t;
    }

    /// <summary>Reads a whole log file (also works on the Game.log of a running game).</summary>
    public void ProcessFile(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sr = new StreamReader(fs);
        string? line;
        while ((line = sr.ReadLine()) != null) ProcessLine(line);
    }
}
