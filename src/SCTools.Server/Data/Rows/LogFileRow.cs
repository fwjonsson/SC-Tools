namespace SCTools.Server.Data;

/// <summary>
/// An old log a player's tracker sent (HistorySender): one game of his, from its first line to its last. Kept for good, so the tracker sends a log once and
/// the playtime knows every game; the lines themselves are in the history folder (gzip), so a new tool can read them again.
/// </summary>
public sealed class LogFileRow
{
    public string Player { get; set; } = "";

    /// <summary>The time of the log's first line (LogSender.FileId), which is when the game started.</summary>
    public string File { get; set; } = "";

    /// <summary>HistorySender.Version of what was sent: an older one is asked for again. 0: a game the live log showed (the server asks for its backup log
    /// later all the same, which has all of it).</summary>
    public int Version { get; set; }

    public DateTime StartUtc { get; set; }

    /// <summary>The time of the log's last line: when the game ended (null: the log had no time after its first line).</summary>
    public DateTime? EndUtc { get; set; }

    /// <summary>The game was quit the normal way (its log has the quit line); false: it crashed; null: not known (an old log sent before version 2).</summary>
    public bool? Quit { get; set; }

    /// <summary>The game's build, from the log's name (Game Build(9876543) ...).</summary>
    public string? Build { get; set; }

    /// <summary>How many lines were kept.</summary>
    public int Lines { get; set; }

    public DateTime ImportedUtc { get; set; }

    /// <summary>Why the log was not read for the tools, or null: "zzatvo's game" when it is another player's log on the same PC.</summary>
    public string? Note { get; set; }
}
