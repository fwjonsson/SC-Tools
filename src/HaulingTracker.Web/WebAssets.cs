namespace HaulingTracker.Web;

/// <summary>
/// The live web page. It is written as ordinary web files in wwwroot (index.html, app.css, app.js), so they can be edited and checked like any
/// web page; they are embedded in this library, and put together here once into the single page the server sends (the style and the script inline).
/// </summary>
static class WebAssets
{
    public static readonly string Page = Build();

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
