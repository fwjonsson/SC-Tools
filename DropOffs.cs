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

    /// <summary>
    /// The Cargo cell of a load that goes out from Ruin: the SCU of every drop-off in it with the containers it is cut into ("152 SCU: 4×32 + 24"), one part for
    /// each drop-off; "? SCU" when the game never said how big the load is.
    /// </summary>
    internal static string LoadText(IEnumerable<int> parts)
    {
        var all = parts.ToList();
        return all.Any(p => p > 0) ? string.Join(" | ", all.Select(p => p > 0 ? ScuText(p) : "? SCU")) : "? SCU";
    }

    /// <summary>What a from-Ruin contract pays: the From Ruin rows of the rewards table on the Session tab, by its number of drop-offs (nothing typed = the built-in payout).</summary>
    static decimal WasteRewardOf(Mission m, SessionInputs? inp) =>
        inp == null ? 0
        : m.OutDrops == 2 ? Or(inp.WasteReward2, DefaultReward2)   // the same payouts as the to-Ruin contracts with as many stops
        : m.OutDrops == 3 ? Or(inp.WasteReward3, DefaultReward3)
        : m.OutDrops == 4 ? Or(inp.WasteReward4, DefaultReward4) : 0;

    /// <summary>
    /// The value of from-Ruin drop-offs ("mission id|objective id"): every drop-off is worth the reward of its contract split over its drop-offs, like a pickup
    /// is worth reward / stops.
    /// </summary>
    decimal WasteValue(IEnumerable<string> keys)
    {
        if (_log == null || _inputs == null) return 0;
        decimal total = 0;
        foreach (var key in keys)
        {
            var bar = key.IndexOf('|');
            if (bar <= 0 || !_log.Missions.TryGetValue(key[..bar], out var m) || m.OutDrops <= 0) continue;
            total += WasteRewardOf(m, _inputs) / m.OutDrops;
        }
        return total;
    }

    /// <summary>The waste a run delivered: what it is worth.</summary>
    decimal WasteValue(WorkerTrip t) => WasteValue(t.Drops.Select(d => d.MissionId + "|" + d.Objective));

    /// <summary>
    /// The from-Ruin contracts of a session, like the hauling missions: one that is still going belongs to the latest session, one that is done to the session it was
    /// completed in (that is when it pays). Abandoned and failed ones are left out. They are not hauling missions, so they are not in the session itself.
    /// </summary>
    List<Mission> SessionWaste(SessionGroup g)
    {
        var list = new List<Mission>();
        if (_log == null) return list;
        foreach (var m in _log.Missions.Values.Where(x => x.IsOut && !x.Dead && !x.Dropped))
        {
            var owner = m.EndedAt is { } ended
                ? _sessions.Where(s => s.Start <= ended).OrderBy(s => s.Start).LastOrDefault()
                : _sessions.OrderBy(s => s.Start).LastOrDefault();
            if (owner != null && owner.Key == g.Key) list.Add(m);
        }
        return list;
    }

    /// <summary>The SCU you typed for drop-offs the game did not tell the size of: added to the missions of the merged log, where the game's own number is missing.</summary>
    void ApplyTypedScu(HaulingLog log)
    {
        foreach (var kv in _data.TypedScu)
        {
            var bar = kv.Key.IndexOf('|');
            if (bar <= 0 || kv.Value <= 0) continue;
            if (log.Missions.TryGetValue(kv.Key[..bar], out var m)) m.ObjScu.TryAdd(kv.Key[(bar + 1)..], kv.Value);
        }
    }

    /// <summary>What a drop-off is, for the dialog where its SCU is typed: "Waste for Patch City".</summary>
    string DropLabel(string key)
    {
        var bar = key.IndexOf('|');
        if (_log == null || bar <= 0 || !_log.Missions.TryGetValue(key[..bar], out var m)) return "Drop-off";
        var place = m.ObjZone.TryGetValue(key[(bar + 1)..], out var zone) ? LocationName(zone) : "a station";
        return (m.OutCargo ?? "Cargo") + " for " + place;
    }

    /// <summary>
    /// Asks the SCU of drop-offs the game did not tell the size of (the "? SCU" of the Cargo column): type what the contract says, and the program works out the
    /// containers. Saved with your data; the game's own number wins once it writes it. 0 clears it.
    /// </summary>
    void AskScu(List<string> keys)
    {
        using var f = new Form
        {
            Text = "SCU of the load",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            Font = Font,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12),
        };
        var table = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill };
        var intro = new Label
        {
            Text = "The game has not said how many SCU this is. Type the SCU from the contract, and the program works out the containers.",
            AutoSize = true,
            MaximumSize = new Size(380, 0),
            Margin = new Padding(0, 0, 0, 10),
        };
        table.Controls.Add(intro, 0, 0);
        table.SetColumnSpan(intro, 2);
        var inputs = new List<NumericUpDown>();
        for (var i = 0; i < keys.Count; i++)
        {
            table.Controls.Add(new Label { Text = DropLabel(keys[i]) + ":", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 10, 6) }, 0, i + 1);
            var input = new NumericUpDown { Minimum = 0, Maximum = 10000, Width = 90, Value = _data.TypedScu.GetValueOrDefault(keys[i]) };
            inputs.Add(input);
            table.Controls.Add(input, 1, i + 1);
        }
        var ok = new Button { Text = "Save", AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        var bar = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 0) };
        bar.Controls.Add(cancel);
        bar.Controls.Add(ok);
        table.Controls.Add(bar, 0, keys.Count + 1);
        table.SetColumnSpan(bar, 2);
        f.Controls.Add(table);
        f.AcceptButton = ok;
        f.CancelButton = cancel;
        ApplyTheme(f);
        if (f.ShowDialog(this) != DialogResult.OK) return;

        for (var i = 0; i < keys.Count; i++)
        {
            var typed = (int)inputs[i].Value;
            if (typed > 0) _data.TypedScu[keys[i]] = typed;
            else _data.TypedScu.Remove(keys[i]);
        }
        Storage.Save(_data);
        BeginInvoke(new Action(() =>
        {
            _renderedVersion = -1;   // the logs are merged again, with the SCU you typed
            _planShown = "";
            _planCore = null;
            RefreshFromLog();
            RefreshPlanner();
        }));
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
                res.Add(new DropBatch { Mission = m, Objective = kv.Key, Zone = kv.Value, Station = StationOf(kv.Value), Scu = m.ObjScu.GetValueOrDefault(kv.Key) });
            }
        }
        return res;
    }

    /// <summary>
    /// Where a worker is after he delivered the load of a from-Ruin mission he holds: the station of his latest completed drop-off. It only counts until he picks up
    /// something or hands in a run after it (a run in progress knows where it is by itself), and while the delivery is recent. Null = nothing to go by.
    /// </summary>
    (string Station, DateTime Time)? DropStartOf(string name)
    {
        var best = LastDropOf(name);
        if (_log == null || best == null || best.Value.Station == null || best.Value.Station == PyroDistances.Ruin) return null;
        if (_log.LastTime - best.Value.Time > TimeSpan.FromHours(3)) return null;
        var at = best.Value.Time;
        var moved = _trips.Any(t => SameName(t.Worker, name) && ((t.End ?? DateTime.MinValue) > at || t.Pickups.Any(p => p.Time > at)));
        return moved ? null : (best.Value.Station!, at);
    }

    /// <summary>
    /// The latest drop-off of a from-Ruin load that this worker delivered himself: the station (null = a place that is not in the distance table) and when.
    /// A shared mission is in the log of everyone who has it, and a delivery shows up in all of those logs, so who delivered it comes from the loading platform
    /// lines (see WorkerTripBuilder.OutSteps): only the one who did it is placed at that station. What nobody's log shows is not counted.
    /// </summary>
    (string? Station, DateTime Time)? LastDropOf(string name)
    {
        if (_log == null) return null;
        (string? Station, DateTime Time)? best = null;
        foreach (var s in WorkerTripBuilder.OutSteps(_log))
        {
            if (s.Pickup || s.Worker == null || !SameName(s.Worker, name)) continue;
            if (!s.Mission.ObjZone.TryGetValue(s.Objective, out var zone)) continue;
            if (best == null || s.Time > best.Value.Time) best = (StationOf(zone), s.Time);
        }
        return best;
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
            var keys = g.Select(d => d.Mission.Id + "|" + d.Objective).ToList();
            var done = 0;
            while (done < scus.Count)
            {
                var take = Math.Min(capacity, scus.Count - done);
                var part = scus.Skip(done).Take(take).ToList();
                var partKeys = keys.Skip(done).Take(take).ToList();
                var scu = part.Sum();
                done += take;
                // a ship that holds fewer SCU than the load cannot take it (a worker without a ship set is assumed to manage)
                var who = allowed.Length == 0 ? flyers : allowed.ToList();
                var fits = scu <= 0 ? who : who.Where(n => ShipOf(n) is not { } ship || ship.Total >= scu).ToList();
                var set = fits.Count == 0 || fits.Count == flyers.Count ? allowed : fits.ToArray();
                chunks.Add(new DropChunk { Station = g.Key.Station, Cargo = mission.OutCargo ?? "Cargo", Batches = take, Scu = scu, Parts = part, Keys = partKeys, Allowed = set });
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
