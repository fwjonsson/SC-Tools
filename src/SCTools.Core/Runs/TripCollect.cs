using System.Text.RegularExpressions;

namespace SCTools.Runs;

/// <summary>Cargo of a from-Ruin contract that a worker took out at Ruin during a run (one entry for every batch).</summary>
public sealed class TripCollect
{
    public string MissionId = "";
    public string Objective = "";
    public string Cargo = "Cargo";
    public DateTime Time;       // UTC
    public int Scu;             // the load of the drop-off it is for, from the "Deliver 0/187 SCU" popup (0 = not seen)
}
