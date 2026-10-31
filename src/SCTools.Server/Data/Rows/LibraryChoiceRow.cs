namespace SCTools.Server.Data;

/// <summary>
/// One entry of the admin's lists the Mission Library's drop-downs pick from (Frank, 31 Oct 2026): a faction or a category (Kind: "faction", "category"),
/// added and removed in the box above the library's table. They start with what the library had then (typed in, and the Star Citizen Wiki's).
/// </summary>
public sealed class LibraryChoiceRow
{
    public string Kind { get; set; } = "";
    public string Value { get; set; } = "";
}
