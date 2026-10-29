namespace SCTools.Server.Models;

/// <summary>A tool on the Tools page: whether everybody sees it.</summary>
public sealed record ToolLine(SiteTool Tool, bool Public);

/// <summary>
/// What a viewer asked the Mission Library for (the address of the page): a part of a name or a giver (Q), one giver, the kinds with or without rewards,
/// only the ones he played (Mine: a logged in viewer), and the column it is sorted by and which way (played and last: his own plays, so only for a
/// logged in viewer). The same on the read-only library (/missions) and the admin's Edit Mission Info (/admin/missions).
/// </summary>
public sealed record LibraryFilter(string Q = "", string Giver = "", string Rewards = "all", bool Mine = false, string Sort = "name", bool Desc = false)
{
    public const string DefaultSort = "name";

    /// <summary>The columns to sort by: the names, the givers, the numbers, how often the viewer completed it (Failed / Completed) and when he played it last.</summary>
    public static readonly string[] Sorts = { "name", "giver", "scu", "auec", "mg", "council", "played", "last" };

    /// <summary>A column of numbers (or dates): sorted the biggest (the newest) first when it is clicked (the names A to Z).</summary>
    public static bool Numeric(string sort) => sort is "scu" or "auec" or "mg" or "council" or "played" or "last";

    /// <summary>A column of the viewer's own plays: only for somebody logged in.</summary>
    public static bool Own(string sort) => sort is "played" or "last";

    /// <summary>The filter of a page's address; what is not a choice is the default. loggedIn: Mine and Played by me are only for a viewer with a name.</summary>
    public static LibraryFilter From(string? q, string? giver, string? rewards, string? mine, string? sort, string? dir, bool loggedIn)
    {
        var s = sort != null && Sorts.Contains(sort) && (!Own(sort) || loggedIn) ? sort : DefaultSort;
        return new(q?.Trim() ?? "", giver?.Trim() ?? "", rewards is "known" or "todo" ? rewards : "all", loggedIn && mine is "1" or "true" or "on", s,
            dir == null ? Numeric(s) : dir == "desc");
    }

    /// <summary>Nothing asked: every kind, by name.</summary>
    public bool IsDefault => Q.Length == 0 && Giver.Length == 0 && Rewards == "all" && !Mine && Sort == DefaultSort && !Desc;

    /// <summary>The kinds this filter keeps, in its order. viewer: whose plays count for Mine and Played by me (null: nobody's).</summary>
    public IEnumerable<LibraryEntry> Apply(IEnumerable<LibraryEntry> all, string? viewer)
    {
        var list = all;
        if (Rewards == "todo") list = list.Where(e => e.NeedsInfo);
        else if (Rewards == "known") list = list.Where(e => !e.NeedsInfo);
        if (Mine) list = list.Where(e => e.PlaysOf(viewer) != null);
        if (Giver.Length > 0) list = list.Where(e => string.Equals(e.Row.Giver?.Trim(), Giver, StringComparison.OrdinalIgnoreCase));
        if (Q.Length > 0)
            list = list.Where(e => e.Shown.Contains(Q, StringComparison.OrdinalIgnoreCase) || e.Row.Title.Contains(Q, StringComparison.OrdinalIgnoreCase)
                || (e.Row.Giver ?? "").Contains(Q, StringComparison.OrdinalIgnoreCase));
        return Sorted(list.ToList(), viewer);
    }

    /// <summary>In the order asked: by the column, the kinds without a value in it last (by name), and the same values by name.</summary>
    IEnumerable<LibraryEntry> Sorted(List<LibraryEntry> list, string? viewer)
    {
        var names = StringComparer.OrdinalIgnoreCase;
        Func<LibraryEntry, long?>? number = Sort switch
        {
            "scu" => e => e.ScuShown,
            "auec" => e => e.AuecShown,
            "mg" => e => e.Row.MgScrip,
            "council" => e => e.Row.CouncilScrip,
            "played" => e => e.PlaysOf(viewer)?.Completed,
            "last" => e => e.PlaysOf(viewer)?.LastPlayed?.Ticks,
            _ => null,
        };
        if (number != null)
        {
            var known = list.Where(e => number(e) != null);
            var sorted = Desc ? known.OrderByDescending(e => number(e)!.Value) : known.OrderBy(e => number(e)!.Value);
            return sorted.ThenBy(e => e.Shown, names).Concat(list.Where(e => number(e) == null).OrderBy(e => e.Shown, names));
        }
        Func<LibraryEntry, string> text = Sort == "giver" ? e => e.Row.Giver?.Trim() ?? "" : e => e.Shown;
        var with = list.Where(e => text(e).Length > 0);
        var ordered = Desc ? with.OrderByDescending(text, names) : with.OrderBy(text, names);
        return ordered.ThenBy(e => e.Shown, names).Concat(list.Where(e => text(e).Length == 0).OrderBy(e => e.Shown, names));
    }

    /// <summary>The address of this list under this path: a page of it, or sorted another way.</summary>
    public string Link(string path, int page = 1, string? sort = null, bool? desc = null)
    {
        var s = sort ?? Sort;
        var d = desc ?? Desc;
        var parts = new List<string>();
        if (Q.Length > 0) parts.Add("q=" + Uri.EscapeDataString(Q));
        if (Giver.Length > 0) parts.Add("giver=" + Uri.EscapeDataString(Giver));
        if (Rewards != "all") parts.Add("rewards=" + Rewards);
        if (Mine) parts.Add("mine=1");
        if (s != DefaultSort || d)
        {
            parts.Add("sort=" + s);
            parts.Add("dir=" + (d ? "desc" : "asc"));
        }
        if (page > 1) parts.Add("page=" + page);
        return path + (parts.Count > 0 ? "?" + string.Join("&", parts) : "");
    }

    /// <summary>The address that sorts by this column: the other way round when it is sorted by it already, else its own first way.</summary>
    public string SortLink(string path, string sort) => Link(path, 1, sort, sort == Sort ? !Desc : Numeric(sort));
}

/// <summary>
/// A page of the Mission Library as a viewer asked for it (LibraryFilter): the kinds of mission on it, how many there are in all and found, how many
/// without and with rewards and played by the viewer (for the choices), the page of how many, the version of the library it shows (the page asks for the
/// version and gets the list again when it changed), every giver typed in so far (to pick from), the viewer's name (null: not logged in: no Played by me),
/// and whether it is the admin's editable one (Edit Mission Info: a field per cell) under Path.
/// </summary>
public sealed record MissionLibraryPage(IReadOnlyList<LibraryEntry> Entries, LibraryFilter Filter, int Total, int Found, int ToDo, int Known, int Mine,
    int Page, int Pages, int Version, IReadOnlyList<string> Givers, string? Viewer, string Path, bool Editable)
{
    /// <summary>The address of this list: a page of it.</summary>
    public string Link(int page) => Filter.Link(Path, page);

    /// <summary>The address of this list sorted by this column (clicked in its header).</summary>
    public string SortLink(string sort) => Filter.SortLink(Path, sort);
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
