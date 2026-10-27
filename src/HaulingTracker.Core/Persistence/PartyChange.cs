using System.Text.Json;

namespace HaulingTracker.Persistence;

/// <summary>Someone joined (Active) or left the party at this time. Kept so mission payouts are split between who was there when they paid out.</summary>
public sealed class PartyChange
{
    /// <summary>Source: what the logs say (the in-game party and the trackers, see AutoParty); made again from the logs whenever they change.</summary>
    public const string FromLogs = "logs";

    /// <summary>Source: In or Out set by the admin on the Party page; it holds until the logs change their mind about him.</summary>
    public const string ByAdmin = "admin";

    /// <summary>Source: the admin set him back to Auto (from here on the logs decide; Active is what they said then).</summary>
    public const string BackToLogs = "auto";

    public string Name { get; set; } = "";
    public DateTime TimeUtc { get; set; }
    public bool Active { get; set; }

    /// <summary>Who made this change: FromLogs, ByAdmin, BackToLogs, or null (the Active tick of the program, before the logs decided).</summary>
    public string? Source { get; set; }
}
