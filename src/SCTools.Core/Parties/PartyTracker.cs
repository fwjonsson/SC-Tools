namespace SCTools.Parties;

/// <summary>
/// The party tracker: who plays with whom, now and before, from the party lines of everybody's log (on the server). It is the one place that knows the
/// parties: the Party tool, the Hauling Planner (who is active, its party, the payouts), the contracts, the Gauntlet Tracker, the Companion and who may see
/// whom all ask its answer (PartyBook), worked out once per refresh (LiveTracker.Parties).
///
/// The game writes the in-game party in its notifications ("zzatvo has joined the party.", "Bileh123 has left the party." also when he quits or his game
/// crashes, "zzoega is now party leader.", "The party has been disbanded.", "zzatvo connected.", "Bileh123 disconnected."), and a tracker uploads its
/// player's log while his game runs: the log grows all the time (at least every few minutes), so a log that has been quiet for fifteen minutes is a game that
/// ended (about as long as the game keeps a crashed player in his party). A member is active while his own game runs, or while somebody who is playing has
/// him in his party: a player whose tracker is off, or who has none, is still seen through the others' logs. Somebody who is gone for less than fifteen
/// minutes (a crash, a relog) stays active.
/// A player's party only changes when the game says so: he joins or makes a party, somebody joins or leaves it (when the game says he left, he is out of the
/// party in his own log too), it is disbanded. A crash or a relog keeps it: the game comes back to the same party and does not always say so again (zzatvo's
/// crash on 28 Oct: nothing about the party in anybody's log, and still in it in the game). Only a game started more than an hour after his last one ended
/// starts alone, until the game says otherwise.
/// A party made from an invite names nobody: the one who invited reads "You have created party", the one who accepted "You have joined party", and neither
/// log says who the other is (Frank's logs, 17 Sep 2026). Those two lines in two logs within fifteen seconds of each other (the PCs' clocks differ by a few
/// seconds) are one party: 343 of the 384 such pairs in the three accounts' logs of 4 Aug to 30 Oct 2026 were less than ten seconds apart.
/// </summary>
public static class PartyTracker
{
    /// <summary>
    /// A game whose log has not grown for this long has ended (the logs show the game writes something at least every five minutes or so; the game keeps a
    /// crashed player in his party about this long, Frank 28 Oct).
    /// </summary>
    public static readonly TimeSpan Quiet = TimeSpan.FromMinutes(15);

    /// <summary>Gone for less than this (a crash, a relog) does not count: he stays active.</summary>
    public static readonly TimeSpan Blip = TimeSpan.FromMinutes(15);

    /// <summary>A game started this soon after his last one ended (a crash, a relog) is in the same party as that one was.</summary>
    public static readonly TimeSpan Rejoin = TimeSpan.FromHours(1);

    /// <summary>"You have created party" and "You have joined party" in two logs this close together are one party (the invite was accepted).</summary>
    public static readonly TimeSpan Pairing = TimeSpan.FromSeconds(15);

    /// <summary>
    /// The history is written when the logs have been quiet this long, so the same change told by every log a few seconds apart (the PCs' clocks differ) is
    /// one change, at the time of the first log that told it.
    /// </summary>
    public static readonly TimeSpan Settle = TimeSpan.FromSeconds(20);

    static readonly StringComparer Same = StringComparer.OrdinalIgnoreCase;

    /// <summary>Works out from the logs (one per tracker) the parties now and before, and when every player in members was active.</summary>
    /// <param name="members">the players whose activity is wanted (their names are written as here, in the parties too)</param>
    public static PartyBook Work(IEnumerable<HaulingLog> logs, IReadOnlyCollection<string> members, DateTime nowUtc)
    {
        var book = new PartyBook { Now = nowUtc };
        // everything that happened, in order: a game started (0), a notification about the party (1), a game ended (2)
        var points = new List<(DateTime Time, int Order, int Index, string Who, PartyEvent? Event)>();
        foreach (var log in logs)
        {
            var who = log.Player ?? log.Uploader;
            if (string.IsNullOrWhiteSpace(who)) continue;
            book.WithLog.Add(who);
            foreach (var run in log.GameRuns.Values)
            {
                points.Add((run.Start, 0, points.Count, who, null));
                Seen(book, who, run.Last);
                // quit, or quiet for fifteen minutes (a crash): it ended with its last line
                if (run.Quit || nowUtc - run.Last >= Quiet) points.Add((run.Last, 2, points.Count, who, null));
            }
            foreach (var e in log.PartyEvents)
            {
                points.Add((e.Time, 1, points.Count, who, e));
                if (e.Name != null) Seen(book, e.Name, e.Time);
            }
        }
        points.Sort((a, b) => a.Time != b.Time ? a.Time.CompareTo(b.Time) : a.Order != b.Order ? a.Order.CompareTo(b.Order) : a.Index.CompareTo(b.Index));

        var s = new State(members);
        var raw = s.Raw;
        var now = raw.Keys.ToDictionary(m => m, _ => false, Same);
        var history = new History(book, s);
        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            history.Begin(p.Time);
            if (p.Order == 0)
            {
                // a new game long after the last one: in no party until the game says so; soon after it (a crash, a relog): in the party it was in
                if (!s.Playing(p.Who) && !(s.Ended.TryGetValue(p.Who, out var end) && p.Time - end <= Rejoin)) s.Alone(p.Who);
                s.Games[p.Who] = s.Games.GetValueOrDefault(p.Who) + 1;
            }
            else if (p.Order == 2)
            {
                s.Games[p.Who] = Math.Max(0, s.Games.GetValueOrDefault(p.Who) - 1);
                if (!s.Playing(p.Who)) s.Ended[p.Who] = p.Time;   // his party stays as it was: only the game says when it changes
            }
            else
            {
                var e = p.Event!;
                switch (e.Kind)
                {
                    case PartyEventKind.Self:   // he joined a party, or made one: one with the player who did the other half within seconds
                        s.Alone(p.Who);
                        s.PartyOf(p.Who, e.Time);
                        s.Pair(p.Who, e.Time);
                        break;
                    case PartyEventKind.Joined:
                        s.PartyOf(p.Who, e.Time).Add(e.Name!);
                        break;
                    case PartyEventKind.Connected:
                        // only a member of his party is said to connect: that tells about a player without a tracker (Bileh123) when no line said he joined
                        // (he was in the party before this player came); a player with a tracker is playing by his own log, and his activity stays as it was
                        if (!book.WithLog.Contains(e.Name!)) s.PartyOf(p.Who, e.Time).Add(e.Name!);
                        history.Back(e.Name!, e.Time);
                        break;
                    case PartyEventKind.Leader:
                        s.PartyOf(p.Who, e.Time).Add(e.Name!);
                        s.Leaders[p.Who] = (e.Name!, e.Time);
                        break;
                    case PartyEventKind.Left:
                        if (s.Party.TryGetValue(p.Who, out var mine)) mine.Remove(e.Name!);
                        if (s.Leaders.TryGetValue(p.Who, out var l) && Same.Equals(l.Name, e.Name)) s.Leaders.Remove(p.Who);
                        // he left this player's party: in his own log too (his own game does not always say it), until the game puts him in a party again
                        if (!Same.Equals(e.Name, p.Who) && s.Party.TryGetValue(e.Name!, out var his) && his.Contains(p.Who)) s.Alone(e.Name!);
                        break;
                    case PartyEventKind.Disbanded:
                        s.Alone(p.Who);
                        history.Disbanded(p.Who);
                        break;
                    case PartyEventKind.Disconnected:   // his game went out; he is in the party until the game says he left
                        history.Dropped(e.Name!, e.Time);
                        break;
                }
            }
            foreach (var m in raw.Keys)
            {
                var a = s.Active(m);
                if (a == now[m]) continue;
                now[m] = a;
                raw[m].Add((p.Time, a));
            }
            if (i + 1 == points.Count || points[i + 1].Time - p.Time >= Settle) history.Settled();
        }

        foreach (var (m, changes) in raw)
        {
            // gone for less than fifteen minutes, or gone for less than fifteen minutes until now: not gone
            var kept = new List<(DateTime Time, bool Active)>();
            for (var i = 0; i < changes.Count; i++)
            {
                if (!changes[i].Active && (i + 1 < changes.Count ? changes[i + 1].Time : nowUtc) - changes[i].Time < Blip)
                {
                    i++;   // and the change back
                    continue;
                }
                kept.Add(changes[i]);
            }
            book.Changes[m] = kept;
        }

        book.Groups.AddRange(s.Groups());
        history.Finish(nowUtc);
        return book;
    }

    static void Seen(PartyBook book, string name, DateTime at)
    {
        if (!book.LastSeen.TryGetValue(name, out var was) || at > was) book.LastSeen[name] = at;
    }

    /// <summary>The parties as the logs have told them so far, while the points are gone through.</summary>
    sealed class State
    {
        public State(IReadOnlyCollection<string> members) =>
            Raw = members.Distinct(Same).ToDictionary(m => m, _ => new List<(DateTime Time, bool Active)>(), Same);

        /// <summary>The members, with their changes of active as they come (before the short gaps are taken out).</summary>
        public Dictionary<string, List<(DateTime Time, bool Active)>> Raw { get; }

        public Dictionary<string, int> Games { get; } = new(Same);                       // the games running, by player
        public Dictionary<string, HashSet<string>> Party { get; } = new(Same);           // who is in the party of the player of a log, as his game told him (himself too)
        public Dictionary<string, (string Name, DateTime At)> Leaders { get; } = new(Same);
        public Dictionary<string, DateTime> Joined { get; } = new(Same);                 // when the player of a log came into his party (the first one there made it)
        public Dictionary<string, DateTime> Ended { get; } = new(Same);                  // when the last game of the player of a log ended

        // which party of his a player of a log is in (a new one every time his game puts him in a party), when it began with "You have created / joined
        // party", and the parties of two players that are one by those lines
        readonly Dictionary<string, int> _turn = new(Same);
        readonly Dictionary<string, (DateTime At, int Turn)> _self = new(Same);
        readonly List<(string A, int TurnA, string B, int TurnB)> _pairs = new();

        public bool Playing(string who) => Games.TryGetValue(who, out var n) && n > 0;

        public bool Active(string member) => Playing(member) || Party.Any(kv => Playing(kv.Key) && kv.Value.Contains(member));

        public HashSet<string> PartyOf(string who, DateTime at)
        {
            if (Party.TryGetValue(who, out var p)) return p;
            Joined.TryAdd(who, at);
            _turn[who] = _turn.GetValueOrDefault(who) + 1;
            return Party[who] = new HashSet<string>(Same) { who };
        }

        public void Alone(string who)
        {
            Party.Remove(who);
            Leaders.Remove(who);
            Joined.Remove(who);
        }

        /// <summary>He made or joined a party now: one with another player's that was made or joined within seconds of it.</summary>
        public void Pair(string who, DateTime at)
        {
            var turn = _turn[who];
            foreach (var (other, self) in _self)
            {
                if (Same.Equals(other, who) || at - self.At > Pairing || _turn.GetValueOrDefault(other) != self.Turn || !Party.ContainsKey(other)) continue;
                _pairs.Add((who, turn, other, self.Turn));
            }
            _self[who] = (at, turn);
        }

        /// <summary>The parties now: everybody playing, together with everybody somebody playing has in his party, and the pairs made at the same time.</summary>
        public List<PartyGroup> Groups()
        {
            var root = new Dictionary<string, string>(Same);
            string Find(string x)
            {
                if (!root.TryGetValue(x, out var r)) return root[x] = x;
                return Same.Equals(r, x) ? x : root[x] = Find(r);
            }
            void Union(string a, string b) => root[Find(a)] = Find(b);
            foreach (var who in Games.Keys.Where(Playing)) Find(who);
            foreach (var (who, names) in Party)
            {
                if (!Playing(who)) continue;
                foreach (var n in names) Union(n, who);
            }
            foreach (var (a, turnA, b, turnB) in _pairs)
            {
                // both still in the party those lines began, and both playing
                if (_turn.GetValueOrDefault(a) == turnA && _turn.GetValueOrDefault(b) == turnB && Party.ContainsKey(a) && Party.ContainsKey(b)
                    && Playing(a) && Playing(b)) Union(a, b);
            }
            string Written(string name) => Raw.Keys.FirstOrDefault(m => Same.Equals(m, name)) ?? name;   // as in the members
            var groups = new List<PartyGroup>();
            foreach (var g in root.Keys.ToList().GroupBy(Find, Same))
            {
                var names = g.Select(Written).Distinct(Same).OrderBy(n => n, Same).ToList();
                // its leader: the one the game named last, else the first who was in it (who made it), else the first by name
                var named = g.Where(Leaders.ContainsKey).Select(o => Leaders[o]).OrderBy(x => x.At).LastOrDefault().Name;
                var first = g.Where(Joined.ContainsKey).OrderBy(o => Joined[o]).FirstOrDefault();
                groups.Add(new PartyGroup(Written(named ?? first ?? names[0]), names));
            }
            groups.Sort((a, b) => Same.Compare(a.Leader, b.Leader));
            return groups;
        }
    }

    /// <summary>
    /// The history of the parties: whenever the logs have settled (see Settle), the parties of two or more then are matched with the ones before (the one
    /// with the most of the same players goes on), and what changed is written in their news, at the time of the first point since the last settle. As for
    /// who is active, a gap of less than fifteen minutes does not count (Blip): a party that ended and is back that soon goes on (a member's relog, a new
    /// log file), and a member who left and is back, or a leader who was replaced and is back, that soon has no news of it.
    /// </summary>
    sealed class History
    {
        readonly PartyBook _book;
        readonly State _s;
        readonly List<PartyRecord> _open = new();
        readonly List<PartyRecord> _ended = new();   // the parties that ended less than fifteen minutes ago and were not disbanded: they may go on
        readonly HashSet<string> _disbanded = new(Same);
        DateTime? _since;

        public History(PartyBook book, State s)
        {
            _book = book;
            _s = s;
        }

        /// <summary>A point is gone through: the first since the last settle is the time of what changes.</summary>
        public void Begin(DateTime at) => _since ??= at;

        /// <summary>The game of this player told him his party was disbanded.</summary>
        public void Disbanded(string who) => _disbanded.Add(who);

        /// <summary>The others' games say his game went out: once, in the party he is in.</summary>
        public void Dropped(string name, DateTime at)
        {
            var party = _open.FirstOrDefault(r => r.Names.Contains(name, Same));
            if (party == null || Last(party, name) == PartyNewsKind.Dropped) return;
            party.News.Add(new PartyNews(at, PartyNewsKind.Dropped, Written(name)));
        }

        /// <summary>His game is back after it went out.</summary>
        public void Back(string name, DateTime at)
        {
            var party = _open.FirstOrDefault(r => r.Names.Contains(name, Same));
            if (party != null && Last(party, name) == PartyNewsKind.Dropped) party.News.Add(new PartyNews(at, PartyNewsKind.Back, Written(name)));
        }

        static PartyNewsKind? Last(PartyRecord party, string name) =>
            party.News.LastOrDefault(n => n.Kind is PartyNewsKind.Dropped or PartyNewsKind.Back && Same.Equals(n.Name, name))?.Kind;

        string Written(string name) => _s.Raw.Keys.FirstOrDefault(m => Same.Equals(m, name)) ?? name;

        /// <summary>The logs are quiet: the parties as they are now are written into the history.</summary>
        public void Settled()
        {
            var at = _since ?? DateTime.MinValue;
            _since = null;
            var left = _open.ToList();
            _ended.RemoveAll(r => at - r.Ended >= Blip);
            PartyRecord? Best(List<PartyRecord> from, PartyGroup g, int least) =>
                from.Select(r => (Party: r, Shared: r.Names.Count(n => g.Names.Contains(n, Same)), Leader: Same.Equals(r.Leader, g.Leader)))
                    .Where(x => x.Shared >= least).OrderByDescending(x => x.Shared).ThenByDescending(x => x.Leader).Select(x => x.Party).FirstOrDefault();
            foreach (var g in _s.Groups().Where(g => g.Names.Count > 1).OrderByDescending(g => g.Names.Count))
            {
                var best = Best(left, g, 1);
                if (best == null && Best(_ended, g, 2) is { } back)
                {
                    // ended less than fifteen minutes ago with two of these players or more: it goes on
                    _ended.Remove(back);
                    back.Ended = null;
                    _open.Add(back);
                    best = back;
                }
                else if (best != null) left.Remove(best);
                if (best == null)
                {
                    var made = new PartyRecord(_book.History.Count + 1, at, g.Leader, g.Names);
                    foreach (var n in g.Names.OrderBy(n => Same.Equals(n, g.Leader) ? 0 : 1)) made.News.Add(new PartyNews(at, PartyNewsKind.Joined, n));
                    made.News.Add(new PartyNews(at, PartyNewsKind.Leader, g.Leader));
                    _book.History.Add(made);
                    _open.Add(made);
                    continue;
                }
                foreach (var n in g.Names.Where(n => !best.Names.Contains(n, Same))) Add(best, new PartyNews(at, PartyNewsKind.Joined, n));
                foreach (var n in best.Names.Where(n => !g.Names.Contains(n, Same)))
                {
                    best.News.Add(new PartyNews(at, PartyNewsKind.Left, n));
                    Seen(_book, n, at);
                }
                if (!Same.Equals(best.Leader, g.Leader)) Add(best, new PartyNews(at, PartyNewsKind.Leader, g.Leader));
                best.Names = g.Names.ToList();
                best.Leader = g.Leader;
            }
            foreach (var r in left)
            {
                r.Ended = at;
                r.Disbanded = r.Names.Any(_disbanded.Contains);
                _open.Remove(r);
                if (!r.Disbanded) _ended.Add(r);
                foreach (var n in r.Names) Seen(_book, n, at);
            }
            _disbanded.Clear();
        }

        /// <summary>
        /// A member joins or a leader is named: when the same member left, or the same leader was replaced, less than fifteen minutes before, that news goes
        /// instead (he was not gone).
        /// </summary>
        static void Add(PartyRecord party, PartyNews news)
        {
            if (news.Kind == PartyNewsKind.Joined)
            {
                var last = party.News.FindLastIndex(n => n.Kind is PartyNewsKind.Joined or PartyNewsKind.Left && Same.Equals(n.Name, news.Name));
                if (last >= 0 && party.News[last].Kind == PartyNewsKind.Left && news.Time - party.News[last].Time < Blip)
                {
                    party.News.RemoveAt(last);
                    return;
                }
            }
            else if (news.Kind == PartyNewsKind.Leader)
            {
                var leaders = party.News.Select((n, i) => (News: n, Index: i)).Where(x => x.News.Kind == PartyNewsKind.Leader).ToList();
                if (leaders.Count >= 2 && Same.Equals(leaders[^2].News.Name, news.Name) && news.Time - leaders[^1].News.Time < Blip)
                {
                    party.News.RemoveAt(leaders[^1].Index);
                    return;
                }
            }
            party.News.Add(news);
        }

        /// <summary>Everybody in a party going on now is seen now.</summary>
        public void Finish(DateTime nowUtc)
        {
            foreach (var n in _open.SelectMany(r => r.Names)) Seen(_book, n, nowUtc);
        }
    }
}
