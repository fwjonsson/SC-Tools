using System.Security.Cryptography;
using System.Text;
using HaulingTracker.Server.Data;

namespace HaulingTracker.Server;

/// <summary>
/// The keys of the trackers: a tracker sends its key with every upload (the X-Tracker-Key header), and the key says whose log it is. The database keeps only a
/// hash of every key (TrackerKeyRow); the hashes are kept here in memory, so an upload does not need the database.
/// </summary>
public sealed class TrackerKeys
{
    /// <summary>A shorter key is too easy to guess and is never accepted.</summary>
    public const int MinLength = 16;

    readonly List<(byte[] Hash, string Player)> _keys;

    public TrackerKeys(IEnumerable<TrackerKeyRow> rows) =>
        _keys = rows.Where(r => !string.IsNullOrWhiteSpace(r.Player)).Select(r => (Convert.FromHexString(r.KeyHash), r.Player.Trim())).ToList();

    /// <summary>The hash of a key as the database keeps it (SHA-256, hex).</summary>
    public static string Hash(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    /// <summary>The player of this key, or null (no key, a key that is too short to be safe, or an unknown one). Every key is compared in full, so the time an answer takes tells nothing.</summary>
    public string? PlayerOf(string? key)
    {
        if (string.IsNullOrEmpty(key) || key.Length < MinLength) return null;
        var given = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        string? player = null;
        foreach (var (hash, p) in _keys)
        {
            if (CryptographicOperations.FixedTimeEquals(hash, given)) player = p;
        }
        return player;
    }
}
