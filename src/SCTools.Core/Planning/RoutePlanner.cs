namespace SCTools.Planning;

/// <summary>
/// Divides the open batches over the whole crew. Everyone has a ship that holds a number of batches (or so many SCU); a batch is cargo waiting at a station
/// for a place: Ruin for the contracts to Ruin, another station for a contract to that station (stage 1b of docs/planner-any-hauling.md). A trip starts where
/// the worker is (a run in progress continues from where it is; a later trip starts where the one before it ended), picks up at its stations and ends at the
/// last place it delivers at: for the contracts to Ruin that is Ruin, so those plans are what they always were. A location can be split over several trips
/// and several workers (take what fits, fill the last seats with a small pickup that is close by). The plan aims for the crew to finish at about the same
/// time (the longest worker total is the main number) while flying as little as possible in total.
/// This is the planner without a hub, the Route Planner's one planner since the evening of 30 Oct 2026 (it was tried out beside the old TripPlanner, which
/// planned every trip from Ruin and back, for a day; docs/planner-any-hauling.md).
/// A place is a planner id (StarRoutes: a Pyro station's id, or a place key of the map), and every leg is StarRoutes' way between two places: within a system a
/// jump, between systems the jumps to and from the gateways and the crossing (stage 2, interstellar legs), so the hauling jobs of the Other hauling board whose
/// places the map knows are planned with the Pyro contracts.
/// </summary>
public static class RoutePlanner
{
    const int Restarts = 30;
    const double TotalWeight = 0.25;   // how much the combined Gm counts next to the longest worker total
    const double SmallShare = 0.25;    // a stop with less than this part of the ship is a small stop ...
    const double FullShare = 0.75;     // ... and the ship is nearly full when the rest of the trip fills this part of it
    const double BigShare = 0.5;       // a trip that fills at least this part of the ship is worth flying now (the queue order)
    const double TopUpSeconds = 600;   // a small stop added to a ship that is nearly full already counts this much more: it is left for a later trip, where new missions can make it worth the landing (a half full ship does fill up on the way home)
    // What a plan costs is mostly its LANDINGS, not its flying: set the destination, fly in, ask for a hangar, land, leave the seat, load, back in the seat,
    // ask to take off. That is the work. Quantum travel is close to free (with two accounts the other one is played meanwhile), so it only counts for a part.
    const double StopSeconds = 1200;   // every station a trip lands at: that many seconds when comparing plans (not in the times shown), so every landing has to be worth it (about 20 min each: Frank, 30 Oct 2026, more than the 15 measured)
    const double TripSeconds = StopSeconds;   // every place a trip delivers at is a landing too (the hand-in at Ruin: a run of 38 min is 9 flying, 3-4 loading waste at the elevator, the rest two landings of about 13-15 min), so two half empty trips cost a landing more than one full one
    const double FlightWeight = 0.1;   // the part of the flying time that counts when comparing plans (the times shown are the real ones): a landing is worth over three hours of flying, so the plan goes a long way for a fuller stop (Frank, 30 Oct 2026; a quarter before)
    const double SyncSeconds = StopSeconds;   // delivering cargo from Ruin where the trip picks up anyway: one landing does both, so that is a landing saved
    // The trip he is flying now is the one thing of the plan that is sure to happen: the later trips are planned again as missions get done and new ones come in.
    // So its room is used now, where it fills up, and it never lands for a leftover: the smaller part of a station that another trip lands at anyway saves no landing
    // at all (that one would take it), it only costs his. The leftover waits for a later trip, where new missions can make it worth the landing.
    const double FillNowSeconds = StopSeconds;    // what the trip he is flying picks up (not leftovers), as a part of his ship: a full ship of pickups counts as a landing saved
    const double LeftoverSeconds = 2 * StopSeconds;  // the trip he is flying takes the smaller part of a station that is landed at anyway: that costs as much as a trip of its own for it later (a landing and the hand-in), so the leftover waits
    // The stops kept for a worker after the one he is flying to are soft (Frank, 30 Oct 2026): they stay unless letting one go saves a landing for the same
    // cargo or fills the ship better (a new mission makes a stop of his two batches, or a stop elsewhere; then the stop with one batch goes back to everybody).
    // A kept stop that leaves his trip costs this much, a third of a landing, so a change needs a real gain and the plan never flips on a tie or because
    // somebody else moved; and every batch his planned trip loses without a replacement costs a landing (StopSeconds), so his trip is never emptied to save
    // its landings: the kept stops with one batch each stay when nothing better is about.
    const double KeepSeconds = StopSeconds / 3;
    const double LaterLoneShare = 0.5;   // a landing for a lone batch in a later trip counts this much of one: new missions may fill that station before it is flown (the trip he is flying counts in full)
    const double LoneBatchSeconds = StopSeconds;   // the trip he is flying lands at a station for its one and only batch: a landing more than it is worth now, so it waits for a later trip, where new missions can fill the station up (Frank, 30 Oct 2026: the biggest stops first, a lone batch last)
    const double WasteWaitSeconds = StopSeconds;   // picking up at a station with waste waiting for it, on a trip that cannot take the waste along: the waste then needs a landing of its own later (or waits for ever), so that pickup is left for a trip from Ruin that brings the waste

    /// <summary>
    /// The key of a group of batches: the station they wait at, and where they go. The batches for Ruin have the station alone as their key (as every table and
    /// saved lock has it), the batches for another station "station>destination" (both PyroDistances ids).
    /// </summary>
    public static string GroupKey(string station, string destination) => destination == PyroDistances.Ruin ? station : station + ">" + destination;

    /// <summary>The station of a group key (where its batches are picked up).</summary>
    public static string OriginOf(string key)
    {
        var i = key.IndexOf('>');
        return i < 0 ? key : key[..i];
    }

    /// <summary>Where the batches of a group key go: Ruin, or the station after the ">".</summary>
    public static string DestinationOf(string key)
    {
        var i = key.IndexOf('>');
        return i < 0 ? PyroDistances.Ruin : key[(i + 1)..];
    }

    /// <summary>
    /// The cheapest way from a start through all of these places (in any order) and then through all of the ends (in any order, the trip ends at the last; with
    /// none, at the last of the places). Exact for up to 12 places and ends together (a subset search), else the order given. The order has the indexes into
    /// mids first and the indexes into ends (offset by mids.Count) after them.
    /// </summary>
    internal static (double Cost, int[] Order) Path(string start, IReadOnlyList<string> mids, IReadOnlyList<string> ends)
    {
        var m = mids.Count;
        var e = ends.Count;
        var all = mids.Concat(ends).ToList();
        var k = all.Count;
        if (k == 0) return (0, Array.Empty<int>());
        if (k > 12)
        {
            var identity = Enumerable.Range(0, k).ToArray();
            var sum = StarRoutes.Gm(start, all[0]);
            for (var i = 1; i < k; i++) sum += StarRoutes.Gm(all[i - 1], all[i]);
            return (sum, identity);
        }
        var d0 = all.Select(id => StarRoutes.Gm(start, id)).ToArray();
        var d = new double[k, k];
        for (var i = 0; i < k; i++)
        {
            for (var j = 0; j < k; j++) d[i, j] = StarRoutes.Gm(all[i], all[j]);
        }
        // a state is the set visited and the last place; an end may only come once every place of mids is visited
        var midMask = (1 << m) - 1;
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
        for (var j = 0; j < k; j++)
        {
            if (j < m || m == 0) dp[1 << j, j] = d0[j];
        }
        for (var s = 1; s <= full; s++)
        {
            for (var j = 0; j < k; j++)
            {
                if (((s >> j) & 1) == 0 || dp[s, j] == double.MaxValue) continue;
                var midsDone = (s & midMask) == midMask;
                for (var nx = 0; nx < k; nx++)
                {
                    if (((s >> nx) & 1) != 0) continue;
                    if (nx >= m && !midsDone) continue;   // the ends come after every place
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
            if (e > 0 && j < m) continue;   // with ends, the trip ends at one of them
            if (dp[full, j] < best)
            {
                best = dp[full, j];
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

    /// <summary>The cheapest way from a start through these stations and back to Ruin (exact for up to 12 stations): the cost in Gm and the order (indexes into ids). The planner before stage 1b; kept for the tests.</summary>
    internal static (double Cost, int[] Order) Cheapest(string start, IReadOnlyList<string> ids)
    {
        var (cost, order) = Path(start, ids, new[] { PyroDistances.Ruin });
        return (cost, order.Where(o => o < ids.Count).ToArray());
    }

    /// <summary>The Gm of a route: every leg between the places in order (the same place twice in a row is no leg).</summary>
    public static double PathGm(IEnumerable<string> places)
    {
        string? last = null;
        double total = 0;
        foreach (var id in places)
        {
            if (last != null && last != id) total += StarRoutes.Gm(last, id);
            last = id;
        }
        return total;
    }

    /// <summary>The flight time of a route with this drive: every leg between the places in order.</summary>
    public static double PathSeconds(QtDrive drive, IEnumerable<string> places)
    {
        string? last = null;
        double total = 0;
        foreach (var id in places)
        {
            if (last != null && last != id) total += StarRoutes.Seconds(drive, last, id);   // the crossings' time included
            last = id;
        }
        return total;
    }

    /// <param name="remaining">group key (see GroupKey: a station id for batches to Ruin, "station>destination" for batches to another station) to the batches still to pick up there (without the batches of the locked stops)</param>
    /// <param name="crew">every worker with the batches his ship holds (0 = not flying) and his quantum drive</param>
    /// <param name="starts">what is decided already: a worker's locked next stop (his first trip goes there first, the rest of the trip is planned), a run he is flying (his first trip continues it and ends where its cargo goes) and where he is (his first trip starts there)</param>
    /// <param name="limits">batches only some of the crew can take, by group key: the workers who have that mission running and how many batches (the rest of the batches of the group is for everybody)</param>
    /// <param name="drops">cargo that goes out from Ruin (from-Ruin contracts), one load per mission and station: a trip takes at most one load, for its FIRST stop, and unloads it there before it picks anything up.
    /// The pickups come first: the trips are planned for the pickups alone, then the loads ride along on them. A load only some workers can take, at a station none of them was planned to
    /// pick up at, takes those pickups over to a free trip of one of them. Only a worker with nothing to pick up and no run in progress gets a trip just for a load; a load that no trip can
    /// take along is left out (it waits).</param>
    /// <param name="shipFullScu">what every ship holds in full (shipScu is the plan's 90 %): a load is unloaded at the first stop before anything is picked up, so a load the ship holds in full
    /// rides along (a direct contract of 650 SCU in a C2; the game loaded it already)</param>
    public static List<PlannedTrip> Plan(IReadOnlyDictionary<string, int> remaining, IReadOnlyList<(string Name, int Capacity, QtDrive Drive)> crew, IReadOnlyList<WorkerStart>? starts = null,
        IReadOnlyDictionary<string, List<(string[] Workers, int Batches)>>? limits = null, IReadOnlyList<DropChunk>? drops = null,
        IReadOnlyDictionary<string, int>? batchScu = null, IReadOnlyDictionary<string, int>? shipScu = null, IReadOnlyDictionary<string, int>? shipFullScu = null)
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
        foreach (var c in chunks) idSet.Add(c.Station);   // a load's station is a group of its own (to Ruin) when nothing is picked up there: its index is where the load goes
        foreach (var st in startOf)
        {
            if (st?.FirstId != null && st.FirstBatches > 0) idSet.Add(st.FirstId);
            foreach (var k in st?.Keep ?? new List<(string Id, int Batches)>())
            {
                if (k.Batches > 0) idSet.Add(k.Id);
            }
        }
        var ids = idSet.OrderBy(k => k, StringComparer.Ordinal).ToList();
        var n = ids.Count;
        if (n == 0) return trips;
        var origin = ids.Select(OriginOf).ToArray();   // where every group is picked up
        var dest = ids.Select(DestinationOf).ToArray();   // where every group goes

        var need = ids.Select(id => remaining.GetValueOrDefault(id)).ToArray();
        var wt = ids.Select(id => scuMode ? Math.Max(1, batchScu!.GetValueOrDefault(id, 170)) : 1).ToArray();   // what one batch of that group takes in a ship
        int CapOf(int k) => scuMode ? Math.Max(0, shipScu!.GetValueOrDefault(workers[k].Name, workers[k].Capacity * 232)) : workers[k].Capacity;
        int LoadedOf(WorkerStart? st) => st == null ? 0 : scuMode ? st.LoadedScu : st.Loaded;
        var reservedIdx = new int[w];   // the locked stop of the first trip of each worker (-1 = none) and its batches
        var reservedN = new int[w];
        var prefer = new Dictionary<int, int>[w];   // the other stops of his planned trip (Frank, 30 Oct 2026): preferred in his first trip, not fixed (KeepSeconds): group index -> batches
        var firstCapacity = new int[w];
        for (var k = 0; k < w; k++)
        {
            var st = startOf[k];
            prefer[k] = new Dictionary<int, int>();
            firstCapacity[k] = Math.Max(0, CapOf(k) - LoadedOf(st));
            reservedIdx[k] = -1;
            if (st?.FirstId == null || st.FirstBatches <= 0) continue;
            var at = ids.IndexOf(st.FirstId);
            reservedIdx[k] = at;
            reservedN[k] = Math.Min(st.FirstBatches, firstCapacity[k] / wt[at]);
            if (reservedN[k] < st.FirstBatches) need[at] += st.FirstBatches - reservedN[k];   // what does not fit in his first trip goes back to everybody
            if (reservedN[k] <= 0) reservedIdx[k] = -1;
            // the stops after it: their batches are open to everybody again, with his first trip preferring them (the start of the search puts them there, and
            // a stop that leaves it costs KeepSeconds)
            foreach (var (id, batches) in st.Keep)
            {
                if (batches <= 0) continue;
                var idx = ids.IndexOf(id);
                if (idx < 0) continue;
                need[idx] += batches;
                if (idx != at) prefer[k][idx] = prefer[k].GetValueOrDefault(idx) + batches;
            }
        }
        var total = 0;
        for (var i = 0; i < n; i++) total += need[i] * wt[i];
        for (var k = 0; k < w; k++)
        {
            if (reservedIdx[k] >= 0) total += reservedN[k] * wt[reservedIdx[k]];
        }
        var atGroup = (int[])need.Clone();   // every batch of a group, the locked ones too
        for (var k = 0; k < w; k++)
        {
            if (reservedIdx[k] >= 0) atGroup[reservedIdx[k]] += reservedN[k];
        }
        // every batch waiting at a station, whichever place it goes to: the lone-batch and leftover rules look at the station (one landing serves every group there)
        var atStation = new int[n];
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < n; j++)
            {
                if (origin[j] == origin[i]) atStation[i] += atGroup[j];
            }
        }

        // batches only some workers can take: per group the groups (a set of workers, as a bit mask, and the batches), the rest is for everybody
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
        string WorkerStartPlace(int k) => startOf[k]?.From ?? PyroDistances.Ruin;

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
        var chunksAt = new List<int>?[n];   // the loads from Ruin that go to each group's station
        for (var c = 0; c < chunks.Count; c++)
        {
            if (chunkStation[c] < 0) continue;
            for (var i = 0; i < n; i++)
            {
                if (origin[i] == origin[chunkStation[c]]) (chunksAt[i] ??= new List<int>()).Add(c);
            }
        }
        // a load is unloaded at the first stop before the pickups: a trip from Ruin takes one the ship holds in full (a direct contract fills most of a C2)
        var fullCap = workers.Select(c => shipFullScu != null && shipFullScu.TryGetValue(c.Name, out var f) ? f : 0).ToArray();
        bool CanDrop(int chunk, int slot) =>
            ((chunkMask[chunk] >> slotWorker[slot]) & 1) != 0
            && (scuMode ? chunks[chunk].Scu <= Math.Max(slotCapacity[slot], special[slot] ? 0 : fullCap[slotWorker[slot]]) : chunks[chunk].Batches <= slotCapacity[slot])
            && (!special[slot] || (startOf[slot]!.From == null && startOf[slot]!.Loaded <= 0 && reservedIdx[slot] == chunkStation[chunk]));

        // is this division of a group over the workers possible with the batches they have (every set of workers can only take what at least one of them can)
        bool Allowed(int[][] assign, int group)
        {
            var gs = groups[group];
            if (gs == null) return true;
            var x = new int[w];
            for (var t = 0; t < slots; t++) x[slotWorker[t]] += assign[t][group];
            for (var k = 0; k < w; k++)
            {
                if (reservedIdx[k] == group) x[k] -= reservedN[k];
            }
            for (var s = 1; s <= allMask; s++)
            {
                var take = 0;
                for (var k = 0; k < w; k++)
                {
                    if (((s >> k) & 1) != 0) take += x[k];
                }
                var supply = free[group];
                foreach (var (mask, count) in gs)
                {
                    if ((mask & s) != 0) supply += count;
                }
                if (take > supply) return false;
            }
            return true;
        }

        // The route of a trip slot: from its start (where the worker is for his first slot, else where his slot before it ends), its forced first stop (his
        // locked stop, or the station its load from Ruin goes to), the other stations of its groups in the cheapest order, then the places the batches go
        // (Ruin, the other stations) in the cheapest order; it ends at the last of those. The cargo a run in progress carries already goes to its own places.
        // Remembered per start, forced stop and set of groups.
        var routes = new Dictionary<(string Start, int Forced, int Mask), (double Gm, List<string> Places, List<string> Ends)>();
        (double Gm, List<string> Places, List<string> Ends) RouteOf(string start, int forced, int mask, IEnumerable<string>? aboardTo)
        {
            var aboard = aboardTo?.ToList() ?? new List<string>();
            var key = (start + "|" + string.Join(",", aboard), forced, mask);
            if (routes.TryGetValue(key, out var known)) return known;
            var stations = new List<string>();   // the pickup stations, the forced one first
            var ends = new List<string>(aboard);
            string? first = forced >= 0 ? origin[forced] : null;
            for (var i = 0; i < n; i++)
            {
                if (((mask >> i) & 1) == 0) continue;
                if (!stations.Contains(origin[i]) && origin[i] != first) stations.Add(origin[i]);
                if (!ends.Contains(dest[i])) ends.Add(dest[i]);
            }
            var places = new List<string> { start };
            var from = start;
            double gm = 0;
            if (first != null)
            {
                gm += StarRoutes.Gm(from, first);
                places.Add(first);
                from = first;
            }
            var (cost, order) = Path(from, stations, ends);
            gm += cost;
            var endsInOrder = new List<string>();
            foreach (var o in order)
            {
                var id = o < stations.Count ? stations[o] : ends[o - stations.Count];
                if (o >= stations.Count) endsInOrder.Add(id);
                places.Add(id);
            }
            var result = (gm, places, endsInOrder);
            routes[key] = result;
            return result;
        }

        // where a slot starts: his first slot where he is; a later slot where the slot before it with anything in it ends (its last place)
        string SlotStart(int[][] assign, int slot)
        {
            var k = slotWorker[slot];
            for (var t = slot - w; t >= 0; t -= w)
            {
                var route = SlotRoute(assign, t);
                if (route != null) return route.Value.Places[^1];
            }
            return WorkerStartPlace(k);
        }
        (double Gm, List<string> Places, List<string> Ends)? SlotRoute(int[][] assign, int slot)
        {
            var mask = MaskOf(assign[slot]);
            var drop = dropAt[slot];
            var aboard = special[slot] ? startOf[slot]!.Destinations : null;
            if (mask == 0 && drop < 0 && (aboard == null || !aboard.Any())) return null;   // nothing in it: the worker passes it by
            var start = SlotStart(assign, slot);
            var forced = special[slot] && reservedIdx[slot] >= 0 && ((mask >> reservedIdx[slot]) & 1) != 0 ? reservedIdx[slot] : drop >= 0 ? chunkStation[drop] : -1;
            if (mask == 0 && drop >= 0 && !special[slot]) return (StarRoutes.Gm(start, chunks[drop].Station), new List<string> { start, chunks[drop].Station }, new List<string>());   // a trip just for a load: it ends where the load goes
            return RouteOf(start, forced, mask, aboard);
        }

        // the flight time of a trip for one worker: his drive flies every jump of its route
        double SlotCost(int[][] assign, int slot)
        {
            var route = SlotRoute(assign, slot);
            return route == null ? 0 : PathSeconds(workers[slotWorker[slot]].Drive, route.Value.Places);
        }

        // what the search compares: the flight time and the landings (every stop, and every place delivered at), less the saving of unloading and loading at the same station
        double Cost(int[][] assign, int slot)
        {
            var row = assign[slot];
            var drop = dropAt[slot];
            var cap = (double)Math.Max(1, CapOf(slotWorker[slot]));
            double carried = special[slot] ? LoadedOf(startOf[slot]) : 0;   // a run in progress: what is on board already
            var stations = new HashSet<string>();
            for (var i = 0; i < n; i++)
            {
                if (row[i] <= 0) continue;
                stations.Add(origin[i]);
                carried += row[i] * wt[i];
            }
            var stops = stations.Count;
            // the landings: every station in full on the trip he is flying; in a later trip a station with one batch waiting counts LaterLoneShare of one
            double landings = 0;
            foreach (var st in stations)
            {
                var i = Array.IndexOf(origin, st);
                landings += !special[slot] && atStation[i] == 1 ? StopSeconds * LaterLoneShare : StopSeconds;
            }
            // a small stop on a ship that the rest of the trip fills to 75% or more is a top-up that is not worth the landing
            double topUp = 0;
            for (var i = 0; i < n; i++)
            {
                if (row[i] <= 0) continue;
                var scu = row[i] * wt[i];
                if (scu < cap * SmallShare && carried - scu >= cap * FullShare) topUp += TopUpSeconds;
            }
            // a pickup at a station that has waste waiting, on a trip that could never take that waste along (a run in progress, a worker without that
            // mission, a ship too small): the landing there is wasted for the waste. A trip takes one load at most, so a second station with waste in the same
            // trip strands that waste just the same
            double wasteWait = 0;
            var withWaste = new HashSet<string>();
            foreach (var st in stations)
            {
                var i = Array.IndexOf(origin, st);
                if (chunksAt[i] == null) continue;
                if (!chunksAt[i]!.Any(c => CanDrop(c, slot))) wasteWait += WasteWaitSeconds;
                else withWaste.Add(st);
            }
            if (withWaste.Count > 1) wasteWait += (withWaste.Count - 1) * WasteWaitSeconds;
            // every place the trip delivers at is a landing (the hand-in at Ruin): the cargo a run in progress carries already lands anyway, so only the places
            // its new stops add count for a special slot; a trip just for a load lands where the load goes
            double hand = 0;
            if (stops > 0)
            {
                var aboard = special[slot] ? startOf[slot]!.Destinations.ToHashSet() : new HashSet<string>();
                var ends = new HashSet<string>();
                for (var i = 0; i < n; i++)
                {
                    if (row[i] > 0 && !aboard.Contains(dest[i])) ends.Add(dest[i]);
                }
                hand = ends.Count * TripSeconds;
            }
            else if (drop >= 0 && !special[slot]) hand = TripSeconds;
            // the trip he is flying now: what it picks up counts (fill up now), a leftover it lands for is a wasted landing (see FillNowSeconds)
            double now = 0;
            if (special[slot])
            {
                double picked = 0;
                for (var i = 0; i < n; i++)
                {
                    if (row[i] <= 0) continue;
                    if (row[i] * 2 < atGroup[i]) now += LeftoverSeconds;   // the bigger part of that group is for another trip, which lands there anyway
                    else picked += row[i] * wt[i];
                    if (atStation[i] == 1 && !(slot < w && prefer[slot].ContainsKey(i))) now += LoneBatchSeconds;   // one batch there: the biggest stops first, this one later (not a stop kept for him: it was planned)
                }
                now -= FillNowSeconds * Math.Min(1.0, picked / cap);
            }
            var sync = drop >= 0 && stations.Contains(origin[chunkStation[drop]]) ? SyncSeconds : 0;
            // the stops kept for him that his first trip let go, and the cargo his planned trip lost without a replacement (see KeepSeconds)
            double keep = 0;
            if (slot < w && prefer[slot].Count > 0)
            {
                var planned = (reservedIdx[slot] >= 0 ? reservedN[slot] : 0) + prefer[slot].Values.Sum();
                var carriedNow = 0;
                for (var i = 0; i < n; i++) carriedNow += row[i];
                keep = StopSeconds * Math.Max(0, planned - carriedNow);
                foreach (var kv in prefer[slot])
                {
                    if (row[kv.Key] == 0) keep += KeepSeconds;
                }
            }
            return SlotCost(assign, slot) * FlightWeight + landings + hand + topUp + wasteWait + now + keep - sync;
        }

        // the number to beat: the longest worker total, plus a part of the combined time (all in seconds)
        static double Objective(double[] totals) => totals.Max() + TotalWeight * totals.Sum();

        // a worker's trips cost together (each starting where the one before it ends): his slots in order
        double WorkerTotal(int[][] assign, int k)
        {
            double sum = 0;
            for (var t = k; t < slots; t += w) sum += Cost(assign, t);
            return sum;
        }

        // the batches of a locked stop (the one he is flying to) stay in the first trip of their worker; the stops kept after it are only preferred (KeepSeconds)
        bool IsReserved(int[][] assign, int slot, int group) => slot < w && reservedIdx[slot] == group && assign[slot][group] <= reservedN[slot];
        bool Fixed(int slot, int group) => slot < w && reservedIdx[slot] == group;   // a stop that stays in his first trip

        // A move was made in the trips of workers a and b (the assignment, the loads): keep it when the plan got better (the totals of those workers are brought up
        // to date), else say so (the caller takes the move back)
        bool Keep(int[][] assign, double[] totals, ref double current, int wa, int wb)
        {
            var oldA = totals[wa];
            var oldB = totals[wb];
            totals[wa] = WorkerTotal(assign, wa);
            if (wb != wa) totals[wb] = WorkerTotal(assign, wb);
            var value = Objective(totals);
            if (value < current - 1e-9)
            {
                current = value;
                return true;
            }
            totals[wa] = oldA;
            totals[wb] = oldB;
            return false;
        }

        // one improving move: take a batch to another trip with room, or swap two batches between two trips
        bool Improve(int[][] assign, int[] loads, double[] totals, ref double current)
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
                    if (Keep(assign, totals, ref current, slotWorker[a], slotWorker[b])) return true;
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
                            if (Allowed(assign, i) && Keep(assign, totals, ref current, slotWorker[a], slotWorker[b]))
                            {
                                loads[a] -= wt[i];
                                loads[b] += wt[i];
                                return true;
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
                            if (Allowed(assign, i) && Allowed(assign, j) && Keep(assign, totals, ref current, slotWorker[a], slotWorker[b]))
                            {
                                loads[a] = loadA;
                                loads[b] = loadB;
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
            // swap two whole stops between two trips: all of group i of trip a for all of group j of trip b. When every ship is full nothing can be moved,
            // only swapped (a run in progress that should fill up with Starlight instead of Endgame, whose waste waits for a trip from Ruin)
            for (var a = 0; a < slots; a++)
            {
                for (var i = 0; i < n; i++)
                {
                    var ci = assign[a][i];
                    if (ci == 0 || Fixed(a, i)) continue;
                    for (var b = a + 1; b < slots; b++)
                    {
                        for (var j = 0; j < n; j++)
                        {
                            var cj = assign[b][j];
                            if (j == i || cj == 0 || Fixed(b, j) || (ci == 1 && cj == 1)) continue;   // one batch each: the single swap does that
                            var loadA = loads[a] - ci * wt[i] + cj * wt[j];
                            var loadB = loads[b] - cj * wt[j] + ci * wt[i];
                            if ((loadA > slotCapacity[a] && loadA > loads[a]) || (loadB > slotCapacity[b] && loadB > loads[b])) continue;
                            assign[a][i] = 0;
                            assign[b][i] += ci;
                            assign[b][j] = 0;
                            assign[a][j] += cj;
                            if (Allowed(assign, i) && Allowed(assign, j) && Keep(assign, totals, ref current, slotWorker[a], slotWorker[b]))
                            {
                                loads[a] = loadA;
                                loads[b] = loadB;
                                return true;
                            }
                            assign[a][j] -= cj;
                            assign[b][j] = cj;
                            assign[b][i] -= ci;
                            assign[a][i] = ci;
                        }
                    }
                }
            }

            // move a whole stop: every batch of group i in trip a goes to trip b. One batch at a time cannot find this either: half a stop moved still leaves
            // the landing in trip a (and a lone batch is a small top-up), so a half full ship was not filled up with a stop on the way home
            for (var a = 0; a < slots; a++)
            {
                for (var i = 0; i < n; i++)
                {
                    var count = assign[a][i];
                    if (count <= 1 || Fixed(a, i)) continue;   // one batch: the single moves do that; his locked, kept or next stop stays
                    for (var b = 0; b < slots; b++)
                    {
                        if (a == b || loads[b] + count * wt[i] > slotCapacity[b]) continue;
                        assign[a][i] = 0;
                        assign[b][i] += count;
                        if (Allowed(assign, i) && Keep(assign, totals, ref current, slotWorker[a], slotWorker[b]))
                        {
                            loads[a] -= count * wt[i];
                            loads[b] += count * wt[i];
                            return true;
                        }
                        assign[a][i] = count;
                        assign[b][i] -= count;
                    }
                }
            }

            // merge two trips: every batch of trip b goes into trip a when the ship has room for both. One batch at a time cannot find this (moving one stop of
            // a trip to another first makes the plan worse), so a few scattered batches ended up as single trips: Ruin, one stop, Ruin, again and again
            for (var a = 0; a < slots; a++)
            {
                for (var b = 0; b < slots; b++)
                {
                    if (a == b || loads[b] == 0 || loads[a] + loads[b] > slotCapacity[a]) continue;
                    if (b < w && reservedIdx[b] >= 0) continue;   // his locked or next stop stays in his first trip
                    var moved = (int[])assign[b].Clone();
                    for (var i = 0; i < n; i++)
                    {
                        assign[a][i] += moved[i];
                        assign[b][i] = 0;
                    }
                    var ok = true;
                    for (var i = 0; i < n && ok; i++)
                    {
                        if (moved[i] > 0 && !Allowed(assign, i)) ok = false;
                    }
                    if (ok && Keep(assign, totals, ref current, slotWorker[a], slotWorker[b]))
                    {
                        loads[a] += loads[b];
                        loads[b] = 0;
                        return true;
                    }
                    for (var i = 0; i < n; i++)
                    {
                        assign[a][i] -= moved[i];
                        assign[b][i] = moved[i];
                    }
                }
            }
            return false;
        }

        // The search only counts the landings (and the flying), so how the batches of a group are divided over the trips that land there anyway hardly matters to
        // it: 4 + 1 is as good as 3 + 2. Fill up: the trip with the most of that group takes as many as its ship holds, the leftover stays in the other one, where
        // new missions can still make it worth the landing (or it is the trip he is flying: that one fills up by itself, see FillNowSeconds). The landings stay the
        // same; a batch is only moved when the plan does not get worse.
        void Consolidate(int[][] assign)
        {
            var load = new int[slots];
            var totals = new double[w];
            for (var t = 0; t < slots; t++)
            {
                for (var i = 0; i < n; i++) load[t] += assign[t][i] * wt[i];
            }
            for (var k = 0; k < w; k++) totals[k] = WorkerTotal(assign, k);
            var value = Objective(totals);
            bool Move(int from, int to, int i)
            {
                if (IsReserved(assign, from, i) || load[to] + wt[i] > slotCapacity[to]) return false;
                assign[from][i]--;
                assign[to][i]++;
                if (Allowed(assign, i))
                {
                    var (wa, wb) = (slotWorker[from], slotWorker[to]);
                    var (oldA, oldB) = (totals[wa], totals[wb]);
                    totals[wa] = WorkerTotal(assign, wa);
                    if (wb != wa) totals[wb] = WorkerTotal(assign, wb);
                    var v = Objective(totals);
                    if (v <= value + 1e-9)
                    {
                        value = v;
                        load[from] -= wt[i];
                        load[to] += wt[i];
                        return true;
                    }
                    totals[wa] = oldA;
                    totals[wb] = oldB;
                }
                assign[from][i]++;
                assign[to][i]--;
                return false;
            }
            for (var i = 0; i < n; i++)
            {
                var moved = true;
                while (moved)
                {
                    moved = false;
                    for (var a = 0; a < slots && !moved; a++)
                    {
                        if (assign[a][i] < 2) continue;   // the last batch stays: the landings do not change here
                        for (var b = 0; b < slots && !moved; b++)
                        {
                            if (a == b || assign[b][i] <= 0 || assign[b][i] < assign[a][i]) continue;   // to the trip with the most there (equal: either way, once)
                            if (assign[b][i] == assign[a][i] && b > a) continue;
                            moved = Move(a, b, i);
                        }
                    }
                }
            }
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
            // the stops kept for a worker start in his first trip, as far as they fit (the search may move them for a gain: KeepSeconds), in every other restart; the
            // others start free, so a plan that swaps two of his kept stops for one fuller stop elsewhere (two moves that are each a loss) is found too, and the
            // best of all restarts wins, the kept stops' cost counted. A group only some workers can take is placed below by its limits
            var used = new bool[units.Count];
            for (var k = 0; k < w && restart % 2 == 0; k++)
            {
                foreach (var (i, batches) in prefer[k].Select(kv => (kv.Key, kv.Value)))
                {
                    if (groups[i] != null) continue;
                    var left = batches;
                    for (var u = 0; u < units.Count && left > 0; u++)
                    {
                        if (used[u] || units[u] != i || loads[k] + wt[i] > slotCapacity[k]) continue;
                        used[u] = true;
                        assign[k][i]++;
                        loads[k] += wt[i];
                        left--;
                    }
                }
            }
            // batches only some workers can take go first, to a trip of one of them with room (the first ones of a group in this order are the limited ones)
            var isLimited = new bool[units.Count];
            var seen = new int[n];
            for (var u = 0; u < units.Count; u++)
            {
                if (used[u]) continue;
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
                if (isLimited[u] || used[u]) continue;
                var i = units[u];
                while (slot < slots - 1 && loads[slot] + wt[i] > slotCapacity[slot]) slot++;
                assign[slot][i]++;
                loads[slot] += wt[i];
            }

            Array.Fill(dropAt, -1);   // the loads from Ruin are not part of this search: they are put on the trips afterwards (below)

            var totals = new double[w];
            for (var k = 0; k < w; k++) totals[k] = WorkerTotal(assign, k);
            var current = Objective(totals);
            while (Improve(assign, loads, totals, ref current)) { }
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
        Consolidate(picks);
        // a load only some workers can take, at a station none of them picks up at (the pickups there were planned for somebody else): the pickups move to a
        // free trip of one who can take it, so the load rides along with him, dropped there before he picks up, instead of waiting (Frank, 29 Oct 2026: a
        // direct contract to Orbituary, where his trip was going anyway, sent him to Rod's Fuel instead)
        for (var c = 0; c < chunks.Count; c++)
        {
            var st = chunkStation[c];
            if (st < 0) continue;
            bool May(int k) => ((chunkMask[c] >> k) & 1) != 0;
            bool PicksAtStation(int t) => Enumerable.Range(0, n).Any(i => origin[i] == origin[st] && picks[t][i] > 0);
            if (Enumerable.Range(0, slots).Any(t => May(slotWorker[t]) && PicksAtStation(t))) continue;
            var from = Enumerable.Range(0, slots).Where(t => picks[t][st] > 0).OrderByDescending(t => picks[t][st]).DefaultIfEmpty(-1).First();
            if (from < 0) continue;
            var batches = picks[from][st];
            var to = Enumerable.Range(0, slots).Where(t => May(slotWorker[t]) && !special[t] && !picks[t].Any(x => x > 0) && batches * wt[st] <= slotCapacity[t] && CanDrop(c, t))
                .DefaultIfEmpty(-1).First();
            if (to < 0) continue;
            picks[to][st] = batches;
            picks[from][st] = 0;
            if (!Allowed(picks, st)) { picks[from][st] = batches; picks[to][st] = 0; }   // batches only the other one may take: they stay
        }
        var picksUp = new bool[w];
        for (var t = 0; t < slots; t++)
        {
            if (picks[t].Any(x => x > 0)) picksUp[slotWorker[t]] = true;
        }
        // a load never adds a stop (the landings are what costs the time): it only rides along on a trip that picks up at its station anyway. Only a worker with
        // nothing at all to pick up and no run in progress may fly a trip just for a load (the last resort)
        bool Fits(int chunk, int slot) =>
            CanDrop(chunk, slot) && (Enumerable.Range(0, n).Any(i => origin[i] == origin[chunkStation[chunk]] && picks[slot][i] > 0)
                || (!picksUp[slotWorker[slot]] && (startOf[slotWorker[slot]]?.Loaded ?? 0) <= 0 && !picks[slot].Any(x => x > 0)));
        var workerTotals = new double[w];
        for (var k = 0; k < w; k++) workerTotals[k] = WorkerTotal(picks, k);
        var objective = Objective(workerTotals);
        var placed = new bool[chunks.Count];

        // the value of the plan when this slot takes this load (-1 = none) instead of the one it has
        double With(int slot, int chunk)
        {
            var had = dropAt[slot];
            var k = slotWorker[slot];
            var old = workerTotals[k];
            dropAt[slot] = chunk;
            workerTotals[k] = WorkerTotal(picks, k);
            var value = Objective(workerTotals);
            workerTotals[k] = old;
            dropAt[slot] = had;
            return value;
        }
        void Set(int slot, int chunk)
        {
            if (dropAt[slot] >= 0) placed[dropAt[slot]] = false;
            if (chunk >= 0) placed[chunk] = true;
            dropAt[slot] = chunk;
            workerTotals[slotWorker[slot]] = WorkerTotal(picks, slotWorker[slot]);
            objective = Objective(workerTotals);
        }

        // every load that can go somewhere is put on a trip, the cheapest one first
        void PlaceAll()
        {
            while (true)
            {
                var pick = (Chunk: -1, Slot: -1, Value: double.MaxValue);
                for (var c = 0; c < chunks.Count; c++)
                {
                    if (placed[c]) continue;
                    for (var t = 0; t < slots; t++)
                    {
                        if (dropAt[t] >= 0 || !Fits(c, t)) continue;
                        var value = With(t, c);
                        if (value < pick.Value - 1e-9) pick = (c, t, value);
                    }
                }
                if (pick.Chunk < 0) return;
                Set(pick.Slot, pick.Chunk);
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
                    var (wa, wb) = (slotWorker[a], slotWorker[b]);
                    var (oldA, oldB) = (workerTotals[wa], workerTotals[wb]);
                    dropAt[a] = cb;
                    dropAt[b] = ca;
                    workerTotals[wa] = WorkerTotal(picks, wa);
                    if (wb != wa) workerTotals[wb] = WorkerTotal(picks, wb);
                    var value = Objective(workerTotals);
                    if (value < objective - 1e-9)
                    {
                        objective = value;
                        better = true;
                        break;
                    }
                    workerTotals[wa] = oldA;
                    workerTotals[wb] = oldB;
                    dropAt[a] = ca;
                    dropAt[b] = cb;
                }
                for (var c = 0; c < chunks.Count && !better; c++)
                {
                    if (placed[c] || !Fits(c, a)) continue;
                    if (With(a, c) >= objective - 1e-9) continue;
                    Set(a, c);
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
            var route = SlotRoute(picks, t)!.Value;
            var start = route.Places[0];
            trip.From = start == PyroDistances.Ruin && !special[t] ? null : start;
            trip.Special = special[t];
            if (special[t]) trip.From = startOf[t]!.From;   // where his run is (null: Ruin), as before
            trip.Distance = route.Gm;
            trip.Seconds = PathSeconds(workers[slotWorker[t]].Drive, route.Places);
            trip.Ends.AddRange(route.Ends);
            // the stops in the order of the route, every group at a station together
            foreach (var place in route.Places.Skip(1))
            {
                if (route.Ends.Contains(place) && !Enumerable.Range(0, n).Any(i => row[i] > 0 && origin[i] == place)) continue;
                if (trip.Stops.Any(s => s.Id == place)) continue;
                var batches = 0;
                for (var i = 0; i < n; i++)
                {
                    if (row[i] <= 0 || origin[i] != place) continue;
                    batches += row[i];
                    trip.Picks.Add((ids[i], place, dest[i], row[i]));
                }
                if (batches > 0) trip.Stops.Add((place, batches));
            }
            trip.Locked = special[t] && reservedIdx[t] >= 0 && trip.Stops.Count > 0 && trip.Stops[0].Id == origin[reservedIdx[t]];
            if (dropAt[t] >= 0)
            {
                var chunk = dropAt[t];
                trip.Drops.Add((chunks[chunk].Station, chunks[chunk].Batches));
                trip.DropCargo = chunks[chunk].Cargo;
                trip.DropScu = chunks[chunk].Scu;
                trip.DropParts = new List<int>(chunks[chunk].Parts);
                trip.DropKeys = new List<string>(chunks[chunk].Keys);
            }
            trips.Add(trip);
        }

        // the Trip Planner orders the trips again (PlanService.Queue: the waste first, then one stop trips, then the value, three stops last); this order decides between trips the same on that
        var order2 = workers.Select(c => c.Name).ToList();
        var sorted = trips
            .OrderBy(tp => order2.IndexOf(tp.Worker))
            .ThenBy(tp => tp.Special ? 0 : 1)   // his first trip: the one he is flying or heading for
            // then: one stop that (nearly) fills the ship, the best trips there are; then the other trips that fill at least half the ship; the ones with little
            // in them wait at the back, where new missions can fill them up
            .ThenBy(tp => tp.Stops.Count == 1 && tp.Fill >= FullShare ? 0 : tp.Fill >= BigShare || tp.Stops.Count == 0 ? 1 : 2)
            .ThenBy(tp => tp.Stops.Count)   // fewer stops first: while he flies those, missions get done and new ones come in, and the trips with more stops are planned again with them
            .ThenByDescending(tp => Math.Round(tp.Fill, 1))   // the fuller ship first
            .ThenBy(tp => tp.Drops.Count > 0 ? 0 : 1)   // with as many stops, the trip that takes cargo out from Ruin first, so he is rid of it (and can take a new from-Ruin contract) sooner
            .ThenByDescending(tp => tp.Batches)
            .ThenBy(tp => tp.Distance)
            .ToList();
        // a worker's first trip starts where he is; when his run in progress is not in the list (nothing was added to it) his next trip starts where that run ends
        var homeOf = new string[w];
        for (var k = 0; k < w; k++) homeOf[k] = SlotRoute(picks, k)?.Places[^1] ?? WorkerStartPlace(k);
        Chain(sorted, name => homeOf[workers.FindIndex(c => c.Name == name)], name => workers.First(c => c.Name == name).Drive);
        return sorted;
    }

    /// <summary>
    /// The trips of every worker in the order they are flown (the order of the list): each starts where the one before it ends, its route is worked out
    /// again from there (the locked first stop, or the station of its load, stays first; then its stations, then its ends in the cheapest order), and its
    /// Gm and seconds with it. Called after the trips are put in their order (here, and in PlanService.Queue), so the routes shown are the ones flown.
    /// </summary>
    public static void Chain(List<PlannedTrip> trips, Func<string, string> locationOf, Func<string, QtDrive> driveOf)
    {
        foreach (var group in trips.GroupBy(t => t.Worker))
        {
            var prev = locationOf(group.Key);
            foreach (var trip in group)
            {
                var start = trip.Special ? trip.From ?? PyroDistances.Ruin : prev;
                var forced = trip.Locked && trip.Stops.Count > 0 ? trip.Stops[0].Id : trip.Drops.Count > 0 ? trip.Drops[0].Id : null;
                var stations = trip.Stops.Select(s => s.Id).Where(id => id != forced).Distinct().ToList();
                var ends = trip.Ends.Distinct().ToList();
                var places = new List<string> { start };
                var from = start;
                if (forced != null)
                {
                    places.Add(forced);
                    from = forced;
                }
                var (_, order) = Path(from, stations, ends);
                var byId = trip.Stops.ToDictionary(s => s.Id, s => s.Batches);
                var newStops = new List<(string Id, int Batches)>();
                if (forced != null && byId.TryGetValue(forced, out var fb)) newStops.Add((forced, fb));
                var newEnds = new List<string>();
                foreach (var o in order)
                {
                    var id = o < stations.Count ? stations[o] : ends[o - stations.Count];
                    places.Add(id);
                    if (o < stations.Count) newStops.Add((id, byId[id]));
                    else newEnds.Add(id);
                }
                trip.Stops = newStops;
                trip.Ends = newEnds;
                trip.From = start == PyroDistances.Ruin && !trip.Special ? null : start;
                trip.Distance = PathGm(places);
                trip.Seconds = PathSeconds(driveOf(group.Key), places);
                prev = trip.End;
            }
        }
    }
}
