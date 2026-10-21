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
    }

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
                res.Add(new DropBatch { Mission = m, Objective = kv.Key, Zone = kv.Value, Station = StationOf(kv.Value) });
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
            var left = g.Count();
            while (left > 0)
            {
                var take = Math.Min(capacity, left);
                chunks.Add(new DropChunk { Station = g.Key.Station, Cargo = mission.OutCargo ?? "Cargo", Batches = take, Allowed = allowed });
                left -= take;
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
            var text = string.Join(", ", byStation.GroupBy(d => d.Mission.OutCargo ?? "Cargo").Select(g => g.Key + " \u00d7" + g.Count()));
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
