namespace HaulingTracker.Tracking;

/// <summary>
/// The contracts of the session that are still going, and who of the party has each one (it was shared with him) or took it. What the logs say, unless a box
/// was clicked on the Contracts tab; and the contracts that were marked as gone.
/// </summary>
public sealed class Contracts
{
    readonly TrackerState _s;
    readonly Places _places;
    readonly Crew _crew;

    public Contracts(TrackerState state, Places places, Crew crew)
    {
        _s = state;
        _places = places;
        _crew = crew;
    }

    AppData Data => _s.Data;

    /// <summary>The missions of the selected session that are still going, oldest first.</summary>
    public List<Mission> ActiveMissions() =>
        _s.Current == null ? new List<Mission>() : _s.Current.Missions.Where(m => m.EndState == null).OrderBy(m => m.Accepted ?? m.FirstSeen ?? DateTime.MaxValue).ThenBy(m => m.Id, StringComparer.Ordinal).ToList();

    /// <summary>The missions still going and the ones marked as gone (so they can be brought back), oldest first. The from-Ruin contracts come with the newest session.</summary>
    public List<Mission> ContractRows() =>
        _s.Current == null ? new List<Mission>() : _s.Current.Missions.Where(m => m.EndState == null).Concat(_s.Current.Failed.Where(m => m.Dropped)).Concat(OutContracts())
            .OrderBy(m => m.Accepted ?? m.FirstSeen ?? DateTime.MaxValue).ThenBy(m => m.Id, StringComparer.Ordinal).ToList();

    /// <summary>
    /// The from-Ruin contracts (waste) still going, and the ones marked as gone: the same ones the Trip Planner plans (OpenDrops), so only with the newest session,
    /// and not one that was accepted more than 12 hours ago and never ended.
    /// </summary>
    public List<Mission> OutContracts()
    {
        if (_s.Log == null || _s.Current == null || _s.Sessions.Count == 0 || _s.Current.Key != _s.Sessions[^1].Key) return new List<Mission>();
        var log = _s.Log;
        return log.Missions.Values.Where(m => m.IsOut && (m.EndState == null || m.Dropped)
            && !((m.Accepted ?? m.FirstSeen) is { } seen && log.LastTime - seen > TimeSpan.FromHours(12))).ToList();
    }

    /// <summary>The name of a contract in the tables, the same as the rows of the rewards table: "3 To Ruin", "2 From Ruin".</summary>
    public static string ContractName(Mission m) => m.IsOut ? m.OutDrops + " From Ruin" : m.Multi > 0 ? m.Multi + " To Ruin" : "?";

    /// <summary>The Stops cell of a contract: its pickups (✓ = picked up), or for a from-Ruin contract its drop-offs with their SCU (✓ = delivered).</summary>
    public string ContractStopsText(Mission m)
    {
        if (!m.IsOut) return string.Join(",  ", SessionBuilder.StopsOf(m).Select(s => _places.LocationName(s.Zone) + (s.PickedUp ? " ✓" : "")));
        return string.Join(",  ", m.ObjZone.Where(kv => kv.Key.StartsWith("dropoff_")).OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv =>
        {
            var scu = m.ObjScu.GetValueOrDefault(kv.Key);
            var delivered = m.Events.TryGetValue(kv.Key, out var ev) && ev.Any(e => e.State == "COMPLETED");
            return _places.LocationName(kv.Value) + (scu > 0 ? " " + scu + " SCU" : "") + (delivered ? " ✓" : "");
        }));
    }

    /// <summary>The missions that were marked as gone count as abandoned everywhere (planner, session, payouts), unless the logs ended them already.</summary>
    public void ApplyDroppedMissions(HaulingLog log)
    {
        foreach (var kv in Data.DroppedMissions)
        {
            if (!log.Missions.TryGetValue(kv.Key, out var m) || m.EndState != null) continue;
            m.EndState = "DROPPED";
            m.EndedAt = kv.Value;
            m.Dropped = true;
        }
    }

    /// <summary>Marks a mission as gone (it is not in the game any more, but the logs never said so), or brings back one that was marked.</summary>
    public void ToggleGone(Mission m)
    {
        if (m.Dropped) Data.DroppedMissions.Remove(m.Id);
        else Data.DroppedMissions[m.Id] = DateTime.UtcNow;
    }

    /// <summary>The id of a player the logs name only by number: the one party member without a log (when there is exactly one such id and member).</summary>
    public string? PlayerIdOf(string member)
    {
        var log = _s.Log;
        if (log == null) return null;
        var named = log.IdNames.FirstOrDefault(kv => SameName(kv.Value, member));
        if (named.Key != null) return named.Key;
        var unnamed = log.MemberEvents.Select(e => e.Player).Distinct().Where(id => !log.IdNames.ContainsKey(id)).ToList();
        var withoutLog = Data.Party.Where(n => !log.Players.Any(p => SameName(p, n))).ToList();
        return unnamed.Count == 1 && withoutLog.Count == 1 && SameName(withoutLog[0], member) ? unnamed[0] : null;
    }

    /// <summary>What the logs say: does this party member have the mission? A member with a log has it when his log has it (not ended it); one without a log (nobody can read his game) unless the others were told that he left it.</summary>
    public bool LogSaysHas(Mission m, string member, DateTime? at = null)
    {
        var log = _s.Log;
        if (log == null) return true;
        if (log.Players.Any(p => SameName(p, member))) return m.Holders.Any(h => SameName(h, member));
        var id = PlayerIdOf(member);
        // nothing tells when it was shared with him: he is assumed to have it while he is in the party (click his box when he does not), and not when he is not
        var inParty = at.HasValue ? _crew.IsActiveAt(member, at.Value) : !Data.InactiveParty.Any(x => SameName(x, member));
        if (id == null) return inParty;
        var last = log.MemberEvents.Where(e => e.Mission == m.Id && e.Player == id).OrderBy(e => e.Time).LastOrDefault();
        return last == null ? inParty : !last.Left;
    }

    /// <summary>Does this member have the mission: what was ticked on the Contracts tab, else what the logs say.</summary>
    public bool HasMission(Mission m, string member, DateTime? at = null)
    {
        if (Data.MissionHolders.TryGetValue(m.Id, out var ticked))
        {
            foreach (var kv in ticked)
            {
                if (SameName(kv.Key, member)) return kv.Value;
            }
        }
        return LogSaysHas(m, member, at);
    }

    /// <summary>A box was clicked: the choice is kept (so it wins over the logs) until it is the same as what the logs say.</summary>
    public void SetHolder(Mission m, string member, bool has)
    {
        if (!Data.MissionHolders.TryGetValue(m.Id, out var ticked)) Data.MissionHolders[m.Id] = ticked = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        if (has == LogSaysHas(m, member)) ticked.Remove(member);
        else ticked[member] = has;
        if (ticked.Count == 0) Data.MissionHolders.Remove(m.Id);
    }
}
