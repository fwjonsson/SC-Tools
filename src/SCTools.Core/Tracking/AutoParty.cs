namespace SCTools.Tracking;

/// <summary>A party in the game now: its leader and everybody in it (a player who plays alone is a party of one).</summary>
public sealed record PartyGroup(string Leader, IReadOnlyList<string> Names)
{
    /// <summary>"zzoega's party", or the name alone for a player who plays alone.</summary>
    public string Title => Names.Count > 1 ? Leader + "'s party" : Leader;
}

/// <summary>
/// Who of the party is playing, and with whom, from the logs (on the server). The game writes the in-game party in its notifications ("zzatvo has joined the
/// party.", "Bileh123 has left the party." also when he quits or his game crashes, "zzoega is now party leader.", "The party has been disbanded."), and a
/// tracker uploads its player's log while his game runs: the log grows all the time (at least every few minutes), so a log that has been quiet for ten minutes
/// is a game that ended. A member is active while his own game runs, or while somebody who is playing has him in his party: a player whose tracker is off is
/// still seen through the others' logs. Somebody who is gone for less than ten minutes (a crash, a relog) stays active.
/// A player's party only changes when the game says so: he joins or makes a party, somebody joins or leaves it (when the game says he left, he is out of the
/// party in his own log too), it is disbanded. A crash or a relog keeps it: the game comes back to the same party and does not always say so again (zzatvo's
/// crash on 28 Oct: nothing about the party in anybody's log, and still in it in the game). Only a game started more than an hour after his last one ended
/// starts alone, until the game says otherwise (it says "You have joined party" when it puts him back in his party then).
/// Apply writes it to the party changes with its times (Source "logs"), so the payouts and the runs go by it as they went by the Active tick. In or Out set by
/// the admin on the Party page holds until the logs change their mind about that member.
/// </summary>
public static class AutoParty
{
    /// <summary>A game whose log has not grown for this long has ended (the logs show the game writes something at least every five minutes or so).</summary>
    public static readonly TimeSpan Quiet = TimeSpan.FromMinutes(10);

    /// <summary>Gone for less than this (a crash, a relog) does not count: he stays active.</summary>
    public static readonly TimeSpan Blip = TimeSpan.FromMinutes(10);

    /// <summary>A game started this soon after his last one ended (a crash, a relog) is in the same party as that one was.</summary>
    public static readonly TimeSpan Rejoin = TimeSpan.FromHours(1);

    static readonly StringComparer Same = StringComparer.OrdinalIgnoreCase;

    /// <summary>What the logs say.</summary>
    public sealed class Result
    {
        /// <summary>Per member: when he became active and when not, in order (he was not active before the first one).</summary>
        public Dictionary<string, List<(DateTime Time, bool Active)>> Changes { get; } = new(Same);

        /// <summary>The parties now: everybody playing (also players who are not in the party table), each party with its leader.</summary>
        public List<PartyGroup> Groups { get; } = new();

        /// <summary>The party this player is in now, or null when he is not playing.</summary>
        public PartyGroup? GroupOf(string name) => Groups.FirstOrDefault(g => g.Names.Any(n => Same.Equals(n, name)));

        /// <summary>What the logs say about this member at this time.</summary>
        public bool ActiveAt(string member, DateTime utc)
        {
            var active = false;
            if (!Changes.TryGetValue(member, out var changes)) return false;
            foreach (var c in changes)
            {
                if (c.Time > utc) break;
                active = c.Active;
            }
            return active;
        }
    }

    /// <summary>Works out from the logs (one per tracker) when every member was active, and the parties now.</summary>
    public static Result Work(IEnumerable<HaulingLog> logs, IReadOnlyCollection<string> members, DateTime nowUtc)
    {
        // everything that happened, in order: a game started (0), a notification about the party (1), a game ended (2)
        var points = new List<(DateTime Time, int Order, int Index, string Who, PartyEvent? Event)>();
        foreach (var log in logs)
        {
            var who = log.Player ?? log.Uploader;
            if (string.IsNullOrWhiteSpace(who)) continue;
            foreach (var run in log.GameRuns.Values)
            {
                points.Add((run.Start, 0, points.Count, who, null));
                if (nowUtc - run.Last >= Quiet) points.Add((run.Last, 2, points.Count, who, null));   // quiet for ten minutes: it ended with its last line
            }
            foreach (var e in log.PartyEvents) points.Add((e.Time, 1, points.Count, who, e));
        }
        points.Sort((a, b) => a.Time != b.Time ? a.Time.CompareTo(b.Time) : a.Order != b.Order ? a.Order.CompareTo(b.Order) : a.Index.CompareTo(b.Index));

        var playing = new Dictionary<string, int>(Same);                 // the games running, by player
        var party = new Dictionary<string, HashSet<string>>(Same);       // who is in the party of the player of a log, as his game told him (himself too)
        var leader = new Dictionary<string, (string Name, DateTime At)>(Same);
        var joined = new Dictionary<string, DateTime>(Same);             // when the player of a log came into his party (the first one there made it)
        var ended = new Dictionary<string, DateTime>(Same);              // when the last game of the player of a log ended
        bool Playing(string who) => playing.TryGetValue(who, out var n) && n > 0;
        bool Active(string member) => Playing(member) || party.Any(kv => Playing(kv.Key) && kv.Value.Contains(member));
        HashSet<string> PartyOf(string who, DateTime at)
        {
            if (party.TryGetValue(who, out var p)) return p;
            joined.TryAdd(who, at);
            return party[who] = new HashSet<string>(Same) { who };
        }
        void Alone(string who)
        {
            party.Remove(who);
            leader.Remove(who);
            joined.Remove(who);
        }

        var raw = members.Distinct(Same).ToDictionary(m => m, _ => new List<(DateTime Time, bool Active)>(), Same);
        var now = raw.Keys.ToDictionary(m => m, _ => false, Same);
        foreach (var p in points)
        {
            if (p.Order == 0)
            {
                // a new game long after the last one: in no party until the game says so; soon after it (a crash, a relog): in the party it was in
                if (!Playing(p.Who) && !(ended.TryGetValue(p.Who, out var end) && p.Time - end <= Rejoin)) Alone(p.Who);
                playing[p.Who] = playing.GetValueOrDefault(p.Who) + 1;
            }
            else if (p.Order == 2)
            {
                playing[p.Who] = Math.Max(0, playing.GetValueOrDefault(p.Who) - 1);
                if (!Playing(p.Who)) ended[p.Who] = p.Time;   // his party stays as it was: only the game says when it changes
            }
            else
            {
                var e = p.Event!;
                switch (e.Kind)
                {
                    case PartyEventKind.Self:   // he joined a party, or made one
                        Alone(p.Who);
                        PartyOf(p.Who, e.Time);
                        break;
                    case PartyEventKind.Joined:
                        PartyOf(p.Who, e.Time).Add(e.Name!);
                        break;
                    case PartyEventKind.Leader:
                        PartyOf(p.Who, e.Time).Add(e.Name!);
                        leader[p.Who] = (e.Name!, e.Time);
                        break;
                    case PartyEventKind.Left:
                        if (party.TryGetValue(p.Who, out var mine)) mine.Remove(e.Name!);
                        if (leader.TryGetValue(p.Who, out var l) && Same.Equals(l.Name, e.Name)) leader.Remove(p.Who);
                        // he left this player's party: in his own log too (his own game does not always say it), until the game puts him in a party again
                        if (!Same.Equals(e.Name, p.Who) && party.TryGetValue(e.Name!, out var his) && his.Contains(p.Who)) Alone(e.Name!);
                        break;
                    case PartyEventKind.Disbanded:
                        Alone(p.Who);
                        break;
                }
            }
            foreach (var m in raw.Keys)
            {
                var a = Active(m);
                if (a == now[m]) continue;
                now[m] = a;
                raw[m].Add((p.Time, a));
            }
        }

        var result = new Result();
        foreach (var (m, changes) in raw)
        {
            // gone for less than ten minutes, or gone for less than ten minutes until now: not gone
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
            result.Changes[m] = kept;
        }

        // the parties now: everybody playing, together with everybody somebody playing has in his party
        var root = new Dictionary<string, string>(Same);
        string Find(string x)
        {
            if (!root.TryGetValue(x, out var r)) return root[x] = x;
            return Same.Equals(r, x) ? x : root[x] = Find(r);
        }
        void Union(string a, string b) => root[Find(a)] = Find(b);
        foreach (var who in playing.Keys.Where(Playing)) Find(who);
        foreach (var (who, names) in party)
        {
            if (!Playing(who)) continue;
            foreach (var n in names) Union(n, who);
        }
        string Written(string name) => raw.Keys.FirstOrDefault(m => Same.Equals(m, name)) ?? name;   // as in the party table
        foreach (var g in root.Keys.ToList().GroupBy(Find, Same))
        {
            var names = g.Select(Written).Distinct(Same).OrderBy(n => n, Same).ToList();
            // its leader: the one the game named last, else the first who was in it (who made it), else the first by name
            var named = g.Where(leader.ContainsKey).Select(o => leader[o]).OrderBy(x => x.At).LastOrDefault().Name;
            var first = g.Where(joined.ContainsKey).OrderBy(o => joined[o]).FirstOrDefault();
            result.Groups.Add(new PartyGroup(Written(named ?? first ?? names[0]), names));
        }
        result.Groups.Sort((a, b) => Same.Compare(a.Leader, b.Leader));
        return result;
    }

    /// <summary>
    /// Writes what the logs say into the party changes, from the time the logs took over (AutoPartySince, set the first time: then every member follows them).
    /// The logs' changes are made again every time (only where they change something); the admin's In or Out stays and holds until the logs change their
    /// mind about him; back to Auto takes what the logs said then. The members active now are the ones shown as active (InactiveParty). True when anything
    /// changed.
    /// </summary>
    public static bool Apply(AppData data, Result r, DateTime nowUtc)
    {
        var changed = false;
        if (data.AutoPartySince == null)
        {
            data.AutoPartySince = nowUtc;
            data.PartyTrackingSince ??= nowUtc;
            foreach (var m in data.Party)
                data.PartyChanges.Add(new PartyChange { Name = m, TimeUtc = nowUtc, Active = r.ActiveAt(m, nowUtc), Source = PartyChange.BackToLogs });
            changed = true;
        }
        var since = data.AutoPartySince.Value;
        bool Member(string name) => data.Party.Any(m => Same.Equals(m, name));

        // everything but the logs' own changes of the members stays as it is
        var result = data.PartyChanges.Where(c => c.Source != PartyChange.FromLogs || !Member(c.Name)).ToList();
        foreach (var m in data.Party)
        {
            var mine = result.Where(c => Same.Equals(c.Name, m)).OrderBy(c => c.TimeUtc).ToList();
            foreach (var c in mine.Where(c => c.Source == PartyChange.BackToLogs))
            {
                var logs = r.ActiveAt(m, c.TimeUtc);
                if (c.Active == logs) continue;
                c.Active = logs;
                changed = true;
            }
            // how he was when the logs took over, and every change of the logs after that, where it changes what he is then
            var wanted = new List<(DateTime Time, bool Active)> { (since, r.ActiveAt(m, since)) };
            if (r.Changes.TryGetValue(m, out var logChanges)) wanted.AddRange(logChanges.Where(c => c.Time > since));
            foreach (var (time, active) in wanted)
            {
                if (StateAt(mine, time, data, m) == active) continue;
                var c = new PartyChange { Name = m, TimeUtc = time, Active = active, Source = PartyChange.FromLogs };
                var at = mine.FindIndex(x => x.TimeUtc > time);
                mine.Insert(at < 0 ? mine.Count : at, c);   // after the changes at the same time: it is the one that counts then
                result.Add(c);
            }
        }
        result = result.OrderBy(c => c.TimeUtc).ToList();   // stable: changes at the same time keep their order
        if (!result.Select(Key).SequenceEqual(data.PartyChanges.Select(Key)))
        {
            data.PartyChanges = result;
            changed = true;
        }

        var inactive = data.Party.Where(m => !StateAt(data.PartyChanges.Where(c => Same.Equals(c.Name, m)).OrderBy(c => c.TimeUtc).ToList(), nowUtc, data, m)).ToList();
        if (!inactive.OrderBy(n => n, Same).SequenceEqual(data.InactiveParty.OrderBy(n => n, Same), Same))
        {
            data.InactiveParty = inactive;
            changed = true;
        }
        return changed;
    }

    static (string, DateTime, bool, string?) Key(PartyChange c) => (c.Name.ToLowerInvariant(), c.TimeUtc, c.Active, c.Source);

    /// <summary>Active at this time by these changes (in order), as Crew.IsActiveAt: before the first one he was the opposite of it.</summary>
    static bool StateAt(List<PartyChange> changes, DateTime utc, AppData data, string member)
    {
        if (changes.Count == 0) return !data.InactiveParty.Any(x => Same.Equals(x, member));
        PartyChange? last = null;
        foreach (var c in changes)
        {
            if (c.TimeUtc <= utc) last = c;
            else break;
        }
        return last != null ? last.Active : !changes[0].Active;
    }
}
