using System.Globalization;
using SCTools.Server.Editing;

namespace SCTools.Server.Models;

/// <summary>The admin's Session page: the session buttons, the start time, the stops, the runs (to register lost cargo) and the SCU of the from-Ruin drop-offs.</summary>
public sealed class AdminSessionModel
{
    public string Session { get; set; } = "";
    public bool HasSession { get; set; }

    /// <summary>When the running session began (local time), or null when none runs.</summary>
    public string? OpenSince { get; set; }

    /// <summary>The logs start and stop the sessions (AutoSessions): no Start / Stop Session button.</summary>
    public bool Auto { get; set; }

    public string StartTime { get; set; } = "";
    public List<RunInput> Runs { get; set; } = new();
    public List<ScuInput> Scu { get; set; } = new();

    public static AdminSessionModel Of(Tracker t)
    {
        var s = t.State;
        var inp = s.Inputs;
        var open = Edits.OpenSession(t);
        var m = new AdminSessionModel
        {
            HasSession = s.Current != null && inp != null,
            Session = s.Current == null ? "No session yet: it starts by itself when somebody in the party accepts a hauling contract." : BoardTexts.DescribeSession(s.Current),
            OpenSince = open?.StartUtc.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.CurrentCulture),
            StartTime = inp?.Start ?? "",
        };
        if (inp == null) return m;

        m.Runs = s.Trips.Select(r => new RunInput(
            r.Key, r.Number, r.Worker,
            r.Start.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture),
            r.End.HasValue ? r.End.Value.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture) : "on the way",
            string.Join(", ", r.Pickups.Select(p => t.Places.LocationName(p.Zone)).Distinct()),
            t.Runs.LostLevel(r.Key))).ToList();
        var typed = t.State.Data.TypedScu;
        m.Scu = t.DropOffs.OpenDrops()
            .Select(d => (Key: d.Mission.Id + "|" + d.Objective, d.Scu))
            .Where(d => d.Scu <= 0 || typed.ContainsKey(d.Key))
            .Select(d => new ScuInput(d.Key, t.DropOffs.DropLabel(d.Key), typed.GetValueOrDefault(d.Key)))
            .ToList();
        return m;
    }
}

public sealed record RunInput(string Key, int Number, string Worker, string Started, string Completed, string Places, string Lost);
public sealed record ScuInput(string Key, string Label, int Scu);

/// <summary>The admin's Party page: every member with what can be set for him, and the lists to choose from.</summary>
public sealed class AdminPartyModel
{
    public List<MemberInput> Members { get; set; } = new();
    public List<Choice> Stations { get; set; } = new();
    public List<Choice> Ships { get; set; } = new();
    public List<Choice> Drives { get; set; } = new();

    /// <summary>The logs decide who is active (the server); otherwise Active is the tick as in the program.</summary>
    public bool FollowsLogs { get; set; }

    /// <summary>The parties now, as the logs show them ("zzoega's party: zzoega, zzatvo").</summary>
    public List<PartyGroup> Groups { get; set; } = new();

    /// <summary>The members the logs put out of the party (Auto, not active): not in the table (Frank, 30 Oct 2026: gone from the page when he leaves), named under it.</summary>
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
        // with the logs deciding, a member they put out of the party is not in the table: he is back when they show him in a party with a member again (or you set him In)
        bool Gone(MemberInput m) => follows && !m.Active && m.Mode == "auto";
        return new AdminPartyModel
        {
            FollowsLogs = follows,
            Gone = members.Where(Gone).Select(m => m.Name).ToList(),
            Groups = t.State.Parties?.Groups.ToList() ?? new List<PartyGroup>(),
            Stations = MeModel.StationChoices(),   // the same lists as My page
            Ships = MeModel.ShipChoices(),
            Drives = MeModel.DriveChoices(),
            Members = members.Where(m => !Gone(m)).ToList(),
        };
    }
}

public sealed record MemberInput(
    string Name, bool Active, string Location, string AutoLocation, string Color, string Ship, string Drive, string Status = "", string Mode = "auto")
{
    /// <summary>Black or white, whichever reads on his colour (as on the live page).</summary>
    public string TextColor => ViewPalette.Hex(ViewPalette.TextOn(Convert.ToInt32(Color.TrimStart('#'), 16)));
}

/// <summary>The admin's Debts page: the balances of one member (typed over to correct them), the money transfers, the mission payouts, and the session party.</summary>
public sealed class AdminDebtsModel
{
    public string Me { get; set; } = "";
    public List<string> Names { get; set; } = new();
    public List<BalanceInput> Balances { get; set; } = new();
    public List<TransferInput> Transfers { get; set; } = new();
    public List<PayoutInput> Payouts { get; set; } = new();
    public bool HasSession { get; set; }
    public List<(string Name, bool In)> SessionParty { get; set; } = new();

    public static AdminDebtsModel Of(Tracker t, string? me)
    {
        var names = t.Payouts.DebtPartyNames();
        var who = names.FirstOrDefault(n => me != null && PlayerNames.SameName(n, me)) ?? names.FirstOrDefault() ?? "";
        var data = t.State.Data;
        var inp = t.State.Inputs;
        var party = inp == null ? new List<string>() : inp.Party ?? inp.Members;
        return new AdminDebtsModel
        {
            Me = who,
            Names = names,
            Balances = who.Length == 0 ? new() : Edits.Balances(t, who)
                .Select(b => new BalanceInput(b.Other, Amounts.Signed(b.Shown), global::SCTools.Tracking.Payouts.Meaning(who, b.Other, b.Shown))).ToList(),
            Transfers = data.Transfers.OrderByDescending(x => x.Time)
                .Where(x => who.Length == 0 || PlayerNames.SameName(x.From, who) || PlayerNames.SameName(x.To, who))
                .Select(x => new TransferInput(Edits.TransferKey(x), x.Time.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.CurrentCulture), x.From, x.To,
                    x.Amount.ToString("N0", CultureInfo.CurrentCulture), x.Payment)).ToList(),
            Payouts = data.Payouts.OrderByDescending(r => r.Time)
                .Select(r => new PayoutInput(r.MissionId, r.Time.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.CurrentCulture), r.Contract,
                    Amounts.Edit(r.Payout), data.PayoutOverrides.ContainsKey(r.MissionId), string.Join(", ", r.Members), r.Each.ToString("N0", CultureInfo.CurrentCulture))).ToList(),
            HasSession = inp != null,
            SessionParty = names.Select(n => (n, party.Any(x => PlayerNames.SameName(x, n)))).ToList(),
        };
    }
}

public sealed record BalanceInput(string Other, string Balance, string Meaning);
public sealed record TransferInput(string Key, string Time, string From, string To, string Amount, bool Payment);
public sealed record PayoutInput(string MissionId, string Time, string Contract, string Payout, bool Typed, string Members, string Each);

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
