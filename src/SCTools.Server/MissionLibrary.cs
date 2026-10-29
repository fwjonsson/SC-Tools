using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SCTools.Missions;
using SCTools.Server.Data;

namespace SCTools.Server;

/// <summary>How one player played a kind of mission; Running: one of his missions of it was taken in the last hours and did not end yet.</summary>
public sealed record PlayCount(int Taken, int Completed, int Failed, int Abandoned, DateTime? LastPlayed, bool Running);

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
/// Council scrip it pays, another name, the giver, notes. The other tools count with it: the Session tool (what your missions earned), the Gauntlet Tracker
/// (the rewards of the scenarios) and the Hauling Planner (what a contract line pays). The first time, the old logs already on the server are read.
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
            var missions = db.HaulScu.AsNoTracking().ToDictionary(r => r.MissionId.ToLowerInvariant(), r => r.Scu, StringComparer.Ordinal);
            var kinds = db.MissionKinds.AsNoTracking().ToList()
                .ToDictionary(k => k.Key, k => new JobKind(k.Scu, k.ScuSuggested, k.ScuSuggestedBy, k.Auec), StringComparer.OrdinalIgnoreCase);
            var places = _places;
            _live.Tracker.State.Jobs = new JobBook(missions, kinds, () => places?.Current ?? Atlas.Default, WikiMissions.Known(db));
        }
    }

    /// <summary>The SCU a hauler set for a mission as a sentence for the page, or null when it is not a whole number.</summary>
    public string? SetMissionScu(string missionId, string? title, string? value, string who, DateTime nowUtc)
    {
        value = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        long? scu = null;
        if (value != null)
        {
            if (!long.TryParse(value.Replace(" ", "").Replace(",", "").Replace(".", ""), out var n) || n <= 0) return null;
            scu = n;
        }
        if (string.IsNullOrWhiteSpace(missionId)) return null;
        var id = missionId.Trim().ToLowerInvariant();
        lock (_lock)
        {
            using var db = _store.Open();
            Prepare(db);
            var row = db.HaulScu.Find(id);
            if (scu == null)
            {
                if (row != null) db.HaulScu.Remove(row);
            }
            else
            {
                if (row == null) db.HaulScu.Add(row = new HaulScuRow { MissionId = id });
                (row.Scu, row.By, row.Utc) = (scu.Value, who, nowUtc);
                Suggest(db, title, scu.Value, who, nowUtc);   // his number helps the next one: the library's suggestion for the kind
            }
            db.SaveChanges();
            Changed();
        }
        return scu == null ? "Saved: the mission has the game's or the library's SCU again." : "Saved: " + scu.Value.ToString("N0") + " SCU for this mission, and suggested in the Mission Library.";
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
    /// the old ones' (whatever names a string mod shows). A kind that has one keeps it. Returns how many kinds got one.
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
            var linked = 0;
            foreach (var kind in db.MissionKinds.Where(k => k.WikiUuid == null).ToList())
            {
                if (!newest.TryGetValue(kind.Key, out var uuid)) continue;
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
        foreach (var line in Earnings.RewardLines)
        {
            var key = MissionKinds.HaulingKey(line.Contract);
            if (kinds.ContainsKey(key)) continue;
            var pays = _live.Read(t => Earnings.StartReward(line, t.State.Data));
            KindOf(db, kinds, key, MissionKinds.HaulingTitle(line.Contract)).Auec = (long)Math.Round(pays);
        }
        NameHaulingKinds(db, kinds);
        db.SaveChanges();

        // what was typed in the library by ContractId: to the kind most of its missions have
        var typed = db.Library.ToList();
        if (typed.Count == 0) return;
        var kindOf = db.Missions.Where(m => m.Kind != null).Select(m => new { m.ContractId, m.Kind }).ToList()
            .GroupBy(m => m.ContractId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.GroupBy(m => m.Kind!).OrderByDescending(k => k.Count()).First().Key, StringComparer.OrdinalIgnoreCase);
        foreach (var old in typed)
        {
            var any = old.Name != null || old.Giver != null || old.Auec != null || old.MgScrip != null || old.CouncilScrip != null || old.Notes != null;
            if (any)
            {
                if (!kindOf.TryGetValue(old.ContractId, out var key) || !kinds.TryGetValue(key, out var k)) continue;   // no mission with a name: it stays
                k.Name ??= old.Name;
                k.Giver ??= old.Giver;
                k.Auec ??= old.Auec;
                k.MgScrip ??= old.MgScrip;
                k.CouncilScrip ??= old.CouncilScrip;
                k.Notes ??= old.Notes;
                k.EditedBy ??= old.EditedBy;
                k.EditedUtc ??= old.EditedUtc;
            }
            db.Library.Remove(old);
        }
        db.SaveChanges();
    }

    /// <summary>
    /// The missions whose kind is another one now (the bounties go by their kind and risk since 28 Oct, not by their target) go to the kind they are now; what
    /// was typed in for the old kind goes with them where the new one has nothing yet (not another name: that was the target's), and an old kind with no
    /// mission left goes.
    /// </summary>
    static void Regroup(TrackerDb db, Dictionary<string, MissionKindRow> kinds)
    {
        var missions = db.Missions.Where(m => m.Kind != null && m.Title != null).ToList();
        var left = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in missions)
        {
            if (MissionKinds.IsHauling(m.Kind!) || MissionKinds.TitleKey(m.Title) is not { } now || string.Equals(now, m.Kind, StringComparison.OrdinalIgnoreCase)) continue;
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

    /// <summary>
    /// The kinds of the Hauling Planner that are still named by their contract line ("Hauling: 4 To Ruin", from before they were named like every other
    /// kind) get the name the game shows for their missions: the one most of them have (a string mod names them differently on another PC).
    /// </summary>
    static void NameHaulingKinds(TrackerDb db, Dictionary<string, MissionKindRow> kinds)
    {
        var placeholders = kinds.Values.Where(k => MissionKinds.IsHauling(k.Key) && MissionKinds.IsPlaceholder(k.Title)).ToList();
        if (placeholders.Count == 0) return;
        var titles = db.Missions.Where(m => m.Kind != null && m.Title != null).Select(m => new { m.Kind, m.Title }).ToList()
            .Where(m => !MissionKinds.Unfilled(m.Title)).GroupBy(m => m.Kind!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(m => MissionKinds.KindTitle(m.Title)!).GroupBy(t => t).OrderByDescending(t => t.Count()).First().Key, StringComparer.OrdinalIgnoreCase);
        foreach (var k in placeholders)
            if (titles.TryGetValue(k.Key, out var named)) k.Title = named;
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
            r.Kind != null && MissionKinds.IsHauling(r.Kind) ? r.Kind[MissionKinds.HaulingPrefix.Length..] : null, r.Definition);

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

    /// <summary>Writes these missions where they are new or went on (an old log a tracker sent, or the live logs). Returns how many were written.</summary>
    public int Add(IReadOnlyDictionary<string, IReadOnlyList<MissionSeen>> found)
    {
        lock (_lock)
        {
            using var db = _store.Open();
            Prepare(db);
            _known ??= db.Missions.AsNoTracking().AsEnumerable().ToDictionary(r => (r.Player.ToLowerInvariant(), r.MissionId.ToLowerInvariant()), Seen);
            var kinds = db.MissionKinds.ToDictionary(k => k.Key, StringComparer.OrdinalIgnoreCase);
            var written = 0;
            foreach (var (player, missions) in found)
            {
                foreach (var m in missions)
                {
                    // a mission he took himself is in it, also when the log never says which offer it is (most missions with comms, like the bounties); a
                    // mission shared with him whose offer never showed is the other account's (it is in the library from that log), unless the Hauling
                    // Planner reads it
                    if (m.ContractId == null && m.Hauling == null && (m.Shared || m.Title == null)) continue;
                    var key = (player.ToLowerInvariant(), m.MissionId.ToLowerInvariant());
                    _known.TryGetValue(key, out var had);
                    var merged = had == null ? m : new MissionSeen(had.MissionId, had.ContractId ?? m.ContractId, MissionKinds.Better(had.Title, m.Title), had.Shared,
                        had.StartedUtc ?? m.StartedUtc, had.EndedUtc ?? m.EndedUtc, had.Outcome ?? m.Outcome, had.Hauling ?? m.Hauling, had.Definition ?? m.Definition);
                    if (merged == had) continue;
                    var row = had == null ? null : db.Missions.Find(player, had.MissionId) ?? db.Missions.FirstOrDefault(r => r.Player == player && r.MissionId == had.MissionId);
                    if (row == null) db.Missions.Add(row = new MissionRow { Player = player, MissionId = m.MissionId, ContractId = m.ContractId ?? "" });
                    if (merged.ContractId != null) row.ContractId = merged.ContractId;
                    row.Title = merged.Title;
                    row.Shared = merged.Shared;
                    row.StartedUtc = merged.StartedUtc;
                    row.EndedUtc = merged.EndedUtc;
                    row.Outcome = merged.Outcome?.ToString();
                    row.Definition = merged.Definition;
                    row.Kind = MissionKinds.KeyOf(merged.Title, merged.Hauling);
                    if (row.Kind != null)
                    {
                        var kind = KindOf(db, kinds, row.Kind, merged.Hauling != null ? MissionKinds.HaulingTitle(merged.Hauling) : MissionKinds.KindTitle(merged.Title)!);
                        // a kind of the Hauling Planner is named as the game names its missions, like every other kind, as soon as one of them is seen
                        if (merged.Hauling != null && MissionKinds.IsPlaceholder(kind.Title) && MissionKinds.KindTitle(merged.Title) is { } named && !MissionKinds.Unfilled(named))
                            kind.Title = named;
                    }
                    _known[key] = merged;
                    written++;
                }
            }
            if (written > 0)
            {
                db.SaveChanges();
                Changed();
            }
            return written;
        }
    }

    /// <summary>
    /// What the old logs are read for (in the history folder, missions-read.txt, when they were read last): 2, the contract ids of their missions (for the Star
    /// Citizen Wiki API). A server that read them for less reads them again once.
    /// </summary>
    public const int HistoryReadVersion = 2;

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

    /// <summary>The missions this player completed, with what the library says they pay (worked out now: a reward typed in later counts for the past).</summary>
    public List<SCTools.Play.EarnedMission> Earned(string player)
    {
        lock (_lock)
        {
            using var db = _store.Open();
            Prepare(db);
            var rewards = db.MissionKinds.AsNoTracking().ToDictionary(k => k.Key, StringComparer.OrdinalIgnoreCase);
            var wiki = WikiMissions.Known(db);
            return db.Missions.AsNoTracking().Where(m => m.Player == player && m.Outcome == nameof(MissionOutcome.Complete) && m.EndedUtc != null).ToList()
                .Select(m =>
                {
                    var r = m.Kind == null ? null : rewards.GetValueOrDefault(m.Kind);
                    var auec = r?.Auec ?? (r?.WikiUuid != null ? wiki.GetValueOrDefault(r.WikiUuid)?.Auec : null);   // nothing typed in: the Wiki's reward
                    return new SCTools.Play.EarnedMission(m.EndedUtc!.Value, m.Kind ?? m.ContractId, m.Shared, auec, r?.MgScrip, r?.CouncilScrip);
                })
                .OrderBy(m => m.EndedUtc).ToList();
        }
    }

    /// <summary>The fields a player can type in.</summary>
    public static readonly string[] Fields = { "name", "giver", "scu", "auec", "mg", "council", "notes" };

    /// <summary>
    /// Changes one field of a kind of mission (empty: not known). The aUEC of a Hauling Planner kind is what the planner pays for that contract line: the
    /// session it shows gets it, and every new session (empty: its built-in reward). The sentence for the page, or null when that is not a field or a number.
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
        if (field == "auec" && Earnings.RewardLines.FirstOrDefault(l => MissionKinds.HaulingKey(l.Contract) == key) is { } line)
        {
            _live.Edit(t => Earnings.SetReward(t.State.Data, t.State.Inputs, line, number ?? 0), nowUtc);
            return "Saved: the Hauling Planner pays this for " + line.Contract + " now.";
        }
        return "Saved.";
    }
}

/// <summary>
/// Reads the old logs into the Mission Library once when the server starts (when it has none of them yet) and gives the tracker the library's SCU; then
/// every minute the SCU the game said for a hauling job becomes the library's suggestion for its kind, and the Star Citizen Wiki API is asked about up to
/// 10 contracts of the library's missions (the live logs' and the old ones') it was not asked about yet (WikiMissions): until it was asked about every one,
/// then only about a new one.
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
}
