using System.Security.Cryptography;
using System.Text;

namespace HaulingTracker.Server;

/// <summary>The keys of the trackers: a tracker sends its key with every upload (the X-Tracker-Key header), and the key says whose log it is.</summary>
public sealed class TrackerKeys
{
    readonly List<(byte[] Key, string Player)> _keys;

    public TrackerKeys(IReadOnlyDictionary<string, string> keys) =>
        _keys = keys.Where(kv => kv.Key.Length >= 16 && !string.IsNullOrWhiteSpace(kv.Value)).Select(kv => (Encoding.UTF8.GetBytes(kv.Key), kv.Value.Trim())).ToList();

    /// <summary>The player of this key, or null (no key, a key that is too short to be safe, or an unknown one). Every key is compared in full, so the time an answer takes tells nothing.</summary>
    public string? PlayerOf(string? key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        var given = Encoding.UTF8.GetBytes(key);
        string? player = null;
        foreach (var (k, p) in _keys)
        {
            if (k.Length == given.Length && CryptographicOperations.FixedTimeEquals(k, given)) player = p;
        }
        return player;
    }
}
