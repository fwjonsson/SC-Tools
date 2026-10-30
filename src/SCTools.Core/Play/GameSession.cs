namespace SCTools.Play;

/// <summary>
/// A game session (the Companion, Frank 30 Oct 2026): from when the game started until an hour after it was closed, crashed or quit and not started again.
/// A game started again within that hour (after a crash, a relog) is the same session. Over: the hour went by with no game; the session is still shown then,
/// marked ended, until the next game starts one.
/// </summary>
/// <param name="Games">its games, oldest first</param>
public sealed record GameSession(DateTime StartUtc, DateTime LastEndUtc, IReadOnlyList<PlayedGame> Games, bool Over)
{
    /// <summary>How long after the last game ended a new game is still the same session.</summary>
    public static readonly TimeSpan Gap = TimeSpan.FromHours(1);

    /// <summary>The game going on now, if any.</summary>
    public PlayedGame? Running => Games.LastOrDefault(g => g.Running);

    /// <summary>When the session ends (or ended): an hour after its last game; while a game runs, not yet.</summary>
    public DateTime? EndUtc => Running != null ? null : LastEndUtc + Gap;

    /// <summary>The time played in the games that ended (the browser counts the running one on from its start: the clock rule).</summary>
    public TimeSpan PlayedBefore => Games.Where(g => !g.Running).Aggregate(TimeSpan.Zero, (t, g) => t + g.Length);

    /// <summary>The games that crashed.</summary>
    public int Crashes => Games.Count(g => g.Crashed);

    /// <summary>The latest game session of these games (in any order), or null when there are none.</summary>
    public static GameSession? Latest(IEnumerable<PlayedGame> games, DateTime nowUtc)
    {
        var sorted = games.OrderBy(g => g.StartUtc).ToList();
        if (sorted.Count == 0) return null;
        var first = 0;
        var end = sorted[0].EndUtc;
        for (var i = 1; i < sorted.Count; i++)
        {
            if (sorted[i].StartUtc - end >= Gap)   // an hour or more with no game: a new session
            {
                first = i;
                end = sorted[i].EndUtc;
            }
            else if (sorted[i].EndUtc > end) end = sorted[i].EndUtc;
        }
        var own = sorted.Skip(first).ToList();
        var running = own.Any(g => g.Running);
        return new GameSession(own[0].StartUtc, end, own, !running && nowUtc - end >= Gap);
    }
}
