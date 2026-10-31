namespace SCTools.Locations;

/// <summary>What a player is doing now, for the map: at a place, on a jump the map draws, heading somewhere it cannot draw, at a mission's objective, or not known.</summary>
public enum PlayerState { At, Jump, Heading, Mission, Unknown }
