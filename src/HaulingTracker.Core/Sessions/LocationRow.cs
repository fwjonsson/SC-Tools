using System.Text.RegularExpressions;

namespace HaulingTracker.Sessions;

/// <summary>One pickup location with every mission that has cargo there.</summary>
public sealed class LocationRow
{
    public string Zone = "";
    public int Missions;        // missions with a stop here
    public int Stops2;          // of those, stops that belong to 2 stop missions
    public int Stops3;          // of those, stops that belong to 3 stop missions
    public int Stops4;          // of those, stops that belong to 4 stop missions
    public int Loaded;          // stops fully picked up
    public int Partial;         // stops where only part is picked up so far
    public int Delivered;       // stops whose cargo has been handed in at Ruin
    public int Missing;         // of those missions, the ones whose cargo is all handed in but that never completed (cargo missing)
    public List<Mission> OpenMissions = new();   // the missions of the stops that are not picked up yet (a mission twice when two of its stops are here)
    public DateTime? LoadedTime;     // latest pickup event here (UTC)
    public DateTime? DeliveredTime;  // set when every stop here has been handed in (UTC)
}
