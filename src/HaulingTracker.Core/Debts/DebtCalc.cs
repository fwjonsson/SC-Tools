namespace HaulingTracker.Debts;

/// <summary>Who owes whom. net &gt; 0 means others owe that person, net &lt; 0 means he owes others.</summary>
public static class DebtCalc
{
    /// <summary>
    /// Every saved session: the rewards are split equally between everyone in the party, but every worker is only
    /// entitled to the value he carried. The difference is what he is owed (or owes).
    /// Payments (ticked transfers) move money from the sender to the receiver.
    /// </summary>
    public static Dictionary<string, decimal> Nets(IEnumerable<SessionInputs> sessions, IEnumerable<SavedTransfer> transfers)
    {
        var net = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        void Add(string name, decimal value)
        {
            net.TryGetValue(name, out var old);
            net[name] = old + value;
        }

        foreach (var s in sessions)
        {
            foreach (var (k, v) in SessionNets(s)) Add(k, v);
        }

        foreach (var t in transfers)
        {
            if (!t.Payment) continue;
            Add(t.From, t.Amount);    // the sender has paid, so he is owed more (or owes less)
            Add(t.To, -t.Amount);
        }
        return net;
    }

    /// <summary>What every member of one session is owed (+) or owes (-): the value he carried less his share of its payouts. Nothing when it has no values saved.</summary>
    static List<(string Name, decimal Net)> SessionNets(SessionInputs s)
    {
        var res = new List<(string Name, decimal Net)>();
        if (s.Carried.Count == 0) return res;
        if (s.Received.Count > 0)
        {
            // every payout was split between the party present at that moment: you are owed what you carried and you owe what you received
            foreach (var k in s.Carried.Keys.Concat(s.Received.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var carried = s.Carried.Where(kv => Same(kv.Key, k)).Sum(kv => kv.Value);
                var got = s.Received.Where(kv => Same(kv.Key, k)).Sum(kv => kv.Value);
                res.Add((k, carried - got));
            }
            return res;
        }
        var members = new List<string>(s.Members);
        foreach (var k in s.Carried.Keys)
        {
            if (!members.Any(x => Same(x, k))) members.Add(k);
        }
        var share = s.Carried.Values.Sum() / members.Count;
        foreach (var m in members) res.Add((m, s.Carried.Where(kv => Same(kv.Key, m)).Sum(kv => kv.Value) - share));
        return res;
    }

    /// <summary>
    /// Who owes whom, pair by pair, from what really happened (the balances of the Debts tab). In every session the members who carried more than their share
    /// of its payouts are owed by those who carried less, each in proportion to how much less; a ticked payment counts between the two who made it. So a
    /// payment to one member never moves a debt to somebody else (as the fewest payments of Settle did), and the balances of a member add up to his net.
    /// </summary>
    public static List<(string Debtor, string Creditor, decimal Amount)> Pairs(IEnumerable<SessionInputs> sessions, IEnumerable<SavedTransfer> transfers)
    {
        var res = new List<(string Debtor, string Creditor, decimal Amount)>();
        foreach (var s in sessions)
        {
            var nets = SessionNets(s);
            var owed = nets.Where(n => n.Net > 0).ToList();
            var owing = nets.Where(n => n.Net < 0).ToList();
            var basis = Math.Max(owed.Sum(n => n.Net), -owing.Sum(n => n.Net));   // the same when the payouts are the value carried; never more than anybody's share
            if (basis <= 0) continue;
            foreach (var c in owed)
            {
                foreach (var d in owing) res.Add((d.Name, c.Name, c.Net * -d.Net / basis));
            }
        }
        foreach (var t in transfers)
        {
            if (t.Payment) res.Add((t.To, t.From, t.Amount));   // the sender has paid: the receiver owes him that (or is owed that much less)
        }
        return res;
    }

    /// <summary>Fewest payments that settle everything: the biggest debtor pays the biggest creditor first.</summary>
    public static List<(string Debtor, string Creditor, decimal Amount)> Settle(Dictionary<string, decimal> nets)
    {
        var debtors = nets.Where(kv => kv.Value < -0.5m)
            .Select(kv => (Name: kv.Key, Left: -kv.Value))
            .OrderByDescending(x => x.Left).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var creditors = nets.Where(kv => kv.Value > 0.5m)
            .Select(kv => (Name: kv.Key, Left: kv.Value))
            .OrderByDescending(x => x.Left).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();

        var res = new List<(string Debtor, string Creditor, decimal Amount)>();
        var i = 0;
        var j = 0;
        while (i < debtors.Count && j < creditors.Count)
        {
            var d = debtors[i];
            var c = creditors[j];
            var amount = Math.Min(d.Left, c.Left);
            res.Add((d.Name, c.Name, amount));
            debtors[i] = (d.Name, d.Left - amount);
            creditors[j] = (c.Name, c.Left - amount);
            if (debtors[i].Left <= 0.5m) i++;
            if (creditors[j].Left <= 0.5m) j++;
        }
        return res;
    }

    /// <summary>Balance of "me" with "other" (from Pairs, or Settle): positive = other owes me, negative = I owe other.</summary>
    public static decimal Between(List<(string Debtor, string Creditor, decimal Amount)> settle, string me, string other)
    {
        decimal v = 0;
        foreach (var s in settle)
        {
            if (Same(s.Creditor, me) && Same(s.Debtor, other)) v += s.Amount;
            else if (Same(s.Debtor, me) && Same(s.Creditor, other)) v -= s.Amount;
        }
        return v;
    }

    static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
