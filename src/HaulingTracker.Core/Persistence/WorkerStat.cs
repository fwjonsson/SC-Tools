using System.Text.Json;

namespace HaulingTracker.Persistence;

/// <summary>What one worker did in one session, saved so the all-time leaderboard works without the logs.</summary>
public sealed record WorkerStat
{
    public int Runs { get; set; }            // completed runs (ended by a hand-in)
    public int Perfect { get; set; }         // completed runs with no lost cargo
    public int Missions { get; set; }        // missions carried, also in a run still on the way
    public int RunMissions { get; set; }     // missions in the completed runs
    public double RunSeconds { get; set; }   // total time of the completed runs
    public double Distance { get; set; }     // distance travelled in the completed runs, in Gm (Ruin, the pickups, back to Ruin)
}
