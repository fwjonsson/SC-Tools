using System.Globalization;

namespace HaulingTracker.Tracking;

/// <summary>One drop-off of a from-Ruin contract that is still to do: the cargo is loaded at Ruin and dropped off at 2 to 4 stations, every drop-off is one batch.</summary>
public sealed class DropBatch
{
    public Mission Mission = null!;
    public string Objective = "";
    public string Zone = "";
    public string? Station;   // null = a location that is not in the distance table
    public int Scu;           // the SCU to bring to this drop-off, from the game's "Deliver 0/147 SCU" popup (0 = not seen)
}

/// <summary>
/// From-Ruin contracts ("Single to Multi", and the direct ones with one drop-off): what still has to go out to the stations, cut into loads the planner can take along, where a worker is after he
/// delivered one, and the SCU you typed where the game did not say it.
/// </summary>
public sealed class DropOffs
{
    readonly TrackerState _s;
    readonly Places _places;
    readonly Crew _crew;
    readonly Contracts _contracts;

    public DropOffs(TrackerState state, Places places, Crew crew, Contracts contracts)
    {
        _s = state;
        _places = places;
        _crew = crew;
        _contracts = contracts;
    }

    /// <summary>
    /// The containers of a load, as big as possible: 147 SCU is 4 x 32, then 16, 2 and 1 (the sizes are 32, 24, 16, 8, 4, 2 and 1).
    /// </summary>
    public static string ContainersText(int scu)
    {
        var parts = new List<string>();
        foreach (var size in new[] { 32, 24, 16, 8, 4, 2, 1 })
        {
            var n = scu / size;
            if (n <= 0) continue;
            parts.Add(n == 1 ? size.ToString(CultureInfo.InvariantCulture) : n + "×" + size);
            scu -= n * size;
        }
        return string.Join(" + ", parts);
    }

    /// <summary>"147 SCU: 4×32 + 16 + 2 + 1", or an empty text when the SCU is not known.</summary>
    public static string ScuText(int scu) => scu <= 0 ? "" : scu + " SCU: " + ContainersText(scu);

    /// <summary>
    /// The Cargo cell of a load that goes out from Ruin: the SCU of every drop-off in it with the containers it is cut into ("152 SCU: 4×32 + 24"), one part for
    /// each drop-off; "? SCU" when the game never said how big the load is.
    /// </summary>
    public static string LoadText(IEnumerable<int> parts)
    {
        var all = parts.ToList();
        return all.Any(p => p > 0) ? string.Join(" | ", all.Select(p => p > 0 ? ScuText(p) : "? SCU")) : "? SCU";
    }

    /// <summary>The Drop-off cell of a station on the Stops board: what has to be brought out to it, by cargo ("Waste ×2 (152 SCU: 4×32 + 24)").</summary>
    public static string StationText(IEnumerable<DropBatch> drops) =>
        string.Join(", ", drops.GroupBy(d => d.Mission.OutCargo ?? "Cargo").Select(g => g.Key + " ×" + g.Count() + (g.Sum(d => d.Scu) > 0 ? " (" + ScuText(g.Sum(d => d.Scu)) + ")" : "")));

    /// <summary>The SCU you typed for drop-offs the game did not tell the size of: added to the missions of the merged log, where the game's own number is missing.</summary>
    public void ApplyTypedScu(HaulingLog log)
    {
        foreach (var kv in _s.Data.TypedScu)
        {
            var bar = kv.Key.IndexOf('|');
            if (bar <= 0 || kv.Value <= 0) continue;
            if (log.Missions.TryGetValue(kv.Key[..bar], out var m)) m.ObjScu.TryAdd(kv.Key[(bar + 1)..], kv.Value);
        }
    }

    /// <summary>Remembers the SCU typed for a drop-off ("mission id|objective id"); 0 forgets it. The game's own number wins once it writes it.</summary>
    public void SetTypedScu(string key, int scu)
    {
        if (scu > 0) _s.Data.TypedScu[key] = scu;
        else _s.Data.TypedScu.Remove(key);
    }

    /// <summary>What a drop-off is, for the dialog where its SCU is typed: "Waste for Patch City".</summary>
    public string DropLabel(string key)
    {
        var bar = key.IndexOf('|');
        if (_s.Log == null || bar <= 0 || !_s.Log.Missions.TryGetValue(key[..bar], out var m)) return "Drop-off";
        var place = m.ObjZone.TryGetValue(key[(bar + 1)..], out var zone) ? _places.LocationName(zone) : "a station";
        return (m.OutCargo ?? "Cargo") + " for " + place;
    }

    /// <summary>The drop-offs still to do: every drop-off objective of a from-Ruin mission that is running and not completed yet.</summary>
    public List<DropBatch> OpenDrops()
    {
        var res = new List<DropBatch>();
        var log = _s.Log;
        if (log == null) return res;
        foreach (var m in log.Missions.Values.Where(x => x.IsOut && x.EndState == null))
        {
            var seen = m.Accepted ?? m.FirstSeen;
            if (seen.HasValue && log.LastTime - seen.Value > TimeSpan.FromHours(12)) continue;   // an old one that never ended
            foreach (var kv in m.ObjZone.Where(o => o.Key.StartsWith("dropoff_")))
            {
                if (m.Events.TryGetValue(kv.Key, out var ev) && ev.Any(e => e.State == "COMPLETED")) continue;
                res.Add(new DropBatch { Mission = m, Objective = kv.Key, Zone = kv.Value, Station = _places.StationOf(kv.Value), Scu = m.ObjScu.GetValueOrDefault(kv.Key) });
            }
        }
        return res;
    }

    /// <summary>
    /// Where a worker is after he delivered the load of a from-Ruin mission he holds: the station of his latest completed drop-off. It only counts until he picks up
    /// something or hands in a run after it (a run in progress knows where it is by itself), and while the delivery is recent. Null = nothing to go by.
    /// </summary>
    public (string Station, DateTime Time)? DropStartOf(string name)
    {
        var best = LastDropOf(name);
        if (_s.Log == null || best == null || best.Value.Station == null || best.Value.Station == PyroDistances.Ruin) return null;
        if (_s.Log.LastTime - best.Value.Time > TimeSpan.FromHours(3)) return null;
        var at = best.Value.Time;
        var moved = _s.Trips.Any(t => SameName(t.Worker, name) && ((t.End ?? DateTime.MinValue) > at || t.Pickups.Any(p => p.Time > at)));
        return moved ? null : (best.Value.Station!, at);
    }

    /// <summary>
    /// The latest drop-off of a from-Ruin load that this worker delivered himself: the station (null = a place that is not in the distance table) and when.
    /// A shared mission is in the log of everyone who has it, and a delivery shows up in all of those logs, so who delivered it comes from the loading platform
    /// lines (see WorkerTripBuilder.OutSteps): only the one who did it is placed at that station. What nobody's log shows is not counted.
    /// </summary>
    public (string? Station, DateTime Time)? LastDropOf(string name)
    {
        if (_s.Log == null) return null;
        (string? Station, DateTime Time)? best = null;
        foreach (var s in WorkerTripBuilder.OutSteps(_s.Log))
        {
            if (s.Pickup || s.Worker == null || !SameName(s.Worker, name)) continue;
            if (!s.Mission.ObjZone.TryGetValue(s.Objective, out var zone)) continue;
            if (best == null || s.Time > best.Value.Time) best = (_places.StationOf(zone), s.Time);
        }
        return best;
    }

    /// <summary>
    /// The cargo that goes out, cut into loads for one station: the drop-offs there of every from-Ruin contract the same workers have (a direct contract and
    /// a waste drop-off for the same stop go out together), as many as a ship holds. Waste for one station looks the same whichever contract it is for, so it is
    /// all unloaded there; waste for two stations does not go in one ship, so a trip only leaves Ruin with the load for its first stop, and only the workers who
    /// have those contracts can take it.
    /// </summary>
    /// <param name="capacity">the most drop-offs in one load (every drop-off is a batch: the batches the smallest ship holds)</param>
    /// <param name="maxScu">the most SCU in one load (what the smallest ship is planned to hold; 0 = no limit): a direct load of 600 SCU and a waste drop-off of 274
    /// do not fit in one C2, so they are two loads</param>
    public List<DropChunk> DropChunks(List<DropBatch> drops, List<string> flyers, int capacity, int maxScu = 0)
    {
        var chunks = new List<DropChunk>();
        if (capacity <= 0) return chunks;
        var holders = new Dictionary<string, string[]>();   // mission id -> the workers who have it (empty = everybody)
        foreach (var m in drops.Select(d => d.Mission).DistinctBy(m => m.Id))
        {
            var can = flyers.Where(n => _contracts.HasMission(m, n)).ToArray();
            holders[m.Id] = can.Length == 0 || can.Length == flyers.Count ? Array.Empty<string>() : can;
        }
        foreach (var g in drops.Where(d => d.Station != null).GroupBy(d => (Station: d.Station!, Who: string.Join("|", holders[d.Mission.Id]))))
        {
            var allowed = holders[g.First().Mission.Id];
            var list = g.ToList();
            var done = 0;
            while (done < list.Count)
            {
                var part = new List<DropBatch> { list[done] };
                var scu = list[done].Scu;
                while (done + part.Count < list.Count && part.Count < capacity && (maxScu <= 0 || scu + list[done + part.Count].Scu <= maxScu))
                {
                    scu += list[done + part.Count].Scu;
                    part.Add(list[done + part.Count]);
                }
                done += part.Count;
                // a ship that holds fewer SCU than the load cannot take it (a worker without a ship set is assumed to manage)
                var who = allowed.Length == 0 ? flyers : allowed.ToList();
                var fits = scu <= 0 ? who : who.Where(n => _crew.ShipOf(n) is not { } ship || ship.Total >= scu).ToList();
                var set = fits.Count == 0 || fits.Count == flyers.Count ? allowed : fits.ToArray();
                chunks.Add(new DropChunk
                {
                    Station = g.Key.Station,
                    Cargo = string.Join(" + ", part.Select(d => d.Mission.OutCargo ?? "Cargo").Distinct()),
                    Batches = part.Count,
                    Scu = scu,
                    Parts = part.Select(d => d.Scu).ToList(),
                    Keys = part.Select(d => d.Mission.Id + "|" + d.Objective).ToList(),
                    Allowed = set,
                });
            }
        }
        return chunks;
    }
}
