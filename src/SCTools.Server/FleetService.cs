using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SCTools.Server.Data;

namespace SCTools.Server;

/// <summary>A ship of a fleet as the pages show it: the row, its drive, how much faster the drive is when crafted (percent), and the hauler it is (or null).</summary>
public sealed record FleetShip(FleetShipRow Row, QtDrive? Drive, double Bonus, Ship? Hauler)
{
    /// <summary>"TS-2 S3 +20 %".</summary>
    public string DriveText => Drive == null ? "" : Drive.Label + (Bonus == 0 ? "" : " " + QuantumDrives.PercentText(Bonus));

    /// <summary>Its drive speed, crafted as it is (m/s), or null without a drive.</summary>
    public double? Speed => Drive?.Crafted(Bonus).Speed;
}

/// <summary>
/// The ship a player flies now: the one he picked, or Automatic (the ship of his latest quantum jump, as the logs name it, matched to his fleet: Ship is null
/// when it is not in it). Vehicle and SeenUtc: what the logs say, when they say something.
/// </summary>
public sealed record ActiveShip(FleetShip? Ship, bool Picked, string? Vehicle, DateTime? SeenUtc)
{
    /// <summary>The ship's name, or what the logs call it when it is not in the fleet, or "".</summary>
    public string Text => Ship?.Row.Ship ?? (Vehicle == null ? "" : VehicleLines.Pretty(Vehicle));
}

/// <summary>
/// The fleets: every player's ships (My page), each with its quantum drive and the quality of a crafted drive, and the ship he flies now (picked, or the
/// one of his latest quantum jump). The Hauling Planner flies the active ship: its name, its drive and how much faster the drive is go to the planner
/// (SyncPlanner), so the party table, the capacities and the flying times follow the fleets. Kept in memory as well, so the planner can be kept up to date
/// every few seconds without the database.
/// </summary>
public sealed class FleetService
{
    /// <summary>The most ships one player can have.</summary>
    public const int MaxShips = 100;

    /// <summary>The longest name of a ship.</summary>
    public const int MaxName = 60;

    readonly DataStore _store;
    readonly LogUploads _uploads;
    readonly LiveTracker _live;
    readonly object _lock = new();
    List<FleetShipRow>? _ships;
    Dictionary<string, int>? _picked;

    public FleetService(DataStore store, LogUploads uploads, LiveTracker live)
    {
        _store = store;
        _uploads = uploads;
        _live = live;
    }

    void Load()
    {
        if (_ships != null) return;
        using var db = _store.Open();
        _ships = db.FleetShips.AsNoTracking().OrderBy(s => s.Id).ToList();
        _picked = db.ActiveShips.AsNoTracking().ToDictionary(a => a.Player, a => a.FleetShipId, StringComparer.OrdinalIgnoreCase);
    }

    static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    static FleetShip Describe(FleetShipRow r)
    {
        var drive = QuantumDrives.Find(r.Drive);
        var bonus = drive != null && r.Quality is int q ? QuantumDrives.CraftedSpeedPercent(q) : 0;
        // a hauler by its name, or by the name in it ("Crusader C2 Hercules Starlifter" is the C2 Hercules Starlifter)
        var hauler = Ships.Find(r.Ship) ?? Ships.All.Where(s => r.Ship.Contains(s.Name, StringComparison.OrdinalIgnoreCase)).OrderByDescending(s => s.Name.Length).FirstOrDefault();
        return new FleetShip(r, drive, bonus, hauler);
    }

    /// <summary>A player's ships, in the order he added them.</summary>
    public List<FleetShip> Fleet(string player)
    {
        lock (_lock)
        {
            Load();
            return _ships!.Where(s => Same(s.Player, player)).Select(Describe).ToList();
        }
    }

    /// <summary>The players who have a fleet.</summary>
    public List<string> Players()
    {
        lock (_lock)
        {
            Load();
            return _ships!.Select(s => s.Player).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    /// <summary>The name of a ship as typed (trimmed), or null when it is empty or too long.</summary>
    static string? ShipName(string? typed) => typed?.Trim() is { Length: > 0 and <= MaxName } name ? name : null;

    /// <summary>A drive as picked: (true, its key), (true, null) for none, (false, _) when it is not a drive.</summary>
    static (bool Ok, string? Key) DriveKey(string? typed) =>
        string.IsNullOrWhiteSpace(typed) ? (true, null) : QuantumDrives.Find(typed.Trim()) is { } d ? (true, d.Key) : (false, null);

    /// <summary>A quality as typed: (true, it) from 0 to 1000, (true, null) when empty, (false, _) otherwise.</summary>
    static (bool Ok, int? Quality) QualityOf(string? typed)
    {
        if (string.IsNullOrWhiteSpace(typed)) return (true, null);
        return int.TryParse(typed.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var q) && q >= 0 && q <= QuantumDrives.MaxQuality ? (true, q) : (false, null);
    }

    /// <summary>Adds a ship to a player's fleet. The sentence for the page when something is wrong, else null.</summary>
    public string? Add(string player, string? ship, string? drive, string? quality, DateTime nowUtc)
    {
        if (ShipName(ship) is not { } name) return "Type the ship's name (at most " + MaxName + " letters).";
        var (driveOk, driveKey) = DriveKey(drive);
        if (!driveOk) return "That is not one of the quantum drives.";
        var (qualityOk, q) = QualityOf(quality);
        if (!qualityOk) return "The quality of a crafted drive is a whole number from 0 to 1000 (empty: not crafted).";
        lock (_lock)
        {
            Load();
            if (_ships!.Count(s => Same(s.Player, player)) >= MaxShips) return "That is a big fleet: at most " + MaxShips + " ships.";
            using var db = _store.Open();
            var row = new FleetShipRow { Player = player, Ship = name, Drive = driveKey, Quality = q, AddedUtc = nowUtc };
            db.FleetShips.Add(row);
            db.SaveChanges();
            _ships.Add(row);
        }
        return null;
    }

    /// <summary>Changes one field of one of his ships (ship, drive, quality). The sentence for the page when it is not saved, else null.</summary>
    public string? Set(string player, int id, string field, string? value)
    {
        lock (_lock)
        {
            Load();
            var row = _ships!.FirstOrDefault(s => s.Id == id && Same(s.Player, player));
            if (row == null) return "That ship is not in your fleet.";
            switch (field)
            {
                case "ship":
                    if (ShipName(value) is not { } name) return "Type the ship's name (at most " + MaxName + " letters).";
                    row.Ship = name;
                    break;
                case "drive":
                    var (driveOk, driveKey) = DriveKey(value);
                    if (!driveOk) return "That is not one of the quantum drives.";
                    row.Drive = driveKey;
                    break;
                case "quality":
                    var (qualityOk, q) = QualityOf(value);
                    if (!qualityOk) return "The quality of a crafted drive is a whole number from 0 to 1000 (empty: not crafted).";
                    row.Quality = q;
                    break;
                default:
                    return "That cannot be changed.";
            }
            using var db = _store.Open();
            db.FleetShips.Update(row);
            db.SaveChanges();
            return null;
        }
    }

    /// <summary>Takes a ship out of his fleet (and his pick of it). False when it is not his.</summary>
    public bool Remove(string player, int id)
    {
        lock (_lock)
        {
            Load();
            var row = _ships!.FirstOrDefault(s => s.Id == id && Same(s.Player, player));
            if (row == null) return false;
            using var db = _store.Open();
            db.FleetShips.Remove(db.FleetShips.Find(id)!);
            if (_picked!.TryGetValue(player, out var picked) && picked == id)
            {
                foreach (var a in db.ActiveShips.ToList().Where(a => Same(a.Player, player))) db.ActiveShips.Remove(a);
                foreach (var k in _picked.Keys.Where(k => Same(k, player)).ToList()) _picked.Remove(k);
            }
            db.SaveChanges();
            _ships.Remove(row);
            return true;
        }
    }

    /// <summary>The ship he flies: one of his fleet, or null for Automatic. False when it is not his.</summary>
    public bool Pick(string player, int? id)
    {
        lock (_lock)
        {
            Load();
            if (id is int i && !_ships!.Any(s => s.Id == i && Same(s.Player, player))) return false;
            using var db = _store.Open();
            var mine = db.ActiveShips.ToList().Where(a => Same(a.Player, player)).ToList();
            foreach (var k in _picked!.Keys.Where(k => Same(k, player)).ToList()) _picked.Remove(k);
            if (id is int pick)
            {
                if (mine.FirstOrDefault() is { } had) had.FleetShipId = pick;
                else db.ActiveShips.Add(new ActiveShipRow { Player = player, FleetShipId = pick });
                foreach (var extra in mine.Skip(1)) db.ActiveShips.Remove(extra);
                _picked[player] = pick;
            }
            else
            {
                foreach (var a in mine) db.ActiveShips.Remove(a);
            }
            db.SaveChanges();
            return true;
        }
    }

    /// <summary>The ship he flies now: the one he picked, else the one of his latest quantum jump (matched to his fleet by its name).</summary>
    public ActiveShip Active(string player)
    {
        var fleet = Fleet(player);
        int? picked;
        lock (_lock) picked = _picked!.TryGetValue(player, out var p) ? p : null;
        (string Vehicle, DateTime At)? seen = _uploads.Ships().TryGetValue(player, out var s) ? s : null;
        if (picked is int id && fleet.FirstOrDefault(f => f.Row.Id == id) is { } chosen) return new ActiveShip(chosen, true, seen?.Vehicle, seen?.At);
        if (seen == null) return new ActiveShip(null, false, null, null);
        var best = VehicleLines.Best(seen.Value.Vehicle, fleet.Select(f => f.Row.Ship).ToList());
        return new ActiveShip(best >= 0 ? fleet[best] : null, false, seen.Value.Vehicle, seen.Value.At);
    }

    /// <summary>
    /// The Hauling Planner flies the active ships: for every player with a fleet whose active ship is known, its name (when it is one of the haulers; else
    /// none: he is not hauling in it), its drive and how much faster the drive is go to the planner, where they differ. True when anything changed.
    /// </summary>
    public bool SyncPlanner(DateTime nowUtc)
    {
        var wanted = new List<(string Player, string? Ship, string? Drive, double Bonus)>();
        foreach (var player in Players())
        {
            var active = Active(player);
            if (active.Ship is not { } ship) continue;
            wanted.Add((player, ship.Hauler?.Name, ship.Drive?.Key, ship.Bonus));
        }
        if (wanted.Count == 0) return false;
        var differ = _live.Read(t => wanted.Where(w =>
            t.Crew.ShipOf(w.Player)?.Name != w.Ship || t.Crew.DriveOf(w.Player)?.Key != w.Drive || t.Crew.DriveBonusOf(w.Player) != w.Bonus).ToList());
        if (differ.Count == 0) return false;
        _live.Edit(t =>
        {
            foreach (var w in differ)
            {
                var name = t.State.Data.Party.FirstOrDefault(n => Same(n, w.Player)) ?? w.Player;   // as written in the party table
                t.Crew.SetShip(name, w.Ship);
                t.Crew.SetDrive(name, w.Drive);
                t.Crew.SetDriveBonus(name, w.Bonus);
            }
        }, nowUtc);
        return true;
    }
}
