namespace SCTools.GameLog;

/// <summary>What the game told the player of a log about his in-game party (its notifications).</summary>
public enum PartyEventKind
{
    /// <summary>"You have joined party" or "You have created party": the player of the log is in a party now.</summary>
    Self,

    /// <summary>"zzatvo has joined the party."</summary>
    Joined,

    /// <summary>"Bileh123 has left the party." (also when he quits the game or it crashes)</summary>
    Left,

    /// <summary>"zzoega is now party leader." or "Initiated by party leader zzoega." (a party launch)</summary>
    Leader,

    /// <summary>"The party has been disbanded.": the player of the log is alone again.</summary>
    Disbanded,
}

/// <summary>A notification about the party, as the game wrote it in the log of one player. Name is the other player (null for Self and Disbanded).</summary>
public sealed record PartyEvent(DateTime Time, PartyEventKind Kind, string? Name);

/// <summary>
/// One game of a player, as his tracker uploaded its log: from the time of the log's first line to the latest time in it the tracker saw; Quit: the game was
/// quit the normal way (the log has the quit line), so it ended then and not when the log has been quiet for a while.
/// </summary>
public readonly record struct GameRun(DateTime Start, DateTime Last, bool Quit = false);
