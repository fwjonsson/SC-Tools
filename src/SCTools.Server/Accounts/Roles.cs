namespace SCTools.Server.Accounts;

/// <summary>
/// The two kinds of login. The admin (Frank) logs in with a name and a password and changes everything; a player logs in with his tracker ("Open my page")
/// and changes only his own settings. Without a login the pages can only be watched.
/// </summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string Player = "Player";
}
