namespace SCTools.GameLog;

/// <summary>
/// Where the player of a log was, as the game wrote it when he opened the inventory of a place: "&lt;RequestLocationInventory&gt; Player[zzoii] requested
/// inventory for Location[Nyx_Levski]" (the id of the place: its system, then its name).
/// </summary>
public sealed record LocationSeen(DateTime Time, string Location);
