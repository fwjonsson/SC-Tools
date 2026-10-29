namespace SCTools.GameLog;

/// <summary>What came of one upload round of the tracker: RetryDelay says how long to wait before the next one.</summary>
public enum SendOutcome
{
    /// <summary>Everything went through, or there was nothing to do (not set up yet, no Game.log).</summary>
    Ok,

    /// <summary>The server could not be reached or answered with an error, or the log could not be read.</summary>
    Failed,

    /// <summary>The server does not know the key: nothing will change until the settings do.</summary>
    UnknownKey,
}
