namespace SCTools.Server.Models;

/// <summary>A tool on the Tools page: whether everybody sees it.</summary>
public sealed record ToolLine(SiteTool Tool, bool Public);

/// <summary>The Mission Library page: the kinds of mission shown (all, or the ones without rewards yet, or a search), and how many there are.</summary>
public sealed record MissionLibraryPage(IReadOnlyList<LibraryEntry> Entries, int Total, int ToDo, string Show, string Query);
