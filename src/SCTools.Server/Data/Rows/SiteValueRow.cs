namespace SCTools.Server.Data;

/// <summary>A number the admin sets for the whole site, by its name (MgScrip, CouncilScrip: what one scrip is worth in aUEC).</summary>
public sealed class SiteValueRow
{
    public string Key { get; set; } = "";
    public long Value { get; set; }
    public DateTime ChangedUtc { get; set; }
}
