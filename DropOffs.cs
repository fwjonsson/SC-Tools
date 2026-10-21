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

    /// <summary>The cargo that goes out, cut into loads for one stop each: no more than a ship holds, and only for the workers who have those missions.</summary>
    List<DropChunk> DropChunks(List<DropBatch> drops, List<string> flyers, int capacity)
    {
        var chunks = new List<DropChunk>();
        if (capacity <= 0) return chunks;
        var groups = drops.Where(d => d.Station != null).GroupBy(d =>
        {
            var can = flyers.Where(n => HasMission(d.Mission, n)).ToArray();
            var allowed = can.Length == 0 || can.Length == flyers.Count ? "" : string.Join(",", can);
            return (d.Station!, d.Mission.OutCargo ?? "Cargo", allowed);
        });
        foreach (var g in groups)
        {
            var left = g.Count();
            while (left > 0)
            {
                var take = Math.Min(capacity, left);
                chunks.Add(new DropChunk
                {
                    Station = g.Key.Item1,
                    Cargo = g.Key.Item2,
                    Batches = take,
                    Allowed = g.Key.Item3.Length == 0 ? Array.Empty<string>() : g.Key.Item3.Split(','),
                });
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
