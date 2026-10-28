using System.Text.RegularExpressions;

namespace SCTools.Runs;

public sealed class TripPickup
{
    public string Zone = "";
    public int Missions;        // missions loaded at this location during the run
    public DateTime Time;       // last pickup event here (UTC)
    public List<string> MissionIds = new();   // the missions loaded here (one batch each)
}
