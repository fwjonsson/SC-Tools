using System.Globalization;
using HaulingTracker.Server.Editing;

namespace HaulingTracker.Server.Models;

/// <summary>The admin's Session page: the session buttons, the mission rewards, the start time, the stops, the runs and the SCU of the from-Ruin drop-offs.</summary>
public sealed class AdminSessionModel
{
    public string Session { get; set; } = "";
    public bool HasSession { get; set; }

    /// <summary>When the running session of Start Session began (local time), or null when none runs.</summary>
    public string? OpenSince { get; set; }

    public bool Locked { get; set; }
    public string StartTime { get; set; } = "";
    public List<RewardInput> Rewards { get; set; } = new();
    public List<StopInput> Stops { get; set; } = new();
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
            Session = s.Current == null ? "No session yet: start one, or it starts by itself with the first missions in the logs." : BoardTexts.DescribeSession(s.Current),
            OpenSince = open?.StartUtc.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.CurrentCulture),
            Locked = inp?.NextStopsLocked == true,
            StartTime = inp?.Start ?? "",
        };
        m.Rewards = Earnings.RewardLines
            .Select((l, i) => new RewardInput(i, l.Contract, Amounts.Edit(inp != null && l.Get(inp) > 0 ? l.Get(inp) : Earnings.StartReward(l, s.Data)))).ToList();
        if (inp == null) return m;

        m.Stops = s.OpenRows.Select(r => new StopInput(
            r.Zone, t.Places.LocationName(r.Zone), inp.Rows.TryGetValue(r.Zone, out var row) ? row.Notes : "", BoardTexts.MissionsText(r))).ToList();
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

public sealed record RewardInput(int Index, string Contract, string Value);
public sealed record StopInput(string Zone, string Name, string Notes, string Batches);
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
        return new AdminPartyModel
        {
            FollowsLogs = follows,
            Groups = t.State.Parties?.Groups.ToList() ?? new List<PartyGroup>(),
            Stations = MeModel.StationChoices(),   // the same lists as My page
            Ships = MeModel.ShipChoices(),
            Drives = MeModel.DriveChoices(),
            Members = t.State.Data.Party.Select(n =>
            {
                var (location, byHand) = t.Whereabouts.LocationOf(n);
                var mode = t.Crew.OverrideOf(n) switch { true => "in", false => "out", null => "auto" };
                return new MemberInput(
                    n, t.Crew.IsActiveAt(n, nowUtc), Math.Max(1, t.Crew.ShipCapacityOf(n)),
                    byHand ? location : "", PyroDistances.NameOf(t.Whereabouts.AutoLocationOf(n)),
                    ViewPalette.Hex(Edits.ColorOf(t, n)), t.Crew.ShipOf(n)?.Label ?? "", t.Crew.DriveOf(n)?.Label ?? "",
                    follows ? StatusOf(t, n) : "", mode);
            }).ToList(),
        };
    }
}

public sealed record MemberInput(
    string Name, bool Active, int Batches, string Location, string AutoLocation, string Color, string Ship, string Drive, string Status = "", string Mode = "auto");

/// <summary>The admin's Contracts page: every mission still going (and the ones marked as gone), with who of the party has it.</summary>
public sealed class AdminContractsModel
{
    public List<string> Members { get; set; } = new();
    public List<ContractLine> Contracts { get; set; } = new();

    public static AdminContractsModel Of(Tracker t)
    {
        var members = t.State.Data.Party.ToList();
        return new AdminContractsModel
        {
            Members = members,
            Contracts = t.Contracts.ContractRows().Select(m => new ContractLine(
                m.Id,
                global::HaulingTracker.Tracking.Contracts.ContractName(m),
                m.IsOut ? m.OutCargo ?? "Cargo" : m.Cargo ?? "",
                t.Contracts.ContractStopsText(m),
                m.Dropped,
                members.Select(n => new HolderBox(n, t.Contracts.HasMission(m, n), m.Taker != null && PlayerNames.SameName(m.Taker, n))).ToList())).ToList(),
        };
    }
}

public sealed record ContractLine(string Id, string Contract, string Cargo, string Stops, bool Gone, List<HolderBox> Holders);
public sealed record HolderBox(string Member, bool Has, bool Took);

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
                .Select(b => new BalanceInput(b.Other, Amounts.Signed(b.Shown), global::HaulingTracker.Tracking.Payouts.Meaning(who, b.Other, b.Shown))).ToList(),
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
