using System.Globalization;

namespace HaulingTracker.Tracking;

/// <summary>
/// The party: who is in it, who was active when (every change of Active is saved with its time, so someone can join or leave in the middle of a session and the
/// payouts still know who was there), and the ship, quantum drive, Batch Capacity and colour of every member.
/// </summary>
public sealed class Crew
{
    /// <summary>The batches a ship holds when nothing is set.</summary>
    public const int DefaultShipCapacity = 3;

    /// <summary>The most batches a ship can be set to hold.</summary>
    public const int MaxShipCapacity = 24;

    readonly TrackerState _s;

    public Crew(TrackerState state) => _s = state;

    AppData Data => _s.Data;

    // ------------------------------------------------------------------ who was in the party when

    /// <summary>All names that are or were in the party.</summary>
    public List<string> PartyNamesAll()
    {
        var list = new List<string>(Data.Party);
        foreach (var c in Data.PartyChanges)
        {
            if (!list.Any(x => SameName(x, c.Name))) list.Add(c.Name);
        }
        return list;
    }

    /// <summary>
    /// Was this person active in the party at this time? Without any saved change that is simply the Active tick now.
    /// Before his first saved change he was the opposite of what that change says (a first "joined" means he was not there before).
    /// </summary>
    public bool IsActiveAt(string name, DateTime utc)
    {
        var changes = Data.PartyChanges.Where(c => SameName(c.Name, name)).OrderBy(c => c.TimeUtc).ToList();
        if (changes.Count == 0) return !Data.InactiveParty.Any(x => SameName(x, name));
        PartyChange? last = null;
        foreach (var c in changes)
        {
            if (c.TimeUtc <= utc) last = c;
            else break;
        }
        return last != null ? last.Active : !changes[0].Active;
    }

    /// <summary>The members who were active at this time.</summary>
    public List<string> PartyAt(DateTime utc) => PartyNamesAll().Where(n => IsActiveAt(n, utc)).ToList();

    /// <summary>From this time on the payouts are split by who was in the party at that moment. Earlier missions keep the old split.</summary>
    public DateTime PartyTrackingStart()
    {
        var t = Data.PartyTrackingSince ?? DateTime.UtcNow;
        foreach (var c in Data.PartyChanges)
        {
            if (c.TimeUtc < t) t = c.TimeUtc;
        }
        return t;
    }

    /// <summary>Marks a member active or not from now (or from the time given), and remembers the change (and who made it, see PartyChange.Source).</summary>
    public void SetMemberActive(string name, bool active, DateTime? at = null, string? source = null)
    {
        Data.PartyTrackingSince ??= DateTime.UtcNow;
        Data.PartyChanges.Add(new PartyChange { Name = name, TimeUtc = at ?? DateTime.UtcNow, Active = active, Source = source });
        Data.InactiveParty.RemoveAll(x => SameName(x, name));
        if (!active) Data.InactiveParty.Add(name);
    }

    /// <summary>
    /// What the admin set for this member on the Party page while the logs decide: true (In) or false (Out) while it holds, null when the logs decide (Auto).
    /// It holds until the logs change their mind about him (see AutoParty.Apply).
    /// </summary>
    public bool? OverrideOf(string name)
    {
        var last = Data.PartyChanges.Where(c => SameName(c.Name, name)).OrderBy(c => c.TimeUtc).LastOrDefault();
        return last?.Source == PartyChange.ByAdmin ? last.Active : null;
    }

    /// <summary>The members who are active now.</summary>
    public List<string> ActiveCrew() => Data.Party.Where(n => !Data.InactiveParty.Any(x => SameName(x, n))).ToList();

    /// <summary>Somebody joins the party: a new member, or one who is in the table but not active. False when he is active already.</summary>
    public bool Join(string name)
    {
        var existing = Data.Party.FirstOrDefault(x => SameName(x, name));
        if (existing != null)
        {
            if (IsActiveAt(existing, DateTime.UtcNow)) return false;
            SetMemberActive(existing, true);
            return true;
        }
        Data.Party.Add(name);
        if (!Data.ShipCapacity.ContainsKey(name)) Data.ShipCapacity[name] = DefaultShipCapacity;
        SetMemberActive(name, true);
        return true;
    }

    /// <summary>Takes a member out of the party. His earlier work and debts stay; he just stops getting a share of new payouts.</summary>
    public void Remove(string name)
    {
        if (IsActiveAt(name, DateTime.UtcNow)) SetMemberActive(name, false);
        Data.Party.RemoveAll(x => SameName(x, name));
        Data.InactiveParty.RemoveAll(x => SameName(x, name));
    }

    // ------------------------------------------------------------------ ships, quantum drives, capacity, colour

    /// <summary>The ship set for this worker, or null.</summary>
    public Ship? ShipOf(string name)
    {
        var key = Data.Ship.FirstOrDefault(kv => SameName(kv.Key, name)).Value;
        return Ships.Find(key);
    }

    /// <summary>Sets the ship of a worker (by its label or name; nothing or an unknown ship = none).</summary>
    public void SetShip(string name, string? ship)
    {
        var found = Ships.Find(ship);
        foreach (var k in Data.Ship.Keys.Where(k => SameName(k, name)).ToList()) Data.Ship.Remove(k);
        if (found != null) Data.Ship[name] = found.Name;
    }

    /// <summary>The quantum drive set for this worker, or null.</summary>
    public QtDrive? DriveOf(string name)
    {
        var key = Data.QuantumDrive.FirstOrDefault(kv => SameName(kv.Key, name)).Value;
        return QuantumDrives.Find(key);
    }

    /// <summary>Sets the quantum drive of a worker (by its label or key; nothing or an unknown drive = none).</summary>
    public void SetDrive(string name, string? drive)
    {
        var picked = QuantumDrives.Find(drive);
        foreach (var k in Data.QuantumDrive.Keys.Where(k => SameName(k, name)).ToList()) Data.QuantumDrive.Remove(k);
        if (picked != null) Data.QuantumDrive[name] = picked.Key;
    }

    /// <summary>The drive for the travel time estimates. Without a drive set, a common one is assumed.</summary>
    public QtDrive QtOf(string name) => DriveOf(name) ?? QuantumDrives.Assumed;

    /// <summary>The batches his ship holds (the Trip Planner).</summary>
    public int ShipCapacityOf(string name) =>
        Data.ShipCapacity.TryGetValue(name, out var c) ? Math.Clamp(c, 0, MaxShipCapacity) : DefaultShipCapacity;

    /// <summary>Sets the batches his ship holds from what was typed (a number from 1 to the most; anything else = the default).</summary>
    public void SetShipCapacity(string name, string typed) =>
        Data.ShipCapacity[name] = int.TryParse(typed.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? Math.Clamp(n, 1, MaxShipCapacity)
            : DefaultShipCapacity;

    /// <summary>The colour picked for this worker (ARGB), or null when none was picked.</summary>
    public int? PickedColor(string name)
    {
        var picked = Data.WorkerColors.FirstOrDefault(kv => SameName(kv.Key, name));
        return picked.Key != null ? picked.Value : null;
    }

    /// <summary>Remembers the colour picked for this worker (ARGB).</summary>
    public void SetColor(string name, int argb)
    {
        foreach (var k in Data.WorkerColors.Keys.Where(k => SameName(k, name)).ToList()) Data.WorkerColors.Remove(k);
        Data.WorkerColors[name] = argb;
    }
}
