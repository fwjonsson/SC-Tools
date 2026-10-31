namespace SCTools.Server;

/// <summary>
/// One time a player met a text of the logs the map does not know (MapPlaces.Sightings): the text and what it is, who, when (UTC), whether it puts him at
/// the place (an area, a start name, a destination he arrived at), and the system he was in, when known.
/// </summary>
public sealed record UnknownSighting(string Text, UnknownKind Kind, string Player, DateTime TimeUtc, bool Here, string? System)
{
    /// <summary>The place's key on the admin's page: the text in small letters and its kind (a destination and an area with the same text are two).</summary>
    public string Key => Text.ToLowerInvariant() + "|" + Kind;

    /// <summary>"Quantum destination", "Start name", "Area", as Map places says it.</summary>
    public string KindText => new UnknownPlace(Text, Kind, 0, TimeUtc, Array.Empty<string>(), System).KindText;
}
