namespace SCTools.Server;

/// <summary>Where the server keeps its files, as full paths: a path in the settings that is not full is in the server's folder.</summary>
public sealed class ServerPaths
{
    public ServerPaths(ServerSettings settings, IHostEnvironment env)
    {
        string Full(string path) => Path.IsPathRooted(path) ? path : Path.Combine(env.ContentRootPath, path);
        Database = Full(settings.Database);
        BackupDir = Full(settings.BackupDir);
        DataFile = Full(settings.DataFile);
        LogDir = Full(settings.LogDir);
    }

    public string Database { get; }
    public string BackupDir { get; }
    public string DataFile { get; }
    public string LogDir { get; }
}
