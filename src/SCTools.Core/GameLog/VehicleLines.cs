using System.Text.RegularExpressions;

namespace SCTools.GameLog;

/// <summary>
/// The ship a player flies, from the quantum travel lines of his log that are about his own ship: he picked a destination, the route was worked out, his
/// drive arrived, his ship's navigation looked for its route. They name the ship by the game's class and its id ("| CRUS_Starlifter_C2_891702459996[891702459996]|",
/// "RSI_Perseus_...", "ARGO_MOLE_..."). Not the "Local Route Guard - Server Rerouted" lines: the game writes one for every ship that comes near him (zzoega's
/// log of 28 Oct: a Hornet, a Sabre, a Clipper, a MOLE, a Cutlass, a Constellation... within an hour, while he flew his Perseus). The trackers send those lines
/// already.
/// </summary>
public static class VehicleLines
{
    static readonly Regex Vehicle = new(@"\|\s*(?<v>[A-Za-z][A-Za-z0-9_]*?)_(?<id>\d{6,})\[\k<id>\]\s*\|", RegexOptions.Compiled);

    /// <summary>What the game does with the player's own ship in the lines that name it.</summary>
    static readonly string[] OwnShip =
    {
        "OnPlayerSelectedQuantumTarget", "CSCItemNavigation::CalculateRoute", "OnQuantumDriveArrived", "GetStarmapRouteSegmentData",
    };

    /// <summary>The class of the player's own ship in a quantum travel line ("CRUS_Starlifter_C2"), or null (another ship, or no ship).</summary>
    public static string? VehicleOf(string line)
    {
        if (!line.Contains("[QuantumTravel]", StringComparison.Ordinal)) return null;
        if (!OwnShip.Any(o => line.Contains(o, StringComparison.Ordinal))) return null;
        var m = Vehicle.Match(line);
        return m.Success ? m.Groups["v"].Value : null;
    }

    /// <summary>The class for people: "CRUS Starlifter C2".</summary>
    public static string Pretty(string vehicle) => vehicle.Replace('_', ' ');

    static string[] Words(string s) =>
        s.ToLowerInvariant().Split(new[] { ' ', '_', '-', '.', '\'', '(', ')' }, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// How well a ship's name fits a class: the words of the class after its maker ("Starlifter", "C2") that are in the name ("C2 Hercules Starlifter"), or
    /// that a word of the name starts with. 0: it does not fit.
    /// </summary>
    public static int Fit(string vehicle, string shipName)
    {
        var words = Words(vehicle);
        var name = Words(shipName);
        return words.Skip(1).Count(w => name.Any(n => n == w || (w.Length >= 3 && n.StartsWith(w, StringComparison.Ordinal))));
    }

    /// <summary>The ship of these that fits the class best (the first of the best), or -1 when none fits.</summary>
    public static int Best(string vehicle, IReadOnlyList<string> shipNames)
    {
        var best = -1;
        var score = 0;
        for (var i = 0; i < shipNames.Count; i++)
        {
            var s = Fit(vehicle, shipNames[i]);
            if (s > score)
            {
                score = s;
                best = i;
            }
        }
        return best;
    }
}
