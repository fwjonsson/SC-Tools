namespace HaulingTracker;

/// <summary>
/// The cargo of ONE from-Ruin mission that goes out together: its drop stations with the batches for each (no more than a ship holds).
/// The waste of different missions looks the same, so loads of different missions are never mixed.
/// </summary>
public sealed class DropChunk
{
    public List<(string Station, int Batches)> Drops = new();
    public int Batches => Drops.Sum(d => d.Batches);
    public string Cargo = "";
    public string[] Allowed = Array.Empty<string>();   // the workers who have that mission (empty = everybody)
}

/// <summary>
/// Puts the cargo that goes out from Ruin into the trips of the pickup plan. A trip with outgoing cargo leaves Ruin with the load of one mission, flies to its drop
/// stations (the cheapest order) and unloads everything there, and only then takes on pickups (the rest of the trip is the cheapest way over its stops). A load goes
/// into the trip where it adds the least to the crew's flying time; when no trip suits it (for example at the end of a session) it gets a trip of its own.
/// </summary>
public static class DropPlanner
{
    const double TotalWeight = 0.25;   // as in TripPlanner: the longest worker total, plus a part of the combined time

    static double Objective(IEnumerable<double> totals)
    {
        var list = totals.ToList();
        return list.Count == 0 ? 0 : list.Max() + TotalWeight * list.Sum();
    }

    static IEnumerable<List<T>> Permutations<T>(List<T> items)
    {
        if (items.Count <= 1)
        {
            yield return new List<T>(items);
            yield break;
        }
        for (var i = 0; i < items.Count; i++)
        {
            var rest = new List<T>(items);
            rest.RemoveAt(i);
            foreach (var p in Permutations(rest))
            {
                p.Insert(0, items[i]);
                yield return p;
            }
        }
    }

    public static List<PlannedTrip> Attach(IReadOnlyList<PlannedTrip> planned, IReadOnlyList<DropChunk> chunks, IReadOnlyList<(string Name, int Capacity, QtDrive Drive)> crew)
    {
        var trips = planned.Select(t => t.Copy()).ToList();
        var flyers = crew.Where(c => c.Capacity > 0).ToList();
        if (chunks.Count == 0 || flyers.Count == 0) return trips;

        double SecondsOf(string worker, IEnumerable<string> route)
        {
            var drive = flyers.First(c => c.Name == worker).Drive;
            var from = PyroDistances.Ruin;
            double seconds = 0;
            foreach (var id in route)
            {
                if (id == from) continue;
                seconds += drive.Seconds(PyroDistances.Between(from, id));
                from = id;
            }
            return seconds + drive.Seconds(PyroDistances.Between(from, PyroDistances.Ruin));
        }

        // the trip with this load dropped first: the drops in their best order, then the pickups, with the Gm and the flying time
        (List<(string Id, int Batches)> Drops, List<(string Id, int Batches)> Stops, double Gm, double Seconds) Route(PlannedTrip t, DropChunk c)
        {
            // a locked first pickup stays the first pickup (he goes there right after the drops)
            var locked = t.Locked && t.Stops.Count > 0 ? t.Stops[0] : ((string Id, int Batches)?)null;
            var rest = locked == null ? t.Stops : t.Stops.Skip(1).ToList();
            var bestGm = double.MaxValue;
            List<(string Id, int Batches)>? bestDrops = null;
            List<(string Id, int Batches)>? bestStops = null;
            foreach (var order in Permutations(c.Drops.Select(d => (d.Station, d.Batches)).ToList()))
            {
                double gm = 0;
                var from = PyroDistances.Ruin;
                foreach (var d in order)
                {
                    gm += PyroDistances.Between(from, d.Station);
                    from = d.Station;
                }
                List<(string Id, int Batches)> ordered;
                if (locked != null)
                {
                    var cheapest = TripPlanner.Cheapest(locked.Value.Id, rest.Select(x => x.Id).ToList());
                    gm += PyroDistances.Between(from, locked.Value.Id) + cheapest.Cost;
                    ordered = new List<(string Id, int Batches)> { locked.Value };
                    ordered.AddRange(cheapest.Order.Select(o => rest[o]));
                }
                else
                {
                    var cheapest = TripPlanner.Cheapest(from, rest.Select(x => x.Id).ToList());
                    gm += cheapest.Cost;
                    ordered = cheapest.Order.Select(o => rest[o]).ToList();
                }
                if (gm < bestGm - 1e-9)
                {
                    bestGm = gm;
                    bestDrops = order.Select(d => (d.Station, d.Batches)).ToList();
                    bestStops = ordered;
                }
            }
            var route = bestDrops!.Select(d => d.Id).Concat(bestStops!.Select(s => s.Id)).ToList();
            return (bestDrops!, bestStops!, bestGm, SecondsOf(t.Worker, route));
        }

        double Total(string worker, PlannedTrip? replaced = null, double replacedSeconds = 0, double added = 0) =>
            trips.Where(t => t.Worker == worker).Sum(t => ReferenceEquals(t, replaced) ? replacedSeconds : t.Seconds) + added;

        foreach (var c in chunks.OrderByDescending(x => x.Drops.Max(d => PyroDistances.Between(PyroDistances.Ruin, d.Station))))
        {
            var allowed = flyers.Where(f => c.Allowed.Length == 0 || c.Allowed.Contains(f.Name)).ToList();
            if (allowed.Count == 0) allowed = flyers;
            var bestScore = double.MaxValue;
            PlannedTrip? bestTrip = null;
            (List<(string Id, int Batches)> Drops, List<(string Id, int Batches)> Stops, double Gm, double Seconds) bestRoute = default;
            string? standaloneWorker = null;

            foreach (var t in trips)
            {
                if (t.Drops.Count > 0 || (t.Special && (t.From != null || !t.Locked))) continue;   // a run in progress (or a start elsewhere) cannot take cargo from Ruin
                var cap = flyers.FirstOrDefault(f => f.Name == t.Worker).Capacity;
                if (!allowed.Any(a => a.Name == t.Worker) || c.Batches > cap) continue;
                var r = Route(t, c);
                var score = Objective(flyers.Select(f => f.Name == t.Worker ? Total(f.Name, t, r.Seconds) : Total(f.Name)));
                if (score < bestScore - 1e-9)
                {
                    bestScore = score;
                    bestTrip = t;
                    bestRoute = r;
                    standaloneWorker = null;
                }
            }
            foreach (var f in allowed)
            {
                if (c.Batches > f.Capacity) continue;
                var empty = new PlannedTrip { Worker = f.Name, Room = f.Capacity };
                var r = Route(empty, c);
                var score = Objective(flyers.Select(g => g.Name == f.Name ? Total(g.Name, null, 0, r.Seconds) : Total(g.Name)));
                if (score < bestScore - 1e-9)
                {
                    bestScore = score;
                    bestTrip = null;
                    standaloneWorker = f.Name;
                    bestRoute = r;
                }
            }

            if (bestTrip != null)
            {
                bestTrip.Drops = bestRoute.Drops;
                bestTrip.Stops = bestRoute.Stops;
                bestTrip.Distance = bestRoute.Gm;
                bestTrip.Seconds = bestRoute.Seconds;
                bestTrip.DropCargo = c.Cargo;
            }
            else if (standaloneWorker != null)
            {
                var f = flyers.First(x => x.Name == standaloneWorker);
                trips.Add(new PlannedTrip
                {
                    Worker = f.Name,
                    Room = f.Capacity,
                    Drops = bestRoute.Drops,
                    Stops = bestRoute.Stops,
                    Distance = bestRoute.Gm,
                    Seconds = bestRoute.Seconds,
                    DropCargo = c.Cargo,
                });
            }
        }
        return trips;
    }
}
