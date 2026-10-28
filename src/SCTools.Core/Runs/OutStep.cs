using System.Text.RegularExpressions;

namespace SCTools.Runs;

/// <summary>One finished step of a from-Ruin contract: cargo taken out at Ruin (a pickup objective) or delivered at a station (a drop-off objective), and who did it.</summary>
public sealed class OutStep
{
    public Mission Mission = null!;
    public string Objective = "";
    public bool Pickup;         // true = taken out at Ruin, false = delivered at a station
    public string? Worker;      // who did it, from the logs; null = no log shows it
    public DateTime Time;       // UTC, the earliest sign of it in the logs
}
