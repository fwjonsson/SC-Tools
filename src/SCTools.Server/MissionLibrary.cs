using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SCTools.Missions;
using SCTools.Server.Data;

namespace SCTools.Server;

/// <summary>What the Star Citizen Wiki says about a kind of mission (its ContractId).</summary>
public sealed record WikiMission(string? Title, string? Giver, long? RewardMin, long? RewardMax, string? Currency);

/// <summary>Asks the Star Citizen Wiki about a kind of mission: null when it does not know it (an exception when it could not be asked).</summary>
public interface IMissionWiki
{
    Task<WikiMission?> FindAsync(string contractId, CancellationToken ct);
}

/// <summary>api.star-citizen.wiki/api/missions/{uuid}: the ContractId in the log is the mission's uuid there (or one of its variants').</summary>
public sealed class HttpMissionWiki : IMissionWiki
{
    readonly HttpClient _http = new() { BaseAddress = new Uri("https://api.star-citizen.wiki/"), Timeout = TimeSpan.FromSeconds(20) };

    public HttpMissionWiki() => _http.DefaultRequestHeaders.UserAgent.ParseAdd("SCTools-missions (sc-tools.fwjonsson.dev)");

    public async Task<WikiMission?> FindAsync(string contractId, CancellationToken ct)
    {
        using var resp = await _http.GetAsync("api/missions/" + Uri.EscapeDataString(contractId), ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var d = doc.RootElement.TryGetProperty("data", out var data) ? data : doc.RootElement;
        string? Text(string name) => d.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        long? Number(string name) => d.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) && n > 0 ? n : null;
        return new WikiMission(Text("title"), Text("mission_giver"), Number("reward_min"), Number("reward_max"), Text("reward_currency"));
    }
}

/// <summary>A kind of mission in the library, with how it was played.</summary>
public sealed record LibraryEntry(LibraryRow Row, string? Title, int Taken, int Completed, int Failed, int Abandoned, DateTime? LastPlayed)
{
    /// <summary>The name shown: the one typed in, else the title from the log, else the Wiki's.</summary>
    public string Shown => Row.Name ?? Title ?? Row.WikiTitle ?? Row.ContractId;

    /// <summary>Nothing typed in about its rewards yet.</summary>
    public bool NeedsInfo => Row.Auec == null && Row.MgScrip == null && Row.CouncilScrip == null;
}

/// <summary>
/// The Mission Library: every mission the players take, from the live logs and the old ones (MissionReader), kept for good (Missions) and grouped by its
/// kind (the game's ContractId, Library), where the players type in what the log does not say: the name, the aUEC, MG scrip and Council scrip it pays,
/// the giver, notes. The Wiki is asked about every new kind (its giver and reward range). The first time, the old logs already on the server are read.
/// </summary>
public sealed class MissionLibrary
{
    readonly DataStore _store;
    readonly LogUploads _uploads;
    readonly ServerPaths _paths;
    readonly IMissionWiki _wiki;
    readonly object _lock = new();
    Dictionary<(string, string), MissionSeen>? _known;   // what is in the database, so an unchanged mission is not written again

    public MissionLibrary(DataStore store, LogUploads uploads, ServerPaths paths, IMissionWiki wiki)
    {
        _store = store;
        _uploads = uploads;
        _paths = paths;
        _wiki = wiki;
    }

    /// <summary>Writes the missions of the live logs that are new or went on. Returns how many were written.</summary>
    public int Collect() => Add(_uploads.MissionsSeen());

    /// <summary>Writes these missions where they are new or went on (an old log a tracker sent, or the live logs). Returns how many were written.</summary>
    public int Add(IReadOnlyDictionary<string, IReadOnlyList<MissionSeen>> found)
    {
        lock (_lock)
        {
            using var db = _store.Open();
            _known ??= db.Missions.AsNoTracking().AsEnumerable().ToDictionary(
                r => (r.Player.ToLowerInvariant(), r.MissionId.ToLowerInvariant()),
                r => new MissionSeen(r.MissionId, r.ContractId, r.Title, r.Shared, r.StartedUtc, r.EndedUtc,
                    Enum.TryParse<MissionOutcome>(r.Outcome, out var o) ? o : null));
            var kinds = db.Library.Select(l => l.ContractId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var written = 0;
            foreach (var (player, missions) in found)
            {
                foreach (var m in missions)
                {
                    if (m.ContractId == null) continue;   // what kind it is never showed (a mission of the other account): not in the library
                    var key = (player.ToLowerInvariant(), m.MissionId.ToLowerInvariant());
                    _known.TryGetValue(key, out var had);
                    var merged = had == null ? m : new MissionSeen(had.MissionId, had.ContractId, had.Title ?? m.Title, had.Shared,
                        had.StartedUtc ?? m.StartedUtc, had.EndedUtc ?? m.EndedUtc, had.Outcome ?? m.Outcome);
                    if (merged == had) continue;
                    var row = had == null ? null : db.Missions.Find(player, had.MissionId) ?? db.Missions.FirstOrDefault(r => r.Player == player && r.MissionId == had.MissionId);
                    if (row == null) db.Missions.Add(row = new MissionRow { Player = player, MissionId = m.MissionId, ContractId = m.ContractId });
                    row.Title = merged.Title;
                    row.Shared = merged.Shared;
                    row.StartedUtc = merged.StartedUtc;
                    row.EndedUtc = merged.EndedUtc;
                    row.Outcome = merged.Outcome?.ToString();
                    _known[key] = merged;
                    if (kinds.Add(m.ContractId)) db.Library.Add(new LibraryRow { ContractId = m.ContractId });
                    written++;
                }
            }
            if (written > 0) db.SaveChanges();
            return written;
        }
    }

    /// <summary>
    /// The first time (no mission in the database yet): the missions of the old logs already on the server (the history folder), so the library starts with
    /// everything played before it existed. Returns how many logs were read.
    /// </summary>
    public int ReadKeptHistory()
    {
        lock (_lock)
        {
            using var db = _store.Open();
            if (db.Missions.Any() || !Directory.Exists(_paths.HistoryDir)) return 0;
            _known = null;   // read from the database again
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

    /// <summary>Every kind of mission in the library with how it was played, the one played last first.</summary>
    public List<LibraryEntry> Entries()
    {
        lock (_lock)
        {
            using var db = _store.Open();
            var rows = db.Library.AsNoTracking().ToList();
            var played = db.Missions.AsNoTracking().ToList().GroupBy(m => m.ContractId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
            return rows.Select(r =>
                {
                    var mine = played.GetValueOrDefault(r.ContractId) ?? new List<MissionRow>();
                    var title = mine.Where(m => m.Title != null).OrderByDescending(m => m.StartedUtc ?? m.EndedUtc).Select(m => m.Title).FirstOrDefault();
                    return new LibraryEntry(r, title, mine.Count, mine.Count(m => m.Outcome == nameof(MissionOutcome.Complete)),
                        mine.Count(m => m.Outcome == nameof(MissionOutcome.Fail)), mine.Count(m => m.Outcome == nameof(MissionOutcome.Abandon)),
                        mine.Select(m => m.EndedUtc ?? m.StartedUtc).Max());
                })
                .OrderByDescending(e => e.LastPlayed ?? DateTime.MinValue).ThenBy(e => e.Shown, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    /// <summary>The missions this player completed, with what the library says they pay (worked out now: a reward typed in later counts for the past).</summary>
    public List<SCTools.Play.EarnedMission> Earned(string player)
    {
        lock (_lock)
        {
            using var db = _store.Open();
            var rewards = db.Library.AsNoTracking().ToDictionary(l => l.ContractId, StringComparer.OrdinalIgnoreCase);
            return db.Missions.AsNoTracking().Where(m => m.Player == player && m.Outcome == nameof(MissionOutcome.Complete) && m.EndedUtc != null).ToList()
                .Select(m =>
                {
                    rewards.TryGetValue(m.ContractId, out var r);
                    return new SCTools.Play.EarnedMission(m.EndedUtc!.Value, m.ContractId, m.Shared, r?.Auec, r?.MgScrip, r?.CouncilScrip);
                })
                .OrderBy(m => m.EndedUtc).ToList();
        }
    }

    /// <summary>The fields a player can type in.</summary>
    public static readonly string[] Fields = { "name", "giver", "auec", "mg", "council", "notes" };

    /// <summary>Changes one field of a kind of mission (empty: not known). The sentence for the page, or null when that is not a field or a number.</summary>
    public string? Set(string contractId, string field, string? value, string who, DateTime nowUtc)
    {
        value = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        long? number = null;
        if (field is "auec" or "mg" or "council" && value != null)
        {
            if (!long.TryParse(value.Replace(" ", "").Replace(",", "").Replace(".", ""), out var n) || n < 0) return null;
            number = n;
        }
        lock (_lock)
        {
            using var db = _store.Open();
            var row = db.Library.Find(contractId);
            if (row == null) return null;
            switch (field)
            {
                case "name": row.Name = value; break;
                case "giver": row.Giver = value; break;
                case "notes": row.Notes = value; break;
                case "auec": row.Auec = number; break;
                case "mg": row.MgScrip = number; break;
                case "council": row.CouncilScrip = number; break;
                default: return null;
            }
            row.EditedBy = who;
            row.EditedUtc = nowUtc;
            db.SaveChanges();
            return "Saved.";
        }
    }

    /// <summary>Asks the Wiki about the kinds of mission it was not asked about yet (at most this many). Returns how many were asked.</summary>
    public async Task<int> FillFromWikiAsync(int most, CancellationToken ct)
    {
        List<string> ids;
        lock (_lock)
        {
            using var db = _store.Open();
            ids = db.Library.Where(l => l.WikiCheckedUtc == null).Select(l => l.ContractId).Take(most).ToList();
        }
        var asked = 0;
        foreach (var id in ids)
        {
            var found = await _wiki.FindAsync(id, ct);   // an exception: the Wiki could not be asked, the rest waits for the next time
            lock (_lock)
            {
                using var db = _store.Open();
                var row = db.Library.Find(id);
                if (row == null) continue;
                row.WikiCheckedUtc = DateTime.UtcNow;
                row.WikiFound = found != null;
                row.WikiTitle = found?.Title;
                row.WikiGiver = found?.Giver;
                row.WikiRewardMin = found?.RewardMin;
                row.WikiRewardMax = found?.RewardMax;
                row.WikiCurrency = found?.Currency;
                db.SaveChanges();
            }
            asked++;
        }
        return asked;
    }
}

/// <summary>Reads the old logs into the library once when the server starts, then asks the Wiki about new kinds of mission every minute (MissionWiki).</summary>
public sealed class MissionWikiService : BackgroundService
{
    readonly MissionLibrary _library;
    readonly ServerSettings _settings;
    readonly ILogger<MissionWikiService> _log;

    public MissionWikiService(MissionLibrary library, ServerSettings settings, ILogger<MissionWikiService> log)
    {
        _library = library;
        _settings = settings;
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
        if (!_settings.MissionWiki) return;
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            do
            {
                try
                {
                    await _library.FillFromWikiAsync(20, stop);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.LogWarning("The Star Citizen Wiki could not be asked about missions: {Message}", ex.Message);
                }
            }
            while (await timer.WaitForNextTickAsync(stop));
        }
        catch (OperationCanceledException)
        {
            // the server stops
        }
    }
}
