namespace HaulingTracker.Server.Models;

/// <summary>A tool on the Tools page: whether everybody sees it.</summary>
public sealed record ToolLine(SiteTool Tool, bool Public);
