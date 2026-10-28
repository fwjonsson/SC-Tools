using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace SCTools.Server.Accounts;

/// <summary>
/// The links a tracker opens to log its player in ("Open my page"): the tracker asks for one with its key, and the link logs him in once, within two
/// minutes. So the key itself is never in an address (a browser keeps those in its history).
/// </summary>
public sealed class LoginLinks
{
    public static readonly TimeSpan Valid = TimeSpan.FromMinutes(2);

    readonly ConcurrentDictionary<string, (string Player, DateTime Until)> _links = new();

    /// <summary>A new link for this player: the token in it.</summary>
    public string Make(string player, DateTime nowUtc)
    {
        foreach (var (t, l) in _links)
        {
            if (l.Until < nowUtc) _links.TryRemove(t, out _);   // old ones that were never used
        }
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        _links[token] = (player, nowUtc + Valid);
        return token;
    }

    /// <summary>The player of this token, once: the token is gone afterwards. Null when it is unknown, used or too old.</summary>
    public string? Use(string? token, DateTime nowUtc)
    {
        if (string.IsNullOrEmpty(token) || !_links.TryRemove(token, out var link)) return null;
        return link.Until >= nowUtc ? link.Player : null;
    }
}
