using System.Globalization;
using System.Text.RegularExpressions;

namespace SCTools.GameLog;

/// <summary>
/// Reads Star Citizen Game.log lines one at a time and collects the missions: the Red Wind hauling contracts (to Ruin, from Ruin, and since stage 1a of
/// docs/planner-any-hauling.md the ones to another station) with their cargo and stops, and every other mission with its objectives (the Other hauling
/// board and the map read those). All times are UTC.
/// </summary>
public sealed class HaulingLog
{
    static readonly Regex Ts = new(@"^<(\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\.\d+)Z> ", RegexOptions.Compiled);

    static readonly Regex Marker = new(
        @"Creating objective marker: missionId \[(?<m>[0-9a-f-]{36})\], generator name \[[^\]]*\], contract \[(?<c>[^\]]*)\], contractDefinitionId\[(?<d>[^\]]*)\], objectiveId \[(?<o>[^\]]*)\], markerEntityId \[(?<e>\d+)\], zoneHostId \[(?<z>\d+)\](?:, position \[x: (?<px>-?[\d.]+), y: (?<py>-?[\d.]+), z: (?<pz>-?[\d.]+)\])?",
        RegexOptions.Compiled);

    // the objective's text, from its popup: "New Objective: Go to Asteroid Cluster: " (MissionId and ObjectiveId go with it)
    static readonly Regex NewObjective = new(
        @"Added notification ""New Objective: (?<x>[^""]*?):?\s*"" \[\d+\] to queue.*MissionId: \[(?<m>[0-9a-f-]{36})\], ObjectiveId: \[(?<o>[^\]]+)\]",
        RegexOptions.Compiled);

    // the quantum beacon of an objective's marker (MissionBeacon)
    static readonly Regex Beacon = new(
        @"PairWithMissionNavpoint> MissionObjectiveMarker_(?<e>\d+)\[\d+\] - Paired with NavPoint: (?<n>\S+?)\[\d+\]",
        RegexOptions.Compiled);

    // the star system a contract is in, when its name says: "BountyHuntersGuild_ShipWaveAttack_DrawOutBoss_Nyx_Super"
    static readonly Regex ContractSystem = new(@"_(?<s>Stanton|Pyro|Nyx)(?:_|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

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

    // the title of a contract: "Contract Accepted:  RSI Disc. Month: Ling Small Haul: " (or Shared, when it was shared with him)
    static readonly Regex ContractTitle = new(
        @"Added notification ""Contract (?<k>Accepted|Shared):\s*(?<t>.*?)\s*"" \[\d+\] to queue.*MissionId: \[(?<m>[0-9a-f-]{36})\]",
        RegexOptions.Compiled);

    static readonly Regex Deliver = new(
        @"Added notification ""New Objective: Deliver (?<x>\d+)/(?<n>\d+) SCU of (?<c>.+?) to [^""]*"" \[\d+\] to queue.*MissionId: \[(?<m>[0-9a-f-]{36})\](?:, ObjectiveId: \[(?<o>[^\]]+)\])?",
        RegexOptions.Compiled);

    static readonly Regex Member = new(
        @"<Player(?<k>Joined|Left)> Received Player\k<k> push message for: mission_id (?<m>[0-9a-f-]{36}) - player_id (?<p>\d+)",
        RegexOptions.Compiled);

    // The in-game party, from the game's notifications: the line with their text as it goes to the queue (once per notification; it has "MissionId" in it,
    // so the trackers upload it). "zzatvo has joined the party.", "Bileh123 has left the party.", "zzoega is now party leader.", "Initiated by party leader
    // zzoega." (a party launch), "The party has been disbanded.", "party" (the second line of "You have joined party" and "You have created party"), and
    // "zzatvo connected." / "Bileh123 disconnected." (a member of the party came into the game or went out of it).
    static readonly Regex PartyNote = new(
        @"^<[^>]+> (?:(?<n>[A-Za-z0-9_.-]+) has (?<k>joined|left) the party\.|(?<n>[A-Za-z0-9_.-]+) (?<k>connected|disconnected)\.|(?<n>[A-Za-z0-9_.-]+) is now party (?<k>leader)\.|Initiated by party (?<k>leader) (?<n>[A-Za-z0-9_.-]+)\.|The party has been (?<k>disbanded)\.|(?<k>party)): "" \[\d+\] to queue\.",
        RegexOptions.Compiled);

    // the game names where the player is when he plans a quantum route: "Projected Start Location is Stanton Gateway for route to ..."
    static readonly Regex StartLocation = new(
        @"Projected Start Location is (?<n>Nyx Gateway|Stanton Gateway) for route to destination",
        RegexOptions.Compiled);

    // quantum travel: "Player has selected point rs_ext_pyro6_leo as their destination", "Projected Start Location is Ruin Station for route to destination rs_ext_pyro6_leo",
    // "Quantum Drive has arrived at final destination". The ship is in the front of the line: "| CRUS_Starlifter_C2_891702459996[891702459996]|CSCItemNavigation::..."
    static readonly Regex QtSelect = new(@"Player has selected point (?<d>\S+) as their destination", RegexOptions.Compiled);
    static readonly Regex QtStart = new(@"Projected Start Location is (?<n>.+?) for route to destination (?<d>\S+)", RegexOptions.Compiled);

    // and how much fuel the route takes: "Successfully calculated route to MISSION_QT_Quantum_Beacon_LongRange_864272617968 fuel estimate 738770.250000"
    static readonly Regex QtFuel = new(@"Successfully calculated route to (?<d>\S+) fuel estimate (?<f>[0-9]+(?:\.[0-9]+)?)", RegexOptions.Compiled);
    static readonly Regex QtShip = new(@"\| (?<s>[A-Za-z][A-Za-z0-9_]*?)_\d{6,}\[(?<id>\d+)\]\|CSCItemNavigation::", RegexOptions.Compiled);

    // The game no longer writes kill lines (CIG took "<Actor Death>" out after 4.3). What is still written when the player of this log dies is the list of the items of his
    // body for the item recovery (one line for every item): a burst of those lines is a death.
    internal const string CorpseLine = "Adding non kept item [CSCActorCorpseUtils::PopulateItemPortForItemRecoveryEntitlement]";

    /// <summary>
    /// The kinds of lines the tracker reads: a line with none of these in it is skipped at once (ProcessLine), and a tracker does not upload it (LineFilter).
    /// A new kind of line that is read (a new mission type, a new event) goes here, and the trackers send it from their next update.
    /// </summary>
    internal static readonly string[] LineKinds =
    {
        "sent you:", "You sent ",                                       // money transfers (the amount is on the next line)
        "AccountLoginCharacterStatus_Character",                        // who the player is
        "[QuantumTravel]", CorpseLine,                                  // quantum travel, and the items of a body (he died)
        "Projected Start Location is ",                                 // which gateway he is at
        "FreightElevatorUIProvider::FillUnstowRequest",                 // he takes cargo out of a freight elevator
        "CSCLoadingPlatformManager::OnLoadingPlatformStateChanged",     // he hands cargo in at a loading platform
        InventoryLine,                                                  // he opened the inventory of a place: where he is
        AreaLine,                                                       // he went into another area: when a quantum jump starts and ends
        "Mission",                                                      // the missions: markers, objectives, notifications, members
        QuitLine,                                                       // the game was quit (a log without it ended in a crash)
        SCTools.Missions.MissionReader.EarnedLine,                      // what a completed mission paid him ("You've Earned: 14 Rewards")
    };

    /// <summary>What the game writes when the player opens the inventory of the place he is at (LocationSeen).</summary>
    public const string InventoryLine = "<RequestLocationInventory>";

    /// <summary>What the game writes when the player goes from one area to another (AreaChange).</summary>
    public const string AreaLine = "<Update Inventory Location>";

    static readonly Regex AreaAt = new(@"<Update Inventory Location> Player \[(?<p>[^\]]+)\] is changing location\. Landing \[(?<la>\d+)\] -> \[(?<lb>\d+)\]\. Location \[(?<a>\d+)\] -> \[(?<b>\d+)\]",
        RegexOptions.Compiled);

    static readonly Regex InventoryAt = new(@"<RequestLocationInventory> Player\[(?<p>[^\]]+)\] requested inventory for Location\[(?<l>[^\]]+)\]", RegexOptions.Compiled);

    /// <summary>What the game writes when it is quit the normal way: a log that ends without it crashed (or was killed).</summary>
    public const string QuitLine = "SystemQuit";

    /// <summary>Is this a kind of line the tracker reads (see LineKinds)?</summary>
    internal static bool IsRead(string line)
    {
        foreach (var kind in LineKinds)
        {
            if (line.Contains(kind, StringComparison.Ordinal)) return true;
        }
        return false;
    }
    DateTime _corpseFirst;
    int _corpseCount;

    readonly Dictionary<string, (string Mission, string Objective)> _markers = new();   // a marker's entity id: whose objective it is (for its beacon)

    (DateTime Time, string Zone)? _gateEvent;   // the latest thing that happened at a gateway (a pickup there), to match with the name the game gives next

    static readonly Regex PlayerIdA = new(@"Player\[(?<n>[^\]]+)\] PlayerId\[(?<id>\d+)\]", RegexOptions.Compiled);
    static readonly Regex PlayerIdB = new(@"Player: (?<n>[^\s\[\]]+)\[(?<id>\d{8,})\]", RegexOptions.Compiled);

    // the player of the log, with his id in the game: "Character: createdAt ... - geid 493008374737 - accountId ... - name zzoega - state STATE_CURRENT"
    static readonly Regex Character = new(
        @"<AccountLoginCharacterStatus_Character> Character: (?:.*? - geid (?<id>\d+))?.* - name (?<n>\S+) - state",
        RegexOptions.Compiled);

    static readonly Regex SentOut = new(@"Added notification ""You sent (?<n>[^:""]+):\s*$", RegexOptions.Compiled);
    static readonly Regex SentIn = new(@"Added notification ""(?<n>[^\s:""]+) has sent you:\s*$", RegexOptions.Compiled);
    static readonly Regex AmountLine = new(@"^<[^>]*> (?<a>[\d,]+) aUEC\s*$", RegexOptions.Compiled);

    static readonly Regex HaulingContract = new(
        @"_StationToRuin_(?<cargo>[A-Za-z]+)_CargoHauling_Multi(?<n>\d)ToSingle",
        RegexOptions.Compiled);

    // to another station: "RedWind_Pyro_BulkGrade_Solar_CFP_StationToStation_Waste_CargoHauling_Multi4ToSingle" ("Master | Stellar Large Haul | to Stanton
    // Gateway", with "Deliver 0/520 SCU of Waste to Stanton Gateway"; zzoega, 26 Oct 2026): the same shape as the to-Ruin ones
    static readonly Regex StationContract = new(
        @"_StationToStation_(?<cargo>[A-Za-z]+)_CargoHauling_Multi(?<n>\d)ToSingle",
        RegexOptions.Compiled);

    // from Ruin: "..._RuinToStation_Waste_CargoHauling_SingleToMulti2" (2 to 4 drop-offs), or a direct one ("DIRECT Stellar Large Haul", one drop-off):
    // "..._RuinToStation_RegionC_Waste_CargoHauling_AtoB" (the cargo is the last part before _CargoHauling)
    static readonly Regex OutContract = new(
        @"_RuinToStation_(?:[A-Za-z0-9]+_)*(?<cargo>[A-Za-z]+)_CargoHauling_(?:SingleToMulti(?<n>\d)|(?<direct>AtoB))",
        RegexOptions.Compiled);

    public Dictionary<string, Mission> Missions { get; } = new();

    /// <summary>Somebody joined a mission (it was shared with him, or he took it) or left it, as the game told this player. The player is an id, see IdNames.</summary>
    public List<MemberEvent> MemberEvents { get; } = new();

    /// <summary>Player id to the name of the character, as far as the logs show it.</summary>
    public Dictionary<string, string> IdNames { get; } = new();

    /// <summary>What the game told the player of this log about his in-game party, in the order of the log (see PartyNote).</summary>
    public List<PartyEvent> PartyEvents { get; } = new();

    /// <summary>On the server: whose tracker uploads this log (the player of its key).</summary>
    public string? Uploader { get; set; }

    /// <summary>On the server: the games of this player, by log file (see LogUploads): when each started, and the latest time his tracker saw in it.</summary>
    public Dictionary<string, GameRun> GameRuns { get; } = new(StringComparer.Ordinal);

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

    /// <summary>The quantum travel of the player of this log: the destinations he selected and when his drive arrived.</summary>
    public List<QtEvent> Qt { get; } = new();

    /// <summary>The quantum beacons of the objectives of his missions, in the order of the log (a player jumps to an objective by its beacon).</summary>
    public List<MissionBeacon> Beacons { get; } = new();

    /// <summary>Where the player of this log was when he opened the inventory of a place, in the order of the log.</summary>
    public List<LocationSeen> Locations { get; } = new();

    /// <summary>The areas the player of this log went into (off a pad, out of a station's area, into the area of where he jumped to), in the order of the log.</summary>
    public List<AreaChange> Areas { get; } = new();

    /// <summary>When the player of this log died (the first line of the burst of item recovery lines).</summary>
    public List<DateTime> Deaths { get; } = new();

    /// <summary>The same two lists for every account, set on a merged log (a player without a log is not in them).</summary>
    public Dictionary<string, List<QtEvent>> QtByPlayer { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<DateTime>> DeathsByPlayer { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Character name of the player whose log this is.</summary>
    public string? Player { get; private set; }

    /// <summary>
    /// His id in the game, the one the game's PlayerJoined and PlayerLeft name him by: the login line's geid, or the id a line puts to his name (the objective
    /// markers' "Added to DataBank of Player: zzoega[493008374737]", his "Ending mission" lines). Null until a line says it.
    /// </summary>
    public string? PlayerId { get; private set; }

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
            if (!res.QtByPlayer.TryGetValue(who, out var qs)) res.QtByPlayer[who] = qs = new List<QtEvent>();
            qs.AddRange(l.Qt);
            if (!res.DeathsByPlayer.TryGetValue(who, out var ds)) res.DeathsByPlayer[who] = ds = new List<DateTime>();
            ds.AddRange(l.Deaths);

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
                t.ToStation |= m.ToStation;
                t.OutCargo ??= m.OutCargo;
                if (t.OutDrops == 0) t.OutDrops = m.OutDrops;
                foreach (var kv in m.ObjScu) t.ObjScu.TryAdd(kv.Key, kv.Value);
                foreach (var kv in m.ObjText) t.ObjText.TryAdd(kv.Key, kv.Value);
                foreach (var kv in m.ObjPos) t.ObjPos.TryAdd(kv.Key, kv.Value);
                t.Title = SCTools.Missions.MissionKinds.Better(t.Title, m.Title);
                t.System ??= m.System;
                t.Contract ??= m.Contract;
                t.Definition ??= m.Definition;
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

    /// <summary>The time of the first line of this log (in its first twenty lines), or null: a log the game has not written to yet, or one that cannot be opened.</summary>
    public static DateTime? FirstTimestamp(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return FirstTimestamp(fs);
        }
        catch (IOException) { }
        return null;
    }

    /// <summary>The same from a log that is open already (the tracker opens the Game.log once a tick: its name from the start, then its tail). The stream stays open; where it is afterwards is anyone's guess.</summary>
    public static DateTime? FirstTimestamp(Stream log)
    {
        log.Seek(0, SeekOrigin.Begin);
        using var sr = new StreamReader(log, leaveOpen: true);
        for (var i = 0; i < 20; i++)
        {
            var line = sr.ReadLine();
            if (line == null) break;
            var tm = Ts.Match(line);
            if (tm.Success) return ParseTime(tm.Groups[1].Value);
        }
        return null;
    }

    static DateTime ParseTime(string text) =>
        DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    /// <summary>The time a line of the log was written (UTC), or null when it does not start with one.</summary>
    public static DateTime? TimeOf(string line)
    {
        var m = Ts.Match(line);
        return m.Success ? ParseTime(m.Groups[1].Value) : null;
    }

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
        if (!IsRead(line)) return;   // only the kinds of lines in LineKinds are read (and uploaded by the trackers): a new kind goes there first
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
                if (cm.Groups["id"].Success) PlayerId = cm.Groups["id"].Value;
                else PlayerId ??= IdNames.FirstOrDefault(kv => string.Equals(kv.Value, Player, StringComparison.OrdinalIgnoreCase)).Key;
                Version++;
            }
            return;
        }
        if (line.Contains("[QuantumTravel]") || line.Contains(CorpseLine))
        {
            var qtm = Ts.Match(line);
            if (qtm.Success)
            {
                var qtTime = ParseTime(qtm.Groups[1].Value);
                if (line.Contains(CorpseLine))
                {
                    // one line for every item of the body: the first line of a burst is the moment he died
                    if (_corpseCount == 0 || qtTime - _corpseFirst > TimeSpan.FromSeconds(5))
                    {
                        _corpseFirst = qtTime;
                        _corpseCount = 0;
                    }
                    _corpseCount++;
                    if (_corpseCount == 3)
                    {
                        Deaths.Add(_corpseFirst);
                        Version++;
                    }
                    return;
                }

                var qsel = QtSelect.Match(line);
                if (qsel.Success)
                {
                    var (ship, shipId) = ShipIn(line);
                    Qt.Add(new QtEvent { Time = qtTime, Kind = QtKind.Selected, Destination = qsel.Groups["d"].Value, Ship = ship, ShipId = shipId });
                    Version++;
                    return;
                }
                if (line.Contains("Quantum Drive has arrived at final destination"))
                {
                    var (ship, shipId) = ShipIn(line);
                    Qt.Add(new QtEvent { Time = qtTime, Kind = QtKind.Arrived, Ship = ship, ShipId = shipId });
                    Version++;
                    return;
                }
                var qstart = QtStart.Match(line);
                if (qstart.Success)
                {
                    // the game says where the route starts: it belongs to the destination he selected a moment ago (the line goes on to the gateway check below)
                    for (var qi = Qt.Count - 1; qi >= 0 && qtTime - Qt[qi].Time < TimeSpan.FromSeconds(3); qi--)
                    {
                        if (Qt[qi].Kind != QtKind.Selected || Qt[qi].Destination != qstart.Groups["d"].Value || Qt[qi].From.Length > 0) continue;
                        Qt[qi].From = qstart.Groups["n"].Value;
                        break;
                    }
                }
                var qfuel = QtFuel.Match(line);
                if (qfuel.Success && double.TryParse(qfuel.Groups["f"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var estimate))
                {
                    // the route's fuel estimate: the game works the route out before and after the pick (both lines say the same), so it goes with the
                    // destination selected a moment ago, the first that has none yet
                    for (var qi = Qt.Count - 1; qi >= 0 && qtTime - Qt[qi].Time < TimeSpan.FromSeconds(3); qi--)
                    {
                        if (Qt[qi].Kind != QtKind.Selected || Qt[qi].Destination != qfuel.Groups["d"].Value || Qt[qi].FuelEstimate != null) continue;
                        Qt[qi].FuelEstimate = estimate;
                        break;
                    }
                }
            }
        }
        if (line.Contains("Projected Start Location is "))
        {
            var sm2 = StartLocation.Match(line);
            var st2 = Ts.Match(line);
            if (sm2.Success && st2.Success && _gateEvent.HasValue)
            {
                var when = ParseTime(st2.Groups[1].Value);
                // the two gateways have the same position in the log; the game names the one the player is at, a few minutes after he picked up there
                if (when >= _gateEvent.Value.Time && when - _gateEvent.Value.Time < TimeSpan.FromMinutes(10))
                {
                    StationIds.NameGate(_gateEvent.Value.Zone, sm2.Groups["n"].Value == "Nyx Gateway");
                    Version++;
                }
                _gateEvent = null;
            }
            return;
        }
        if (line.Contains(InventoryLine))
        {
            var im = InventoryAt.Match(line);
            var it = Ts.Match(line);
            if (im.Success && it.Success && (Player == null || string.Equals(im.Groups["p"].Value, Player, StringComparison.OrdinalIgnoreCase)))
            {
                Locations.Add(new LocationSeen(ParseTime(it.Groups[1].Value), im.Groups["l"].Value));
                Version++;
            }
            return;
        }
        if (line.Contains(AreaLine))
        {
            var am = AreaAt.Match(line);
            var at = Ts.Match(line);
            if (am.Success && at.Success && (Player == null || string.Equals(am.Groups["p"].Value, Player, StringComparison.OrdinalIgnoreCase)))
            {
                Areas.Add(new AreaChange(ParseTime(at.Groups[1].Value), long.Parse(am.Groups["la"].Value), long.Parse(am.Groups["lb"].Value),
                    long.Parse(am.Groups["a"].Value), long.Parse(am.Groups["b"].Value)));
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
            if (pa.Success) Named(pa.Groups["id"].Value, pa.Groups["n"].Value);
        }
        else if (line.Contains("Player: "))
        {
            var pb = PlayerIdB.Match(line);
            if (pb.Success) Named(pb.Groups["id"].Value, pb.Groups["n"].Value);
        }

        var x = Member.Match(line);
        if (x.Success)
        {
            var left = x.Groups["k"].Value == "Left";
            MemberEvents.Add(new MemberEvent { Mission = x.Groups["m"].Value, Player = x.Groups["p"].Value, Time = t, Left = left });
            // the game took the mission from him: his own PlayerLeft with no EndMission, a second after an exit to the menu (zzoega, 31 Oct 2026, #399). The
            // normal abandon has his PlayerLeft too, with the EndMission two milliseconds later, which then says how it ended; an accept revives it (an event
            // mission he left and got back two seconds later, 28 Aug 2026)
            if (left && PlayerId != null && x.Groups["p"].Value == PlayerId && Missions.TryGetValue(x.Groups["m"].Value, out var gone) && gone.EndState == null)
            {
                gone.EndedAt = t;
                gone.EndState = Mission.Left;
            }
            Touch(t);
            return;
        }

        if (line.Contains("\" [", StringComparison.Ordinal) && line.Contains("] to queue.", StringComparison.Ordinal))
        {
            var pn = PartyNote.Match(line);
            if (pn.Success)
            {
                var kind = pn.Groups["k"].Value switch
                {
                    "joined" => PartyEventKind.Joined,
                    "left" => PartyEventKind.Left,
                    "leader" => PartyEventKind.Leader,
                    "disbanded" => PartyEventKind.Disbanded,
                    "connected" => PartyEventKind.Connected,
                    "disconnected" => PartyEventKind.Disconnected,
                    _ => PartyEventKind.Self,
                };
                PartyEvents.Add(new PartyEvent(t, kind, pn.Groups["n"].Success ? pn.Groups["n"].Value : null));
                // not a mission line: the time of the last mission line stays; the game says "connected" every few minutes while they play, and the party
                // tracker looks at every new piece of log anyway, so those are no reason to read the session again
                if (kind is not (PartyEventKind.Connected or PartyEventKind.Disconnected)) Version++;
                return;
            }
        }

        x = Marker.Match(line);
        if (x.Success)
        {
            var m = Get(x.Groups["m"].Value);
            var c = HaulingContract.Match(x.Groups["c"].Value);
            var toStation = !c.Success && (c = StationContract.Match(x.Groups["c"].Value)).Success;
            if (c.Success)
            {
                m.Cargo = c.Groups["cargo"].Value;
                m.Multi = int.Parse(c.Groups["n"].Value, CultureInfo.InvariantCulture);
                m.ToStation = toStation;
            }
            else
            {
                var o = OutContract.Match(x.Groups["c"].Value);
                if (o.Success)
                {
                    m.OutCargo = o.Groups["cargo"].Value;
                    m.OutDrops = o.Groups["direct"].Success ? 1 : int.Parse(o.Groups["n"].Value, CultureInfo.InvariantCulture);
                }
            }
            m.ObjZone[x.Groups["o"].Value] = x.Groups["z"].Value;
            if (x.Groups["c"].Value.Length > 0) m.Contract ??= x.Groups["c"].Value;
            if (x.Groups["d"].Value.Length > 0) m.Definition ??= x.Groups["d"].Value.ToLowerInvariant();
            _markers[x.Groups["e"].Value] = (m.Id, x.Groups["o"].Value);
            var cs = ContractSystem.Match(x.Groups["c"].Value);
            if (cs.Success) m.System = cs.Groups["s"].Value.ToLowerInvariant();
            if (x.Groups["px"].Success
                && double.TryParse(x.Groups["px"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var px)
                && double.TryParse(x.Groups["py"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var py)
                && double.TryParse(x.Groups["pz"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var pz))
            {
                StationIds.Register(x.Groups["z"].Value, px, py, pz);   // the same station has another id on every server, but the same position
                m.ObjPos[x.Groups["o"].Value] = (px, py, pz);
            }
            m.FirstSeen ??= t;
            Touch(t);
            return;
        }

        x = Beacon.Match(line);
        if (x.Success)
        {
            if (_markers.TryGetValue(x.Groups["e"].Value, out var mo))
            {
                var nav = x.Groups["n"].Value;
                if (!Beacons.Any(b => b.Objective == mo.Objective && b.NavPoint == nav && b.MissionId == mo.Mission))
                    Beacons.Add(new MissionBeacon(t, nav, x.Groups["e"].Value, mo.Mission, mo.Objective));
                Touch(t);
            }
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
            if (m.ObjZone.TryGetValue(obj, out var evZone) && StationIds.IsGate(evZone)) _gateEvent = (t, evZone);
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
                if (m.EndState == null || m.EndState == Mission.Left)   // his PlayerLeft came two milliseconds before: this says how it ended
                {
                    m.EndedAt = t;
                    m.EndState = type.ToUpperInvariant();
                    Touch(t);
                }
            }
            return;
        }

        x = ContractTitle.Match(line);
        if (x.Success)
        {
            var title = SCTools.Missions.MissionReader.CleanTitle(x.Groups["t"].Value);
            var known = Missions.TryGetValue(x.Groups["m"].Value, out var titled);
            if (x.Groups["k"].Value == "Accepted" && !known) titled = Get(x.Groups["m"].Value);
            if (titled != null && title.Length > 0 && !title.StartsWith("~mission(", StringComparison.Ordinal)) titled.Title = SCTools.Missions.MissionKinds.Better(titled.Title, title);
            if (x.Groups["k"].Value == "Shared")
            {
                if (titled != null) Touch(t);
                return;
            }
        }

        x = Accepted.Match(line);
        if (x.Success)
        {
            var m = Get(x.Groups["m"].Value);
            m.Accepted ??= t;
            if (m.EndState != null && m.EndState != "COMPLETED")
            {
                m.EndState = null;   // abandoned (or lost) before, and now accepted again (shared with this player again): it is running for him again
                m.EndedAt = null;
            }
            Touch(t);
            return;
        }

        x = Deliver.Match(line);
        if (x.Success)
        {
            var m = Get(x.Groups["m"].Value);   // the "Deliver x/n SCU" popup: only the n of an objective is used (its SCU); the x was once noted and never read
            if (x.Groups["o"].Success) m.ObjScu[x.Groups["o"].Value] = int.Parse(x.Groups["n"].Value, CultureInfo.InvariantCulture);
            Touch(t);
        }

        x = NewObjective.Match(line);
        if (x.Success && x.Groups["x"].Value.Length > 0)
        {
            Get(x.Groups["m"].Value).ObjText[x.Groups["o"].Value] = x.Groups["x"].Value.Trim();
            Touch(t);
        }
    }

    static (string Ship, string Id) ShipIn(string line)
    {
        var sm = QtShip.Match(line);
        return sm.Success ? (sm.Groups["s"].Value, sm.Groups["id"].Value) : ("", "");
    }

    void Touch(DateTime t)
    {
        Version++;
        if (t > LastTime) LastTime = t;
    }

    /// <summary>A line put an id to a name (IdNames): the log's own player's id too, when it is his name and the login line did not say it.</summary>
    void Named(string id, string name)
    {
        IdNames[id] = name;
        if (PlayerId == null && Player != null && string.Equals(name, Player, StringComparison.OrdinalIgnoreCase)) PlayerId = id;
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
