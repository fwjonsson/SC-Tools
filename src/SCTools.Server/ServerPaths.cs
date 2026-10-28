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
        HistoryDir = Path.Combine(Path.GetDirectoryName(LogDir.TrimEnd('/', '\\')) ?? LogDir, "history");
    }

    public string Database { get; }
    public string BackupDir { get; }
    public string DataFile { get; }
    public string LogDir { get; }

    /// <summary>The old logs the trackers sent (HistoryStore), next to LogDir: not in it, where every folder is a player's live logs.</summary>
    public string HistoryDir { get; }
}
