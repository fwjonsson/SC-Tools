using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SCTools.Missions;
using SCTools.Server.Data;

namespace SCTools.Server;

/// <summary>How one player played a kind of mission; Running: one of his missions of it was taken in the last hours and did not end yet.</summary>
public sealed record PlayCount(int Taken, int Completed, int Failed, int Abandoned, DateTime? LastPlayed, bool Running);

/// <summary>A mission one player ended (completed, failed or abandoned), for the Companion's Completed Contracts: its kind is its entry in the library.</summary>
public sealed record EndedMission(string Player, string MissionId, string? Title, string? Kind, DateTime EndedUtc, MissionOutcome Outcome);

/// <summary>
/// A kind of mission in the library, with how it was played by everybody (Taken... Running) and by each player (Plays, by his name in small letters: the
/// library's page shows the viewer his own).
/// </summary>
public sealed record LibraryEntry(MissionKindRow Row, int Taken, int Completed, int Failed, int Abandoned, DateTime? LastPlayed, bool Running = false,
    WikiContract? Wiki = null, IReadOnlyDictionary<string, PlayCount>? Plays = null)
{
    /// <summary>The name shown: the one typed in, else the one the game shows.</summary>
    public string Shown => Row.Name ?? Row.Title;

    /// <summary>How this player played it (capitals do not matter), or null when he never did (or nobody is asking).</summary>
    public PlayCount? PlaysOf(string? player) => player != null && Plays != null && Plays.TryGetValue(player.ToLowerInvariant(), out var p) ? p : null;

    /// <summary>The SCU the page shows: the one typed in, else the one suggested last, else the middle of the Star Citizen Wiki's hauling orders.</summary>
    public long? ScuShown => Row.Scu ?? Row.ScuSuggested ?? (Wiki != null && Wiki.Orders.Count > 0 ? (long)Math.Round(Wiki.Total.Middle) : null);

    /// <summary>The aUEC the page shows: the one typed in, else the Star Citizen Wiki's reward (the middle of its range).</summary>
    public long? AuecShown => Row.Auec ?? Wiki?.Auec;

    /// <summary>The faction the page shows: the one typed in (the Giver field, as it was called), else the Star Citizen Wiki's.</summary>
    public string? FactionShown => Row.Giver?.Trim() is { Length: > 0 } g ? g : Wiki?.Faction;

    /// <summary>The Star Citizen Wiki's category of the mission (Hauling, Bounty Hunter, Security...), the reputation it gives and Legal or Illegal; null: not known.</summary>
    public string? Category => Wiki?.Category;
    public long? Xp => Wiki?.Xp;
    public string? Legality => Wiki?.Legality;

    /// <summary>Nothing typed in about its rewards yet.</summary>
    public bool NeedsInfo => Row.Auec == null && Row.MgScrip == null && Row.CouncilScrip == null;

    /// <summary>A contract line of the Hauling Planner: its aUEC is what the planner pays for it.</summary>
    public bool Hauling => MissionKinds.IsHauling(Row.Key);

    /// <summary>The id of its row on the page.</summary>
    public string Anchor => MissionLibrary.Anchor(Row.Key);
}

/// <summary>
/// The Mission Library: every mission the players take, from the live logs and the old ones (MissionReader), kept for good (Missions) and grouped by its
/// kind (MissionKinds: its name, or the Hauling Planner's contract line), where the players type in what the log does not say: the aUEC, MG scrip and
/// Council scrip it pays, another name, the giver, notes. The other tools count with it: the contracts tracker (ContractTracker: what every contract pays and
/// each player's part, for the Session tool, the Companion and the Gauntlet Tracker) and the Hauling Planner (what a contract line pays). The first time, the old logs already on the server are read.
/// </summary>
public sealed class MissionLibrary
{
    readonly DataStore _store;
    readonly LogUploads _uploads;
    readonly ServerPaths _paths;
    readonly LiveTracker _live;
    readonly MapPlaces? _places;
    readonly object _lock = new();
    Dictionary<(string, string), MissionSeen>? _known;   // what is in the database, so an unchanged mission is not written again
    List<LibraryEntry>? _entries;   // the page's list, until a mission or a kind changes (made from every mission in the database)

    /// <summary>How long a mission that did not end yet counts as one being played now.</summary>
    static readonly TimeSpan RunningFor = TimeSpan.FromHours(6);

    /// <summary>Goes up every time something on the library's page changes (a mission came in or went on, a field was saved): the open pages ask for it.</summary>
    public int Version { get; private set; }

    void Changed()
    {
        _entries = null;
        Version++;
        PushBook();
    }
    bool _ready;
    bool _readHistoryAgain;

    public MissionLibrary(DataStore store, LogUploads uploads, ServerPaths paths, LiveTracker live, MapPlaces? places = null)
    {
        _store = store;
        _uploads = uploads;
        _paths = paths;
        _live = live;
        _places = places;
    }

    // ------------------------------------------------------------------ the SCU of the hauling jobs (see HaulJobs)

    /// <summary>
    /// Gives the tracker what the site knows about the hauling jobs (a JobBook: the haulers' SCU of single missions, the kinds' SCU and aUEC, the map's
    /// places), made new from the database: when the library changed, and when the server starts.
    /// </summary>
    public void PushBook()
    {
        lock (_lock)
        {
            using var db = _store.Open();
            // (the SCU a hauler typed for one mission on the Other hauling board, HaulScu, went with the board on 30 Oct 2026: it is typed on the Current Route now)
            var missions = new Dictionary<string, long>(StringComparer.Ordinal);
            var kinds = db.MissionKinds.AsNoTracking().ToList()
                .ToDictionary(k => k.Key, k => new JobKind(k.Scu, k.ScuSuggested, k.ScuSuggestedBy, k.Auec), StringComparer.OrdinalIgnoreCase);
            var places = _places;
            _live.Tracker.State.Jobs = new JobBook(missions, kinds, () => places?.Current ?? Atlas.Default, WikiMissions.Known(db));
        }
    }

    /// <summary>The SCU suggested last for the kind of a mission with this title (made when it is not there yet). True when it changed.</summary>
    static bool Suggest(TrackerDb db, string? title, long scu, string by, DateTime nowUtc)
    {
        if (MissionKinds.TitleKey(title) is not { } key) return false;
        var kind = db.MissionKinds.Find(key);
        if (kind == null) db.MissionKinds.Add(kind = new MissionKindRow { Key = key, Title = MissionKinds.KindTitle(title)! });
        if (kind.ScuSuggested == scu && kind.ScuSuggestedBy == by) return false;
        (kind.ScuSuggested, kind.ScuSuggestedBy, kind.ScuSuggestedUtc) = (scu, by, nowUtc);
        return true;
    }

    /// <summary>What the game said the SCU of the hauling jobs going on is: the library's suggestion for their kinds (the latest one). Returns how many changed.</summary>
    public int SuggestFromGame(DateTime nowUtc)
    {
        var said = _live.Read(t => HaulJobs.Open(t.State).Where(j => j.ScuFrom == ScuFrom.Game && j.Mission.Title != null)
            .Select(j => (Title: j.Mission.Title!, Scu: (long)Math.Round(j.Scu))).ToList());
        if (said.Count == 0) return 0;
        lock (_lock)
        {
            using var db = _store.Open();
            Prepare(db);
            var changed = said.Count(s => Suggest(db, s.Title, s.Scu, GameSaid, nowUtc));
            if (changed == 0) return 0;
            db.SaveChanges();
            Changed();
            return changed;
        }
    }

    /// <summary>Who suggested an SCU the game said.</summary>
    public const string GameSaid = "the game";

    /// <summary>The Star Citizen Wiki API told something new: the list and the tracker's book are made again.</summary>
    public void WikiLearned()
    {
        lock (_lock) Changed();
    }

    /// <summary>
    /// Which mission of the Star Citizen Wiki API every kind is: the contract id (contractDefinitionId) of its newest mission that has one, the live logs' and
    /// the old ones' (whatever names a string mod shows); a kind whose missions never showed one (no marker seen, or only in old logs) gets the Wiki's
    /// mission with the name the game shows for it (from the list of every mission, WikiMissions.LearnAll; the rep and blueprint marks a string mod adds
    /// to a name do not count). A kind that has one keeps it. Returns how many kinds got one.
    /// </summary>
    public int LinkWiki()
    {
        lock (_lock)
        {
            using var db = _store.Open();
            Prepare(db);
            var newest = db.Missions.AsNoTracking().Where(m => m.Kind != null && m.Definition != null).ToList()
                .GroupBy(m => m.Kind!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.StartedUtc ?? DateTime.MinValue).First().Definition!, StringComparer.OrdinalIgnoreCase);
            // the Wiki's missions by name: with the same name more than once (a mission at several places), the one read from its own page, else the one
            // with the biggest reward, so the choice is the same every time
            var byName = db.WikiMissions.AsNoTracking().Where(r => r.Found && r.Title != null).ToList()
                .GroupBy(r => PlainName(r.Title!), StringComparer.OrdinalIgnoreCase).Where(g => g.Key.Length > 0)
                .ToDictionary(g => g.Key, g => g.OrderBy(r => r.ListOnly).ThenByDescending(r => r.RewardMin ?? 0).ThenBy(r => r.Uuid, StringComparer.Ordinal).First().Uuid,
                    StringComparer.OrdinalIgnoreCase);
            var linked = 0;
            foreach (var kind in db.MissionKinds.Where(k => k.WikiUuid == null).ToList())
            {
                if (!newest.TryGetValue(kind.Key, out var uuid))
                {
                    // no contract id in any log: the Wiki's mission with the kind's name (not a contract line of the Hauling Planner that no mission named yet)
                    if (MissionKinds.IsPlaceholder(kind.Title) || !byName.TryGetValue(PlainName(kind.Title), out uuid)) continue;
                }
                kind.WikiUuid = uuid;
                linked++;
            }
            if (linked == 0) return 0;
            db.SaveChanges();
            Changed();
            return linked;
        }
    }

    /// <summary>The contract ids of every mission in the library (the live logs' and the old ones'), the newest first: what to ask the Wiki API about.</summary>
    public List<string> Contracts()
    {
        lock (_lock)
        {
            using var db = _store.Open();
            return db.Missions.AsNoTracking().Where(m => m.Definition != null).ToList()
                .GroupBy(m => m.Definition!, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Max(m => m.StartedUtc ?? DateTime.MinValue))
                .Select(g => g.Key).ToList();
        }
    }

    /// <summary>A mission's name without what a string mod adds to it ("[200 Rep] [BP]*"): what the Wiki calls it.</summary>
    public static string PlainName(string title) => System.Text.RegularExpressions.Regex.Replace(title, @"\s*\[[^\]]*\]|\*", "").Trim();

    /// <summary>The id of a kind's row on the page (a key has spaces, # and the like).</summary>
    public static string Anchor(string key) => "m-" + Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key)))[..12].ToLowerInvariant();

    /// <summary>
    /// Once, before anything else: the missions written before the library went by name get their kind (from their name; the old logs are read again for
    /// the ones of the Hauling Planner), the Hauling Planner's contract lines are there with what it pays now, and what was typed in the library by
    /// ContractId is carried over to the kinds.
    /// </summary>
    void Prepare(TrackerDb db)
    {
        if (_ready) return;
        _ready = true;
        var kinds = db.MissionKinds.ToDictionary(k => k.Key, StringComparer.OrdinalIgnoreCase);
        _readHistoryAgain = kinds.Count == 0 && db.Missions.Any();
        foreach (var m in db.Missions.Where(m => m.Kind == null && m.Title != null).ToList())
        {
            m.Kind = MissionKinds.TitleKey(m.Title);
            if (m.Kind != null) KindOf(db, kinds, m.Kind, MissionKinds.KindTitle(m.Title)!);
        }
        Regroup(db, kinds);
        db.SaveChanges();
    }

    /// <summary>
    /// The missions whose kind is another one now (the bounties go by their kind and risk since 28 Oct, not by their target; the Hauling Planner's contracts by
    /// their names since 29 Oct, not by their contract line) go to the kind they are now; what was typed in for the old kind goes with them where the new
    /// one has nothing yet (not another name: that was the target's), and an old kind with no mission left goes.
    /// </summary>
    static void Regroup(TrackerDb db, Dictionary<string, MissionKindRow> kinds)
    {
        var missions = db.Missions.Where(m => m.Kind != null && m.Title != null).ToList();
        var left = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in missions)
        {
            if (MissionKinds.TitleKey(m.Title) is not { } now || string.Equals(now, m.Kind, StringComparison.OrdinalIgnoreCase)) continue;
            var to = KindOf(db, kinds, now, MissionKinds.KindTitle(m.Title)!);
            if (kinds.TryGetValue(m.Kind!, out var was))
            {
                to.Auec ??= was.Auec;
                to.MgScrip ??= was.MgScrip;
                to.CouncilScrip ??= was.CouncilScrip;
                to.Giver ??= was.Giver;
                to.Scu ??= was.Scu;
                if (to.ScuSuggested == null) (to.ScuSuggested, to.ScuSuggestedBy, to.ScuSuggestedUtc) = (was.ScuSuggested, was.ScuSuggestedBy, was.ScuSuggestedUtc);
                to.Notes ??= was.Notes;
                if (was.EditedUtc != null && (to.EditedUtc == null || was.EditedUtc > to.EditedUtc)) (to.EditedBy, to.EditedUtc) = (was.EditedBy, was.EditedUtc);
            }
            left.Add(m.Kind!);
            m.Kind = now;
        }
        foreach (var old in left)
        {
            if (missions.Any(m => string.Equals(m.Kind, old, StringComparison.OrdinalIgnoreCase)) || !kinds.TryGetValue(old, out var k)) continue;
            db.MissionKinds.Remove(k);
            kinds.Remove(old);
        }
    }

    /// <summary>The kind with this key, made when it is not there yet.</summary>
    static MissionKindRow KindOf(TrackerDb db, Dictionary<string, MissionKindRow> kinds, string key, string title)
    {
        if (kinds.TryGetValue(key, out var k)) return k;
        k = new MissionKindRow { Key = key, Title = title };
        db.MissionKinds.Add(k);
        kinds[key] = k;
        return k;
    }

    static MissionSeen Seen(MissionRow r) =>
        new(r.MissionId, r.ContractId.Length == 0 ? null : r.ContractId, r.Title, r.Shared, r.StartedUtc, r.EndedUtc, Enum.TryParse<MissionOutcome>(r.Outcome, out var o) ? o : null,
            r.Kind != null && MissionKinds.IsHauling(r.Kind) ? r.Kind[MissionKinds.HaulingPrefix.Length..] : null, r.Definition, r.PlayerId,
            r.Members.Count > 0 ? r.Members : null, r.Rewards);

    long _collected = -1;   // Uploads.Pieces when the live logs were last looked at

    /// <summary>
    /// Writes the missions of the live logs that are new or went on, when anything came in since the last time (this runs every two seconds, and for every
    /// page that shows missions: with nothing new, the library has it all and the database is left alone). Returns how many were written.
    /// </summary>
    public int Collect()
    {
        var pieces = _uploads.Pieces;
        if (pieces == _collected) return 0;
        var written = Add(_uploads.MissionsSeen());
        _collected = pieces;
        return written;
    }

    /// <summary>
    /// When a mission was taken: the earliest a log says (the game says every contract he has "Contract Accepted" again when he logs in, and an old log can
    /// come after the live one), so the contracts tracker knows who accepted it first.
    /// </summary>
    static DateTime? Earliest(DateTime? a, DateTime? b) => a == null ? b : b == null || a <= b ? a : b;

    /// <summary>What two logs said about the other players of a mission, together, in time order (null: nothing).</summary>
    static IReadOnlyList<MissionMember>? Union(IReadOnlyList<MissionMember>? a, IReadOnlyList<MissionMember>? b)
    {
        if (b == null || b.Count == 0) return a;
        if (a == null || a.Count == 0) return b;
        return a.Concat(b).Distinct().OrderBy(e => e.Utc).ToList();
    }

    /// <summary>Writes these missions where they are new or went on (an old log a tracker sent, or the live logs). Returns how many were written.</summary>
    public int Add(IReadOnlyDictionary<string, IReadOnlyList<MissionSeen>> found)
    {
        lock (_lock)
        {
            using var db = _store.Open();
            Prepare(db);
            _known ??= db.Missions.AsNoTracking().AsEnumerable().ToDictionary(r => (r.Player.ToLowerInvariant(), r.MissionId.ToLowerInvariant()), Seen);
            Dictionary<string, MissionKindRow>? kinds = null;   // read only when a mission is written (most ticks write none)
            var pending = new Dictionary<(string, string), MissionSeen>();   // noted once the database took them: a write that fails is tried again
            var written = 0;
            foreach (var (player, missions) in found)
            {
                foreach (var m in missions)
                {
                    // a mission is in it also when the log never says which offer it is (most missions, like the bounties): one he took himself, and
                    // one shared with him (marked Shared: the contracts tracker splits it between who had it, and his own plays on the library's page
                    // count it; until #385 it was left to the log of the one who shared it); not one without a name
                    if (m.ContractId == null && m.Hauling == null && m.Title == null) continue;
                    var key = (player.ToLowerInvariant(), m.MissionId.ToLowerInvariant());
                    if (!pending.TryGetValue(key, out var had)) _known.TryGetValue(key, out had);
                    // shared with him once any of his logs says so (an old log read again knows the share, the live one only the login's accept)
                    var merged = had == null ? m : new MissionSeen(had.MissionId, had.ContractId ?? m.ContractId, MissionKinds.Better(had.Title, m.Title), had.Shared || m.Shared,
                        Earliest(had.StartedUtc, m.StartedUtc), had.EndedUtc ?? m.EndedUtc, had.Outcome ?? m.Outcome, had.Hauling ?? m.Hauling, had.Definition ?? m.Definition,
                        had.PlayerId ?? m.PlayerId, Union(had.Members, m.Members), had.Rewards ?? m.Rewards);
                    if (merged.Same(had)) continue;
                    var row = had == null ? null : db.Missions.Find(player, had.MissionId) ?? db.Missions.FirstOrDefault(r => r.Player == player && r.MissionId == had.MissionId);
                    if (row == null) db.Missions.Add(row = new MissionRow { Player = player, MissionId = m.MissionId, ContractId = m.ContractId ?? "" });
                    if (merged.ContractId != null) row.ContractId = merged.ContractId;
                    row.Title = merged.Title;
                    row.Shared = merged.Shared;
                    row.StartedUtc = merged.StartedUtc;
                    row.EndedUtc = merged.EndedUtc;
                    row.Outcome = merged.Outcome?.ToString();
                    row.Definition = merged.Definition;
                    row.PlayerId = merged.PlayerId;
                    row.Members = merged.Members?.ToList() ?? new List<MissionMember>();
                    row.Rewards = merged.Rewards;
                    row.Kind = MissionKinds.KeyOf(merged.Title, merged.Hauling);
                    if (row.Kind != null)
                    {
                        kinds ??= db.MissionKinds.ToDictionary(k => k.Key, StringComparer.OrdinalIgnoreCase);
                        KindOf(db, kinds, row.Kind, MissionKinds.IsHauling(row.Kind) ? MissionKinds.HaulingTitle(merged.Hauling!) : MissionKinds.KindTitle(merged.Title)!);
                    }
                    pending[key] = merged;
                    written++;
                }
            }
            if (written > 0)
            {
                db.SaveChanges();
                foreach (var (key, seen) in pending) _known[key] = seen;
                Changed();
            }
            return written;
        }
    }

    /// <summary>
    /// What the old logs are read for (in the history folder, missions-read.txt, when they were read last): 2, the contract ids of their missions (for the Star
    /// Citizen Wiki API); 3, which missions were shared with the player (the share line has no mission id: his accept just after it says which, #385); 4,
    /// the player's id and what his log said about the other players of a mission (who joined and left it: the contracts tracker); 5, the missions the game
    /// took from the player at an exit to the menu (his own PlayerLeft with no EndMission, #399). A server that read them for less reads them again once.
    /// </summary>
    public const int HistoryReadVersion = 5;

    /// <summary>The file in the history folder that says what the old logs were read for last (HistoryReadVersion).</summary>
    public const string HistoryMarker = "missions-read.txt";

    int HistoryReadFor()
    {
        var path = Path.Combine(_paths.HistoryDir, HistoryMarker);
        try
        {
            return File.Exists(path) && int.TryParse(File.ReadAllText(path).Trim(), out var v) ? v : 0;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    /// <summary>
    /// The first time (no mission in the database yet, or the library went by ContractId before), and once more when there is more to read in them
    /// (HistoryReadVersion): the missions of the old logs already on the server (the history folder), so the library starts with everything played before it
    /// existed. Returns how many logs were read.
    /// </summary>
    public int ReadKeptHistory()
    {
        lock (_lock)
        {
            using var db = _store.Open();
            Prepare(db);
            var again = _readHistoryAgain || (Directory.Exists(_paths.HistoryDir) && HistoryReadFor() < HistoryReadVersion);
            _readHistoryAgain = false;
            if ((db.Missions.Any() && !again) || !Directory.Exists(_paths.HistoryDir)) return 0;
            _known = null;   // read from the database again
            Changed();
        }
        var read = 0;
        foreach (var playerDir in Directory.GetDirectories(_paths.HistoryDir))
        {
            var player = Path.GetFileName(playerDir);
            foreach (var file in Directory.GetFiles(playerDir, "*.log.gz"))
            {
                try
                {
                    using var gz = new GZipStream(File.OpenRead(file), CompressionMode.Decompress);
                    using var reader = new StreamReader(gz, Encoding.UTF8);
                    var lines = new List<string>();
                    while (reader.ReadLine() is { } line) lines.Add(line);
                    var log = OldLog.Read(lines);
                    if (log.Player != null && !string.Equals(log.Player, player, StringComparison.OrdinalIgnoreCase)) continue;   // another player's game
                    Add(new Dictionary<string, IReadOnlyList<MissionSeen>> { [player] = log.Missions });
                    read++;
                }
                catch (IOException) { }
                catch (InvalidDataException) { }
            }
        }
        try
        {
            File.WriteAllText(Path.Combine(_paths.HistoryDir, HistoryMarker), HistoryReadVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (IOException) { }   // read again the next time
        return read;
    }

    /// <summary>
    /// The kind of mission a link names (a screenshot button: #437), by its key, else by its name as the game shows it (MissionKinds.TitleKey: a bounty by its
    /// kind), else a Hauling Planner contract line ("2 To Ruin"); null when the library has none.
    /// </summary>
    public LibraryEntry? EntryOf(string? keyOrName)
    {
        if (string.IsNullOrWhiteSpace(keyOrName)) return null;
        var byKey = Entries().ToDictionary(e => e.Row.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var key in new[] { keyOrName.Trim(), MissionKinds.TitleKey(keyOrName), MissionKinds.HaulingKey(keyOrName) })
            if (key != null && byKey.TryGetValue(key, out var e)) return e;
        return null;
    }

    /// <summary>Every kind of mission in the library with how it was played, by name (the pages sort them as the viewer asks).</summary>
    public List<LibraryEntry> Entries()
    {
        lock (_lock)
        {
            if (_entries != null) return _entries;
            using var db = _store.Open();
            Prepare(db);
            var kinds = db.MissionKinds.AsNoTracking().ToList();
            var played = db.Missions.AsNoTracking().Where(m => m.Kind != null).ToList().GroupBy(m => m.Kind!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
            var wiki = WikiMissions.Known(db);
            var now = DateTime.UtcNow;
            static PlayCount Counted(IReadOnlyList<MissionRow> rows, DateTime now) =>
                new(rows.Count, rows.Count(m => m.Outcome == nameof(MissionOutcome.Complete)), rows.Count(m => m.Outcome == nameof(MissionOutcome.Fail)),
                    rows.Count(m => m.Outcome == nameof(MissionOutcome.Abandon)), rows.Select(m => m.EndedUtc ?? m.StartedUtc).Max(),
                    rows.Any(m => m.Outcome == null && m.EndedUtc == null && m.StartedUtc > now - RunningFor));
            return _entries = kinds.Select(k =>
                {
                    var rows = played.GetValueOrDefault(k.Key) ?? new List<MissionRow>();
                    var all = Counted(rows, now);
                    var each = rows.GroupBy(m => m.Player.ToLowerInvariant()).ToDictionary(g => g.Key, g => Counted(g.ToList(), now));
                    return new LibraryEntry(k, all.Taken, all.Completed, all.Failed, all.Abandoned, all.LastPlayed, all.Running,
                        k.WikiUuid != null ? wiki.GetValueOrDefault(k.WikiUuid) : null, each);
                })
                .OrderBy(e => e.Shown, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    /// <summary>
    /// The kinds of these keys, by key. One that is not there yet is made from the one given, and one nobody changed yet gets what it does not know from it:
    /// how a tool hands over the rewards it had before the library had them (the Gauntlet Tracker's scenarios).
    /// </summary>
    public Dictionary<string, MissionKindRow> Kinds(IReadOnlyList<MissionKindRow> wanted)
    {
        lock (_lock)
        {
            using var db = _store.Open();
            Prepare(db);
            var kinds = db.MissionKinds.ToDictionary(k => k.Key, StringComparer.OrdinalIgnoreCase);
            var changed = false;
            foreach (var w in wanted)
            {
                if (!kinds.TryGetValue(w.Key, out var k))
                {
                    db.MissionKinds.Add(k = new MissionKindRow { Key = w.Key, Title = w.Title, Auec = w.Auec, MgScrip = w.MgScrip, CouncilScrip = w.CouncilScrip });
                    kinds[w.Key] = k;
                    changed = true;
                }
                else if (k.EditedUtc == null && ((k.Auec == null && w.Auec != null) || (k.MgScrip == null && w.MgScrip != null) || (k.CouncilScrip == null && w.CouncilScrip != null)))
                {
                    k.Auec ??= w.Auec;
                    k.MgScrip ??= w.MgScrip;
                    k.CouncilScrip ??= w.CouncilScrip;
                    changed = true;
                }
            }
            if (changed)
            {
                db.SaveChanges();
                Changed();
            }
            return wanted.ToDictionary(w => w.Key, w => kinds[w.Key], StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// What every kind of mission pays, by its key: the aUEC typed in, else the Star Citizen Wiki's reward (the middle of its range), and the MG and Council
    /// scrip typed in. The contracts tracker counts every contract's money with it.
    /// </summary>
    public Dictionary<string, ContractReward> Rewards()
    {
        lock (_lock)
        {
            using var db = _store.Open();
            Prepare(db);
            var wiki = WikiMissions.Known(db);
            return db.MissionKinds.AsNoTracking().ToList().ToDictionary(k => k.Key,
                k => new ContractReward(k.Auec ?? (k.WikiUuid != null ? wiki.GetValueOrDefault(k.WikiUuid)?.Auec : null), k.MgScrip, k.CouncilScrip),
                StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>The missions these players ended between these times (any capitals), the newest first.</summary>
    public List<EndedMission> Ended(IReadOnlyCollection<string> players, DateTime fromUtc, DateTime toUtc)
    {
        var wanted = new HashSet<string>(players, StringComparer.OrdinalIgnoreCase);
        lock (_lock)
        {
            using var db = _store.Open();
            Prepare(db);
            return db.Missions.AsNoTracking().Where(m => m.Outcome != null && m.EndedUtc != null && m.EndedUtc >= fromUtc && m.EndedUtc <= toUtc).ToList()
                .Where(m => wanted.Contains(m.Player) && Enum.TryParse<MissionOutcome>(m.Outcome, out _))
                .Select(m => new EndedMission(m.Player, m.MissionId, m.Title, m.Kind, m.EndedUtc!.Value, Enum.Parse<MissionOutcome>(m.Outcome!)))
                .OrderByDescending(m => m.EndedUtc).ToList();
        }
    }

    /// <summary>The fields a player can type in.</summary>
    public static readonly string[] Fields = { "name", "giver", "scu", "auec", "mg", "council", "notes" };

    /// <summary>
    /// Changes one field of a kind of mission (empty: not known). The aUEC of a kind the Hauling Planner reads is what the planner pays for every such
    /// contract (empty: the Star Citizen Wiki's reward, else its built-in one). The sentence for the page, or null when that is not a field or a number.
    /// </summary>
    public string? Set(string key, string field, string? value, string who, DateTime nowUtc)
    {
        value = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        long? number = null;
        if (field is "auec" or "mg" or "council" or "scu" && value != null)
        {
            if (!long.TryParse(value.Replace(" ", "").Replace(",", "").Replace(".", ""), out var n) || n < 0) return null;
            number = n;
        }
        lock (_lock)
        {
            using var db = _store.Open();
            Prepare(db);
            var row = db.MissionKinds.Find(key);
            if (row == null) return null;
            switch (field)
            {
                case "name": row.Name = value; break;
                case "giver": row.Giver = value; break;
                case "notes": row.Notes = value; break;
                case "auec": row.Auec = number; break;
                case "mg": row.MgScrip = number; break;
                case "council": row.CouncilScrip = number; break;
                case "scu": row.Scu = number; break;
                default: return null;
            }
            row.EditedBy = who;
            row.EditedUtc = nowUtc;
            db.SaveChanges();
            Changed();
        }
        return "Saved.";   // (the Hauling Planner reads the kinds as its JobBook: a contract of this kind pays this from now on)
    }
}

/// <summary>
/// Reads the old logs into the Mission Library once when the server starts (when it has none of them yet) and gives the tracker the library's SCU; then
/// every minute: the Star Citizen Wiki API's list of every mission is read when it was not read in the last day (WikiMissions.LearnAll: every kind gets
/// its faction, category, reward, reputation and legality; in the loop, not at the start, so the tests' servers never reach the API), the SCU the game said
/// for a hauling job becomes the library's suggestion for its kind, and the API is
/// asked about up to 10 contracts of the library's missions (the live logs' and the old ones') it was not asked about yet (WikiMissions.Learn: their
/// hauling orders): until it was asked about every one, then only about a new one.
/// </summary>
public sealed class MissionLibraryService : BackgroundService
{
    readonly MissionLibrary _library;
    readonly WikiMissions _wiki;
    readonly ILogger<MissionLibraryService> _log;

    public MissionLibraryService(MissionLibrary library, WikiMissions wiki, ILogger<MissionLibraryService> log)
    {
        _library = library;
        _wiki = wiki;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        try
        {
            var read = await Task.Run(_library.ReadKeptHistory, stop);
            if (read > 0) _log.LogInformation("The Mission Library read {Count} old logs", read);
            _library.PushBook();   // the tracker gets the library's SCU of the hauling jobs
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "Reading the old logs into the Mission Library failed");
        }
        // every minute: what the game said the SCU of the hauling jobs is becomes the library's suggestion for their kinds
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stop);
                _library.SuggestFromGame(DateTime.UtcNow);
                // the contracts of every mission in the library (the live logs' too) the Wiki API was not asked about yet, the newest first: up to 10 a minute
                _library.Collect();
                await ReadList(stop);   // once a day (the first time within a minute of the start)
                var learned = await _wiki.Learn(_library.Contracts(), DateTime.UtcNow, stop);
                if (_library.LinkWiki() == 0 && learned > 0) _library.WikiLearned();   // (a new link made the book and the list again already)
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log.LogError(ex, "Suggesting the SCU the game said failed");
            }
        }
    }

    /// <summary>The list of every mission of the Wiki, when it is due (once a day): then every kind that can be linked to one is, and the pages show what it says.</summary>
    async Task ReadList(CancellationToken stop)
    {
        var listed = await _wiki.LearnAllIfDue(DateTime.UtcNow, stop);
        if (listed == 0) return;
        _log.LogInformation("The Star Citizen Wiki's list has {Count} missions", listed);
        if (_library.LinkWiki() == 0) _library.WikiLearned();
    }
}
