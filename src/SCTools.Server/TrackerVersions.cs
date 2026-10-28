using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using SCTools.Updates;

namespace SCTools.Server;

/// <summary>Which version a player's tracker said it is (its X-Tracker-Version header), and when it was last in touch.</summary>
public sealed record TrackerSeen(string Version, DateTime SeenUtc);

/// <summary>
/// The versions of the trackers, as they say with every request (in memory: after the server starts, a tracker is known again with its next upload, and a
/// tracker that starts asks the server about its old logs at once), and the newest build on GitHub (the "latest" release; asked at most every 10
/// minutes). So the Tracker page and the admin's Keys page can say whether a tracker updated.
/// </summary>
public sealed class TrackerVersions
{
    /// <summary>What a tracker built on a PC (not by GitHub) says: it never updates itself.</summary>
    public const string OwnBuild = "own build";

    static readonly Regex Commit = new("^[0-9a-f]{7,40}$", RegexOptions.Compiled);

    readonly ConcurrentDictionary<string, TrackerSeen> _seen = new(StringComparer.OrdinalIgnoreCase);
    readonly Func<CancellationToken, Task<string?>> _latest;
    readonly SemaphoreSlim _asking = new(1, 1);
    string? _newest;
    DateTime _askedUtc = DateTime.MinValue;

    public TrackerVersions() : this(Updater.LatestCommitAsync)
    {
    }

    /// <param name="latest">asks for the commit of the newest build (null: not known)</param>
    public TrackerVersions(Func<CancellationToken, Task<string?>> latest) => _latest = latest;

    /// <summary>A request of this player's tracker, with what it says its version is (nothing: a tracker from before it said so).</summary>
    public void Seen(string player, string? version, DateTime nowUtc)
    {
        version = (version ?? "").Trim().ToLowerInvariant();
        if (version != OwnBuild && !Commit.IsMatch(version)) version = "";
        _seen[player] = new TrackerSeen(version.Length == 0 ? Older : version, nowUtc);
    }

    /// <summary>This player's tracker, or null when it was not in touch since the server started.</summary>
    public TrackerSeen? Of(string player) => _seen.TryGetValue(player, out var s) ? s : null;

    /// <summary>The commit of the newest build, or null when GitHub did not answer (the last answer is kept for an hour).</summary>
    public async Task<string?> NewestAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        if (nowUtc - _askedUtc < TimeSpan.FromMinutes(10)) return _newest;
        await _asking.WaitAsync(ct);
        try
        {
            if (nowUtc - _askedUtc < TimeSpan.FromMinutes(10)) return _newest;
            string? found = null;
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(5));
                found = await _latest(cts.Token);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // GitHub did not answer: the last answer is still good for a while
            }
            if (found != null || nowUtc - _askedUtc > TimeSpan.FromHours(1)) _newest = found;
            _askedUtc = nowUtc;
            return _newest;
        }
        finally
        {
            _asking.Release();
        }
    }

    /// <summary>What a tracker from before the trackers said their version is.</summary>
    public const string Older = "older";

    /// <summary>Whether this version is the newest build (null: not known, or a build of its own).</summary>
    public static bool? IsNewest(string version, string? newest)
    {
        if (version == Older) return false;
        if (version == OwnBuild || newest == null) return null;
        return newest.StartsWith(version, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A line about a player's tracker for the pages ("version abc1234, the newest, in touch 2 min ago"), and whether it is the newest (its colour).</summary>
    public static (string Text, bool? Newest) Describe(TrackerSeen? seen, string? newest, DateTime nowUtc)
    {
        if (seen == null) return ("Not in touch since the server started.", null);
        var isNew = IsNewest(seen.Version, newest);
        var what = seen.Version switch
        {
            Older => "An older version (from before the trackers said their version)",
            OwnBuild => "A build of its own (it does not update itself)",
            _ => "Version " + seen.Version + (isNew == true ? ", the newest" : isNew == false ? ", not the newest (that is " + newest![..7] + ")" : ""),
        };
        return (what + ", in touch " + Ago(seen.SeenUtc, nowUtc) + ".", isNew);
    }

    /// <summary>"just now", "5 min ago", "3 h ago", "2 days ago".</summary>
    public static string Ago(DateTime utc, DateTime nowUtc)
    {
        var d = nowUtc - utc;
        return d.TotalMinutes < 1 ? "just now" : d.TotalHours < 1 ? (int)d.TotalMinutes + " min ago" : d.TotalDays < 1 ? (int)d.TotalHours + " h ago"
            : (int)d.TotalDays + ((int)d.TotalDays == 1 ? " day ago" : " days ago");
    }
}
