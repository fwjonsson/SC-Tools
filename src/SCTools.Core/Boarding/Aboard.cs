namespace SCTools.Boarding;

/// <summary>
/// The ship a player is in now (ShipTracker): its name and owner as its voice channel says them (or, without one, as the book knows the ship he arrived
/// in), since when, the ship of the game it is when a quantum jump line named it (its class, "RSI_Perseus", and its id), and who flies it (Pilot: who
/// picked its latest jump since he boarded, else its owner).
/// </summary>
public sealed record Aboard(string Player, string Ship, string Owner, DateTime Since, string? Vehicle, string? ShipId, string Pilot)
{
    /// <summary>He rides: somebody else flies the ship (in the Hauling Planner he is crew of that ship, with no ship of his own).</summary>
    public bool Riding => !PlayerNames.SameName(Pilot, Player);

    /// <summary>The ship is somebody else's (its drive is the owner's, from his fleet).</summary>
    public bool Borrowed => !PlayerNames.SameName(Owner, Player);
}
