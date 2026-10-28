namespace SCTools.GameLog;

/// <summary>
/// The address of the site (SC Tools): the pages, and where the trackers send their logs. The site was haulingtracker.fwjonsson.dev, then
/// sct.fwjonsson.dev; a tracker that still has one of those saved moves to the new one by itself.
/// </summary>
public static class SiteAddress
{
    public const string Current = "https://sc-tools.fwjonsson.dev";

    /// <summary>The host names the site had before.</summary>
    public static readonly IReadOnlyList<string> OldHosts = new[] { "haulingtracker.fwjonsson.dev", "sct.fwjonsson.dev" };

    /// <summary>The address to use for this saved one: the site's address for an old one, else the saved one as it is (a server of your own stays).</summary>
    public static string Moved(string saved)
    {
        var s = saved.Trim();
        var host = Uri.TryCreate(s, UriKind.Absolute, out var uri) && uri.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? uri.Host : s.TrimEnd('/');
        return OldHosts.Any(h => string.Equals(host, h, StringComparison.OrdinalIgnoreCase)) ? Current : saved;
    }
}
