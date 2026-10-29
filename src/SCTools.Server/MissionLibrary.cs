using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SCTools.Missions;
using SCTools.Server.Data;

namespace SCTools.Server;

/// <summary>A kind of mission in the library, with how it was played; Running: one of its missions was taken in the last hours and did not end yet.</summary>
public sealed record LibraryEntry(MissionKindRow Row, int Taken, int Completed, int Failed, int Abandoned, DateTime? LastPlayed, bool Running = false)
{
    /// <summary>The name shown: the one typed in, else the one the game shows.</summary>
    public string Shown => Row.Name ?? Row.Title;

    /// <summary>Nothing typed in about its rewards yet.</summary>
    public bool NeedsInfo => Row.Auec == null && Row.MgScrip == null && Row.CouncilScrip == null;

    /// <summary>A contract line of the Hauling Planner: its aUEC is what the planner pays for it.</summary>
    public bool Hauling => MissionKinds.IsHauling(Row.Key);

    /// <summary>The id of its row on the page.</summary>
    public string Anchor => MissionLibrary.Anchor(Row.Key);

    /// <summary>The tool that counts with its rewards besides the Session tool (which counts with all of them), or null.</summary>
    public string? UsedBy => Hauling ? "the Hauling Planner pays its aUEC"
        : Row.Key.StartsWith("combat gauntlet - scenario #", StringComparison.Ordinal) ? "the Gauntlet Tracker counts with it" : null;
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
    }
    bool _ready;
    bool _readHistoryAgain;

    public MissionLibrary(DataStore store, LogUploads uploads, ServerPaths paths, LiveTracker live)
    {
        _store = store;
        _uploads = uploads;
        _paths = paths;
        _live = live;
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
            r.Kind != null && MissionKinds.IsHauling(r.Kind) ? r.Kind[MissionKinds.HaulingPrefix.Length..] : null);

    /// <summary>Writes the missions of the live logs that are new or went on. Returns how many were written.</summary>
    public int Collect() => Add(_uploads.MissionsSeen());

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
                        had.StartedUtc ?? m.StartedUtc, had.EndedUtc ?? m.EndedUtc, had.Outcome ?? m.Outcome, had.Hauling ?? m.Hauling);
                    if (merged == had) continue;
                    var row = had == null ? null : db.Missions.Find(player, had.MissionId) ?? db.Missions.FirstOrDefault(r => r.Player == player && r.MissionId == had.MissionId);
                    if (row == null) db.Missions.Add(row = new MissionRow { Player = player, MissionId = m.MissionId, ContractId = m.ContractId ?? "" });
                    if (merged.ContractId != null) row.ContractId = merged.ContractId;
                    row.Title = merged.Title;
                    row.Shared = merged.Shared;
                    row.StartedUtc = merged.StartedUtc;
                    row.EndedUtc = merged.EndedUtc;
                    row.Outcome = merged.Outcome?.ToString();
                    row.Kind = MissionKinds.KeyOf(merged.Title, merged.Hauling);
                    if (row.Kind != null) KindOf(db, kinds, row.Kind, merged.Hauling != null ? MissionKinds.HaulingTitle(merged.Hauling) : MissionKinds.KindTitle(merged.Title)!);
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
    /// The first time (no mission in the database yet, or the library went by ContractId before): the missions of the old logs already on the server (the
    /// history folder), so the library starts with everything played before it existed. Returns how many logs were read.
    /// </summary>
    public int ReadKeptHistory()
    {
        lock (_lock)
        {
            using var db = _store.Open();
            Prepare(db);
            var again = _readHistoryAgain;
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
        return read;
    }

    /// <summary>Every kind of mission in the library with how it was played, the one played last first (the ones never played after them, by name).</summary>
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
            return _entries = kinds.Select(k =>
                {
                    var mine = played.GetValueOrDefault(k.Key) ?? new List<MissionRow>();
                    return new LibraryEntry(k, mine.Count, mine.Count(m => m.Outcome == nameof(MissionOutcome.Complete)),
                        mine.Count(m => m.Outcome == nameof(MissionOutcome.Fail)), mine.Count(m => m.Outcome == nameof(MissionOutcome.Abandon)),
                        mine.Select(m => m.EndedUtc ?? m.StartedUtc).Max(),
                        mine.Any(m => m.Outcome == null && m.EndedUtc == null && m.StartedUtc > DateTime.UtcNow - RunningFor));
                })
                .OrderByDescending(e => e.LastPlayed ?? DateTime.MinValue).ThenBy(e => e.Shown, StringComparer.OrdinalIgnoreCase).ToList();
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
            return db.Missions.AsNoTracking().Where(m => m.Player == player && m.Outcome == nameof(MissionOutcome.Complete) && m.EndedUtc != null).ToList()
                .Select(m =>
                {
                    var r = m.Kind == null ? null : rewards.GetValueOrDefault(m.Kind);
                    return new SCTools.Play.EarnedMission(m.EndedUtc!.Value, m.Kind ?? m.ContractId, m.Shared, r?.Auec, r?.MgScrip, r?.CouncilScrip);
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

/// <summary>Reads the old logs into the Mission Library once when the server starts (when it has none of them yet).</summary>
public sealed class MissionLibraryService : BackgroundService
{
    readonly MissionLibrary _library;
    readonly ILogger<MissionLibraryService> _log;

    public MissionLibraryService(MissionLibrary library, ILogger<MissionLibraryService> log)
    {
        _library = library;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        try
        {
            var read = await Task.Run(_library.ReadKeptHistory, stop);
            if (read > 0) _log.LogInformation("The Mission Library read {Count} old logs", read);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "Reading the old logs into the Mission Library failed");
        }
    }
}
