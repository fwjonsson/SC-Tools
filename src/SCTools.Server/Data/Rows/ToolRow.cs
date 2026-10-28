namespace SCTools.Server.Data;

/// <summary>Whether everybody sees a tool of the site (the Tools page). A tool without a row is as SiteTools says.</summary>
public sealed class ToolRow
{
    /// <summary>The tool: hauling, session, missions, gauntlet (SiteTool.Id).</summary>
    public string Id { get; set; } = "";

    /// <summary>True: on the landing page for everybody. False: only the admin sees its card and its page.</summary>
    public bool Public { get; set; }
}
