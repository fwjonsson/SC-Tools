using System.ComponentModel.DataAnnotations;
using System.Globalization;
using SCTools.Server.Editing;

namespace SCTools.Server.Models;

/// <summary>A choice of a list on a page: what is sent, and what is shown.</summary>
public sealed record Choice(string Value, string Text);

/// <summary>
/// My page: what a player sets for himself (where he is now, his colour, his ship and its quantum drive), with the choices, as the Party table and the
/// Trip Planner of the program have them.
/// </summary>
public sealed class MeModel
{
    public string Player { get; set; } = "";
    public bool InParty { get; set; }

    /// <summary>Where the logs say he is (the Automatic choice).</summary>
    public string AutoLocation { get; set; } = "";

    /// <summary>The station he set by hand, or "" for Automatic.</summary>
    public string Location { get; set; } = "";

    /// <summary>His colour, as #rrggbb.</summary>
    public string Color { get; set; } = "";

    /// <summary>The label of his ship, or "".</summary>
    public string Ship { get; set; } = "";

    /// <summary>The label of his quantum drive, or "".</summary>
    public string Drive { get; set; } = "";

    public List<Choice> Stations { get; set; } = new();
    public List<Choice> Ships { get; set; } = new();
    public List<Choice> Drives { get; set; } = new();

    /// <summary>His fleet (FleetService), the ship he flies now, and when that comes from the logs, what they said.</summary>
    public List<FleetShip> Fleet { get; set; } = new();
    public ActiveShip? Active { get; set; }

    /// <summary>The ships to pick a name from (any ship can be typed: the haulers are the ones the Hauling Planner knows the cargo grid of).</summary>
    public List<string> ShipNames { get; set; } = new();

    /// <summary>What he has now, from the tracker (read under its lock).</summary>
    public static MeModel Of(Tracker t, string player)
    {
        var (location, byHand) = t.Whereabouts.LocationOf(player);
        var party = t.State.Data.Party;
        return new MeModel
        {
            Player = party.FirstOrDefault(n => PlayerNames.SameName(n, player)) ?? player,
            InParty = party.Any(n => PlayerNames.SameName(n, player)),
            AutoLocation = PyroDistances.NameOf(t.Whereabouts.AutoLocationOf(player)),
            Location = byHand ? location : "",
            Color = ViewPalette.Hex(Edits.ColorOf(t, player)),
            Ship = t.Crew.ShipOf(player)?.Label ?? "",
            Drive = t.Crew.DriveOf(player)?.Label ?? "",
            Stations = StationChoices(),
            Ships = ShipChoices(),
            Drives = DriveChoices(),
        };
    }

    /// <summary>The ships, as in the Party table of the program.</summary>
    public static List<Choice> ShipChoices() => global::SCTools.GameData.Ships.All.Select(s => new Choice(s.Label, s.Label)).ToList();

    /// <summary>The quantum drives by name and size, as in the Party table of the program.</summary>
    public static List<Choice> DriveChoices() =>
        QuantumDrives.All.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ThenBy(d => d.Size).Select(d => new Choice(d.Label, d.Label)).ToList();

    /// <summary>The stations, Ruin Station first (as on the Trip Planner of the program).</summary>
    public static List<Choice> StationChoices() =>
        PyroDistances.Points.OrderBy(s => s.Id == PyroDistances.Ruin ? 0 : 1).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Select(s => new Choice(s.Id, s.Name)).ToList();

    /// <summary>#rrggbb as 0xRRGGBB, or null when it is not a colour.</summary>
    public static int? ParseColor(string? hex) =>
        hex is { Length: 7 } && hex[0] == '#' && int.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb) ? rgb : null;
}

/// <summary>What My page sends back.</summary>
public sealed class MeForm
{
    /// <summary>A station id, or "" for Automatic.</summary>
    public string? Location { get; set; }

    [Required]
    [RegularExpression("^#[0-9a-fA-F]{6}$", ErrorMessage = "Pick a colour.")]
    public string Color { get; set; } = "";

    public string? Ship { get; set; }
    public string? Drive { get; set; }
}
