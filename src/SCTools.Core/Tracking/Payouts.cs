using System.Globalization;

namespace SCTools.Tracking;

/// <summary>Which value of the finished runs: all of it, the part whose missions are completed (earned), or the part known to be lost.</summary>
public enum CarryMode { Run, Confirmed, Failed }

/// <summary>
/// What every worker carried and what he received of the mission payouts, kept with every session so the Debts and History tabs work without the logs; the money
/// transfers found in the logs; and your corrections of the balances.
/// </summary>
public sealed class Payouts
{
    readonly TrackerState _s;
    readonly Crew _crew;
    readonly Contracts _contracts;
    readonly Earnings _earnings;
    readonly CargoRuns _runs;
    readonly Places _places;

    public Payouts(TrackerState state, Crew crew, Contracts contracts, Earnings earnings, CargoRuns runs, Places places)
    {
        _s = state;
        _crew = crew;
        _contracts = contracts;
        _earnings = earnings;
        _runs = runs;
        _places = places;
    }

    AppData Data => _s.Data;

    /// <summary>What this worker carried (names in any capitals count together).</summary>
    public static decimal CarriedBy(Dictionary<string, decimal> carried, string name) =>
        carried.Where(kv => SameName(kv.Key, name)).Sum(kv => kv.Value);

    /// <summary>
    /// What every worker carried, from the finished runs: the whole value of the finished runs (the queue included), just the batches whose mission is completed
    /// (this is what gets saved), or the part that is lost.
    /// </summary>
    public Dictionary<string, decimal> ComputeCarried(SessionGroup g, SessionInputs inp, CarryMode mode = CarryMode.Run)
    {
        var all = ComputeCarriedAll(g, inp);
        return mode == CarryMode.Confirmed ? all.Confirmed : mode == CarryMode.Failed ? all.Failed : all.Run;
    }

    /// <summary>
    /// The three of ComputeCarried at once, from one build of the runs (trips: the runs of the session already built, else they are built here): the
    /// leaderboard and the saved sessions want all three, and each build of the runs reads the whole log again.
    /// </summary>
    public (Dictionary<string, decimal> Run, Dictionary<string, decimal> Confirmed, Dictionary<string, decimal> Failed) ComputeCarriedAll(SessionGroup g, SessionInputs inp,
        List<WorkerTrip>? trips = null)
    {
        var run = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var confirmed = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var failed = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        if (_s.Log == null) return (run, confirmed, failed);
        static void Add(Dictionary<string, decimal> d, string worker, decimal value)
        {
            if (value <= 0) return;
            d.TryGetValue(worker, out var old);
            d[worker] = old + value;
        }
        foreach (var t in trips ?? _runs.BuildRuns(g, inp))
        {
            if (!t.End.HasValue || t.Worker == "Unassigned") continue;
            var v = Earnings.RunValues(t, g, inp, _s.Jobs);
            Add(run, t.Worker, v.Total);
            Add(confirmed, t.Worker, v.Confirmed);
            Add(failed, t.Worker, v.Failed);
        }
        return (run, confirmed, failed);
    }

    /// <summary>
    /// What every worker carried (the batches of completed missions in finished runs) and what every worker received:
    /// the payout of each completed mission is split equally between the players who have it when it paid out (ContractSplit.PlannerPayees: who took
    /// it and who accepted the share and still had it; a party member without it gets nothing). The ones looked at are the party members who were active at
    /// the moment it paid out (for a logless member that is what tells), and everybody whose log has it.
    /// Missions that paid out before the party table started to track times (or in a session with a typed Session Party) look at the whole session party.
    /// </summary>
    /// <param name="groups">gets the parties of the session as the payouts went: who carried a mission and who shared its payout are in one</param>
    public (Dictionary<string, decimal> Carried, Dictionary<string, decimal> Received) ComputePayouts(SessionGroup g, SessionInputs inp, List<PayoutRecord>? records = null,
        List<List<string>>? groups = null, List<WorkerTrip>? built = null)
    {
        var carried = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var received = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        if (_s.Log == null) return (carried, received);

        var trips = built ?? _runs.BuildRuns(g, inp);
        var byMission = new Dictionary<string, Dictionary<string, decimal>>();
        foreach (var t in trips)
        {
            if (!t.End.HasValue || t.Worker == "Unassigned" || t.Manual || CargoRuns.LostLevelOf(inp, t.Key) == "All") continue;
            foreach (var p in t.Pickups)
            {
                foreach (var id in p.MissionIds)
                {
                    var m = g.Missions.FirstOrDefault(x => x.Id == id);
                    if (m == null || m.Parcels == 0 || !m.Completed) continue;
                    var v = Earnings.RewardOf(m, inp, _s.Jobs) / m.Parcels;
                    if (v <= 0) continue;
                    carried.TryGetValue(t.Worker, out var old);
                    carried[t.Worker] = old + v;
                    if (!byMission.TryGetValue(id, out var by)) byMission[id] = by = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
                    by.TryGetValue(t.Worker, out var oldBy);
                    by[t.Worker] = oldBy + v;
                }
            }
        }

        // a mission that paid less (or more) than its contract: what was typed in the Mission Payouts list, spread over the batches that were carried
        foreach (var kv in byMission)
        {
            var sum = kv.Value.Values.Sum();
            if (!Data.PayoutOverrides.TryGetValue(kv.Key, out var real) || real <= 0 || sum <= 0) continue;
            var f = real / sum;
            foreach (var w in kv.Value.Keys.ToList())
            {
                var was = kv.Value[w];
                kv.Value[w] = was * f;
                carried[w] += was * (f - 1m);
            }
        }

        var sessionMembers = new List<string>(inp.Party ?? new List<string>());
        foreach (var k in carried.Keys)
        {
            if (!sessionMembers.Any(x => SameName(x, k))) sessionMembers.Add(k);
        }
        var since = _crew.PartyTrackingStart();
        var together = new PartyGroups();
        foreach (var kv in byMission)
        {
            var m = g.Missions.First(x => x.Id == kv.Key);
            var pool = kv.Value.Values.Sum();
            List<string> members;
            if (inp.Party != null || !m.EndedAt.HasValue || m.EndedAt.Value < since)
            {
                members = sessionMembers;
            }
            else
            {
                // who was in the in-game party when it paid out (the party tracker; the Active tick only says who gets routes)
                members = _crew.MoneyPartyAt(m.EndedAt.Value);
                if (members.Count == 0) members = kv.Value.Keys.ToList();   // nobody was in the party: the ones who carried it share it
            }
            // the ones who have the mission when it pays out share it (ContractSplit, Frank's rule of 30 Oct 2026): the party members who have it, and anybody
            // else whose log has it (what the logs say about who took part, or what was ticked on the Contracts tab), not a party member without it;
            // a mission that already has its payout kept keeps its members, so earlier sessions do not change
            var kept = Data.Payouts.FirstOrDefault(x => x.MissionId == m.Id);
            if (kept != null) members = kept.Members.Count > 0 ? new List<string>(kept.Members) : members;
            else
            {
                var ticked = Data.MissionHolders.TryGetValue(m.Id, out var t) ? t.Where(x => x.Value).Select(x => x.Key) : Enumerable.Empty<string>();
                var candidates = members.Concat(m.Holders.Order(StringComparer.OrdinalIgnoreCase)).Concat(ticked);
                members = ContractSplit.PlannerPayees(candidates, n => _contracts.HasMission(m, n, m.EndedAt), m.Taker, kv.Value.Keys);
            }
            together.Join(kv.Value.Keys.Concat(members));
            var share = pool / members.Count;
            if (records != null && m.EndedAt.HasValue)
            {
                records.Add(new PayoutRecord
                {
                    MissionId = m.Id,
                    Time = m.EndedAt.Value,
                    Contract = m.IsHauling ? (string.IsNullOrEmpty(m.Cargo) ? "Cargo" : m.Cargo) + ", " + m.Multi + " stops" : m.Title ?? "A hauling mission",   // a job of another kind by its name (stage 4)
                    Payout = Data.PayoutOverrides.TryGetValue(m.Id, out var typed) && typed > 0 ? typed : Earnings.RewardOf(m, inp, _s.Jobs),
                    Members = new List<string>(members),
                    Each = share,
                });
            }
            foreach (var name in members)
            {
                received.TryGetValue(name, out var old);
                received[name] = old + share;
            }
        }
        if (groups != null)
        {
            groups.Clear();
            groups.AddRange(together.Groups());
        }
        return (carried, received);
    }

    /// <summary>Names that belong together (case does not matter): each Join puts its names in one group, and groups that share a name become one.</summary>
    sealed class PartyGroups
    {
        readonly Dictionary<string, string> _root = new(StringComparer.OrdinalIgnoreCase);
        readonly List<string> _names = new();

        string Find(string x)
        {
            if (!_root.TryGetValue(x, out var r))
            {
                _root[x] = x;
                _names.Add(x);
                return x;
            }
            return string.Equals(r, x, StringComparison.OrdinalIgnoreCase) ? r : _root[x] = Find(r);
        }

        public void Join(IEnumerable<string> names)
        {
            string? first = null;
            foreach (var n in names)
            {
                var r = Find(n);
                if (first == null) first = r;
                else if (!string.Equals(r, first, StringComparison.OrdinalIgnoreCase)) _root[r] = first;
            }
        }

        /// <summary>The groups, each sorted by name, the groups by their first name.</summary>
        public List<List<string>> Groups() =>
            _names.GroupBy(Find, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList())
                .OrderBy(g => g[0], StringComparer.OrdinalIgnoreCase).ToList();
    }

    static bool SameGroups(List<List<string>> a, List<List<string>> b) =>
        a.Count == b.Count && a.Zip(b).All(p => p.First.SequenceEqual(p.Second, StringComparer.OrdinalIgnoreCase));

    static bool SameMap(Dictionary<string, decimal> a, Dictionary<string, decimal> b) =>
        a.Count == b.Count && b.All(kv => a.TryGetValue(kv.Key, out var v) && v == kv.Value);

    /// <summary>What a new session starts with: the rewards typed last (the Mission Library's on the server), or the built-in ones.</summary>
    SessionInputs NewInputs()
    {
        var inputs = new SessionInputs();
        Earnings.FillDefaults(inputs, Data);
        return inputs;
    }

    /// <summary>Keeps what every session earned per worker, and the money transfers, so they survive closing the program. Saves when something changed.</summary>
    public void UpdateSnapshots()
    {
        var log = _s.Log;
        if (log == null) return;
        var changed = false;
        // the payouts by mission (one look-up each, not a walk through every payout ever saved for every mission)
        var saved = new Dictionary<string, PayoutRecord>(StringComparer.Ordinal);
        foreach (var p in Data.Payouts) saved.TryAdd(p.MissionId, p);

        foreach (var g in _s.Sessions)
        {
            Data.Sessions.TryGetValue(g.Key, out var stored);
            if (stored != null && WorkerTripBuilder.ResetWorkersForNewBatch(g, stored)) changed = true;
            // a session never shown (one that started and stopped by itself while a newer one was shown): the rewards a new session starts with
            var basis = stored ?? NewInputs();
            var payouts = new List<PayoutRecord>();
            var groups = new List<List<string>>();
            var built = _runs.BuildRuns(g, basis);   // the runs once for the payouts and what everybody carried (four builds gave the same list)
            var (carried, received) = ComputePayouts(g, basis, payouts, groups, built);
            foreach (var r in payouts)
            {
                var old = saved.GetValueOrDefault(r.MissionId);
                if (old != null && old.Time == r.Time && old.Contract == r.Contract && old.Payout == r.Payout && old.Each == r.Each
                    && old.Members.SequenceEqual(r.Members, StringComparer.OrdinalIgnoreCase)) continue;
                if (old != null) Data.Payouts.Remove(old);
                Data.Payouts.Add(r);
                saved[r.MissionId] = r;
                changed = true;
            }
            var (runValues, _, failedValues) = ComputeCarriedAll(g, basis, built);
            if (carried.Count == 0 && runValues.Count == 0) continue;

            var inp = stored ?? _s.InputsOf(g.Key);
            var ids = g.Missions.Select(m => m.Id).ToList();
            var idSet = new HashSet<string>(ids, StringComparer.Ordinal);

            // a session that grew and got a new key must not be counted twice
            foreach (var kv in Data.Sessions)
            {
                if (kv.Key == g.Key || (kv.Value.Carried.Count == 0 && kv.Value.RunValues.Count == 0)) continue;
                if (kv.Value.MissionIds.Any(idSet.Contains))
                {
                    kv.Value.Carried = new Dictionary<string, decimal>();
                    kv.Value.RunValues = new Dictionary<string, decimal>();
                    kv.Value.FailedValues = new Dictionary<string, decimal>();
                    changed = true;
                }
            }

            var members = new List<string>(inp.Party ?? new List<string>());
            foreach (var k in carried.Keys.Concat(runValues.Keys).Concat(received.Keys))
            {
                if (!members.Any(x => SameName(x, k))) members.Add(k);
            }

            var summary = SessionBuilder.Summarize(g);
            var revenue = _earnings.Revenue(g, inp);   // the from-Ruin (waste) contracts too, by what you typed for them
            if (inp.StartUtc != g.Start || inp.EndUtc != summary.End || inp.MissionCount != g.Missions.Count || inp.Revenue != revenue)
            {
                inp.StartUtc = g.Start;
                inp.EndUtc = summary.End;
                inp.MissionCount = g.Missions.Count;
                inp.Revenue = revenue;
                changed = true;
            }

            var trips = ReferenceEquals(inp, basis) ? built : _runs.BuildRuns(g, inp);   // the same inputs: the same runs
            var stats = Leaderboard.ComputeStats(trips, inp, _places.StationOf);   // a perfect run stays perfect, whatever the server does later

            var same = Leaderboard.SameStats(inp.Stats, stats)
                && SameMap(inp.Received, received)
                && inp.Carried.Count == carried.Count
                && inp.RunValues.Count == runValues.Count
                && inp.FailedValues.Count == failedValues.Count
                && failedValues.All(kv => inp.FailedValues.TryGetValue(kv.Key, out var fv) && fv == kv.Value)
                && runValues.All(kv => inp.RunValues.TryGetValue(kv.Key, out var rv) && rv == kv.Value)
                && carried.All(kv => inp.Carried.TryGetValue(kv.Key, out var v) && v == kv.Value)
                && inp.Members.Count == members.Count
                && members.All(m => inp.Members.Any(x => SameName(x, m)))
                && SameGroups(inp.Groups, groups);
            if (!same || inp.MissionIds.Count != ids.Count)
            {
                inp.Carried = new Dictionary<string, decimal>(carried);
                inp.Received = new Dictionary<string, decimal>(received);
                inp.RunValues = new Dictionary<string, decimal>(runValues);
                inp.FailedValues = new Dictionary<string, decimal>(failedValues);
                inp.Stats = new Dictionary<string, WorkerStat>(stats);
                inp.Members = members;
                inp.Groups = groups;
                inp.MissionIds = ids;
                changed = true;
            }
        }

        foreach (var t in log.Transfers)
        {
            var known = Data.Transfers.Any(x => x.Amount == t.Amount && SameName(x.From, t.From) && SameName(x.To, t.To)
                && Math.Abs((x.Time - t.Time).TotalSeconds) <= 10);
            if (known) continue;
            Data.Transfers.Add(new SavedTransfer { Time = t.Time, From = t.From, To = t.To, Amount = t.Amount });
            changed = true;
        }

        if (changed) _s.Save();
    }

    // ------------------------------------------------------------------ the Debts tab

    /// <summary>Everybody the Debts tab lists: the party, and whoever carried something in a saved session.</summary>
    public List<string> DebtPartyNames()
    {
        var list = new List<string>(Data.Party);
        foreach (var s in Data.Sessions.Values)
        {
            foreach (var k in s.Carried.Keys)
            {
                if (!list.Any(x => SameName(x, k))) list.Add(k);
            }
        }
        return list;
    }

    static string PairKey(string a, string b, out bool aFirst)
    {
        aFirst = string.Compare(a, b, StringComparison.OrdinalIgnoreCase) <= 0;
        var x = aFirst ? a : b;
        var y = aFirst ? b : a;
        return x.ToLowerInvariant() + "|" + y.ToLowerInvariant();
    }

    /// <summary>Your typed correction for the pair: positive = other owes me.</summary>
    public decimal GetAdj(string me, string other)
    {
        var key = PairKey(me, other, out var meFirst);
        Data.PairAdjust.TryGetValue(key, out var v);
        return meFirst ? v : -v;
    }

    /// <summary>Keeps a correction for the pair (positive = other owes me); none when it is (almost) 0.</summary>
    public void SetAdj(string me, string other, decimal balance)
    {
        var key = PairKey(me, other, out var meFirst);
        var v = meFirst ? balance : -balance;
        if (Math.Abs(v) < 0.5m) Data.PairAdjust.Remove(key);
        else Data.PairAdjust[key] = v;
    }

    /// <summary>What a balance means in words: "zzatvo owes zzoega 1,250", or "even".</summary>
    public static string Meaning(string me, string other, decimal v)
    {
        if (Math.Abs(v) < 0.5m) return "even";
        var amount = Math.Abs(v).ToString("N0", CultureInfo.InvariantCulture);
        return v > 0 ? other + " owes " + me + " " + amount : me + " owes " + other + " " + amount;
    }

    /// <summary>What a mission really paid was typed (0 = back to the reward of its contract): the shares and the debts follow it.</summary>
    public void SetPayoutOverride(string missionId, decimal typed)
    {
        if (typed > 0) Data.PayoutOverrides[missionId] = typed;
        else Data.PayoutOverrides.Remove(missionId);
    }
}
