namespace HaulingTracker.Server.Models;

/// <summary>The landing page: what the hauling tracker is showing now ("Session: 28 Oct 00:22 to now (running)"), empty when no session.</summary>
public sealed record HomeModel(string HaulingSession);
