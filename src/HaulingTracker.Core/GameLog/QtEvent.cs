using System.Globalization;
using System.Text.RegularExpressions;

namespace HaulingTracker.GameLog;

/// <summary>A step of a quantum travel, as the game writes it in the log of the player who flies: he selected a destination, or his quantum drive arrived.</summary>
public sealed class QtEvent
{
    public DateTime Time;                // UTC
    public QtKind Kind;
    public string Destination = "";      // Selected: the id the game gives the destination ("rs_ext_pyro6_leo")
    public string From = "";             // Selected: the name the game gives the place he starts from ("Ruin Station"), when it said so
    public string Ship = "";             // the ship he flies, as the game names it ("CRUS_Starlifter_C2")
}
