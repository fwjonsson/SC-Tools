namespace HaulingTracker.Server;

/// <summary>The settings of the server (the "HaulingTracker" part of appsettings.json, or environment variables like HaulingTracker__DataFile).</summary>
public sealed class ServerSettings
{
    /// <summary>The data file (the same kind as the program's: party, sessions, payouts, debts...). A path that is not full is in the server's folder.</summary>
    public string DataFile { get; set; } = "data/data.json";

    /// <summary>Where the uploaded logs are kept, so a restart of the server reads them again.</summary>
    public string LogDir { get; set; } = "data/logs";

    /// <summary>How many hours back the kept logs are read again when the server starts.</summary>
    public int KeepHours { get; set; } = 36;

    /// <summary>How numbers and dates are written (sv-SE: 3 614 083, like the program on Frank's computer).</summary>
    public string Culture { get; set; } = "sv-SE";

    /// <summary>Every tracker's key and the player it belongs to (the key is the player: his tracker sends it with every upload).</summary>
    public Dictionary<string, string> Trackers { get; set; } = new();
}
