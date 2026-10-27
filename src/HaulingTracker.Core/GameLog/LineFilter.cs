namespace HaulingTracker.GameLog;

/// <summary>
/// The lines of a Game.log the tracker reads: the kinds in HaulingLog.LineKinds, the same list the reader skips everything else with, so a tracker only
/// uploads these and the server reads exactly what it would read from the whole log (the rest of a player's game stays on his computer; a game that spams
/// its log sends nothing more). A money transfer is two lines: the notification, and the amount on the next line, whatever it says, so the line after one
/// is kept too. One filter per log file: it remembers that line.
/// </summary>
public sealed class LineFilter
{
    bool _keepNext;

    public bool Keep(string line)
    {
        if (line.Length < 30 || line[0] != '<') return false;   // ProcessLine never looks at these
        var keep = _keepNext;   // the amount of a money transfer comes on the line after it
        _keepNext = false;
        if (line.Contains("sent you:") || line.Contains("You sent "))
        {
            _keepNext = true;
            return true;
        }
        return keep || HaulingLog.IsRead(line);
    }
}
