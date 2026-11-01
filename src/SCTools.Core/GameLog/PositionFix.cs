using System.Globalization;
using System.Text.RegularExpressions;

namespace SCTools.GameLog;

/// <summary>
/// Where the player was at a moment, to the metre: the game's /showlocation command puts "Coordinates: x:-43498588585.727356 y:-18397314648.922775
/// z:33676.674324" on the clipboard (the position in the system's own frame, in metres, the star at 0, 0, 0; the frame the map's places are in), and the
/// tracker sends it (Frank, 1 Nov 2026). The game writes it nowhere in its log. X, Y and Z in metres; which system it is in, his log says.
/// </summary>
public sealed record PositionFix(DateTime Time, double X, double Y, double Z)
{
    static readonly Regex Coordinates = new(
        @"^\s*Coordinates:\s*x:\s*(?<x>-?\d+(?:\.\d+)?)\s+y:\s*(?<y>-?\d+(?:\.\d+)?)\s+z:\s*(?<z>-?\d+(?:\.\d+)?)\s*$", RegexOptions.Compiled);

    /// <summary>The fix in a text that is exactly the game's /showlocation result, at this time; null for any other text.</summary>
    public static PositionFix? Parse(string? text, DateTime timeUtc)
    {
        if (text == null || text.Length > 200) return null;
        var m = Coordinates.Match(text);
        if (!m.Success) return null;
        static double D(Group g) => double.Parse(g.Value, CultureInfo.InvariantCulture);
        return new PositionFix(timeUtc, D(m.Groups["x"]), D(m.Groups["y"]), D(m.Groups["z"]));
    }

    /// <summary>Where it is on the map's flat system, in Gm (the map's places are in Gm).</summary>
    public (double X, double Y) Gm => (X / 1e9, Y / 1e9);
}
