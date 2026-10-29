namespace SCTools.Web;

/// <summary>
/// The live web page. It is written as ordinary web files in wwwroot (index.html, app.css, app.js), so they can be edited and checked like any
/// web page; they are embedded in this library, and put together here once into the single page the server sends (the style and the script inline).
/// </summary>
public static class WebAssets
{
    /// <summary>
    /// The map engine (systemmap.js): the one map of the site, inlined into the live page for the Trip Planner tab and sent by the server to the Map tool as
    /// /systemmap.js, so both draw a system, its players and its objectives the same way. Before Page: the page is built from it.
    /// </summary>
    public static readonly string SystemMap = Read("systemmap.js");

    /// <summary>A short hash of the engine, for its address on the site (/systemmap.js?v=...): a browser keeps the script until it changes.</summary>
    public static readonly string SystemMapVersion = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(SystemMap)))[..12].ToLowerInvariant();

    /// <summary>The page: index.html with the style and the scripts (the map engine, then the page's own) inline.</summary>
    public static readonly string Page = Build();

    /// <summary>The program icon, the icon of the page and of a bookmark (null when it cannot be read).</summary>
    public static readonly byte[]? Icon = ReadIcon();

    static byte[]? ReadIcon()
    {
        try
        {
            using var stream = typeof(WebAssets).Assembly.GetManifestResourceStream("icon.ico");
            if (stream == null) return null;
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }
        catch (Exception)
        {
            return null;
        }
    }

    static string Build()
    {
        var html = Read("index.html");
        return html
            .Replace("<link rel=\"stylesheet\" href=\"app.css\">", "<style>\n" + Read("app.css") + "</style>")
            .Replace("<script src=\"systemmap.js\"></script>", "<script>\n" + SystemMap + "</script>")
            .Replace("<script src=\"app.js\"></script>", "<script>\n" + Read("app.js") + "</script>");
    }

    static string Read(string name)
    {
        using var stream = typeof(WebAssets).Assembly.GetManifestResourceStream("web/" + name)
            ?? throw new InvalidOperationException("The web page file " + name + " is missing from the program.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace("\r\n", "\n");
    }
}
