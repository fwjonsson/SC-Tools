namespace SCTools.Server.Models;

/// <summary>A tool on the Tools page: whether everybody sees it.</summary>
public sealed record ToolLine(SiteTool Tool, bool Public);

/// <summary>The Mission Library page: the kinds of mission shown (all, or the ones without rewards yet, or a search), and how many there are.</summary>
public sealed record MissionLibraryPage(IReadOnlyList<LibraryEntry> Entries, int Total, int ToDo, string Show, string Query);

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
