namespace HaulingTracker.Tracking;

/// <summary>
/// What the contracts pay and what the stops, runs and loads are worth. The log never says what a contract pays, so the payouts are the ones typed in the
/// rewards table of the session (or the built-in ones). A stop is worth the reward of its mission split over its stops, a drop-off of a from-Ruin load the
/// reward split over its drop-offs.
/// </summary>
public sealed class Earnings
{
    /// <summary>Payout of a 2 stop mission.</summary>
    public const decimal DefaultReward2 = 1268250m;

    /// <summary>Payout of a 3 stop mission.</summary>
    public const decimal DefaultReward3 = 1691000m;

    /// <summary>Payout of a 4 stop mission (Master rank).</summary>
    public const decimal DefaultReward4 = 2113750m;

    // a from-Ruin contract pays the same as a to-Ruin one with as many stops (2, 3 or 4 drop-offs)

    /// <summary>The built-in payout of a contract with this many stops (2, 3 or 4); to and from Ruin pay the same.</summary>
    public static decimal DefaultReward(int stops) => stops == 2 ? DefaultReward2 : stops == 3 ? DefaultReward3 : DefaultReward4;

    /// <summary>The rows of the rewards table, in order: the hauling contracts to Ruin, then the from-Ruin (waste) contracts.</summary>
    public static readonly IReadOnlyList<RewardLine> RewardLines = new RewardLine[]
    {
        new(2, false, i => i.Reward2, (i, v) => i.Reward2 = v),
        new(3, false, i => i.Reward3, (i, v) => i.Reward3 = v),
        new(4, false, i => i.Reward4, (i, v) => i.Reward4 = v),   // Master rank
        new(2, true, i => i.WasteReward2, (i, v) => i.WasteReward2 = v),
        new(3, true, i => i.WasteReward3, (i, v) => i.WasteReward3 = v),
        new(4, true, i => i.WasteReward4, (i, v) => i.WasteReward4 = v),
    };

    readonly TrackerState _s;

    public Earnings(TrackerState state) => _s = state;

    /// <summary>The payout you typed, or the built-in one when nothing is typed (0).</summary>
    public static decimal Or(decimal typed, decimal builtIn) => typed == 0 ? builtIn : typed;

    /// <summary>A payout nobody typed is set to the built-in one (so the rewards table shows it).</summary>
    public static void FillDefaults(SessionInputs inp)
    {
        foreach (var line in RewardLines)
        {
            if (line.Get(inp) == 0) line.Set(inp, DefaultReward(line.Stops));
        }
    }

    /// <summary>What a hauling contract pays: the To Ruin rows of the rewards table, by its number of stops.</summary>
    public static decimal RewardOf(Mission m, SessionInputs inp) =>
        m.Multi == 2 ? Or(inp.Reward2, DefaultReward2)
        : m.Multi == 3 ? Or(inp.Reward3, DefaultReward3)
        : m.Multi == 4 ? Or(inp.Reward4, DefaultReward4) : 0;

    /// <summary>What a from-Ruin contract pays: the From Ruin rows of the rewards table, by its number of drop-offs (nothing typed = the built-in payout).</summary>
    public static decimal WasteRewardOf(Mission m, SessionInputs? inp) =>
        inp == null ? 0
        : m.OutDrops == 2 ? Or(inp.WasteReward2, DefaultReward2)   // the same payouts as the to-Ruin contracts with as many stops
        : m.OutDrops == 3 ? Or(inp.WasteReward3, DefaultReward3)
        : m.OutDrops == 4 ? Or(inp.WasteReward4, DefaultReward4) : 0;

    /// <summary>
    /// Value of a pickup location: every mission pays its reward split over its stops,
    /// so a stop of a 2 stop mission is worth reward / 2 and a stop of a 3 stop mission reward / 3.
    /// </summary>
    public decimal StopValue(LocationRow r)
    {
        var inp = _s.Inputs;
        if (inp == null) return 0;
        return r.Stops2 * Or(inp.Reward2, DefaultReward2) / 2m + r.Stops3 * Or(inp.Reward3, DefaultReward3) / 3m + r.Stops4 * Or(inp.Reward4, DefaultReward4) / 4m;
    }

    /// <summary>The value of a pickup location for its Value cell, or nothing.</summary>
    public string StopValueText(LocationRow r)
    {
        var v = StopValue(r);
        return v == 0 ? "" : v.ToString("N0");
    }

    /// <summary>The value of the batches of a run (reward / stops of each mission), and the part whose mission is completed, and the part that is lost.</summary>
    public static (decimal Total, decimal Confirmed, decimal Failed) RunValues(WorkerTrip t, SessionGroup g, SessionInputs inp)
    {
        decimal total = 0, confirmed = 0, failed = 0;
        foreach (var p in t.Pickups)
        {
            foreach (var id in p.MissionIds)
            {
                var m = g.Missions.FirstOrDefault(x => x.Id == id);
                var dead = false;
                if (m == null)
                {
                    m = g.Failed.FirstOrDefault(x => x.Id == id);
                    dead = true;
                }
                if (m == null || m.Multi == 0) continue;
                var v = RewardOf(m, inp) / m.Multi;
                total += v;
                if (t.Manual || dead || CargoRuns.LostLevelOf(inp, t.Key) == "All") failed += v;   // a failed run lost all its cargo
                else if (m.Completed) confirmed += v;
            }
        }
        return (total, confirmed, failed);
    }

    /// <summary>The value of a run: its pickups (reward / stops of each mission) and the waste it delivered (reward / drop-offs). The Debts tab only counts the pickups.</summary>
    public decimal RunValue(WorkerTrip t) => _s.Current == null || _s.Inputs == null ? 0 : RunValues(t, _s.Current, _s.Inputs).Total + WasteValue(t);

    /// <summary>The value of a run for its Value cell, or nothing.</summary>
    public string RunValueText(WorkerTrip t)
    {
        var v = RunValue(t);
        return v == 0 ? "" : v.ToString("N0");
    }

    /// <summary>What the batches of one pickup are worth: every mission pays its reward split over its stops (the same sum as the value of a run).</summary>
    public decimal PickupValue(TripPickup p)
    {
        if (_s.Current == null || _s.Inputs == null) return 0;
        decimal total = 0;
        foreach (var id in p.MissionIds)
        {
            var m = _s.Current.Missions.FirstOrDefault(x => x.Id == id) ?? _s.Current.Failed.FirstOrDefault(x => x.Id == id);
            if (m == null || m.Multi == 0) continue;
            total += RewardOf(m, _s.Inputs) / m.Multi;
        }
        return total;
    }

    /// <summary>
    /// The value of from-Ruin drop-offs ("mission id|objective id"): every drop-off is worth the reward of its contract split over its drop-offs, like a pickup
    /// is worth reward / stops.
    /// </summary>
    public decimal WasteValue(IEnumerable<string> keys)
    {
        if (_s.Log == null || _s.Inputs == null) return 0;
        decimal total = 0;
        foreach (var key in keys)
        {
            var bar = key.IndexOf('|');
            if (bar <= 0 || !_s.Log.Missions.TryGetValue(key[..bar], out var m) || m.OutDrops <= 0) continue;
            total += WasteRewardOf(m, _s.Inputs) / m.OutDrops;
        }
        return total;
    }

    /// <summary>The waste a run delivered: what it is worth.</summary>
    public decimal WasteValue(WorkerTrip t) => WasteValue(t.Drops.Select(d => d.MissionId + "|" + d.Objective));

    /// <summary>
    /// The from-Ruin contracts of a session, like the hauling missions: one that is still going belongs to the latest session, one that is done to the session it was
    /// completed in (that is when it pays). Abandoned and failed ones are left out. They are not hauling missions, so they are not in the session itself.
    /// </summary>
    public List<Mission> SessionWaste(SessionGroup g)
    {
        var list = new List<Mission>();
        if (_s.Log == null) return list;
        foreach (var m in _s.Log.Missions.Values.Where(x => x.IsOut && !x.Dead && !x.Dropped))
        {
            var owner = m.EndedAt is { } ended
                ? _s.Sessions.Where(s => s.Start <= ended).OrderBy(s => s.Start).LastOrDefault()
                : _s.Sessions.OrderBy(s => s.Start).LastOrDefault();
            if (owner != null && owner.Key == g.Key) list.Add(m);
        }
        return list;
    }

    /// <summary>What the session shown has earned: what its completed missions and its completed from-Ruin contracts really paid.</summary>
    public decimal Earned()
    {
        if (_s.Current == null || _s.Inputs == null) return 0;
        var inp = _s.Inputs;
        return _s.Current.Missions.Where(m => m.Completed).Sum(m => RewardOf(m, inp))
            + SessionWaste(_s.Current).Where(m => m.Completed).Sum(m => WasteRewardOf(m, inp));   // and the waste contracts that are done
    }

    /// <summary>What every contract of a session pays together, the from-Ruin (waste) contracts by what was typed for them.</summary>
    public decimal Revenue(SessionGroup g, SessionInputs inp) =>
        g.Missions.Sum(m => RewardOf(m, inp)) + SessionWaste(g).Sum(m => WasteRewardOf(m, inp));
}

/// <summary>A row of the rewards table: a kind of contract (2, 3 or 4 stops, to or from Ruin) and where its payout is kept in the session.</summary>
public sealed record RewardLine(int Stops, bool FromRuin, Func<SessionInputs, decimal> Get, Action<SessionInputs, decimal> Set)
{
    /// <summary>"2 To Ruin", "4 From Ruin".</summary>
    public string Contract => Stops + (FromRuin ? " From Ruin" : " To Ruin");

    /// <summary>How many contracts of this kind the session has: its accepted missions (to Ruin), or its from-Ruin contracts.</summary>
    public int CountIn(IEnumerable<Mission> accepted, IEnumerable<Mission> waste) =>
        FromRuin ? waste.Count(m => m.OutDrops == Stops) : accepted.Count(m => m.Multi == Stops);
}
