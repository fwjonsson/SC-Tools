using System.Globalization;
using SCTools.Server.Data;

namespace SCTools.Server;

/// <summary>
/// When a session really started, as the admin says (Frank, 31 Oct 2026: the start time an editable row of every tool's session, as the Hauling Planner's;
/// his Session page went): the Companion's game session and the Gauntlet Tracker's game and grind sessions. Those sessions are worked out from the logs
/// each time, so the admin's start is kept by the start the logs give (the tool, whose session for the Companion, and that start): a session the logs
/// start elsewhere is another one and has none. Kept in SiteValues ("Start|companion|zzoega|ticks": the start, ticks UTC); the Hauling Planner's start
/// time is its session's own (Edits.SetStartTime).
/// </summary>
public sealed class SessionStarts
{
    public const string Companion = "companion";
    public const string GauntletGame = "gauntlet";
    public const string GauntletGrind = "grind";

    const string Prefix = "Start|";

    readonly DataStore _store;
    readonly object _lock = new();
    Dictionary<string, long>? _starts;
    int _version;

    public SessionStarts(DataStore store) => _store = store;

    /// <summary>Changes with every start set or taken back (the Companion's cached session goes by it).</summary>
    public int Version { get { lock (_lock) return _version; } }

    static string Key(string tool, string scope, DateTime fromUtc) =>
        Prefix + tool + "|" + scope.ToLowerInvariant() + "|" + fromUtc.Ticks.ToString(CultureInfo.InvariantCulture);

    Dictionary<string, long> Starts()
    {
        if (_starts != null) return _starts;
        using var db = _store.Open();
        return _starts = db.SiteValues.Where(r => r.Key.StartsWith(Prefix)).ToDictionary(r => r.Key, r => r.Value);
    }

    /// <summary>The admin's start of the session the logs start at fromUtc (scope: whose, for the Companion), or null: the logs' own.</summary>
    public DateTime? Of(string tool, string scope, DateTime fromUtc)
    {
        lock (_lock)
            return Starts().TryGetValue(Key(tool, scope, fromUtc), out var ticks) ? new DateTime(ticks, DateTimeKind.Utc) : null;
    }

    /// <summary>
    /// The start the admin typed (19:30) for the session the logs start at fromUtc, on the day of that start in the time zone of the page where he typed it
    /// (tzMinutes: the browser's offset, minutes behind UTC as JavaScript gives it; 0 for a page in UTC). Empty: the logs' start again.
    /// </summary>
    public string Set(string tool, string scope, DateTime fromUtc, string? typed, int tzMinutes, DateTime nowUtc)
    {
        typed = typed?.Trim() ?? "";
        DateTime? start = null;
        if (typed.Length > 0)
        {
            if (!TimeSpan.TryParseExact(typed, new[] { "h\\:mm", "hh\\:mm" }, CultureInfo.InvariantCulture, out var time) || time >= TimeSpan.FromDays(1))
                return "Type the start time as 19:30.";
            var day = fromUtc.AddMinutes(-tzMinutes).Date;   // the day of the logs' start, where he is
            start = DateTime.SpecifyKind(day + time, DateTimeKind.Utc).AddMinutes(tzMinutes);
            if (start > nowUtc) return "That is later than now: type when the session started.";
        }
        lock (_lock)
        {
            var key = Key(tool, scope, fromUtc);
            using var db = _store.Open();
            var row = db.SiteValues.Find(key);
            if (start == null)
            {
                if (row != null) db.SiteValues.Remove(row);
            }
            else
            {
                if (row == null) db.SiteValues.Add(row = new SiteValueRow { Key = key });
                row.Value = start.Value.Ticks;
                row.ChangedUtc = nowUtc;
            }
            db.SaveChanges();
            _starts = null;
            _version++;
        }
        return start == null ? "The session starts when the logs say again." : "The session runs from " + typed + ".";
    }
}
