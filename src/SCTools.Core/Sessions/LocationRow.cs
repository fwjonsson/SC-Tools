using System.Text.RegularExpressions;

namespace SCTools.Sessions;

/// <summary>One pickup location with every mission that has cargo there for one drop-off: Ruin, or (since stage 1a of docs/planner-any-hauling.md) another station.</summary>
public sealed class LocationRow
{
    public string Zone = "";
    public bool ToRuin = true;      // the cargo here goes to Ruin Station (the planner plans it); false: to the station of DropZone (stage 1b plans those)
    public string? DropZone;        // the location id of the drop-off station when it is not Ruin
    public string? DropName;        // the drop-off's name as the game's popup says it ("Stanton Gateway"; "Orbituary, Gaslight" for a job with several), when it said one
    public string? PickupName;      // the pickup's name as the game's popup says it ("Shallow Fields Station at Crusader's L4 Lagrange point"), when it said one (stage 4: the jobs of other systems)
    public bool Job;                // the stops here are a hauling job's of another kind (not a Red Wind contract's; stage 4): the Route Planner (new) plans them from the job, the old planner leaves them
    public int Missions;        // missions with a stop here
    public int Stops2;          // of those, stops that belong to 2 stop missions
    public int Stops3;          // of those, stops that belong to 3 stop missions
    public int Stops4;          // of those, stops that belong to 4 stop missions
    public int Loaded;          // stops fully picked up
    public int Partial;         // stops where only part is picked up so far
    public int Delivered;       // stops whose cargo has been handed in at Ruin
    public int Missing;         // of those missions, the ones whose cargo is all handed in but that never completed (cargo missing)
    public List<Mission> OpenMissions = new();   // the missions of the stops that are not picked up yet (a mission twice when two of its stops are here)
    public List<Mission> StopMissions = new();   // the mission of every stop here, picked up or not (a mission twice when two of its stops are here): what the stops are worth
    public DateTime? LoadedTime;     // latest pickup event here (UTC)
    public DateTime? DeliveredTime;  // set when every stop here has been handed in (UTC)
}
