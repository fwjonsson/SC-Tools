using System.Globalization;

namespace HaulingTracker;

// From-Ruin contracts ("Single to Multi"): the cargo is loaded at Ruin and dropped off at 2 to 4 stations. Alpha: every drop-off objective is one batch.
public sealed partial class MainForm
{
    sealed class DropBatch
    {
        public Mission Mission = null!;
        public string Objective = "";
        public string Zone = "";
        public string? Station;   // null = a location that is not in the distance table
        public int Scu;           // the SCU to bring to this drop-off, from the game's "Deliver 0/147 SCU" popup (0 = not seen)
    }

    /// <summary>
    /// The containers of a load, as big as possible: 147 SCU is 4 x 32, then 16, 2 and 1 (the sizes are 32, 24, 16, 8, 4, 2 and 1).
    /// </summary>
    internal static string ContainersText(int scu)
    {
        var parts = new List<string>();
        foreach (var size in new[] { 32, 24, 16, 8, 4, 2, 1 })
        {
            var n = scu / size;
            if (n <= 0) continue;
            parts.Add(n == 1 ? size.ToString(CultureInfo.InvariantCulture) : n + "\u00d7" + size);
            scu -= n * size;
        }
        return string.Join(" + ", parts);
    }

    /// <summary>"147 SCU: 4×32 + 16 + 2 + 1", or an empty text when the SCU is not known.</summary>
    internal static string ScuText(int scu) => scu <= 0 ? "" : scu + " SCU: " + ContainersText(scu);

    /// <summary>The drop-offs still to do: every drop-off objective of a from-Ruin mission that is running and not completed yet.</summary>
    List<DropBatch> OpenDrops()
    {
        var res = new List<DropBatch>();
        if (_log == null) return res;
        foreach (var m in _log.Missions.Values.Where(x => x.IsOut && x.EndState == null))
        {
            var seen = m.Accepted ?? m.FirstSeen;
            if (seen.HasValue && _log.LastTime - seen.Value > TimeSpan.FromHours(12)) continue;   // an old one that never ended
            foreach (var kv in m.ObjZone.Where(o => o.Key.StartsWith("dropoff_")))
            {
                if (m.Events.TryGetValue(kv.Key, out var ev) && ev.Any(e => e.State == "COMPLETED")) continue;
                res.Add(new DropBatch { Mission = m, Objective = kv.Key, Zone = kv.Value, Station = StationOf(kv.Value), Scu = m.ObjScu.GetValueOrDefault(kv.Key) });
            }
        }
        return res;
    }

    /// <summary>
    /// The cargo that goes out, cut into loads of one mission for one station (no more than a ship holds). The waste of different missions looks the same, so a trip only
    /// leaves Ruin with the load for its first stop, and only the workers who have that mission can take it.
    /// </summary>
    List<DropChunk> DropChunks(List<DropBatch> drops, List<string> flyers, int capacity)
    {
        var chunks = new List<DropChunk>();
        if (capacity <= 0) return chunks;
        foreach (var g in drops.Where(d => d.Station != null).GroupBy(d => (Mission: d.Mission.Id, Station: d.Station!)))
        {
            var mission = g.First().Mission;
            var can = flyers.Where(n => HasMission(mission, n)).ToArray();
            var allowed = can.Length == 0 || can.Length == flyers.Count ? Array.Empty<string>() : can;
            var scus = g.Select(d => d.Scu).ToList();
            var done = 0;
            while (done < scus.Count)
            {
                var take = Math.Min(capacity, scus.Count - done);
                var scu = scus.Skip(done).Take(take).Sum();
                done += take;
                // a ship that holds fewer SCU than the load cannot take it (a worker without a ship set is assumed to manage)
                var who = allowed.Length == 0 ? flyers : allowed.ToList();
                var fits = scu <= 0 ? who : who.Where(n => ShipOf(n) is not { } ship || ship.Scu >= scu).ToList();
                var set = fits.Count == 0 || fits.Count == flyers.Count ? allowed : fits.ToArray();
                chunks.Add(new DropChunk { Station = g.Key.Station, Cargo = mission.OutCargo ?? "Cargo", Batches = take, Scu = scu, Allowed = set });
            }
        }
        return chunks;
    }

    /// <summary>The Drop-off column of the Stops board: what has to be brought out to every station (a station with only drop-offs gets a row of its own).</summary>
    void FillDropOffs()
    {
        if (_logGrid.Columns.Count < 7) return;
        var drops = OpenDrops();
        foreach (var byStation in drops.GroupBy(d => d.Station ?? ("zone " + d.Zone)))
        {
            var text = string.Join(", ", byStation.GroupBy(d => d.Mission.OutCargo ?? "Cargo").Select(g => g.Key + " \u00d7" + g.Count() + (g.Sum(d => d.Scu) > 0 ? " (" + ScuText(g.Sum(d => d.Scu)) + ")" : "")));
            var zone = byStation.First().Zone;
            DataGridViewRow? row = null;
            foreach (DataGridViewRow r in _logGrid.Rows)
            {
                if (r.Tag is string z && (z == zone || (byStation.First().Station != null && StationOf(z) == byStation.First().Station))) { row = r; break; }
            }
            if (row == null)
            {
                var idx = _logGrid.Rows.Add(LocationName(zone), DistanceText(zone), "", "", "", "");
                row = _logGrid.Rows[idx];
                row.Tag = zone;
            }
            row.Cells[6].Value = text;
            row.Cells[6].Style.BackColor = Theme.Gold;
            row.Cells[6].Style.ForeColor = Color.Black;
        }
    }
}
