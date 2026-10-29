namespace SCTools.GameLog;

/// <summary>
/// How long the tracker waits before its next upload round: 3 seconds while all goes well. After a failure the wait doubles with every failure in a row
/// (3, 6, 12, 24 seconds... up to 2 minutes), so a server that is down or being deployed is not asked every 3 seconds, and the first round that works goes
/// back to 3 seconds. A key the server does not know is asked about once a minute: it only changes when the settings do, and then it starts over (Reset).
/// </summary>
public sealed class RetryDelay
{
    /// <summary>The wait while all goes well.</summary>
    public static readonly TimeSpan Normal = TimeSpan.FromSeconds(3);

    /// <summary>The longest wait after failures in a row.</summary>
    public static readonly TimeSpan Longest = TimeSpan.FromMinutes(2);

    /// <summary>The wait while the server does not know the key.</summary>
    public static readonly TimeSpan UnknownKey = TimeSpan.FromMinutes(1);

    TimeSpan _afterFailure = Normal;   // the wait after the next failure: it doubles with every failure in a row

    /// <summary>How long to wait before the next round, after what came of this one.</summary>
    public TimeSpan Next(SendOutcome outcome)
    {
        if (outcome == SendOutcome.Failed)
        {
            var wait = _afterFailure;
            _afterFailure = wait * 2 < Longest ? wait * 2 : Longest;
            return wait;
        }
        _afterFailure = Normal;
        return outcome == SendOutcome.UnknownKey ? UnknownKey : Normal;
    }

    /// <summary>New settings: the next round is tried as if nothing had failed.</summary>
    public void Reset() => _afterFailure = Normal;
}
