namespace HaulingTracker.Server.Models;

/// <summary>
/// The landing page: the cards of the tools this viewer sees, and what the hauling tracker is showing now ("Session: 28 Oct 00:22 to now (running)"),
/// empty when no session.
/// </summary>
public sealed record HomeModel(string HaulingSession, IReadOnlyList<HomeCard> Cards);

/// <summary>The card of a tool. OnlyAdmin: everybody else does not see it (the admin's view of a hidden tool).</summary>
public sealed record HomeCard(SiteTool Tool, bool OnlyAdmin);
