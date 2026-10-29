using SCTools.Server;
using SCTools.Server.Data;
using SCTools.Server.Models;
using SCTools.Tracking;

namespace SCTools.Tests.ServerPages;

/// <summary>
/// What a viewer asks the Mission Library for: a search, a giver, with or without rewards, the kinds he played, and the order (a column, either way: the
/// kinds without a value in it last, the same values by name). The same choices on the read-only library and on Edit Mission Info, kept in the address.
/// </summary>
public class LibraryFilterTests
{
    static readonly DateTime T = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

    /// <param name="plays">who played it and how often he completed it (he took it once more)</param>
    static LibraryEntry Kind(string title, string? giver = null, long? auec = null, long? scu = null, long? suggested = null, WikiContract? wiki = null,
        (string Player, int Completed)[]? plays = null)
    {
        plays ??= Array.Empty<(string, int)>();
        return new(new MissionKindRow { Key = title.ToLowerInvariant(), Title = title, Giver = giver, Auec = auec, Scu = scu, ScuSuggested = suggested },
            plays.Sum(p => p.Completed + 1), plays.Sum(p => p.Completed), 0, 0, plays.Length > 0 ? T : null, false, wiki,
            plays.ToDictionary(p => p.Player, p => new PlayCount(p.Completed + 1, p.Completed, 0, 0, T, false)));
    }

    static readonly WikiContract Master = new("26e80306-6190-447f-a214-634d8ed8ca7d", "Master", "Master Hauler", new[] { new ScuRange(400, 600) }, 2_000_000, 2_200_000, "aUEC");

    static readonly LibraryEntry[] All =
    {
        Kind("Stop Rival Attack", "Red Wind", 25_000, 1_536, plays: new[] { ("zzoega", 2) }),
        Kind("Kill the Outlaw", "Bounty Hunters Guild", 40_000, plays: new[] { ("zzoega", 0), ("bileh123", 5) }),
        Kind("Master Hauler Needed", "Covalex", wiki: Master),
        Kind("Hauling: 2 To Ruin", null, 1_300_000, plays: new[] { ("bileh123", 1) }),
        Kind("Deliver Supplies", "red wind", suggested: 8),
    };

    static string[] Names(LibraryFilter f, string? viewer = null) => f.Apply(All, viewer).Select(e => e.Shown).ToArray();

    [Fact]
    public void By_name_is_the_default_and_a_column_sorts_its_way_with_the_unknown_ones_last()
    {
        var f = LibraryFilter.From(null, null, null, null, null, null, loggedIn: false);
        Assert.True(f.IsDefault);
        Assert.Equal(new[] { "Deliver Supplies", "Hauling: 2 To Ruin", "Kill the Outlaw", "Master Hauler Needed", "Stop Rival Attack" }, Names(f));
        Assert.Equal(Names(f).Reverse(), Names(f with { Desc = true }));

        // the numbers: the biggest first when clicked, the ones without a value last either way; what the page shows counts (the Wiki's aUEC, the suggested SCU)
        var auec = LibraryFilter.From(null, null, null, null, "auec", null, false);
        Assert.True(auec.Desc);
        Assert.Equal(new[] { "Master Hauler Needed", "Hauling: 2 To Ruin", "Kill the Outlaw", "Stop Rival Attack", "Deliver Supplies" }, Names(auec));
        Assert.Equal(new[] { "Stop Rival Attack", "Kill the Outlaw", "Hauling: 2 To Ruin", "Master Hauler Needed", "Deliver Supplies" }, Names(auec with { Desc = false }));
        Assert.Equal(new[] { "Stop Rival Attack", "Master Hauler Needed", "Deliver Supplies", "Hauling: 2 To Ruin", "Kill the Outlaw" }, Names(LibraryFilter.From(null, null, null, null, "scu", null, false)));

        // the givers: A to Z (capitals do not matter), the kinds without one last; the same giver by name
        Assert.Equal(new[] { "Kill the Outlaw", "Master Hauler Needed", "Deliver Supplies", "Stop Rival Attack", "Hauling: 2 To Ruin" }, Names(LibraryFilter.From(null, null, null, null, "giver", null, false)));

        // what the viewer completed: his own, the kinds he never played last; only for somebody logged in
        Assert.Equal(new[] { "Stop Rival Attack", "Kill the Outlaw", "Deliver Supplies", "Hauling: 2 To Ruin", "Master Hauler Needed" }, Names(LibraryFilter.From(null, null, null, null, "played", null, true), "zzoega"));
        Assert.Equal(new[] { "Kill the Outlaw", "Hauling: 2 To Ruin", "Deliver Supplies", "Master Hauler Needed", "Stop Rival Attack" }, Names(LibraryFilter.From(null, null, null, null, "played", null, true), "Bileh123"));
        Assert.Equal("name", LibraryFilter.From(null, null, null, null, "played", null, false).Sort);
        Assert.Equal("name", LibraryFilter.From(null, null, null, null, "nonsense", "desc", true).Sort);
    }

    [Fact]
    public void The_choices_keep_the_kinds_asked_for()
    {
        Assert.Equal(new[] { "Deliver Supplies", "Stop Rival Attack" }, Names(LibraryFilter.From(null, "Red Wind", null, null, null, null, false)));   // capitals do not matter
        Assert.Equal(new[] { "Deliver Supplies", "Stop Rival Attack" }, Names(LibraryFilter.From("wind", null, null, null, null, null, false)));   // the giver too
        Assert.Equal(new[] { "Kill the Outlaw" }, Names(LibraryFilter.From("outlaw", null, null, null, null, null, false)));
        Assert.Equal(new[] { "Deliver Supplies", "Master Hauler Needed" }, Names(LibraryFilter.From(null, null, "todo", null, null, null, false)));
        Assert.Equal(new[] { "Hauling: 2 To Ruin", "Kill the Outlaw", "Stop Rival Attack" }, Names(LibraryFilter.From(null, null, "known", null, null, null, false)));
        Assert.Equal(new[] { "Kill the Outlaw", "Stop Rival Attack" }, Names(LibraryFilter.From(null, null, null, "1", null, null, true), "zzoega"));
        Assert.Equal(new[] { "Hauling: 2 To Ruin", "Kill the Outlaw" }, Names(LibraryFilter.From(null, null, "known", "1", null, null, true), "bileh123"));
        Assert.False(LibraryFilter.From(null, null, null, "1", null, null, loggedIn: false).Mine);   // nobody's plays for a visitor
        Assert.Empty(Names(LibraryFilter.From(null, null, null, "1", null, null, true), null));
    }

    [Fact]
    public void The_address_keeps_the_choices_and_a_header_sorts_by_its_column_or_the_other_way_round()
    {
        var f = LibraryFilter.From(" rival ", "Red Wind", "todo", "1", "auec", "asc", true);
        Assert.Equal("/missions?q=rival&giver=Red%20Wind&rewards=todo&mine=1&sort=auec&dir=asc", f.Link("/missions"));
        Assert.Equal("/admin/missions?q=rival&giver=Red%20Wind&rewards=todo&mine=1&sort=auec&dir=asc&page=3", f.Link("/admin/missions", 3));
        Assert.Equal("/missions?q=rival&giver=Red%20Wind&rewards=todo&mine=1&sort=auec&dir=desc", f.SortLink("/missions", "auec"));   // the other way round
        Assert.Equal("/missions?q=rival&giver=Red%20Wind&rewards=todo&mine=1&sort=scu&dir=desc", f.SortLink("/missions", "scu"));   // a number: the biggest first
        Assert.Equal("/missions?q=rival&giver=Red%20Wind&rewards=todo&mine=1", f.SortLink("/missions", "name"));   // by name A to Z: the default, nothing to say
        Assert.Equal("/missions?sort=name&dir=desc", new LibraryFilter().SortLink("/missions", "name"));
        Assert.Equal("/missions", new LibraryFilter().Link("/missions"));
        Assert.Equal("/missions?page=2", new LibraryFilter().Link("/missions", 2));
    }
}
