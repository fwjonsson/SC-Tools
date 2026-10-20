namespace HaulingTracker;

/// <summary>One suggested trip of one worker: the stops in flying order, with the batches taken at each.</summary>
public sealed class PlannedTrip
{
    public string Worker = "";
    public List<(string Id, int Batches)> Stops = new();
    public int Batches => Stops.Sum(s => s.Batches);
    public double Distance;   // Gm: the start (Ruin, or where a run in progress is), the stops in order, back to Ruin
    public double Seconds;    // estimated flight time with this worker's quantum drive (the time of every jump, see QtDrive.Seconds)
    public bool Locked;       // the first stop is locked to this worker: the planner only plans the rest of the trip around it
    public bool Special;      // his first trip: it continues a run in progress and/or starts at his locked stop
    public string? From;      // a run in progress: the trip continues from this station (null = it starts at Ruin)
    public int Room;          // how many batches this trip can hold (the ship, minus what a run in progress carries already)
}

/// <summary>What is already decided for one worker: his next stop, and the run he is flying.</summary>
public sealed class WorkerStart
{
    public string Worker = "";
    public string? FirstId;     // his next stop is locked to him: his first trip goes there first
    public int FirstBatches;    // the batches he takes there (they are not part of the batches left to divide)
    public int Loaded;          // batches he carries already in a run that is not handed in yet: they take room in his first trip
    public string? From;        // where that run is now: his first trip continues from there instead of from Ruin
}

/// <summary>
/// Divides the open batches over the whole crew. Everyone has a ship that holds a number of batches; every trip starts and ends
/// at Ruin Station (a run in progress continues from where it is). A location can be split over several trips and several workers
/// (take what fits, fill the last seats with a small pickup that is close by). The plan aims for the crew to finish at about the same time
/// (the longest worker total is the main number) while flying as little as possible in total.
/// </summary>
public static class TripPlanner
{
    const int Restarts = 30;
    const double TotalWeight = 0.25;   // how much the combined Gm counts next to the longest worker total

    /// <summary>The cheapest way from a start through these stations and back to Ruin (exact for up to 12 stations): the cost in Gm and the order (indexes into ids).</summary>
    static (double Cost, int[] Order) Cheapest(string start, IReadOnlyList<string> ids)
    {
        var k = ids.Count;
        if (k == 0) return (PyroDistances.Between(start, PyroDistances.Ruin), Array.Empty<int>());
        var d0 = ids.Select(id => PyroDistances.Between(start, id)).ToArray();
        var home = ids.Select(id => PyroDistances.Between(id, PyroDistances.Ruin)).ToArray();
        var d = new double[k, k];
        for (var i = 0; i < k; i++)
        {
            for (var j = 0; j < k; j++) d[i, j] = PyroDistances.Between(ids[i], ids[j]);
        }
        if (k > 12)
        {
            var identity = Enumerable.Range(0, k).ToArray();
            var sum = d0[0] + home[k - 1];
            for (var i = 1; i < k; i++) sum += d[i - 1, i];
            return (sum, identity);
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
            var cost = dp[full, j] + home[j];
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
        return (best, order);
    }

    /// <param name="remaining">station id (see PyroDistances) to the batches still to pick up there (without the batches of the locked stops)</param>
    /// <param name="crew">every worker with the batches his ship holds (0 = not flying) and his quantum drive</param>
    /// <param name="starts">what is decided already: a worker's locked next stop (his first trip goes there first, the rest of the trip is planned) and a run he is flying (his first trip continues it)</param>
    public static List<PlannedTrip> Plan(IReadOnlyDictionary<string, int> remaining, IReadOnlyList<(string Name, int Capacity, QtDrive Drive)> crew, IReadOnlyList<WorkerStart>? starts = null)
    {
        var workers = crew.Where(c => c.Capacity > 0).ToList();
        var w = workers.Count;
        var trips = new List<PlannedTrip>();
        if (w == 0) return trips;

        var startOf = new WorkerStart?[w];
        foreach (var st in starts ?? new List<WorkerStart>())
        {
            var idx = workers.FindIndex(c => c.Name == st.Worker);
            if (idx >= 0) startOf[idx] = st;
        }

        var idSet = remaining.Where(kv => kv.Value > 0).Select(kv => kv.Key).ToHashSet();
        foreach (var st in startOf)
        {
            if (st?.FirstId != null && st.FirstBatches > 0) idSet.Add(st.FirstId);
        }
        var ids = idSet.OrderBy(k => k, StringComparer.Ordinal).ToList();
        var n = ids.Count;
        if (n == 0) return trips;

        var need = ids.Select(id => remaining.GetValueOrDefault(id)).ToArray();
        var reservedIdx = new int[w];   // the locked stop of the first trip of each worker (-1 = none) and its batches
        var reservedN = new int[w];
        var firstCapacity = new int[w];
        for (var k = 0; k < w; k++)
        {
            var st = startOf[k];
            firstCapacity[k] = Math.Max(0, workers[k].Capacity - (st?.Loaded ?? 0));
            reservedIdx[k] = -1;
            if (st?.FirstId == null || st.FirstBatches <= 0) continue;
            reservedIdx[k] = ids.IndexOf(st.FirstId);
            reservedN[k] = Math.Min(st.FirstBatches, firstCapacity[k]);
            if (reservedN[k] <= 0) reservedIdx[k] = -1;
        }
        var total = need.Sum() + reservedN.Sum();

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
            var result = Cheapest(PyroDistances.Ruin, m.Select(i => ids[i]).ToList());
            var mapped = (result.Cost, result.Order.Select(o => m[o]).ToArray());
            tours[mask] = mapped;
            return mapped;
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

        // Every worker gets a few trip slots (an unused slot costs nothing). A slot has the ship size of its worker.
        // The first slot of a worker (slot k) can be special: it continues a run in progress (less room, starts where he is)
        // and/or starts at his locked stop.
        var sumCapacity = Math.Max(1, workers.Sum(c => c.Capacity));
        var perWorker = (total + sumCapacity - 1) / sumCapacity + 2;
        var slots = perWorker * w;
        var slotWorker = new int[slots];
        var slotCapacity = new int[slots];
        var special = new bool[slots];
        for (var t = 0; t < slots; t++)
        {
            slotWorker[t] = t % w;
            slotCapacity[t] = workers[t % w].Capacity;
        }
        for (var k = 0; k < w; k++)
        {
            var st = startOf[k];
            if (st == null || (st.From == null && reservedIdx[k] < 0 && st.Loaded <= 0)) continue;
            special[k] = true;
            slotCapacity[k] = firstCapacity[k];
        }

        // the route of a special slot: from where he is, to his locked stop, through the rest in the cheapest order, back to Ruin
        var specials = new Dictionary<(int Slot, int Mask), (double Cost, List<int> Order)>();
        (double Cost, List<int> Order) Special(int slot, int mask)
        {
            if (specials.TryGetValue((slot, mask), out var known)) return known;
            var st = startOf[slot]!;
            var origin = st.From ?? PyroDistances.Ruin;
            var first = reservedIdx[slot];
            var stations = new List<int>();
            for (var i = 0; i < n; i++)
            {
                if (((mask >> i) & 1) != 0) stations.Add(i);
            }
            var forced = first >= 0 && stations.Contains(first);
            var rest = forced ? stations.Where(i => i != first).ToList() : stations;
            var from = forced ? ids[first] : origin;
            var (cost, order) = Cheapest(from, rest.Select(i => ids[i]).ToList());
            if (forced) cost += PyroDistances.Between(origin, ids[first]);
            var list = new List<int>();
            if (forced) list.Add(first);
            list.AddRange(order.Select(o => rest[o]));
            var result = (cost, list);
            specials[(slot, mask)] = result;
            return result;
        }

        double SpecialSeconds(int slot, List<int> order)
        {
            var drive = workers[slotWorker[slot]].Drive;
            var from = startOf[slot]!.From ?? PyroDistances.Ruin;
            double seconds = 0;
            foreach (var i in order)
            {
                seconds += drive.Seconds(PyroDistances.Between(from, ids[i]));
                from = ids[i];
            }
            return seconds + drive.Seconds(PyroDistances.Between(from, PyroDistances.Ruin));
        }

        // the flight time of a trip for one worker: his drive flies every jump (the start, the stops, back to Ruin)
        double SlotCost(int[] row, int slot)
        {
            var mask = MaskOf(row);
            if (mask == 0) return 0;
            if (special[slot]) return SpecialSeconds(slot, Special(slot, mask).Order);
            var drive = workers[slotWorker[slot]].Drive;
            var order = Tour(mask).Order;
            var seconds = drive.Seconds(d0[order[0]]);
            for (var i = 1; i < order.Length; i++) seconds += drive.Seconds(d[order[i - 1], order[i]]);
            return seconds + drive.Seconds(d0[order[^1]]);
        }

        // the number to beat: the longest worker total, plus a part of the combined time (all in seconds)
        static double Objective(double[] totals) => totals.Max() + TotalWeight * totals.Sum();

        // the batches of a locked stop stay in the first trip of their worker
        bool IsReserved(int[][] assign, int slot, int station) =>
            slot < w && reservedIdx[slot] == station && assign[slot][station] <= reservedN[slot];

        // one improving move: take a batch to another trip with room, or swap two batches between two trips
        bool Improve(int[][] assign, int[] loads, double[] slotCost, double[] totals, ref double current)
        {
            for (var a = 0; a < slots; a++)
            {
                for (var i = 0; i < n; i++)
                {
                    if (assign[a][i] == 0 || IsReserved(assign, a, i)) continue;
                    for (var b = 0; b < slots; b++)
                    {
                        if (a == b) continue;
                        if (loads[b] < slotCapacity[b])
                        {
                            assign[a][i]--;
                            assign[b][i]++;
                            var newA = SlotCost(assign[a], a);
                            var newB = SlotCost(assign[b], b);
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
                            if (j == i || assign[b][j] == 0 || IsReserved(assign, b, j)) continue;
                            assign[a][i]--;
                            assign[b][i]++;
                            assign[b][j]--;
                            assign[a][j]++;
                            var newA = SlotCost(assign[a], a);
                            var newB = SlotCost(assign[b], b);
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
            for (var k = 0; k < w; k++)
            {
                if (reservedIdx[k] < 0) continue;
                assign[k][reservedIdx[k]] = reservedN[k];
                loads[k] = reservedN[k];
            }
            var slot = 0;
            foreach (var unit in units)
            {
                while (slot < slots - 1 && loads[slot] >= slotCapacity[slot]) slot++;
                assign[slot][unit]++;
                loads[slot]++;
            }

            var slotCost = assign.Select((row, t) => SlotCost(row, t)).ToArray();
            var totals = new double[w];
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
            var trip = new PlannedTrip { Worker = workers[slotWorker[t]].Name, Room = slotCapacity[t] };
            if (special[t])
            {
                var route = Special(t, mask);
                trip.Distance = route.Cost;
                trip.Seconds = SpecialSeconds(t, route.Order);
                trip.Special = true;
                trip.From = startOf[t]!.From;
                trip.Locked = reservedIdx[t] >= 0 && route.Order.Count > 0 && route.Order[0] == reservedIdx[t];
                foreach (var idx in route.Order) trip.Stops.Add((ids[idx], row[idx]));
            }
            else
            {
                var tour = Tour(mask);
                trip.Distance = tour.Cost;
                trip.Seconds = SlotCost(row, t);
                foreach (var idx in tour.Order) trip.Stops.Add((ids[idx], row[idx]));
            }
            trips.Add(trip);
        }

        var order2 = workers.Select(c => c.Name).ToList();
        return trips
            .OrderBy(tp => order2.IndexOf(tp.Worker))
            .ThenBy(tp => tp.Special ? 0 : 1)   // his first trip: the one he is flying or heading for
            .ThenByDescending(tp => tp.Batches)
            .ThenBy(tp => tp.Distance)
            .ToList();
    }
}
