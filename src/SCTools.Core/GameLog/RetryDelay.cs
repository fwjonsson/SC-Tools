namespace SCTools.GameLog;

/// <summary>
/// How long the tracker waits before its next try: three seconds while the server takes what it sends, twice as long after every failure in a row (6, 12, 24
/// seconds... up to two minutes), so a server that is down or being updated is not asked twelve hundred times an hour by every tracker, a minute when the
/// server does not know the key (a wrong key does not mend itself), and three seconds again as soon as a try works.
/// </summary>
public sealed class RetryDelay
{
    public static readonly TimeSpan Normal = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan Longest = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan UnknownKey = TimeSpan.FromMinutes(1);

    /// <summary>How long to wait before the next try.</summary>
    public TimeSpan Next { get; private set; } = Normal;

    /// <summary>How many tries failed in a row.</summary>
    public int Failures { get; private set; }

    /// <summary>The server took what was sent (or there was nothing to send): the next try is soon.</summary>
    public void Worked()
    {
        Failures = 0;
        Next = Normal;
    }

    /// <summary>The server could not be reached, or did not answer in time: the next try waits twice as long as the last, up to Longest.</summary>
    public void Failed()
    {
        Failures++;
        var wait = TimeSpan.FromTicks(Normal.Ticks << Math.Min(Failures, 10));
        Next = wait > Longest ? Longest : wait;
    }

    /// <summary>The server does not know the key: it is asked again in a minute (and at once when the settings change).</summary>
    public void KeyUnknown()
    {
        Failures++;
        Next = UnknownKey;
    }

    /// <summary>The game is closed when its Game.log has not grown for this long: the tracker then looks every Idle instead of every 3 seconds.</summary>
    public static readonly TimeSpan QuietAfter = TimeSpan.FromMinutes(3);

    /// <summary>How often the tracker looks while the game is closed. It is back to 3 seconds at the first line the game writes.</summary>
    public static readonly TimeSpan Idle = TimeSpan.FromSeconds(15);

    /// <summary>The wait before the next try, given how long the Game.log has not grown: Next, but at least Idle while the game is closed.</summary>
    public TimeSpan NextFor(TimeSpan quiet) => quiet >= QuietAfter && Next < Idle ? Idle : Next;

    /// <summary>A wait as the menu says it: "24 s", "2 min".</summary>
    public static string Say(TimeSpan wait) => wait < TimeSpan.FromMinutes(1) ? (int)wait.TotalSeconds + " s" : (int)wait.TotalMinutes + " min";
}
