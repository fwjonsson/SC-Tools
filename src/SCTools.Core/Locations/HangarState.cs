namespace SCTools.Locations;

/// <summary>
/// Where a player's ship is at a station, by the area lines: in the station's hangar (Landed; the game's landing area, one number per station for all its
/// hangars, the same in every player's log; moving the ship about inside the hangar writes nothing), or out of the hangar and still in the station's own
/// area (TakingOff, StationAreas). Null: neither (out in space, or the log does not say).
/// </summary>
public enum HangarState { Landed, TakingOff }
