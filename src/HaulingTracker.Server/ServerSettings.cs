namespace HaulingTracker.Server;

/// <summary>The settings of the server (the "HaulingTracker" part of appsettings.json, or environment variables like HaulingTracker__DataFile).</summary>
public sealed class ServerSettings
{
    /// <summary>The database (SQLite): the party, the sessions, the payouts, the debts... A path that is not full is in the server's folder.</summary>
    public string Database { get; set; } = "data/tracker.db";

    /// <summary>A daily copy of the database is kept here (the newest 30).</summary>
    public string BackupDir { get; set; } = "data/backups";

    /// <summary>The program's data file (data.json): it goes into the database the first time the server starts, while the database is still empty.</summary>
    public string DataFile { get; set; } = "data/data.json";

    /// <summary>Where the uploaded logs are kept, so a restart of the server reads them again.</summary>
    public string LogDir { get; set; } = "data/logs";

    /// <summary>How many hours back the kept logs are read again when the server starts.</summary>
    public int KeepHours { get; set; } = 36;

    /// <summary>How numbers and dates are written (sv-SE: 3 614 083, like the program on Frank's computer).</summary>
    public string Culture { get; set; } = "sv-SE";

    /// <summary>
    /// Keys of trackers and the player each belongs to (the key is the player: his tracker sends it with every upload). They are added to the database when
    /// the server starts (only a hash of each), so they can be removed from here afterwards; removing one here does not take it back.
    /// </summary>
    public Dictionary<string, string> Trackers { get; set; } = new();
}
