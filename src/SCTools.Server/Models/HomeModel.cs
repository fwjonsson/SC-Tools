namespace SCTools.Server.Models;

/// <summary>The landing page: the cards of the tools this viewer sees.</summary>
public sealed record HomeModel(IReadOnlyList<HomeCard> Cards);

/// <summary>
/// The card of a tool: what it shows now (State: the hauling session, your gauntlet runs this week, the missions left to fill in; empty: nothing). OnlyAdmin:
/// everybody else does not see it (the admin's view of a hidden tool).
/// </summary>
public sealed record HomeCard(SiteTool Tool, bool OnlyAdmin, string State);
