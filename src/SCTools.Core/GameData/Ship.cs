namespace SCTools.GameData;

/// <summary>
/// A ship that can haul, from the Star Citizen Wiki API (vehicles, flight-ready ships with 30 SCU of cargo or more): the name and how many SCU of cargo it holds.
/// </summary>
public sealed record Ship(string Name, int Scu, int Overload = 0)
{
    /// <summary>What the ship can carry in all: the cargo grid plus what fits off grid (containers on the floor, which a few ships have room for).</summary>
    public int Total => Scu + Overload;

    public string Label => Name + " (" + Scu + " SCU" + (Overload > 0 ? " + " + Overload + " off grid" : "") + ")";
}
