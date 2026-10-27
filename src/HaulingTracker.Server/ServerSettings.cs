namespace HaulingTracker.Server;

/// <summary>The settings of the server (the "HaulingTracker" part of appsettings.json, or environment variables like HaulingTracker__DataFile).</summary>
public sealed class ServerSettings
{
    /// <summary>The database (SQLite): the party, the sessions, the payouts, the debts... A path that is not full is in the server's folder.</summary>
    public string Database { get; set; } = "App_Data/tracker.db";

    /// <summary>A daily copy of the database is kept here (the newest 30).</summary>
    public string BackupDir { get; set; } = "App_Data/backups";

    /// <summary>The program's data file (data.json): it goes into the database the first time the server starts, while the database is still empty.</summary>
    public string DataFile { get; set; } = "App_Data/data.json";

    /// <summary>Where the uploaded logs are kept, so a restart of the server reads them again.</summary>
    public string LogDir { get; set; } = "App_Data/logs";

    /// <summary>How many hours back the kept logs are read again when the server starts.</summary>
    public int KeepHours { get; set; } = 36;

    /// <summary>How numbers and dates are written (sv-SE: 3 614 083, like the program on Frank's computer).</summary>
    public string Culture { get; set; } = "sv-SE";

    /// <summary>
    /// Keys of trackers and the player each belongs to (the key is the player: his tracker sends it with every upload). They are added to the database when
    /// the server starts (only a hash of each), so they can be removed from here afterwards; removing one here does not take it back.
    /// </summary>
    public Dictionary<string, string> Trackers { get; set; } = new();

    /// <summary>
    /// The admin login, made when the server starts and has no admin yet. Never in appsettings.json (it is in the repository): on your own computer in the user
    /// secrets (Manage User Secrets in Visual Studio), on the server in its environment (HaulingTracker__Admin__UserName, HaulingTracker__Admin__Password).
    /// After that the password is changed on the page, and these can be removed.
    /// </summary>
    public AdminSettings Admin { get; set; } = new();
}

/// <summary>The name and the first password of the admin.</summary>
public sealed class AdminSettings
{
    /// <summary>Not a player's name: a tracker of that player would log in as the admin.</summary>
    public string UserName { get; set; } = "admin";

    public string? Password { get; set; }

    /// <summary>
    /// True: when the server starts, the admin's password is set to Password again and his lockout is lifted (a forgotten password). Remove it afterwards,
    /// or every start sets it again.
    /// </summary>
    public bool ResetPassword { get; set; }
}
