namespace SCTools.Boarding;

/// <summary>
/// What the ship tracker knows (ShipTracker.Work): whose every ship of the last day is, who flew each last, and the ship everybody playing is in now.
/// Every tool asks this one answer, worked out once per refresh (LiveTracker.Ships): the location tracker, the fleets (the ship and drive a player flies),
/// the Map and the Party pages.
/// </summary>
public sealed class ShipBook
{
    static readonly StringComparer Same = StringComparer.OrdinalIgnoreCase;

    /// <summary>The time it was worked out for.</summary>
    public DateTime Now { get; init; }

    /// <summary>The ships of the quantum jump lines of the last day, by their id, whose name and owner the voice channels told.</summary>
    public Dictionary<string, ShipOwner> Ships { get; } = new(StringComparer.Ordinal);

    /// <summary>Who picked the latest jump of each ship of the last day, and when, by its id.</summary>
    public Dictionary<string, (string Player, DateTime At)> Pilots { get; } = new(StringComparer.Ordinal);

    /// <summary>The latest jump line that named each ship of the last day, by its id.</summary>
    public Dictionary<string, DateTime> LastSeen { get; } = new(StringComparer.Ordinal);

    /// <summary>Everybody playing who is in a ship, by name.</summary>
    public Dictionary<string, Aboard> Players { get; } = new(Same);

    /// <summary>Who rides in a ship somebody else flies, in one text: the Hauling Planner plans anew when it changes (LiveTracker.Refresh).</summary>
    public string RidersKey => string.Join(",", Players.Values.Where(a => a.Riding).Select(a => a.Player.ToLowerInvariant()).OrderBy(n => n, StringComparer.Ordinal));

    /// <summary>The ship this player is in now, or null (not playing, not in a ship, or the logs do not say).</summary>
    public Aboard? Of(string player) => Players.TryGetValue(player, out var a) ? a : null;

    /// <summary>The owner of the ship of this id, when the voice channels told it; else null.</summary>
    public string? OwnerOf(string? shipId) => shipId != null && Ships.TryGetValue(shipId, out var s) ? s.Owner : null;

    /// <summary>
    /// Is the ship of this id (of this class) the ship of this channel? By the ship the book knows by the id; for an id it does not know, by the class
    /// fitting the channel's name (VehicleLines.Fit: "RSI_Perseus" is "RSI Perseus").
    /// </summary>
    public bool IsShip(string shipId, string vehicle, ShipChannel channel) =>
        Ships.TryGetValue(shipId, out var s)
            ? s.Ship == channel.Ship && Same.Equals(s.Owner, channel.Owner)
            : vehicle.Length > 0 && VehicleLines.Fit(vehicle, channel.Ship) > 0;

    /// <summary>The ids of the ships of this name of this owner, the one a jump line named last first.</summary>
    public List<string> IdsOf(string ship, string owner) =>
        Ships.Where(kv => kv.Value.Ship == ship && Same.Equals(kv.Value.Owner, owner))
            .OrderByDescending(kv => LastSeen.TryGetValue(kv.Key, out var t) ? t : DateTime.MinValue).Select(kv => kv.Key).ToList();
}
