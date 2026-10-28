namespace SCTools.Web;

/// <summary>
/// The live web page. It is written as ordinary web files in wwwroot (index.html, app.css, app.js), so they can be edited and checked like any
/// web page; they are embedded in this library, and put together here once into the single page the server sends (the style and the script inline).
/// </summary>
public static class WebAssets
{
    /// <summary>The page: index.html with the style and the script inline.</summary>
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
