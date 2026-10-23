using System.Text.RegularExpressions;

namespace HaulingTracker.Runs;

/// <summary>Cargo of a from-Ruin contract (waste) that a worker delivered to a station during a run.</summary>
public sealed class TripDrop
{
    public string Zone = "";
    public string MissionId = "";
    public string Objective = "";
    public string Cargo = "Cargo";
    public DateTime Time;       // UTC
    public int Scu;             // the load of this drop-off from the "Deliver 0/187 SCU" popup (0 = not seen)
}
