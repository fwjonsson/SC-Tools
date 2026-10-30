using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace SCTools.Server.Data;

/// <summary>
/// The data of the web app in its database. The core works with the data in memory (AppData, the same as the program's data file); this reads it from the
/// database when the server starts, and writes what changed back: the tracker says when it changed something (Changed), and Flush writes it at most every
/// two seconds. The first time, the program's data file goes into the empty database.
/// </summary>
public sealed class DataStore
{
    readonly DbContextOptions<TrackerDb> _options;
    readonly object _saveLock = new();
    volatile bool _changed;

    /// <summary>
    /// What the last save wrote, table by table: every row's key and the row as JSON (and the row, to delete it by). Only this store writes these tables, so
    /// it is what the database has: the next save compares with it and writes the rows that differ, instead of reading every row of every table to find
    /// them (Frank's history only grows: that read grew with it, under the lock the uploads wait for). Null until the first save after the start, which
    /// compares with the database itself.
    /// </summary>
    Dictionary<string, Dictionary<string, (string Json, object Row)>>? _written;
    DateTime _backedUp;

    public DataStore(DbContextOptions<TrackerDb> options) => _options = options;

    /// <summary>The database in this SQLite file (its folder is made when it is missing).</summary>
    public static DataStore ForFile(string file)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(file));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        return new DataStore(new DbContextOptionsBuilder<TrackerDb>().UseSqlite(ConnectionString(file)).Options);
    }

    /// <summary>How SQLite opens this file.</summary>
    public static string ConnectionString(string file) => "Data Source=" + file;

    /// <summary>A connection to the database, for one piece of work.</summary>
    public TrackerDb Open() => new(_options);

    /// <summary>Makes the database, or brings it up to date (the migrations in Data/Migrations that it does not have yet).</summary>
    public void Migrate()
    {
        using var db = Open();
        db.Database.Migrate();
        // write-ahead logging: a write no longer makes every read wait for it (the pages read while the refresh writes), and it costs the disk one sync instead
        // of two. SQLite keeps the setting in the file, so saying it at every start is enough; the daily copy (VACUUM INTO) is one whole file as before.
        db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
    }

    /// <summary>The data is in the database (it was imported, or started empty, once).</summary>
    public bool HasData()
    {
        using var db = Open();
        return db.Settings.Any();
    }

    /// <summary>
    /// The data, the first time from the program's data file (when there is one): the database is made or brought up to date, an empty one gets the data
    /// file, and the data is read. A data file that cannot be read stops the server, so the database never starts empty by mistake.
    /// </summary>
    public AppData LoadOrImport(string dataFile, Action<string> say)
    {
        Migrate();
        if (!HasData())
        {
            if (File.Exists(dataFile))
            {
                var data = JsonSerializer.Deserialize<AppData>(File.ReadAllText(dataFile))
                    ?? throw new InvalidDataException(dataFile + " has no data.");
                Save(data);
                say($"The data file {dataFile} is now in the database: {data.Party.Count} in the party, {data.Sessions.Count} sessions, {data.Transfers.Count} transfers, {data.Payouts.Count} payouts.");
            }
            else
            {
                Save(new AppData());
                say($"No data file at {dataFile}: the database starts empty.");
            }
        }
        return Load();
    }

    /// <summary>The data as it is in the database.</summary>
    public AppData Load()
    {
        using var db = Open();
        var rows = new DataRows
        {
            Settings = db.Settings.AsNoTracking().SingleOrDefault() ?? new SettingsRow(),
            Members = db.Members.AsNoTracking().ToList()
                .OrderBy(m => m.PartyPosition ?? int.MaxValue).ThenBy(m => m.Name, StringComparer.Ordinal).ToList(),
            PartyChanges = db.PartyChanges.AsNoTracking().ToList(),
            ManualSessions = db.ManualSessions.AsNoTracking().ToList(),
            Sessions = db.Sessions.AsNoTracking().ToList(),
            Transfers = db.Transfers.AsNoTracking().ToList(),
            Payouts = db.Payouts.AsNoTracking().ToList(),
            MissionHolders = db.MissionHolders.AsNoTracking().ToList(),
            PayoutOverrides = db.PayoutOverrides.AsNoTracking().ToList(),
            DroppedMissions = db.DroppedMissions.AsNoTracking().ToList(),
            TypedScu = db.TypedScu.AsNoTracking().ToList(),
            PairAdjust = db.PairAdjust.AsNoTracking().ToList(),
            LocationNames = db.LocationNames.AsNoTracking().ToList(),
        };
        return rows.ToAppData();
    }

    /// <summary>
    /// Writes the data: every table gets the rows of the data (a row that is the same is left alone, a changed one is updated, a new one added, one that is
    /// gone deleted), all in one transaction. Called with the lock of the data held, so nothing changes it meanwhile. The first save after the start compares
    /// with the database; every save after it with what the save before wrote (_written), so only the rows that changed are written and nothing is read.
    /// </summary>
    public void Save(AppData data)
    {
        lock (_saveLock)
        {
            var rows = DataRows.From(data);
            using var db = Open();
            var first = _written == null;
            var written = new Dictionary<string, Dictionary<string, (string Json, object Row)>>();
            void Table<TRow, TKey>(string name, DbSet<TRow> table, IEnumerable<TRow> wanted, Func<TRow, TKey> key) where TRow : class where TKey : notnull =>
                written[name] = first ? Sync(db, table, wanted, key) : Diff(db, table, wanted, key, _written![name]);
            Table("Settings", db.Settings, new[] { rows.Settings }, r => r.Id);
            Table("Members", db.Members, rows.Members, r => r.Name);
            Table("PartyChanges", db.PartyChanges, rows.PartyChanges, r => r.Position);
            Table("ManualSessions", db.ManualSessions, rows.ManualSessions, r => r.Position);
            Table("Sessions", db.Sessions, rows.Sessions, r => r.Key);
            Table("Transfers", db.Transfers, rows.Transfers, r => r.Position);
            Table("Payouts", db.Payouts, rows.Payouts, r => r.Position);
            Table("MissionHolders", db.MissionHolders, rows.MissionHolders, r => (r.MissionId, r.Member));
            Table("PayoutOverrides", db.PayoutOverrides, rows.PayoutOverrides, r => r.MissionId);
            Table("DroppedMissions", db.DroppedMissions, rows.DroppedMissions, r => r.MissionId);
            Table("TypedScu", db.TypedScu, rows.TypedScu, r => r.Objective);
            Table("PairAdjust", db.PairAdjust, rows.PairAdjust, r => r.Pair);
            Table("LocationNames", db.LocationNames, rows.LocationNames, r => r.LocationId);
            db.SaveChanges();
            _written = written;   // only once the database took it: a save that failed compares with the save before again
        }
    }

    static string Fingerprint(object row) => JsonSerializer.Serialize(row, row.GetType());

    static string KeyText<TKey>(TKey key) => Convert.ToString(key, CultureInfo.InvariantCulture) ?? "";

    /// <summary>The first save after the start: the table as it is in the database, made like the rows of the data. Returns what was written.</summary>
    static Dictionary<string, (string Json, object Row)> Sync<TRow, TKey>(TrackerDb db, DbSet<TRow> table, IEnumerable<TRow> wanted, Func<TRow, TKey> key)
        where TRow : class
        where TKey : notnull
    {
        var have = table.ToList().ToDictionary(key);
        var kept = new HashSet<TKey>();
        var written = new Dictionary<string, (string Json, object Row)>(StringComparer.Ordinal);
        foreach (var row in wanted)
        {
            var k = key(row);
            if (!kept.Add(k)) continue;   // the same key twice (a name twice in the party list): the first one counts
            if (have.TryGetValue(k, out var old)) db.Entry(old).CurrentValues.SetValues(row);
            else table.Add(row);
            written[KeyText(k)] = (Fingerprint(row), row);
        }
        foreach (var (k, old) in have)
        {
            if (!kept.Contains(k)) table.Remove(old);
        }
        return written;
    }

    /// <summary>A save after the first: the rows that are new are added, the ones that differ from the last save updated, the ones gone deleted; nothing is read.</summary>
    static Dictionary<string, (string Json, object Row)> Diff<TRow, TKey>(TrackerDb db, DbSet<TRow> table, IEnumerable<TRow> wanted, Func<TRow, TKey> key,
        Dictionary<string, (string Json, object Row)> before)
        where TRow : class
        where TKey : notnull
    {
        var written = new Dictionary<string, (string Json, object Row)>(StringComparer.Ordinal);
        foreach (var row in wanted)
        {
            var k = KeyText(key(row));
            if (written.ContainsKey(k)) continue;   // the same key twice: the first one counts
            var json = Fingerprint(row);
            written[k] = (json, row);
            if (!before.TryGetValue(k, out var was)) table.Add(row);
            else if (was.Json != json) table.Update(row);
        }
        foreach (var (k, was) in before)
        {
            if (!written.ContainsKey(k)) table.Remove((TRow)was.Row);
        }
        return written;
    }

    /// <summary>The save of the tracker: remembers that the data changed (Flush writes it).</summary>
    public void Changed(AppData _) => _changed = true;

    /// <summary>Writes the data when it changed since the last time. Called with the lock of the data held.</summary>
    public bool Flush(AppData data)
    {
        if (!_changed) return false;
        _changed = false;
        try
        {
            Save(data);
        }
        catch
        {
            _changed = true;   // tried again next time
            throw;
        }
        return true;
    }

    const int BackupsToKeep = 30;

    /// <summary>
    /// A copy of the database made once a day (tracker-2026-10-27.db in this folder), so a mistake can be undone; the newest 30 are kept. The copy is made
    /// by SQLite itself (VACUUM INTO), so it is whole even while the server writes.
    /// </summary>
    public bool BackupDaily(string dir, DateTime today)
    {
        today = today.Date;
        if (_backedUp == today) return false;
        _backedUp = today;   // once a day, also when it fails (it is tried again tomorrow, or when the server starts again)
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, "tracker-" + today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".db");
        var made = false;
        if (!File.Exists(target))
        {
            using var db = Open();
            var file = Path.GetFullPath(target);
            db.Database.ExecuteSql($"VACUUM INTO {file}");   // the file name as a parameter of the command
            made = true;
        }
        foreach (var old in Directory.GetFiles(dir, "tracker-*.db").OrderByDescending(f => f, StringComparer.Ordinal).Skip(BackupsToKeep))
            File.Delete(old);
        return made;
    }

    /// <summary>Which tools everybody sees, by tool (a tool not in it was never set: SiteTools says).</summary>
    public Dictionary<string, bool> ToolSettings()
    {
        using var db = Open();
        return db.Tools.AsNoTracking().ToDictionary(t => t.Id, t => t.Public, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Shows this tool to everybody, or only to the admin.</summary>
    public void SetToolPublic(string id, bool value)
    {
        using var db = Open();
        var row = db.Tools.Find(id);
        if (row == null) db.Tools.Add(new ToolRow { Id = id, Public = value });
        else row.Public = value;
        db.SaveChanges();
    }

    /// <summary>The keys of the trackers.</summary>
    public List<TrackerKeyRow> TrackerKeys()
    {
        using var db = Open();
        return db.TrackerKeys.AsNoTracking().OrderBy(k => k.Id).ToList();
    }

    /// <summary>Adds a key for this player (only its hash is kept).</summary>
    public TrackerKeyRow AddTrackerKey(string player, string key, DateTime nowUtc)
    {
        using var db = Open();
        var row = new TrackerKeyRow { Player = player.Trim(), KeyHash = Server.TrackerKeys.Hash(key), CreatedUtc = nowUtc };
        db.TrackerKeys.Add(row);
        db.SaveChanges();
        return row;
    }

    /// <summary>Takes a key back: the tracker that has it can no longer upload. The removed key, or null when there was none with this id.</summary>
    public TrackerKeyRow? RemoveTrackerKey(int id)
    {
        using var db = Open();
        var row = db.TrackerKeys.Find(id);
        if (row == null) return null;
        db.TrackerKeys.Remove(row);
        db.SaveChanges();
        return row;
    }

    /// <summary>
    /// The keys written in the settings that are not in the database yet are added to it (only their hash). Keys shorter than 16 characters are left out:
    /// they are too easy to guess. Says how many were added.
    /// </summary>
    public int ImportTrackerKeys(IReadOnlyDictionary<string, string> keys, DateTime nowUtc)
    {
        using var db = Open();
        var have = db.TrackerKeys.Select(k => k.KeyHash).ToHashSet();
        var added = 0;
        foreach (var (key, player) in keys)
        {
            if (key.Length < Server.TrackerKeys.MinLength || string.IsNullOrWhiteSpace(player)) continue;
            var hash = Server.TrackerKeys.Hash(key);
            if (!have.Add(hash)) continue;
            db.TrackerKeys.Add(new TrackerKeyRow { Player = player.Trim(), KeyHash = hash, CreatedUtc = nowUtc });
            added++;
        }
        db.SaveChanges();
        return added;
    }
}
