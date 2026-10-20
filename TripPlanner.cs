namespace HaulingTracker;

/// <summary>One suggested trip of one worker: the stops in flying order, with the batches taken at each.</summary>
public sealed class PlannedTrip
{
    public string Worker = "";
    public List<(string Id, int Batches)> Stops = new();
    public int Batches => Stops.Sum(s => s.Batches);
    public double Distance;   // Gm: Ruin, the stops in order, back to Ruin
    public double Seconds;    // estimated flight time with this worker's quantum drive (the time of every jump, see QtDrive.Seconds)
    public bool Locked;       // his Current trip, kept as it is: the planner only plans the rest around it
}

/// <summary>
/// Divides the open batches over the whole crew. Everyone has a ship that holds a number of batches; every trip starts and ends
/// at Ruin Station. A location can be split over several trips and several workers (take what fits, fill the last seats with
/// a small pickup that is close by). The plan aims for the crew to finish at about the same time (the longest worker total is
/// the main number) while flying as little as possible in total.
/// </summary>
public static class TripPlanner
{
    const int Restarts = 30;
    const double TotalWeight = 0.25;   // how much the combined Gm counts next to the longest worker total

    /// <summary>The cheapest order (Ruin, these stations, back to Ruin) for a few stations: the indexes into ids, in flying order.</summary>
    static int[] BestOrder(IReadOnlyList<string> ids)
    {
        var k = ids.Count;
        var identity = Enumerable.Range(0, k).ToArray();
        if (k <= 2 || k > 12) return identity;
        var d0 = ids.Select(id => PyroDistances.Between(PyroDistances.Ruin, id)).ToArray();
        var d = new double[k, k];
        for (var i = 0; i < k; i++)
        {
            for (var j = 0; j < k; j++) d[i, j] = PyroDistances.Between(ids[i], ids[j]);
        }
        var full = (1 << k) - 1;
        var dp = new double[full + 1, k];
        var parent = new int[full + 1, k];
        for (var s = 0; s <= full; s++)
        {
            for (var j = 0; j < k; j++)
            {
                dp[s, j] = double.MaxValue;
                parent[s, j] = -1;
            }
        }
        for (var j = 0; j < k; j++) dp[1 << j, j] = d0[j];
        for (var s = 1; s <= full; s++)
        {
            for (var j = 0; j < k; j++)
            {
                if (((s >> j) & 1) == 0 || dp[s, j] == double.MaxValue) continue;
                for (var nx = 0; nx < k; nx++)
                {
                    if (((s >> nx) & 1) != 0) continue;
                    var s2 = s | (1 << nx);
                    var cost = dp[s, j] + d[j, nx];
                    if (cost < dp[s2, nx])
                    {
                        dp[s2, nx] = cost;
                        parent[s2, nx] = j;
                    }
                }
            }
        }
        var best = double.MaxValue;
        var last = 0;
        for (var j = 0; j < k; j++)
        {
            var cost = dp[full, j] + d0[j];
            if (cost < best)
            {
                best = cost;
                last = j;
            }
        }
        var order = new int[k];
        var cur = last;
        var set = full;
        for (var pos = k - 1; pos >= 0; pos--)
        {
            order[pos] = cur;
            var prev = parent[set, cur];
            set &= ~(1 << cur);
            cur = prev;
        }
        return order;
    }

    /// <summary>A trip that is decided already (locked to its worker): the stops in the cheapest order, with the distance and the flight time of his drive.</summary>
    public static PlannedTrip Fixed(string worker, IReadOnlyList<(string Id, int Batches)> stops, QtDrive drive)
    {
        var trip = new PlannedTrip { Worker = worker, Locked = true };
        foreach (var i in BestOrder(stops.Select(s => s.Id).ToList())) trip.Stops.Add(stops[i]);
        var from = PyroDistances.Ruin;
        foreach (var s in trip.Stops)
        {
            var gm = PyroDistances.Between(from, s.Id);
            trip.Distance += gm;
            trip.Seconds += drive.Seconds(gm);
            from = s.Id;
        }
        var home = PyroDistances.Between(from, PyroDistances.Ruin);
        trip.Distance += home;
        trip.Seconds += drive.Seconds(home);
        return trip;
    }

    /// <param name="remaining">station id (see PyroDistances) to the batches still to pick up there (without the locked trips)</param>
    /// <param name="crew">every worker with the batches his ship holds (0 = not flying) and his quantum drive</param>
    /// <param name="locked">trips that stay as they are (see Fixed). They come first for their worker and count in his total time.</param>
    public static List<PlannedTrip> Plan(IReadOnlyDictionary<string, int> remaining, IReadOnlyList<(string Name, int Capacity, QtDrive Drive)> crew, IReadOnlyList<PlannedTrip>? locked = null)
    {
        var workers = crew.Where(c => c.Capacity > 0).ToList();
        var fixedTrips = (locked ?? new List<PlannedTrip>()).Where(t => workers.Any(c => c.Name == t.Worker)).ToList();
        var ids = remaining.Where(kv => kv.Value > 0).Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var n = ids.Count;
        var trips = new List<PlannedTrip>();
        if (workers.Count == 0) return trips;
        if (n == 0) return fixedTrips;
        var lockedSeconds = new double[workers.Count];
        foreach (var t in fixedTrips) lockedSeconds[workers.FindIndex(c => c.Name == t.Worker)] += t.Seconds;

        var need = ids.Select(id => remaining[id]).ToArray();
        var total = need.Sum();

        var d0 = new double[n];
        var d = new double[n, n];
        for (var i = 0; i < n; i++)
        {
            d0[i] = PyroDistances.Between(PyroDistances.Ruin, ids[i]);
            for (var j = 0; j < n; j++) d[i, j] = PyroDistances.Between(ids[i], ids[j]);
        }

        // The shortest round trip from Ruin through a set of locations (exact, the sets are small), remembered per set.
        var tours = new Dictionary<int, (double Cost, int[] Order)>();
        (double Cost, int[] Order) Tour(int mask)
        {
            if (tours.TryGetValue(mask, out var known)) return known;
            var m = new List<int>();
            for (var i = 0; i < n; i++)
            {
                if (((mask >> i) & 1) != 0) m.Add(i);
            }
            var k = m.Count;
            var full = (1 << k) - 1;
            var dp = new double[1 << k, k];
            var parent = new int[1 << k, k];
            for (var s = 0; s <= full; s++)
            {
                for (var j = 0; j < k; j++)
                {
                    dp[s, j] = double.MaxValue;
                    parent[s, j] = -1;
                }
            }
            for (var j = 0; j < k; j++) dp[1 << j, j] = d0[m[j]];
            for (var s = 1; s <= full; s++)
            {
                for (var j = 0; j < k; j++)
                {
                    if (((s >> j) & 1) == 0 || dp[s, j] == double.MaxValue) continue;
                    for (var nx = 0; nx < k; nx++)
                    {
                        if (((s >> nx) & 1) != 0) continue;
                        var s2 = s | (1 << nx);
                        var cost = dp[s, j] + d[m[j], m[nx]];
                        if (cost < dp[s2, nx])
                        {
                            dp[s2, nx] = cost;
                            parent[s2, nx] = j;
                        }
                    }
                }
            }
            var best = double.MaxValue;
            var last = 0;
            for (var j = 0; j < k; j++)
            {
                var cost = dp[full, j] + d0[m[j]];
                if (cost < best)
                {
                    best = cost;
                    last = j;
                }
            }
            var order = new int[k];
            var cur = last;
            var set = full;
            for (var pos = k - 1; pos >= 0; pos--)
            {
                order[pos] = m[cur];
                var prev = parent[set, cur];
                set &= ~(1 << cur);
                cur = prev;
            }
            var result = (best, order);
            tours[mask] = result;
            return result;
        }

        int MaskOf(int[] row)
        {
            var mask = 0;
            for (var i = 0; i < n; i++)
            {
                if (row[i] > 0) mask |= 1 << i;
            }
            return mask;
        }

        // the flight time of a trip for one worker: his drive flies every jump (Ruin, the stops, back to Ruin)
        double SlotCost(int[] row, int worker)
        {
            var mask = MaskOf(row);
            if (mask == 0) return 0;
            var drive = workers[worker].Drive;
            var order = Tour(mask).Order;
            var seconds = drive.Seconds(d0[order[0]]);
            for (var i = 1; i < order.Length; i++) seconds += drive.Seconds(d[order[i - 1], order[i]]);
            return seconds + drive.Seconds(d0[order[^1]]);
        }

        // the number to beat: the longest worker total, plus a part of the combined time (all in seconds)
        static double Objective(double[] totals) => totals.Max() + TotalWeight * totals.Sum();

        // Every worker gets a few trip slots (an unused slot costs nothing). A slot has the ship size of its worker.
        var w = workers.Count;
        var sumCapacity = workers.Sum(c => c.Capacity);
        var perWorker = (total + sumCapacity - 1) / sumCapacity + 1;
        var slots = perWorker * w;
        var slotWorker = new int[slots];
        var slotCapacity = new int[slots];
        for (var t = 0; t < slots; t++)
        {
            slotWorker[t] = t % w;
            slotCapacity[t] = workers[t % w].Capacity;
        }

        // one improving move: take a batch to another trip with room, or swap two batches between two trips
        bool Improve(int[][] assign, int[] loads, double[] slotCost, double[] totals, ref double current)
        {
            for (var a = 0; a < slots; a++)
            {
                for (var i = 0; i < n; i++)
                {
                    if (assign[a][i] == 0) continue;
                    for (var b = 0; b < slots; b++)
                    {
                        if (a == b) continue;
                        if (loads[b] < slotCapacity[b])
                        {
                            assign[a][i]--;
                            assign[b][i]++;
                            var newA = SlotCost(assign[a], slotWorker[a]);
                            var newB = SlotCost(assign[b], slotWorker[b]);
                            var dA = newA - slotCost[a];
                            var dB = newB - slotCost[b];
                            totals[slotWorker[a]] += dA;
                            totals[slotWorker[b]] += dB;
                            var value = Objective(totals);
                            if (value < current - 1e-9)
                            {
                                current = value;
                                slotCost[a] = newA;
                                slotCost[b] = newB;
                                loads[a]--;
                                loads[b]++;
                                return true;
                            }
                            totals[slotWorker[a]] -= dA;
                            totals[slotWorker[b]] -= dB;
                            assign[a][i]++;
                            assign[b][i]--;
                        }
                        for (var j = 0; j < n; j++)
                        {
                            if (j == i || assign[b][j] == 0) continue;
                            assign[a][i]--;
                            assign[b][i]++;
                            assign[b][j]--;
                            assign[a][j]++;
                            var newA = SlotCost(assign[a], slotWorker[a]);
                            var newB = SlotCost(assign[b], slotWorker[b]);
                            var dA = newA - slotCost[a];
                            var dB = newB - slotCost[b];
                            totals[slotWorker[a]] += dA;
                            totals[slotWorker[b]] += dB;
                            var value = Objective(totals);
                            if (value < current - 1e-9)
                            {
                                current = value;
                                slotCost[a] = newA;
                                slotCost[b] = newB;
                                return true;
                            }
                            totals[slotWorker[a]] -= dA;
                            totals[slotWorker[b]] -= dB;
                            assign[a][i]++;
                            assign[b][i]--;
                            assign[b][j]++;
                            assign[a][j]--;
                        }
                    }
                }
            }
            return false;
        }

        // random starts, each improved until no move helps; the best one wins. The seed is fixed so the plan does not jump around.
        var rng = new Random(20261020);
        var bestValue = double.MaxValue;
        int[][]? bestPlan = null;
        for (var restart = 0; restart < Restarts; restart++)
        {
            var units = new List<int>();
            for (var i = 0; i < n; i++)
            {
                for (var u = 0; u < need[i]; u++) units.Add(i);
            }
            for (var k = units.Count - 1; k > 0; k--)
            {
                var r = rng.Next(k + 1);
                (units[k], units[r]) = (units[r], units[k]);
            }

            var assign = new int[slots][];
            for (var t = 0; t < slots; t++) assign[t] = new int[n];
            var loads = new int[slots];
            var slot = 0;
            foreach (var unit in units)
            {
                while (loads[slot] >= slotCapacity[slot]) slot++;
                assign[slot][unit]++;
                loads[slot]++;
            }

            var slotCost = assign.Select((row, t) => SlotCost(row, slotWorker[t])).ToArray();
            var totals = (double[])lockedSeconds.Clone();   // the locked trips are flown too
            for (var t = 0; t < slots; t++) totals[slotWorker[t]] += slotCost[t];
            var current = Objective(totals);
            while (Improve(assign, loads, slotCost, totals, ref current)) { }
            if (current < bestValue - 1e-9)
            {
                bestValue = current;
                bestPlan = assign.Select(row => (int[])row.Clone()).ToArray();
            }
        }

        for (var t = 0; t < slots; t++)
        {
            var row = bestPlan![t];
            var mask = MaskOf(row);
            if (mask == 0) continue;
            var tour = Tour(mask);
            var trip = new PlannedTrip { Worker = workers[slotWorker[t]].Name, Distance = tour.Cost, Seconds = SlotCost(row, slotWorker[t]) };
            foreach (var idx in tour.Order) trip.Stops.Add((ids[idx], row[idx]));
            trips.Add(trip);
        }

        trips.AddRange(fixedTrips);
        var order2 = workers.Select(c => c.Name).ToList();
        return trips
            .OrderBy(tp => order2.IndexOf(tp.Worker))
            .ThenBy(tp => tp.Locked ? 0 : 1)   // the locked trip is his Current
            .ThenByDescending(tp => tp.Batches)
            .ThenBy(tp => tp.Distance)
            .ToList();
    }
}
