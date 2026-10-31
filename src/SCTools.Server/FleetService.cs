using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SCTools.Server.Data;

namespace SCTools.Server;

/// <summary>
/// A ship of a fleet as the pages show it: the row, its drive (the one he picked, else the ship's stock drive), how much faster the drive is when crafted
/// (percent), the hauler it is (or null), the ship of the game data it is (or null), and how much more fuel per distance the drive burns when crafted
/// (percent; less is better).
/// </summary>
public sealed record FleetShip(FleetShipRow Row, QtDrive? Drive, double Bonus, Ship? Hauler, ShipModel? Model, double FuelPercent = 0)
{
    /// <summary>The ship's stock drive (the game data's), or null.</summary>
    public QtDrive? StockDrive => QuantumDrives.Find(Model?.Drive);

    /// <summary>The size of quantum drive the ship takes (its stock drive's), or null for a ship the site does not know the drive of.</summary>
    public int? DriveSize => StockDrive?.Size;

    /// <summary>No drive picked: the stock drive is the one.</summary>
    public bool Stock => Row.Drive == null && Drive != null;

    /// <summary>"TS-2 S3 +20 %", "Kama S3 (stock)".</summary>
    public string DriveText => Drive == null ? "" : Drive.Label + (Bonus == 0 ? "" : " " + QuantumDrives.PercentText(Bonus)) + (Stock ? " (stock)" : "");

    /// <summary>Its drive speed, crafted as it is (m/s), or null without a drive.</summary>
    public double? Speed => Drive?.Crafted(Bonus).Speed;

    /// <summary>
    /// "611.66 Mm/s (+20 %)": its drive speed as the game writes it ("Quantum Speed: 509.72 Mm/s") and how much faster crafting made it, or "" without a
    /// drive. The page's script writes the same while the quality is changed (Views/Me/Index.cshtml), so the two must agree.
    /// </summary>
    public string SpeedText => Speed is not { } mps ? "" : (mps / 1e6).ToString("0.00", CultureInfo.InvariantCulture) + " Mm/s" + Change(Bonus);

    /// <summary>Its drive's fuel burn per km, crafted as it is, or null when the game data has none for the drive.</summary>
    public double? FuelPerKm => Drive is { FuelRate: > 0 } d ? d.FuelPerKm * (1 + FuelPercent / 100) : null;

    /// <summary>"113.34 per km (-20 %)": its drive's fuel burn as the game writes it ("Quantum Fuel Burn: 141.68 per Km") and how much crafting changed it; "" when not known.</summary>
    public string FuelText => FuelPerKm is not { } f ? "" : f.ToString("0.00", CultureInfo.InvariantCulture) + " per km" + Change(FuelPercent);

    static string Change(double percent) => QuantumDrives.PercentText(percent) is { Length: > 0 } t ? " (" + t + ")" : "";
}

/// <summary>
/// The ship a player flies now: the one he picked, or Automatic (the ship of his latest quantum jump, as the logs name it, matched to his fleet: Ship is null
/// when it is not in it). Vehicle and SeenUtc: what the logs say, when they say something.
/// </summary>
public sealed record ActiveShip(FleetShip? Ship, bool Picked, string? Vehicle, DateTime? SeenUtc)
{
    /// <summary>The ship's name, or what the logs call it when it is not in the fleet (the game data's name for its class), or "".</summary>
    public string Text => Ship?.Row.Ship ?? (Vehicle == null ? "" : ShipModels.OfClass(Vehicle)?.Name ?? VehicleLines.Pretty(Vehicle));
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
        var model = ShipModels.Find(r.Ship);
        var drive = QuantumDrives.Find(r.Drive) ?? QuantumDrives.Find(model?.Drive);   // none picked: the stock drive
        var bonus = drive != null && r.Quality is double q ? QuantumDrives.CraftedSpeedPercent(q) : 0;
        var fuel = drive != null && r.FuelQuality is double fq ? QuantumDrives.CraftedFuelPercent(fq) : 0;
        // a hauler by its name, or by the name in it ("Crusader C2 Hercules Starlifter" is the C2 Hercules Starlifter)
        var hauler = Ships.Find(r.Ship) ?? Ships.All.Where(s => r.Ship.Contains(s.Name, StringComparison.OrdinalIgnoreCase)).OrderByDescending(s => s.Name.Length).FirstOrDefault();
        return new FleetShip(r, drive, bonus, hauler, model, fuel);
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

    /// <summary>The size of quantum drive a ship takes (its stock drive's in the game data), or null when the site does not know.</summary>
    public static int? DriveSizeOf(string ship) => QuantumDrives.Find(ShipModels.Find(ship)?.Drive)?.Size;

    /// <summary>The sentence for the page when this drive does not fit this ship (another size than its stock drive's), else null.</summary>
    static string? Misfit(string ship, string? driveKey)
    {
        if (driveKey == null || QuantumDrives.Find(driveKey) is not { } d || DriveSizeOf(ship) is not { } size || d.Size == size) return null;
        return "A " + (ShipModels.Find(ship)?.Name ?? ship) + " takes a size " + size + " quantum drive: " + d.Label + " is size " + d.Size + ".";
    }

    /// <summary>A drive as picked: (true, its key), (true, null) for none, (false, _) when it is not a drive.</summary>
    static (bool Ok, string? Key) DriveKey(string? typed) =>
        string.IsNullOrWhiteSpace(typed) ? (true, null) : QuantumDrives.Find(typed.Trim()) is { } d ? (true, d.Key) : (false, null);

    /// <summary>What the page says when a quality typed is not one.</summary>
    public const string BadQuality = "The quality of a crafted drive is a number from 0 to 1000 with one decimal at most, like 786.6 (500: as bought).";

    /// <summary>
    /// A quality as typed: (true, it) from 0 to 1000, rounded to one decimal ("786.6"; a decimal comma is taken too), (true, null) when empty (as bought),
    /// (false, _) otherwise.
    /// </summary>
    static (bool Ok, double? Quality) QualityOf(string? typed)
    {
        if (string.IsNullOrWhiteSpace(typed)) return (true, null);
        if (!double.TryParse(typed.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var q) || double.IsNaN(q)) return (false, null);
        q = Math.Round(q, 1, MidpointRounding.AwayFromZero);
        return q >= 0 && q <= QuantumDrives.MaxQuality ? (true, q) : (false, null);
    }

    /// <summary>A quality as the page's field has it: "786.6", "500" (as bought when none is kept).</summary>
    public static string QualityText(double? quality) => (quality ?? QuantumDrives.MaxQuality / 2.0).ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>Adds a ship to a player's fleet. The sentence for the page when something is wrong, else null.</summary>
    public string? Add(string player, string? ship, string? drive, string? quality, DateTime nowUtc, string? fuelQuality = null)
    {
        if (ShipName(ship) is not { } name) return "Type the ship's name (at most " + MaxName + " letters).";
        var (driveOk, driveKey) = DriveKey(drive);
        if (!driveOk) return "That is not one of the quantum drives.";
        var (qualityOk, q) = QualityOf(quality);
        var (fuelOk, fq) = QualityOf(fuelQuality);
        if (!qualityOk || !fuelOk) return BadQuality;
        if (Misfit(name, driveKey) is { } misfit) return misfit;
        lock (_lock)
        {
            Load();
            if (_ships!.Count(s => Same(s.Player, player)) >= MaxShips) return "That is a big fleet: at most " + MaxShips + " ships.";
            using var db = _store.Open();
            var row = new FleetShipRow { Player = player, Ship = name, Drive = driveKey, Quality = q, FuelQuality = fq, AddedUtc = nowUtc };
            db.FleetShips.Add(row);
            db.SaveChanges();
            _ships.Add(row);
        }
        return null;
    }

    /// <summary>Changes one field of one of his ships (ship, drive, quality, fuel: the fuel efficiency quality). The sentence for the page when it is not saved, else null.</summary>
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
                    if (Misfit(name, row.Drive) != null) row.Drive = null;   // another ship, another size: its stock drive until one is picked
                    break;
                case "drive":
                    var (driveOk, driveKey) = DriveKey(value);
                    if (!driveOk) return "That is not one of the quantum drives.";
                    if (Misfit(row.Ship, driveKey) is { } wrong) return wrong;
                    row.Drive = driveKey;
                    break;
                case "quality":
                    var (qualityOk, q) = QualityOf(value);
                    if (!qualityOk) return BadQuality;
                    row.Quality = q;
                    break;
                case "fuel":
                    var (fuelOk, fq) = QualityOf(value);
                    if (!fuelOk) return BadQuality;
                    row.FuelQuality = fq;
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

    /// <summary>
    /// The quantum fuel burn per km of his ship of this class of the game ("RSI_Perseus", as the logs name it), crafted as it is, or null when his fleet
    /// does not have that ship or the game data no fuel rate for its drive. The location tracker places a mission's beacon by it (BeaconRanging).
    /// </summary>
    public double? FuelPerKmOf(string player, string shipClass)
    {
        if (ShipModels.OfClass(shipClass) is not { } model) return null;
        return Fleet(player).FirstOrDefault(f => f.Model == model)?.FuelPerKm;
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
        // the ship of his fleet that is the class's ship of the game data, else the one whose name fits the class best
        var model = ShipModels.OfClass(seen.Value.Vehicle);
        var best = model == null ? -1 : fleet.FindIndex(f => f.Model == model);
        if (best < 0) best = VehicleLines.Best(seen.Value.Vehicle, fleet.Select(f => f.Row.Ship).ToList());
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
