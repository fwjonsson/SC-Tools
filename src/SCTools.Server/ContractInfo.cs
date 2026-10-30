using System.Globalization;

namespace SCTools.Server;

/// <summary>
/// What the Mission Library says about a contract's kind, as the contract tables show it (the Map data's Active Contracts, the Companion's Active and
/// Completed Contracts): the name, faction, category, SCU, reward, scrip, XP and legality, as text. Edit: what the admin types over on the Companion (the
/// fields of Edit Mission Info, with what each shows in grey when nothing is typed in), or null for a kind the library does not have.
/// </summary>
public sealed record ContractInfo(string Title, string Faction, string Category, string Scu, string Reward, string Mg, string Council, string Xp, string Legality,
    ContractEdit? Edit)
{
    static string N(long? v) => v?.ToString("N0", CultureInfo.InvariantCulture) ?? "";

    /// <summary>The columns of a contract of this kind (e: its library entry, or null), titled by the game's name when the library does not have it.</summary>
    public static ContractInfo Of(LibraryEntry? e, string title) => new(
        e?.Shown ?? title, e?.FactionShown ?? "", e?.Category ?? "", N(e?.ScuShown), e == null ? "" : e.Row.Auec != null ? N(e.Row.Auec) : e.Wiki?.RewardText ?? "",
        N(e?.Row.MgScrip), N(e?.Row.CouncilScrip), N(e?.Xp), e?.Legality ?? "", e == null ? null : ContractEdit.Of(e));

    /// <summary>The library's entries by their key, to look contracts up in.</summary>
    public static Dictionary<string, LibraryEntry> ByKey(MissionLibrary? library) => library == null
        ? new Dictionary<string, LibraryEntry>(StringComparer.OrdinalIgnoreCase)
        : library.Entries().GroupBy(e => e.Row.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// The admin's fields of a kind of mission (Edit Mission Info's: MissionLibrary.Set), as the Companion's tables give them to him: the key, the values typed
/// in (empty: none), and the grey hints shown in an empty field (the game's name, the Wiki's faction and reward, the suggested or the Wiki's SCU).
/// </summary>
public sealed record ContractEdit(string Key, string Name, string Giver, string Scu, string Auec, string Mg, string Council,
    string NameHint, string GiverHint, string ScuHint, string AuecHint)
{
    static string N(long? v) => v?.ToString(CultureInfo.InvariantCulture) ?? "";   // in a field: no thousands separators

    public static ContractEdit Of(LibraryEntry e)
    {
        var r = e.Row;
        var w = e.Wiki;
        var wikiScu = w != null && w.Orders.Count > 0 ? "~" + N((long)Math.Round(w.Total.Middle)) : "";
        return new ContractEdit(r.Key, r.Name ?? r.Title, r.Giver ?? "", N(r.Scu), N(r.Auec), N(r.MgScrip), N(r.CouncilScrip),
            r.Title, w?.Faction ?? "Type or pick", r.ScuSuggested != null ? N(r.ScuSuggested) : wikiScu, w?.RewardText ?? "");
    }
}
