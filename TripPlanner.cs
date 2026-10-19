namespace HaulingTracker;

/// <summary>One suggested trip: the stops in flying order, with the batches taken at each.</summary>
public sealed class PlannedTrip
{
    public List<(string Id, int Batches)> Stops = new();
    public int Batches => Stops.Sum(s => s.Batches);
    public double Distance;   // Gm: Ruin, the stops in order, back to Ruin
}

/// <summary>
/// Plans how to pick up every open batch with a ship that holds a limited number of batches, flying as little as possible.
/// A location can be split over several trips (take what fits, come back for the rest, or fill the last seats with a small
/// pickup nearby). Every trip starts and ends at Ruin Station.
/// </summary>
public static class TripPlanner
{
    const int Restarts = 40;

    /// <summary>The Gm needed if every location was flown to on its own (full trips only, the rest in one more trip).</summary>
    public static double SeparateTrips(IReadOnlyDictionary<string, int> remaining, int capacity)
    {
        double sum = 0;
        foreach (var kv in remaining)
        {
            if (kv.Value <= 0 || capacity < 1) continue;
            var flights = (kv.Value + capacity - 1) / capacity;
            sum += flights * 2 * PyroDistances.Between(PyroDistances.Ruin, kv.Key);
        }
        return sum;
    }

    /// <param name="remaining">station id (see PyroDistances) to the batches still to pick up there</param>
    /// <param name="capacity">how many batches the ship holds</param>
    public static List<PlannedTrip> Plan(IReadOnlyDictionary<string, int> remaining, int capacity)
    {
        var ids = remaining.Where(kv => kv.Value > 0).Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var n = ids.Count;
        var trips = new List<PlannedTrip>();
        if (n == 0 || capacity < 1) return trips;

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

        // the Gm of a whole plan: assign[trip][location] = batches of that location in that trip
        double PlanCost(int[][] assign)
        {
            double sum = 0;
            foreach (var row in assign)
            {
                var mask = 0;
                for (var i = 0; i < n; i++)
                {
                    if (row[i] > 0) mask |= 1 << i;
                }
                if (mask != 0) sum += Tour(mask).Cost;
            }
            return sum;
        }

        // one improving move: take a batch to another trip with room, or swap two batches between trips
        bool Improve(int[][] assign, int[] loads, ref double current)
        {
            var tripCount = assign.Length;
            for (var a = 0; a < tripCount; a++)
            {
                for (var i = 0; i < n; i++)
                {
                    if (assign[a][i] == 0) continue;
                    for (var b = 0; b < tripCount; b++)
                    {
                        if (a == b) continue;
                        if (loads[b] < capacity)
                        {
                            assign[a][i]--;
                            assign[b][i]++;
                            var moved = PlanCost(assign);
                            if (moved < current - 1e-9)
                            {
                                current = moved;
                                loads[a]--;
                                loads[b]++;
                                return true;
                            }
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
                            var swapped = PlanCost(assign);
                            if (swapped < current - 1e-9)
                            {
                                current = swapped;
                                return true;
                            }
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
        var bestCost = double.MaxValue;
        int[][]? bestPlan = null;
        var minTrips = (total + capacity - 1) / capacity;
        for (var tripCount = minTrips; tripCount <= minTrips + 1; tripCount++)
        {
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

                var assign = new int[tripCount][];
                for (var a = 0; a < tripCount; a++) assign[a] = new int[n];
                var loads = new int[tripCount];
                foreach (var unit in units)
                {
                    var a = 0;
                    while (loads[a] >= capacity) a++;
                    assign[a][unit]++;
                    loads[a]++;
                }

                var current = PlanCost(assign);
                while (Improve(assign, loads, ref current)) { }
                if (current < bestCost - 1e-9)
                {
                    bestCost = current;
                    bestPlan = assign.Select(row => (int[])row.Clone()).ToArray();
                }
            }
        }

        foreach (var row in bestPlan!)
        {
            var mask = 0;
            for (var i = 0; i < n; i++)
            {
                if (row[i] > 0) mask |= 1 << i;
            }
            if (mask == 0) continue;
            var tour = Tour(mask);
            var trip = new PlannedTrip { Distance = tour.Cost };
            foreach (var idx in tour.Order) trip.Stops.Add((ids[idx], row[idx]));
            trips.Add(trip);
        }
        return trips.OrderByDescending(t => t.Batches).ThenBy(t => t.Distance).ToList();
    }
}
