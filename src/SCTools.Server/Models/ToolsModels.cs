namespace SCTools.Server.Models;

/// <summary>A tool on the Tools page: whether everybody sees it.</summary>
public sealed record ToolLine(SiteTool Tool, bool Public);

/// <summary>
/// The Mission Library page: the kinds of mission shown (one page of all, or of the ones without rewards yet, or of a search), how many there are, the page of
/// how many, and the version of the library it shows (the page asks for the version and gets the list again when it changed).
/// </summary>
public sealed record MissionLibraryPage(IReadOnlyList<LibraryEntry> Entries, int Total, int ToDo, string Show, string Query, int Page = 1, int Pages = 1,
    int Found = 0, int Version = 0)
{
    /// <summary>The address of this list (the tab and the search) on another page.</summary>
    public string Link(int page)
    {
        var parts = new List<string>();
        if (Show == "todo") parts.Add("show=todo");
        if (Query.Length > 0) parts.Add("q=" + Uri.EscapeDataString(Query));
        if (page > 1) parts.Add("page=" + page);
        return "/missions" + (parts.Count > 0 ? "?" + string.Join("&", parts) : "");
    }
}

/// <summary>The Session tool's page for one player.</summary>
public sealed record SessionPage(string Player, SCTools.Play.PlayedGame? Running, SCTools.Play.PlayedGame? LastGame, IReadOnlyList<PeriodLine> Periods,
    IReadOnlyList<GameLine> Games, int GameCount, int WithoutReward, long MgValue, long CouncilValue);

/// <summary>A column of the Session tool's totals: today, this week...</summary>
public sealed record PeriodLine(string Name, SCTools.Play.PlayTotals Totals);

/// <summary>A game in the Session tool's list, with the missions completed while it ran and what they earned.</summary>
public sealed record GameLine(SCTools.Play.PlayedGame Game, SCTools.Play.PlayTotals Earned);

/// <summary>The Party tool: the in-game parties now, and everybody else (not playing, or playing outside the logs' parties) with when he was last seen.</summary>
public sealed record PartyToolPage(IReadOnlyList<PartyGroup> Parties, IReadOnlyList<(string Name, bool Playing, DateTime? LastSeenUtc)> Others,
    IReadOnlyDictionary<string, string> Ships, DateTime NowUtc);

/// <summary>A player's page in the Party tool.</summary>
public sealed record PartyMemberPage(PartyMember Member, DateTime NowUtc);

/// <summary>
/// The admin's Map places page: what the logs say that the map does not know, what he said things are, the places (of one system, or all), the places to
/// pick from, and a text of the log to add a place for (from the link in the Not known table).
/// </summary>
public sealed record MapPlacesPage(IReadOnlyList<UnknownPlace> Unknown, IReadOnlyList<SCTools.Server.Data.MapAliasRow> Aliases, IReadOnlyList<PlaceEntry> Places,
    Atlas Atlas, string System, string AddFor, DateTime NowUtc);
