namespace SCTools.Server.Data;

/// <summary>
/// The key of a tracker and the player it belongs to. Only a hash of the key is kept (SHA-256, hex): the key itself is shown once, when it is made, so
/// nobody who reads the database can upload as that player.
/// </summary>
public sealed class TrackerKeyRow
{
    public int Id { get; set; }
    public string Player { get; set; } = "";
    public string KeyHash { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
}
