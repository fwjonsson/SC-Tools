using System.Globalization;

namespace SCTools.View;

public sealed partial class WebSnapshot
{
    /// <summary>
    /// The Debts tab for everybody: earnings, the balance of every member with the others, all money transfers (transfersEdit: the key and sender of each, so
    /// the sender logged in can tick Payment on the page) and the mission payouts.
    /// </summary>
    object Debts()
    {
        var data = _s.Data;
        var party = _t.Payouts.DebtPartyNames();
        var nets = DebtCalc.Nets(data.Sessions.Values, data.Transfers);
        var pairs = DebtCalc.Pairs(data.Sessions.Values, data.Transfers);

        var balances = party.Select(me =>
        {
            var grid = new ViewTable(_p, new ViewColumn("Party Member", "left"), new ViewColumn("Balance", "right"), new ViewColumn("Meaning", "left"));
            foreach (var o in party.Where(o => !SameName(o, me)))
            {
                var shown = DebtCalc.Between(pairs, me, o) + _t.Payouts.GetAdj(me, o);
                grid.Add(o, Signed(shown), Payouts.Meaning(me, o, shown));
            }
            return new { name = me, grid };
        }).ToList();

        var transfers = new ViewTable(_p,
            new ViewColumn("Time"), new ViewColumn("From"), new ViewColumn("To"), new ViewColumn("Amount", "right"),
            new ViewColumn("Payment"));
        var transfersEdit = new List<object>();
        foreach (var t in data.Transfers.OrderByDescending(t => t.Time))
        {
            transfersEdit.Add(new { key = t.Key, from = t.From, payment = t.Payment });
            transfers.Add(
                t.Time.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.CurrentCulture),
                t.From,
                t.To,
                t.Amount.ToString("N0", CultureInfo.InvariantCulture),
                t.Payment ? "yes" : "");
        }

        var payouts = new ViewTable(_p,
            new ViewColumn("Time"), new ViewColumn("Mission", "left"), new ViewColumn("Payout", "right"),
            new ViewColumn("Shared Between", "left"), new ViewColumn("Each", "right"));
        foreach (var r in data.Payouts.OrderByDescending(r => r.Time))
        {
            payouts.Add(
                r.Time.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.CurrentCulture),
                r.Contract,
                r.Payout.ToString("N0", CultureInfo.InvariantCulture),
                string.Join(", ", r.Members),
                r.Each.ToString("N0", CultureInfo.InvariantCulture));
        }

        // earnings: what every member carried this session and in all sessions, and his net balance (green: owed to him, red: he owes)
        var earnings = new ViewTable(_p,
            new ViewColumn("Worker", "left"), new ViewColumn("This Session", "right"), new ViewColumn("All Sessions", "right"), new ViewColumn("Net Balance (+ Owed to Them)", "right"));
        decimal sumNow = 0;
        decimal sumAll = 0;
        foreach (var p in party)
        {
            var now = _s.Inputs == null ? 0m : Payouts.CarriedBy(_s.Inputs.Carried, p);
            var all = data.Sessions.Values.Sum(s => Payouts.CarriedBy(s.Carried, p));
            nets.TryGetValue(p, out var net);
            sumNow += now;
            sumAll += all;
            var row = earnings.Add(p, now.ToString("N0", CultureInfo.InvariantCulture), all.ToString("N0", CultureInfo.InvariantCulture), Signed(net));
            if (Math.Abs(net) >= 0.5m) row.Color(3, net > 0 ? _p.Green : _p.Red);
        }
        // one player's accounts added up (Frank, 30 Oct 2026): a look at what he earns in all; the balances and the debts stay each account's own
        foreach (var group in data.CombinedAccounts)
        {
            var members = group.Where(n => party.Any(p => string.Equals(p, n, StringComparison.OrdinalIgnoreCase))).ToList();
            if (members.Count < 2) continue;
            var now = _s.Inputs == null ? 0m : members.Sum(m => Payouts.CarriedBy(_s.Inputs.Carried, m));
            var all = members.Sum(m => data.Sessions.Values.Sum(s => Payouts.CarriedBy(s.Carried, m)));
            var net = members.Sum(m => nets.TryGetValue(m, out var n) ? n : 0m);
            earnings.Add(string.Join(" + ", members), now.ToString("N0", CultureInfo.InvariantCulture), all.ToString("N0", CultureInfo.InvariantCulture), Signed(net)).Color(0, _p.Cell, _p.Muted);
        }
        earnings.Add("Total", sumNow.ToString("N0", CultureInfo.InvariantCulture), sumAll.ToString("N0", CultureInfo.InvariantCulture), "").Bold = true;

        return new { earnings, balances, transfers, transfersEdit, payouts, pos = ViewPalette.Hex(_p.Green), neg = ViewPalette.Hex(_p.Red) };
    }

    /// <summary>The History tab: the all-time leaderboard, the best session, and every saved session with the Total.</summary>
    object History()
    {
        var lines = _t.Leaderboard.AllTimeLines(out var sessions, out var totalSeconds);
        var allTime = Board(lines, true, false, sessions, totalSeconds);

        var saved = new ViewTable(_p,
            new ViewColumn("Date"), new ViewColumn("Time"), new ViewColumn("Missions", "right"), new ViewColumn("Revenue", "right"), new ViewColumn("Carried by Worker", "left"));
        foreach (var s in _t.Leaderboard.HistoryRows()) saved.Add(s.Date, s.Time, s.Missions.ToString(CultureInfo.InvariantCulture), s.Revenue, s.Carried);
        var (missions, revenue) = _t.Leaderboard.HistoryTotals();
        saved.Add("Total", "", missions.ToString(CultureInfo.InvariantCulture), revenue.ToString("N0", CultureInfo.InvariantCulture), "").Bold = true;

        return new { allTime, best = _t.Leaderboard.BestLine(), saved };
    }
}
