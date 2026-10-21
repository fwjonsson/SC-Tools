namespace HaulingTracker;

/// <summary>
/// The load of ONE from-Ruin mission for ONE station (no more than a ship holds). The waste of different missions looks the same, so a trip only ever leaves Ruin
/// with the load for its first stop.
/// </summary>
public sealed class DropChunk
{
    public string Station = "";
    public int Batches;
    public string Cargo = "";
    public string[] Allowed = Array.Empty<string>();   // the workers who have that mission (empty = everybody)
}

/// <summary>
/// Puts the cargo that goes out from Ruin into the trips of the pickup plan, which stays the priority: the load of a trip is only for ITS FIRST STOP, so the
/// ship is empty again when the pickups begin. A station with a drop-off therefore becomes the first stop of a trip (a trip that already goes there gets that stop
/// moved to the front, the rest of the trip is the cheapest way from there); when no trip goes there, the drop is an extra first stop before the pickups of a trip
/// (or a trip of its own at the end of a session). Every trip takes at most one load. The loads go to the trips where they cost the crew the least flying time.
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

        // the trip with this load first: the station of the drop is the first stop, the other stops follow in the cheapest order
        (List<(string Id, int Batches)> Stops, double Gm, double Seconds) Route(PlannedTrip t, DropChunk c)
        {
            var s = c.Station;
            var here = t.Stops.Where(x => x.Id == s).ToList();
            var rest = t.Stops.Where(x => x.Id != s).ToList();
            var cheapest = TripPlanner.Cheapest(s, rest.Select(x => x.Id).ToList());
            var ordered = new List<(string Id, int Batches)>();
            ordered.AddRange(here);   // the pickups at the drop station (if any) come first: the load is unloaded, then they are taken
            ordered.AddRange(cheapest.Order.Select(o => rest[o]));
            var gm = PyroDistances.Between(PyroDistances.Ruin, s) + cheapest.Cost;
            var route = new List<string> { s };
            route.AddRange(ordered.Select(x => x.Id));
            return (ordered, gm, SecondsOf(t.Worker, route));
        }

        double Total(string worker, PlannedTrip? replaced = null, double replacedSeconds = 0, double added = 0) =>
            trips.Where(t => t.Worker == worker).Sum(t => ReferenceEquals(t, replaced) ? replacedSeconds : t.Seconds) + added;

        var open = chunks.ToList();
        while (open.Count > 0)
        {
            var bestScore = double.MaxValue;
            DropChunk? bestChunk = null;
            PlannedTrip? bestTrip = null;
            string? standaloneWorker = null;
            (List<(string Id, int Batches)> Stops, double Gm, double Seconds) bestRoute = default;

            foreach (var c in open)
            {
                var allowed = flyers.Where(f => c.Allowed.Length == 0 || c.Allowed.Contains(f.Name)).ToList();
                if (allowed.Count == 0) allowed = flyers;
                foreach (var t in trips)
                {
                    if (t.Drops.Count > 0 || (t.Special && (t.From != null || !t.Locked))) continue;   // a run in progress (or a start elsewhere) cannot take cargo from Ruin; one load per trip
                    var cap = flyers.FirstOrDefault(f => f.Name == t.Worker).Capacity;
                    if (!allowed.Any(a => a.Name == t.Worker) || c.Batches > cap) continue;
                    var r = Route(t, c);
                    var score = Objective(flyers.Select(f => f.Name == t.Worker ? Total(f.Name, t, r.Seconds) : Total(f.Name)));
                    if (score < bestScore - 1e-9)
                    {
                        bestScore = score;
                        bestChunk = c;
                        bestTrip = t;
                        bestRoute = r;
                        standaloneWorker = null;
                    }
                }
                foreach (var f in allowed)
                {
                    if (c.Batches > f.Capacity) continue;
                    var r = Route(new PlannedTrip { Worker = f.Name, Room = f.Capacity }, c);   // a trip of its own: Ruin, the station, back to Ruin
                    var score = Objective(flyers.Select(g => g.Name == f.Name ? Total(g.Name, null, 0, r.Seconds) : Total(g.Name)));
                    if (score < bestScore - 1e-9)
                    {
                        bestScore = score;
                        bestChunk = c;
                        bestTrip = null;
                        standaloneWorker = f.Name;
                        bestRoute = r;
                    }
                }
            }
            if (bestChunk == null) break;   // nobody can take the rest of the loads

            var drops = new List<(string Id, int Batches)> { (bestChunk.Station, bestChunk.Batches) };
            if (bestTrip != null)
            {
                bestTrip.Drops = drops;
                bestTrip.Stops = bestRoute.Stops;
                bestTrip.Distance = bestRoute.Gm;
                bestTrip.Seconds = bestRoute.Seconds;
                bestTrip.DropCargo = bestChunk.Cargo;
            }
            else if (standaloneWorker != null)
            {
                var f = flyers.First(x => x.Name == standaloneWorker);
                trips.Add(new PlannedTrip
                {
                    Worker = f.Name,
                    Room = f.Capacity,
                    Drops = drops,
                    Stops = bestRoute.Stops,
                    Distance = bestRoute.Gm,
                    Seconds = bestRoute.Seconds,
                    DropCargo = bestChunk.Cargo,
                });
            }
            open.Remove(bestChunk);
        }
        return trips;
    }
}
