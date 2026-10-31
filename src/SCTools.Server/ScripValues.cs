using SCTools.Server.Data;

namespace SCTools.Server;

/// <summary>
/// What one MG scrip and one Council scrip are worth in aUEC: the price the admin thinks they can be sold for now (his Settings page). Every tool counts with
/// them (the Gauntlet Tracker's totals, the Session tool's earnings), so a new price changes the totals of the past too. The MG scrip value was the Gauntlet
/// Tracker's own before: until the admin sets one here, that one is used.
/// </summary>
public sealed class ScripValues
{
    public const string MgKey = "MgScrip";
    public const string CouncilKey = "CouncilScrip";

    readonly DataStore _store;
    readonly object _lock = new();
    (long Mg, long Council)? _values;

    public ScripValues(DataStore store) => _store = store;

    /// <summary>aUEC for one MG scrip (Mercenary Guild).</summary>
    public long Mg => Values.Mg;

    /// <summary>aUEC for one Council scrip.</summary>
    public long Council => Values.Council;

    (long Mg, long Council) Values
    {
        get
        {
            lock (_lock)
            {
                if (_values is { } v) return v;
                using var db = _store.Open();
                var rows = db.SiteValues.ToDictionary(r => r.Key, r => r.Value);
                var mg = rows.GetValueOrDefault(MgKey);   // (the old Gauntlet settings' scrip value went on 30 Oct 2026: the table was empty)
                _values = (mg, rows.GetValueOrDefault(CouncilKey));
                return _values.Value;
            }
        }
    }

    /// <summary>Sets one of them (MgKey or CouncilKey).</summary>
    public void Set(string key, long value, DateTime nowUtc)
    {
        if (key != MgKey && key != CouncilKey) throw new ArgumentException("Not a scrip.", nameof(key));
        lock (_lock)
        {
            using var db = _store.Open();
            var row = db.SiteValues.Find(key);
            if (row == null) db.SiteValues.Add(row = new SiteValueRow { Key = key });
            row.Value = value;
            row.ChangedUtc = nowUtc;
            db.SaveChanges();
            _values = null;
        }
    }
}
