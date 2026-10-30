namespace SCTools.Tracking;

/// <summary>
/// The Hauling Planner's side of the party tracker (PartyTracker): who of its party is active, which is one thing only, "the planner gives this member routes"
/// (Frank, 30 Oct 2026; never who gets money: the payouts go by who had the contract). The logs decide it: Apply writes what the party tracker says into the
/// party changes with their times (Source "logs"), which are the Active ticks of the Party panel and the admin's Party page. In or Out set by the admin there
/// holds until the logs change their mind about that member.
/// </summary>
public static class AutoParty
{
    static readonly StringComparer Same = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// Writes what the logs say into the party changes, from the time the logs took over (AutoPartySince, set the first time: then every member follows them).
    /// The logs' changes are made again every time (only where they change something); the admin's In or Out stays and holds until the logs change their
    /// mind about him; back to Auto takes what the logs said then. The members active now are the ones shown as active (InactiveParty). True when anything
    /// changed.
    /// </summary>
    public static bool Apply(AppData data, PartyBook r, DateTime nowUtc)
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
