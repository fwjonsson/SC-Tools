namespace SCTools.Boarding;

/// <summary>
/// The ship tracker: who is in whose ship, and who flies it, from everybody's log (on the server). It is the one place that knows the ships the players are
/// in: the location tracker (a rider goes where his pilot flies; a jump burns its owner's fuel), the fleets (the ship and drive a player flies now are the
/// owner's, and a rider is crew of the pilot's ship in the Hauling Planner), the Map and the Party pages ("zzoega's RSI Perseus") ask its answer
/// (ShipBook), worked out once per refresh (LiveTracker.Ships).
///
/// The clues: the voice channel a player comes into when he boards a ship names the ship and its owner (ShipChannel, in his own log; the owner's log does
/// not say who boarded); the quantum jump lines name the ship by its class and id ("RSI_Perseus_898683539008"): the pick in the log of whoever flies it,
/// the arrival in everybody's aboard (when the game writes it). A jump line of a ship whose class fits the name of the channel he is in tells whose ship
/// that id is. The owner is not always the pilot: zzoega leaves his Perseus to steal a Freelancer full of gold and zzatvo flies the Perseus on (5 Oct
/// 2026, Frank 31 Oct); zzatvo flew one of zzoega's ships on 23 days of Aug-Oct 2026, zzoii on 13. So a rider rides with the ship and whoever flies it,
/// and the ship's drive is its owner's. Ships carry ships: zzatvo parks zzoega's Ursa in the Perseus, zzoega's Pisces docks in zzatvo's Carrack;
/// climbing out of the docked Pisces said nothing, the Carrack's channel stayed theirs (17 Oct 2026).
/// </summary>
public static class ShipTracker
{
    /// <summary>The jump lines this old and newer tell whose ship an id is (a ship keeps its id while it is out; the next day it is mostly another).</summary>
    public static readonly TimeSpan Age = TimeSpan.FromDays(1);

    /// <summary>Works out whose ship every id of the last day is, who flew each last, and the ship everybody playing (PartyTracker.Quiet) is in now.</summary>
    /// <param name="games">every game in each player's logs (LogUploads.Games), by uploader</param>
    public static ShipBook Work(IEnumerable<HaulingLog> logs, IReadOnlyDictionary<string, List<(string File, GameRun Run, bool Quit)>> games, DateTime nowUtc)
    {
        var book = new ShipBook { Now = nowUtc };
        var all = logs.Where(l => !string.IsNullOrWhiteSpace(l.Player ?? l.Uploader)).ToList();
        var since = nowUtc - Age;
        var votes = new Dictionary<string, Dictionary<(string Ship, string Owner), int>>(StringComparer.Ordinal);
        var spelled = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // an owner's name as the game writes it
        var classes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var log in all)
        {
            var name = log.Player ?? log.Uploader!;
            var walk = new ChannelWalk(log);
            foreach (var e in log.Qt)
            {
                if (e.Time < since || e.Time > nowUtc || e.ShipId.Length == 0) continue;
                if (!book.LastSeen.TryGetValue(e.ShipId, out var seen) || seen < e.Time) book.LastSeen[e.ShipId] = e.Time;
                if (e.Ship.Length > 0) classes[e.ShipId] = e.Ship;
                if (e.Kind == QtKind.Selected && (!book.Pilots.TryGetValue(e.ShipId, out var p) || p.At <= e.Time)) book.Pilots[e.ShipId] = (name, e.Time);
                // the channel he is in names this ship when its class fits the channel's name (not a Pisces' channel for the Carrack it is docked in)
                if (walk.At(e.Time) is not { } c || VehicleLines.Fit(e.Ship, c.Ship) == 0) continue;
                if (!votes.TryGetValue(e.ShipId, out var v)) votes[e.ShipId] = v = new();
                var key = (c.Ship, c.Owner.ToLowerInvariant());
                v[key] = v.GetValueOrDefault(key) + 1;
                spelled.TryAdd(c.Owner, c.Owner);
            }
        }
        foreach (var (id, v) in votes)
        {
            var best = v.OrderByDescending(kv => kv.Value).First().Key;
            book.Ships[id] = new ShipOwner(best.Ship, spelled.GetValueOrDefault(best.Owner, best.Owner), classes.GetValueOrDefault(id, ""));
        }

        foreach (var log in all)
        {
            var name = log.Player ?? log.Uploader!;
            if (!(games.TryGetValue(log.Uploader ?? name, out var runs) && runs.Any(r => !r.Quit && nowUtc - r.Run.Last < PartyTracker.Quiet))) continue;
            if (Now(book, log, name, nowUtc) is { } aboard) book.Players[name] = aboard;
        }
        return book;
    }

    /// <summary>
    /// The ship he is in now: the one of the channel he is in, unless a jump of his own since he boarded was in another ship (he picked a jump in his own
    /// ship, or in an AI ship he stole, which has no channel; or he arrived in another ship). Without a channel, the ship of his latest arrival when somebody
    /// else picked that jump (he rode it) and the book knows whose ship it is. Null: not in a ship, as far as the logs say.
    /// </summary>
    static Aboard? Now(ShipBook book, HaulingLog log, string name, DateTime nowUtc)
    {
        var start = GameStart(log, nowUtc);
        var own = log.Qt.LastOrDefault(e => e.Time >= start && e.Time <= nowUtc);
        if (HeldAt(log, nowUtc) is { } held)
        {
            var ids = book.IdsOf(held.Ship, held.Owner);
            if (own == null || own.Time < held.Time || own.ShipId.Length == 0 || ids.Contains(own.ShipId))
            {
                var id = ids.FirstOrDefault();
                return new Aboard(name, held.Ship, held.Owner, held.Time, id != null ? book.Ships[id].Vehicle : null, id, PilotOf(book, id, held.Time) ?? held.Owner);
            }
        }
        if (own is { Kind: QtKind.Arrived, ShipId.Length: > 0 } a && book.Ships.TryGetValue(a.ShipId, out var ship)
            && book.Pilots.TryGetValue(a.ShipId, out var p) && !SameName(p.Player, name))
            return new Aboard(name, ship.Ship, ship.Owner, a.Time, ship.Vehicle, a.ShipId, p.Player);
        return null;
    }

    /// <summary>Who picked the latest jump of this ship since this time (he boarded), or null (nobody since: its owner flies it).</summary>
    static string? PilotOf(ShipBook book, string? id, DateTime since) =>
        id != null && book.Pilots.TryGetValue(id, out var p) && p.At >= since ? p.Player : null;

    /// <summary>When the game he was playing at this time started (the start of the latest of his log's games before it), or the earliest time.</summary>
    public static DateTime GameStart(HaulingLog log, DateTime t)
    {
        var start = DateTime.MinValue;
        foreach (var r in log.GameRuns.Values)
            if (r.Start <= t && r.Start > start) start = r.Start;
        return start;
    }

    /// <summary>
    /// The ship channel he is in at this time by his own lines: the newest he came into and was not told he left, in the game he was playing then (a channel
    /// of a game before does not count). Null: none.
    /// </summary>
    public static ShipChannel? HeldAt(HaulingLog log, DateTime t) => new ChannelWalk(log).At(t);

    /// <summary>
    /// A log's channels walked forward in time: the channel he is in at each moment asked, the moments in order (an earlier one answers as the latest).
    /// A new game empties them: the channels were the game's before.
    /// </summary>
    sealed class ChannelWalk
    {
        readonly List<ShipChannel> _channels;
        readonly List<DateTime> _starts;
        readonly List<ShipChannel> _held = new();
        int _c, _s;

        public ChannelWalk(HaulingLog log)
        {
            _channels = log.Channels;
            _starts = log.GameRuns.Values.Select(r => r.Start).OrderBy(s => s).ToList();
        }

        public ShipChannel? At(DateTime t)
        {
            while (true)
            {
                var start = _s < _starts.Count && _starts[_s] <= t ? _starts[_s] : (DateTime?)null;
                var channel = _c < _channels.Count && _channels[_c].Time <= t ? _channels[_c] : null;
                if (start is { } s && (channel == null || s <= channel.Time))
                {
                    _held.Clear();
                    _s++;
                }
                else if (channel != null)
                {
                    _held.RemoveAll(h => h.Ship == channel.Ship && SameName(h.Owner, channel.Owner));
                    if (channel.Joined) _held.Add(channel);
                    _c++;
                }
                else return _held.Count > 0 ? _held[^1] : null;
            }
        }
    }
}
