namespace SCTools.Locations;

/// <summary>
/// A player's death of the last while (LocationTracker.DeathAge), with where he was at that moment: the location tracker's answer from his jumps, inventories
/// and areas up to then (Follow), so the map can put a skull there (#410; Frank, 31 Oct 2026). The death itself is read from his log (HaulingLog.Deaths: the
/// burst of item recovery lines the game writes for his body).
/// </summary>
public sealed record PlayerDeath(string Name, DateTime Time, PlayerLocation Where);
