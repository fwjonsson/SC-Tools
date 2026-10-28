namespace SCTools.Server.Data;

/// <summary>
/// The admin's change to a place of the map (Admin → Map places): his own name and short name for a place of the game data (Added false: the rest of the row
/// is the game data's), or a place he added (Added true: everything is his).
/// </summary>
public sealed class MapPlaceRow
{
    /// <summary>"pyro/ruin-station".</summary>
    public string Key { get; set; } = "";
    public string System { get; set; } = "";
    public string Name { get; set; } = "";
    public string Short { get; set; } = "";

    /// <summary>A PlaceKind ("Station").</summary>
    public string Kind { get; set; } = "";

    /// <summary>The id of the planet or moon it is at, or null.</summary>
    public string? Parent { get; set; }

    /// <summary>Where it is in its system (Gm, the star at 0, 0).</summary>
    public double X { get; set; }
    public double Y { get; set; }

    public bool Added { get; set; }
    public DateTime ChangedUtc { get; set; }
}

/// <summary>
/// What a destination or a start name of the log is, as the admin said (Admin → Map places): the key of a place, or null when it is not a place to show. A
/// text that ends with * is every text that starts so.
/// </summary>
public sealed class MapAliasRow
{
    public int Id { get; set; }
    public string Text { get; set; } = "";
    public string? PlaceKey { get; set; }
    public DateTime AddedUtc { get; set; }
}
