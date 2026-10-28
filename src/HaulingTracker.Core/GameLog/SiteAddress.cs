namespace HaulingTracker.GameLog;

/// <summary>
/// The address of the site (SC Tools): the pages, and where the trackers send their logs. The site was haulingtracker.fwjonsson.dev until it got more tools;
/// a tracker that still has that address saved moves to the new one by itself.
/// </summary>
public static class SiteAddress
{
    public const string Current = "https://sct.fwjonsson.dev";

    /// <summary>The host name the site had before.</summary>
    public const string OldHost = "haulingtracker.fwjonsson.dev";

    /// <summary>The address to use for this saved one: the site's address for the old one, else the saved one as it is (a server of your own stays).</summary>
    public static string Moved(string saved)
    {
        var s = saved.Trim();
        var host = Uri.TryCreate(s, UriKind.Absolute, out var uri) && uri.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? uri.Host : s.TrimEnd('/');
        return string.Equals(host, OldHost, StringComparison.OrdinalIgnoreCase) ? Current : saved;
    }
}
