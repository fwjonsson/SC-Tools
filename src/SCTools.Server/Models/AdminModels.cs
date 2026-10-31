using System.Globalization;
using SCTools.Server.Editing;

namespace SCTools.Server.Models;

/// <summary>
/// The Hauling Planner's party on the admin's Players page: every member, active or not and why, and the parties in the game now (his ship, drive, colour
/// and location are on his My page: the Party page, where the admin set them too, went on 31 Oct 2026).
/// </summary>
public sealed class AdminPartyModel
{
    /// <summary>Every member, the ones the logs put out of the party too (Gone).</summary>
    public List<MemberInput> Members { get; set; } = new();

    /// <summary>The logs decide who is active (the server); otherwise Active is the tick as in the program.</summary>
    public bool FollowsLogs { get; set; }

    /// <summary>The parties now, as the logs show them ("zzoega's party: zzoega, zzatvo").</summary>
    public List<PartyGroup> Groups { get; set; } = new();

    /// <summary>The members the logs put out of the party (Auto, not active): back when they show him in a party with a member again, or the admin sets him In.</summary>
    public List<string> Gone { get; set; } = new();

    /// <summary>What the logs say about a member now: "In zzoega's party", "Playing alone", "Not playing".</summary>
    public static string StatusOf(Tracker t, string name)
    {
        var group = t.State.Parties?.GroupOf(name);
        if (group == null) return "Not playing";
        if (group.Names.Count == 1) return "Playing alone";
        return PlayerNames.SameName(group.Leader, name) ? "Leads his party" : "In " + group.Leader + "'s party";
    }

    public static AdminPartyModel Of(Tracker t, DateTime nowUtc)
    {
        var follows = t.State.Data.AutoPartySince != null;
        var members = t.State.Data.Party.Select(n =>
        {
            var (location, byHand) = t.Whereabouts.LocationOf(n);
            var mode = t.Crew.OverrideOf(n) switch { true => "in", false => "out", null => "auto" };
            return new MemberInput(
                n, t.Crew.IsActiveAt(n, nowUtc),
                byHand ? location : "", PyroDistances.NameOf(t.Whereabouts.AutoLocationOf(n)),
                ViewPalette.Hex(Edits.ColorOf(t, n)), t.Crew.ShipOf(n)?.Label ?? "", t.Crew.DriveOf(n)?.Label ?? "",
                follows ? StatusOf(t, n) : "", mode);
        }).ToList();
        // with the logs deciding, a member they put out of the party is gone: he is back when they show him in a party with a member again (or you set him In)
        bool Gone(MemberInput m) => follows && !m.Active && m.Mode == "auto";
        return new AdminPartyModel
        {
            FollowsLogs = follows,
            Gone = members.Where(Gone).Select(m => m.Name).ToList(),
            Groups = t.State.Parties?.Groups.ToList() ?? new List<PartyGroup>(),
            Members = members,
        };
    }
}

public sealed record MemberInput(
    string Name, bool Active, string Location, string AutoLocation, string Color, string Ship, string Drive, string Status = "", string Mode = "auto")
{
    /// <summary>Black or white, whichever reads on his colour (as on the live page).</summary>
    public string TextColor => ViewPalette.Hex(ViewPalette.TextOn(Convert.ToInt32(Color.TrimStart('#'), 16)));
}

/// <summary>Amounts in the forms: written without spaces (1268250), read with or without them (1 268 250, 1,268,250, −1 250).</summary>
public static class Amounts
{
    public static string Edit(decimal v) => Math.Round(v).ToString("0", CultureInfo.InvariantCulture);

    public static string Signed(decimal v) => Math.Abs(v) < 0.5m ? "0" : (v > 0 ? "+" : "-") + Math.Round(Math.Abs(v)).ToString("0", CultureInfo.InvariantCulture);

    /// <summary>The amount typed, or null for nothing or something that is not a number.</summary>
    public static decimal? Parse(string? typed)
    {
        if (string.IsNullOrWhiteSpace(typed)) return null;
        var s = new string(typed.Where(c => !char.IsWhiteSpace(c) && c != ',' && c != ' ' && c != ' ').ToArray()).Replace('−', '-');
        return decimal.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}
