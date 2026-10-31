namespace SCTools.Locations;

/// <summary>
/// Where a player's ship is at a station, by the area lines: on a pad or in a hangar (Landed), or off the pad and still in the station's own area (TakingOff,
/// StationAreas). Null: neither (out in space, or the log does not say).
/// </summary>
public enum PadState { Landed, TakingOff }
