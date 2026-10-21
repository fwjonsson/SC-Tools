namespace HaulingTracker;

/// <summary>Batches that go out from Ruin to one station (from-Ruin contracts): at most what a ship holds, brought to a single stop and unloaded there before any pickup.</summary>
public sealed class DropChunk
{
    public string Station = "";
    public int Batches;
    public string Cargo = "";
    public string[] Allowed = Array.Empty<string>();   // the workers who have these missions (empty = everybody)
}

/// <summary>
/// Puts the cargo that goes out from Ruin into the trips of the pickup plan. A trip with outgoing cargo leaves Ruin full, flies to ONE station and unloads
/// everything there, and only then takes on pickups (from that station on, the rest of the trip is the cheapest way over its stops). A chunk goes into the trip
/// where it adds the least to the crew's flying time; when no trip suits it (for example at the end of a session) it gets a trip of its own.
/// </summary>
public static class DropPlanner
{
    const double TotalWeight = 0.25;   // as in TripPlanner: the longest worker total, plus a part of the combined time

    static double Objective(IEnumerable<double> totals)
    {
        var list = totals.ToList();
        return list.Count == 0 ? 0 : list.Max() + TotalWeight * list.Sum();
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

        // the trip with this chunk dropped first: the stops in their new order, the Gm and the flying time
        (List<(string Id, int Batches)> Stops, double Gm, double Seconds) Route(PlannedTrip t, DropChunk c)
        {
            var s = c.Station;
            // a locked first stop stays the first pickup (he goes there right after the drop); else a pickup at the drop station comes first
            var first = t.Locked && t.Stops.Count > 0 ? t.Stops[0].Id : t.Stops.Any(x => x.Id == s) ? s : null;
            List<(string Id, int Batches)> ordered;
            double gm;
            if (first != null)
            {
                var firstEntry = t.Stops.First(x => x.Id == first);
                var rest = t.Stops.Where(x => x.Id != first).ToList();
                var cheapest = TripPlanner.Cheapest(first, rest.Select(x => x.Id).ToList());
                ordered = new List<(string Id, int Batches)> { firstEntry };
                ordered.AddRange(cheapest.Order.Select(o => rest[o]));
                gm = PyroDistances.Between(PyroDistances.Ruin, s) + PyroDistances.Between(s, first) + cheapest.Cost;
            }
            else
            {
                var cheapest = TripPlanner.Cheapest(s, t.Stops.Select(x => x.Id).ToList());
                ordered = cheapest.Order.Select(o => t.Stops[o]).ToList();
                gm = PyroDistances.Between(PyroDistances.Ruin, s) + cheapest.Cost;
            }
            var route = new List<string> { s };
            route.AddRange(ordered.Select(x => x.Id));
            return (ordered, gm, SecondsOf(t.Worker, route));
        }

        double Total(string worker, PlannedTrip? replaced = null, double replacedSeconds = 0, PlannedTrip? added = null) =>
            trips.Where(t => t.Worker == worker).Sum(t => ReferenceEquals(t, replaced) ? replacedSeconds : t.Seconds) + (added?.Seconds ?? 0);

        foreach (var c in chunks.OrderByDescending(x => PyroDistances.Between(PyroDistances.Ruin, x.Station)))
        {
            var allowed = flyers.Where(f => c.Allowed.Length == 0 || c.Allowed.Contains(f.Name)).ToList();
            if (allowed.Count == 0) allowed = flyers;
            double bestScore = double.MaxValue;
            PlannedTrip? bestTrip = null;
            (List<(string Id, int Batches)> Stops, double Gm, double Seconds) bestRoute = default;
            string? standaloneWorker = null;

            foreach (var t in trips)
            {
                if (t.DropId != null || (t.Special && (t.From != null || !t.Locked))) continue;   // a run in progress (or a start elsewhere) cannot take cargo from Ruin
                var cap = flyers.FirstOrDefault(f => f.Name == t.Worker).Capacity;
                if (!allowed.Any(a => a.Name == t.Worker) || c.Batches > cap) continue;
                var r = Route(t, c);
                var totals = flyers.Select(f => f.Name == t.Worker ? Total(f.Name, t, r.Seconds) : Total(f.Name));
                var score = Objective(totals);
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
                var alone = new PlannedTrip { Seconds = SecondsOf(f.Name, new[] { c.Station }) };
                var totals = flyers.Select(g => g.Name == f.Name ? Total(g.Name, null, 0, alone) : Total(g.Name));
                var score = Objective(totals);
                if (score < bestScore - 1e-9)
                {
                    bestScore = score;
                    bestTrip = null;
                    standaloneWorker = f.Name;
                }
            }

            if (bestTrip != null)
            {
                bestTrip.Stops = bestRoute.Stops;
                bestTrip.Distance = bestRoute.Gm;
                bestTrip.Seconds = bestRoute.Seconds;
                bestTrip.DropId = c.Station;
                bestTrip.DropBatches = c.Batches;
                bestTrip.DropCargo = c.Cargo;
            }
            else if (standaloneWorker != null)
            {
                var f = flyers.First(x => x.Name == standaloneWorker);
                trips.Add(new PlannedTrip
                {
                    Worker = f.Name,
                    Room = f.Capacity,
                    Distance = PyroDistances.Between(PyroDistances.Ruin, c.Station) * 2,
                    Seconds = SecondsOf(f.Name, new[] { c.Station }),
                    DropId = c.Station,
                    DropBatches = c.Batches,
                    DropCargo = c.Cargo,
                });
            }
        }
        return trips;
    }
}
