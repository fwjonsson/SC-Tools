namespace SCTools.Server;

/// <summary>What the admin's pages get (UnknownPlaceAlerts.News): the newest notice's number, the notices after the one asked with, and the Companion's cards.</summary>
public sealed record UnknownNews(int Seq, List<UnknownAlert> Alerts, List<UnknownSighting> Here);
