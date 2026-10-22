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
    public double Fill;       // how full the ship is on this trip (0 to 1, with what a run in progress carries already): a trip that is not worth flying yet goes to the back of the queue
    public List<(string Id, int Batches)> Drops = new();   // a from-Ruin contract: this trip leaves Ruin with the load for its FIRST stop (at most one entry) and unloads it there, before the pickups
    public string? DropCargo; // what it is (Waste ...)
    public int DropScu;       // how many SCU the load is (0 = not known)
    public List<int> DropParts = new();
    public List<string> DropKeys = new();   // "mission id|objective id" of every drop-off in it, in the same order   // the SCU of every drop-off in it, one by one (0 = not known)

    public PlannedTrip Copy() => new()
    {
        Worker = Worker, Stops = new List<(string Id, int Batches)>(Stops), Distance = Distance, Seconds = Seconds, Locked = Locked,
        Special = Special, From = From, Room = Room, Fill = Fill, Drops = new List<(string Id, int Batches)>(Drops), DropCargo = DropCargo, DropScu = DropScu, DropParts = new List<int>(DropParts), DropKeys = new List<string>(DropKeys),
    };
}

/// <summary>What is already decided for one worker: his next stop, and the run he is flying.</summary>
public sealed class WorkerStart
{
    public string Worker = "";
    public string? FirstId;     // his next stop is locked to him: his first trip goes there first
    public int FirstBatches;    // the batches he takes there (they are not part of the batches left to divide)
    public int Loaded;          // batches he carries already in a run that is not handed in yet: they take room in his first trip
    public int LoadedScu;       // the same in SCU (an estimate), when the plan fills the ships by SCU
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
    const double SmallShare = 0.25;    // a stop with less than this part of the ship is a small stop ...
    const double BigShare = 0.5;       // ... and one with at least this part a big one
    const double TopUpSeconds = 600;   // a small stop added to a trip that has a big load already counts this much more: it is left for a later trip, where new missions can make it worth the landing
    const double StopSeconds = 240;    // every station a trip lands at costs a landing, the loading and a take-off: that many seconds per stop when comparing plans (not in the times shown), so the plan prefers fewer stops, and full one-stop trips most of all
    const double SyncSeconds = 240;    // unloading cargo from Ruin and loading a pickup at the SAME station saves a landing and a take-off: that many seconds are taken off such a trip when comparing plans (not in the times shown)

    /// <summary>The cheapest way from a start through these stations and back to Ruin (exact for up to 12 stations): the cost in Gm and the order (indexes into ids).</summary>
    internal static (double Cost, int[] Order) Cheapest(string start, IReadOnlyList<string> ids)
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
    /// <param name="limits">batches only some of the crew can take, by station id: the workers who have that mission running and how many batches (the rest of the batches of the station is for everybody)</param>
    /// <param name="drops">cargo that goes out from Ruin (from-Ruin contracts), one load per mission and station: a trip takes at most one load, for its FIRST stop, and unloads it there before it picks anything up.
    /// The pickups come first: the trips are planned for the pickups alone, then the loads ride along on them. Only a worker with nothing to pick up and no run in progress gets a trip just
    /// for a load; a load that no trip can take along is left out (it waits).</param>
    public static List<PlannedTrip> Plan(IReadOnlyDictionary<string, int> remaining, IReadOnlyList<(string Name, int Capacity, QtDrive Drive)> crew, IReadOnlyList<WorkerStart>? starts = null,
        IReadOnlyDictionary<string, List<(string[] Workers, int Batches)>>? limits = null, IReadOnlyList<DropChunk>? drops = null,
        IReadOnlyDictionary<string, int>? batchScu = null, IReadOnlyDictionary<string, int>? shipScu = null)
    {
        // With the SCU of a batch at every station and the SCU of every ship, the ships are filled by SCU (a batch of a 4 stop mission is smaller than one of a
        // 2 stop mission); without them every batch counts as one and a ship holds its number of batches.
        var scuMode = batchScu != null && shipScu != null;
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
        var chunks = (drops ?? Array.Empty<DropChunk>()).Where(c => c.Batches > 0).ToList();
        foreach (var c in chunks) idSet.Add(c.Station);
        foreach (var st in startOf)
        {
            if (st?.FirstId != null && st.FirstBatches > 0) idSet.Add(st.FirstId);
        }
        var ids = idSet.OrderBy(k => k, StringComparer.Ordinal).ToList();
        var n = ids.Count;
        if (n == 0) return trips;

        var need = ids.Select(id => remaining.GetValueOrDefault(id)).ToArray();
        var wt = ids.Select(id => scuMode ? Math.Max(1, batchScu!.GetValueOrDefault(id, 170)) : 1).ToArray();   // what one batch of that station takes in a ship
        int CapOf(int k) => scuMode ? Math.Max(0, shipScu!.GetValueOrDefault(workers[k].Name, workers[k].Capacity * 232)) : workers[k].Capacity;
        int LoadedOf(WorkerStart? st) => st == null ? 0 : scuMode ? st.LoadedScu : st.Loaded;
        var reservedIdx = new int[w];   // the locked stop of the first trip of each worker (-1 = none) and its batches
        var reservedN = new int[w];
        var firstCapacity = new int[w];
        for (var k = 0; k < w; k++)
        {
            var st = startOf[k];
            firstCapacity[k] = Math.Max(0, CapOf(k) - LoadedOf(st));
            reservedIdx[k] = -1;
            if (st?.FirstId == null || st.FirstBatches <= 0) continue;
            var at = ids.IndexOf(st.FirstId);
            reservedIdx[k] = at;
            reservedN[k] = Math.Min(st.FirstBatches, firstCapacity[k] / wt[at]);
            if (reservedN[k] < st.FirstBatches) need[at] += st.FirstBatches - reservedN[k];   // what does not fit in his first trip goes back to everybody
            if (reservedN[k] <= 0) reservedIdx[k] = -1;
        }
        var total = 0;
        for (var i = 0; i < n; i++) total += need[i] * wt[i];
        for (var k = 0; k < w; k++)
        {
            if (reservedIdx[k] >= 0) total += reservedN[k] * wt[reservedIdx[k]];
        }

        // batches only some workers can take: per station the groups (a set of workers, as a bit mask, and the batches), the rest is for everybody
        var allMask = (1 << w) - 1;
        var groups = new List<(int Mask, int Count)>?[n];
        var free = new int[n];
        for (var i = 0; i < n; i++)
        {
            free[i] = need[i];
            if (limits == null || !limits.TryGetValue(ids[i], out var gl)) continue;
            var list = new List<(int Mask, int Count)>();
            var left = need[i];
            foreach (var (names, count) in gl)
            {
                var mask = 0;
                for (var k = 0; k < w; k++)
                {
                    if (names.Contains(workers[k].Name)) mask |= 1 << k;
                }
                var c = Math.Min(count, left);
                if (mask == 0 || mask == allMask || c <= 0) continue;
                list.Add((mask, c));
                left -= c;
            }
            if (list.Count == 0) continue;
            groups[i] = list;
            free[i] = left;
        }

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
        var sumCapacity = Math.Max(1, Enumerable.Range(0, w).Sum(CapOf));
        var perWorker = (total + sumCapacity - 1) / sumCapacity + 2 + chunks.Count;
        var slots = perWorker * w;
        var slotWorker = new int[slots];
        var slotCapacity = new int[slots];
        var special = new bool[slots];
        for (var t = 0; t < slots; t++)
        {
            slotWorker[t] = t % w;
            slotCapacity[t] = CapOf(t % w);
        }
        for (var k = 0; k < w; k++)
        {
            var st = startOf[k];
            if (st == null || (st.From == null && reservedIdx[k] < 0 && LoadedOf(st) <= 0)) continue;
            special[k] = true;
            slotCapacity[k] = firstCapacity[k];
        }

        // the loads from Ruin: where they go (an index into ids), how many batches, and which workers can take them (as a bit mask)
        var chunkStation = chunks.Select(c => ids.IndexOf(c.Station)).ToArray();
        var chunkMask = chunks.Select(c =>
        {
            var mask = 0;
            for (var k = 0; k < w; k++)
            {
                if (c.Allowed.Length == 0 || c.Allowed.Contains(workers[k].Name)) mask |= 1 << k;
            }
            return mask == 0 ? allMask : mask;
        }).ToArray();
        var dropAt = new int[slots];   // the load (index into chunks) a trip slot takes out from Ruin, -1 = none
        Array.Fill(dropAt, -1);
        // a trip slot takes a load when the worker can have it and the ship holds it; a slot that continues a run (or starts elsewhere) cannot, but a locked first stop at the very station of the load can (the load is unloaded there, then the locked batches are taken)
        bool CanDrop(int chunk, int slot) =>
            ((chunkMask[chunk] >> slotWorker[slot]) & 1) != 0 && (scuMode ? chunks[chunk].Scu : chunks[chunk].Batches) <= slotCapacity[slot]
            && (!special[slot] || (startOf[slot]!.From == null && startOf[slot]!.Loaded <= 0 && reservedIdx[slot] == chunkStation[chunk]));

        // is this division of a station over the workers possible with the batches they have (every set of workers can only take what at least one of them can)
        bool Allowed(int[][] assign, int station)
        {
            var gs = groups[station];
            if (gs == null) return true;
            var x = new int[w];
            for (var t = 0; t < slots; t++) x[slotWorker[t]] += assign[t][station];
            for (var k = 0; k < w; k++)
            {
                if (reservedIdx[k] == station) x[k] -= reservedN[k];
            }
            for (var s = 1; s <= allMask; s++)
            {
                var take = 0;
                for (var k = 0; k < w; k++)
                {
                    if (((s >> k) & 1) != 0) take += x[k];
                }
                var supply = free[station];
                foreach (var (mask, count) in gs)
                {
                    if ((mask & s) != 0) supply += count;
                }
                if (take > supply) return false;
            }
            return true;
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

        // a trip with a load from Ruin: Ruin, the station of the load first, the other stops in the cheapest order, back to Ruin
        var dropRoutes = new Dictionary<(int Mask, int Stop), (double Cost, int[] Order)>();
        (double Cost, int[] Order) DropRoute(int mask, int stop)
        {
            if (dropRoutes.TryGetValue((mask, stop), out var known)) return known;
            var rest = new List<int>();
            for (var i = 0; i < n; i++)
            {
                if (((mask >> i) & 1) != 0 && i != stop) rest.Add(i);
            }
            var r = Cheapest(ids[stop], rest.Select(i => ids[i]).ToList());
            var order = new int[rest.Count + 1];
            order[0] = stop;
            for (var k = 0; k < rest.Count; k++) order[k + 1] = rest[r.Order[k]];
            var result = (d0[stop] + r.Cost, order);
            dropRoutes[(mask, stop)] = result;
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
            if (dropAt[slot] >= 0 && !special[slot])
            {
                var viaDrop = DropRoute(mask, chunkStation[dropAt[slot]]).Order;
                var dropDrive = workers[slotWorker[slot]].Drive;
                var dropSeconds = dropDrive.Seconds(d0[viaDrop[0]]);
                for (var i = 1; i < viaDrop.Length; i++) dropSeconds += dropDrive.Seconds(d[viaDrop[i - 1], viaDrop[i]]);
                return dropSeconds + dropDrive.Seconds(d0[viaDrop[^1]]);
            }
            if (mask == 0)
            {
                // a run in progress, or a worker who is at a station (he dropped off a load there), has to fly home to Ruin even when nothing is added to
                // his first trip: that leg is part of his time, so a stop on the way home is compared with the flight home he needs anyway, and not with
                // nothing (else a trip that starts at Ruin looks free to him, as if he were at Ruin already)
                if (special[slot])
                    return workers[slotWorker[slot]].Drive.Seconds(PyroDistances.Between(startOf[slot]!.From ?? PyroDistances.Ruin, PyroDistances.Ruin));
                return 0;
            }
            if (special[slot]) return SpecialSeconds(slot, Special(slot, mask).Order);
            var drive = workers[slotWorker[slot]].Drive;
            var order = Tour(mask).Order;
            var seconds = drive.Seconds(d0[order[0]]);
            for (var i = 1; i < order.Length; i++) seconds += drive.Seconds(d[order[i - 1], order[i]]);
            return seconds + drive.Seconds(d0[order[^1]]);
        }

        // what the search compares: the flight time and the landings (every stop), less the saving of unloading and loading at the same station
        double Cost(int[] row, int slot)
        {
            var drop = dropAt[slot];
            var stops = 0;
            var small = 0;
            var cap = (double)Math.Max(1, CapOf(slotWorker[slot]));
            var loaded = special[slot] ? LoadedOf(startOf[slot]) : 0;   // a run in progress: what is on board already
            var big = loaded >= cap * BigShare;
            for (var i = 0; i < n; i++)
            {
                if (row[i] <= 0) continue;
                stops++;
                var scu = row[i] * wt[i];
                if (scu >= cap * BigShare) big = true;
                else if (scu < cap * SmallShare) small++;
            }
            var topUp = big && stops + (loaded > 0 ? 1 : 0) >= 2 ? small * TopUpSeconds : 0;   // only a small stop next to a big load (or a run that is full already)
            return SlotCost(row, slot) + stops * StopSeconds + topUp - (drop >= 0 && row[chunkStation[drop]] > 0 ? SyncSeconds : 0);
        }

        // the number to beat: the longest worker total, plus a part of the combined time (all in seconds)
        static double Objective(double[] totals) => totals.Max() + TotalWeight * totals.Sum();

        // the batches of a locked stop stay in the first trip of their worker
        bool IsReserved(int[][] assign, int slot, int station) =>
            slot < w && reservedIdx[slot] == station && assign[slot][station] <= reservedN[slot];

        // one improving move: take a batch to another trip with room, or swap two batches between two trips
        bool Improve(int[][] assign, int[] loads, double[] slotCost, double[] totals, ref double current)
        {
            // a load from Ruin to another trip (or swapped with that trip's load)
            for (var a = 0; a < slots; a++)
            {
                var ca = dropAt[a];
                if (ca < 0) continue;
                for (var b = 0; b < slots; b++)
                {
                    if (a == b) continue;
                    var cb = dropAt[b];
                    if (!CanDrop(ca, b) || (cb >= 0 && !CanDrop(cb, a))) continue;
                    dropAt[a] = cb;
                    dropAt[b] = ca;
                    var newA = Cost(assign[a], a);
                    var newB = Cost(assign[b], b);
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
                    dropAt[a] = ca;
                    dropAt[b] = cb;
                }
            }
            for (var a = 0; a < slots; a++)
            {
                for (var i = 0; i < n; i++)
                {
                    if (assign[a][i] == 0 || IsReserved(assign, a, i)) continue;
                    for (var b = 0; b < slots; b++)
                    {
                        if (a == b) continue;
                        if (loads[b] + wt[i] <= slotCapacity[b])
                        {
                            assign[a][i]--;
                            assign[b][i]++;
                            if (Allowed(assign, i))
                            {
                                var newA = Cost(assign[a], a);
                                var newB = Cost(assign[b], b);
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
                                    loads[a] -= wt[i];
                                    loads[b] += wt[i];
                                    return true;
                                }
                                totals[slotWorker[a]] -= dA;
                                totals[slotWorker[b]] -= dB;
                            }
                            assign[a][i]++;
                            assign[b][i]--;
                        }
                        for (var j = 0; j < n; j++)
                        {
                            if (j == i || assign[b][j] == 0 || IsReserved(assign, b, j)) continue;
                            // the two batches can differ in size: neither ship may end up over its room (unless it was already, and does not get fuller)
                            var loadA = loads[a] - wt[i] + wt[j];
                            var loadB = loads[b] - wt[j] + wt[i];
                            if ((loadA > slotCapacity[a] && loadA > loads[a]) || (loadB > slotCapacity[b] && loadB > loads[b])) continue;
                            assign[a][i]--;
                            assign[b][i]++;
                            assign[b][j]--;
                            assign[a][j]++;
                            if (Allowed(assign, i) && Allowed(assign, j))
                            {
                                var newA = Cost(assign[a], a);
                                var newB = Cost(assign[b], b);
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
                                    loads[a] = loadA;
                                    loads[b] = loadB;
                                    return true;
                                }
                                totals[slotWorker[a]] -= dA;
                                totals[slotWorker[b]] -= dB;
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
                loads[k] = reservedN[k] * wt[reservedIdx[k]];
            }
            // batches only some workers can take go first, to a trip of one of them with room (the first ones of a station in this order are the limited ones)
            var isLimited = new bool[units.Count];
            var seen = new int[n];
            for (var u = 0; u < units.Count; u++)
            {
                var i = units[u];
                var gs = groups[i];
                if (gs == null) continue;
                var k = seen[i]++;
                var at = 0;
                foreach (var (mask, count) in gs)
                {
                    if (k < at + count)
                    {
                        isLimited[u] = true;
                        var target = -1;
                        for (var t = 0; t < slots && target < 0; t++)
                        {
                            if (((mask >> slotWorker[t]) & 1) != 0 && loads[t] + wt[i] <= slotCapacity[t]) target = t;
                        }
                        if (target < 0)
                        {
                            for (var t = 0; t < slots && target < 0; t++)
                            {
                                if (((mask >> slotWorker[t]) & 1) != 0) target = t;
                            }
                        }
                        assign[target][i]++;
                        loads[target] += wt[i];
                        break;
                    }
                    at += count;
                }
            }
            var slot = 0;
            for (var u = 0; u < units.Count; u++)
            {
                if (isLimited[u]) continue;
                var i = units[u];
                while (slot < slots - 1 && loads[slot] + wt[i] > slotCapacity[slot]) slot++;
                assign[slot][i]++;
                loads[slot] += wt[i];
            }

            Array.Fill(dropAt, -1);   // the loads from Ruin are not part of this search: they are put on the trips afterwards (below)

            var slotCost = assign.Select((row, t) => Cost(row, t)).ToArray();
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

        // The loads from Ruin ride along on the trips planned for the pickups (those stay as they are: the pickups are the priority). A trip takes at most one load,
        // only when it picks up at the station of the load anyway (that station becomes its first stop), so a load never adds a stop. Only a worker with nothing
        // at all to pick up and no run in progress may get a trip just for a load (the last resort). A load that no trip can take along waits.
        var picks = bestPlan!;
        var picksUp = new bool[w];
        for (var t = 0; t < slots; t++)
        {
            if (picks[t].Any(x => x > 0)) picksUp[slotWorker[t]] = true;
        }
        // a load never adds a stop (the landings are what costs the time): it only rides along on a trip that picks up at its station anyway. Only a worker with
        // nothing at all to pick up and no run in progress may fly a trip just for a load (the last resort)
        bool Fits(int chunk, int slot) =>
            CanDrop(chunk, slot) && (picks[slot][chunkStation[chunk]] > 0
                || (!picksUp[slotWorker[slot]] && (startOf[slotWorker[slot]]?.Loaded ?? 0) <= 0 && !picks[slot].Any(x => x > 0)));
        var tripCost = picks.Select((row, t) => Cost(row, t)).ToArray();
        var workerTotals = new double[w];
        for (var t = 0; t < slots; t++) workerTotals[slotWorker[t]] += tripCost[t];
        var objective = Objective(workerTotals);
        var placed = new bool[chunks.Count];

        // the value of the plan when this slot takes this load (-1 = none) instead of the one it has
        (double Value, double SlotCost) With(int slot, int chunk)
        {
            var had = dropAt[slot];
            dropAt[slot] = chunk;
            var cost = Cost(picks[slot], slot);
            dropAt[slot] = had;
            workerTotals[slotWorker[slot]] += cost - tripCost[slot];
            var value = Objective(workerTotals);
            workerTotals[slotWorker[slot]] -= cost - tripCost[slot];
            return (value, cost);
        }
        void Set(int slot, int chunk, double cost)
        {
            if (dropAt[slot] >= 0) placed[dropAt[slot]] = false;
            if (chunk >= 0) placed[chunk] = true;
            dropAt[slot] = chunk;
            workerTotals[slotWorker[slot]] += cost - tripCost[slot];
            tripCost[slot] = cost;
            objective = Objective(workerTotals);
        }

        // every load that can go somewhere is put on a trip, the cheapest one first
        void PlaceAll()
        {
            while (true)
            {
                var pick = (Chunk: -1, Slot: -1, Value: double.MaxValue, Cost: 0.0);
                for (var c = 0; c < chunks.Count; c++)
                {
                    if (placed[c]) continue;
                    for (var t = 0; t < slots; t++)
                    {
                        if (dropAt[t] >= 0 || !Fits(c, t)) continue;
                        var (value, cost) = With(t, c);
                        if (value < pick.Value - 1e-9) pick = (c, t, value, cost);
                    }
                }
                if (pick.Chunk < 0) return;
                Set(pick.Slot, pick.Chunk, pick.Cost);
            }
        }
        // then better places for them: another trip (or swapped with its load), or a load that waits instead of this one when that is cheaper
        var better = true;
        while (better)
        {
            better = false;
            PlaceAll();
            for (var a = 0; a < slots && !better; a++)
            {
                var ca = dropAt[a];
                if (ca < 0) continue;
                for (var b = 0; b < slots && !better; b++)
                {
                    var cb = dropAt[b];
                    if (a == b || !Fits(ca, b) || (cb >= 0 && !Fits(cb, a))) continue;
                    var oldA = tripCost[a];
                    var oldB = tripCost[b];
                    dropAt[a] = cb;
                    dropAt[b] = ca;
                    var newA = Cost(picks[a], a);
                    var newB = Cost(picks[b], b);
                    workerTotals[slotWorker[a]] += newA - oldA;
                    workerTotals[slotWorker[b]] += newB - oldB;
                    var value = Objective(workerTotals);
                    if (value < objective - 1e-9)
                    {
                        tripCost[a] = newA;
                        tripCost[b] = newB;
                        objective = value;
                        better = true;
                        break;
                    }
                    workerTotals[slotWorker[a]] -= newA - oldA;
                    workerTotals[slotWorker[b]] -= newB - oldB;
                    dropAt[a] = ca;
                    dropAt[b] = cb;
                }
                for (var c = 0; c < chunks.Count && !better; c++)
                {
                    if (placed[c] || !Fits(c, a)) continue;
                    var (value, cost) = With(a, c);
                    if (value >= objective - 1e-9) continue;
                    Set(a, c, cost);
                    better = true;
                }
            }
        }

        for (var t = 0; t < slots; t++)
        {
            var row = picks[t];
            var mask = MaskOf(row);
            if (mask == 0 && dropAt[t] < 0) continue;
            var carried = special[t] ? LoadedOf(startOf[t]) : 0;
            for (var i = 0; i < n; i++) carried += row[i] * wt[i];
            var trip = new PlannedTrip { Worker = workers[slotWorker[t]].Name, Room = slotCapacity[t], Fill = carried / (double)Math.Max(1, CapOf(slotWorker[t])) };
            if (special[t])
            {
                var route = Special(t, mask);
                trip.Distance = route.Cost;
                trip.Seconds = SpecialSeconds(t, route.Order);
                trip.Special = true;
                trip.From = startOf[t]!.From;
                trip.Locked = reservedIdx[t] >= 0 && route.Order.Count > 0 && route.Order[0] == reservedIdx[t];
                foreach (var idx in route.Order) trip.Stops.Add((ids[idx], row[idx]));
                if (dropAt[t] >= 0)
                {
                    trip.Drops.Add((ids[chunkStation[dropAt[t]]], chunks[dropAt[t]].Batches));
                    trip.DropCargo = chunks[dropAt[t]].Cargo;
                    trip.DropScu = chunks[dropAt[t]].Scu;
                    trip.DropParts = new List<int>(chunks[dropAt[t]].Parts);
                    trip.DropKeys = new List<string>(chunks[dropAt[t]].Keys);
                }
            }
            else if (dropAt[t] >= 0)
            {
                var chunk = dropAt[t];
                var route = DropRoute(mask, chunkStation[chunk]);
                trip.Distance = route.Cost;
                trip.Seconds = SlotCost(row, t);
                trip.Drops.Add((ids[chunkStation[chunk]], chunks[chunk].Batches));
                trip.DropCargo = chunks[chunk].Cargo;
                trip.DropScu = chunks[chunk].Scu;
                trip.DropParts = new List<int>(chunks[chunk].Parts);
                trip.DropKeys = new List<string>(chunks[chunk].Keys);
                foreach (var idx in route.Order)
                {
                    if (row[idx] > 0) trip.Stops.Add((ids[idx], row[idx]));
                }
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
            .ThenBy(tp => tp.Fill >= BigShare || tp.Stops.Count == 0 ? 0 : 1)   // then the trips that fill at least half the ship; the ones with little in them wait at the back, where new missions can fill them up
            .ThenBy(tp => tp.Stops.Count)   // then the one-stop trips (before everything else, also before a trip with waste that has more stops): while he flies those, missions get done and new ones come in, and the trips with more stops are planned again with them
            .ThenBy(tp => tp.Drops.Count > 0 ? 0 : 1)   // with as many stops, the trip that takes cargo out from Ruin first, so he is rid of it (and can take a new from-Ruin contract) sooner
            .ThenByDescending(tp => tp.Batches)
            .ThenBy(tp => tp.Distance)
            .ToList();
    }
}
