namespace SCTools.Locations;

/// <summary>A player playing now, with his log and where the location tracker has him.</summary>
public sealed record LocatedPlayer(string Name, HaulingLog Log, PlayerLocation Where);
