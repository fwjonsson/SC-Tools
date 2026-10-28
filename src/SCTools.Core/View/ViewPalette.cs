using System.Globalization;

namespace SCTools.View;

/// <summary>
/// The colours of the live web view, light and dark, as 0xRRGGBB: the window and the tables, and the colours that mean something (done, some lost, all lost, on
/// the way, the best). The same as the program's own (UI/Theming/Theme.cs), so the page looks like the program.
/// </summary>
public sealed record ViewPalette(
    int Window, int Cell, int Text, int Muted, int Header, int Calc, int Lines,
    int Red, int Green, int Orange, int Yellow, int Gold)
{
    public static readonly ViewPalette Light = new(
        Window: 0xFFFFFF, Cell: 0xFFFFFF, Text: 0x000000, Muted: 0x696969, Header: Rgb(54, 92, 138), Calc: Rgb(243, 248, 253), Lines: 0x000000,
        Red: Rgb(253, 224, 224), Green: Rgb(226, 244, 226), Orange: Rgb(255, 222, 178), Yellow: Rgb(255, 244, 204), Gold: Rgb(255, 240, 184));

    /// <summary>"Deep space" (picked 28 Oct 2026): a blue-black window, the tables a shade lighter, a brighter blue for the headers.</summary>
    public static readonly ViewPalette Dark = new(
        Window: Rgb(13, 17, 23), Cell: Rgb(21, 27, 35), Text: Rgb(230, 237, 243), Muted: Rgb(139, 148, 158), Header: Rgb(31, 75, 122), Calc: Rgb(27, 37, 51),
        Lines: Rgb(47, 58, 72), Red: Rgb(100, 46, 46), Green: Rgb(29, 68, 48), Orange: Rgb(110, 74, 30), Yellow: Rgb(90, 77, 22), Gold: Rgb(107, 87, 20));

    public static ViewPalette Of(bool dark) => dark ? Dark : Light;

    public static int Rgb(int r, int g, int b) => (r << 16) | (g << 8) | b;

    /// <summary>"#1e1e20".</summary>
    public static string Hex(int rgb) => "#" + (rgb & 0xFFFFFF).ToString("x6", CultureInfo.InvariantCulture);

    /// <summary>A worker's colour half faded into the table colour (a member who is not active, a trip that is not the next one).</summary>
    public int Faded(int c) => Rgb(
        (((c >> 16) & 255) * 55 + ((Cell >> 16) & 255) * 45) / 100,
        (((c >> 8) & 255) * 55 + ((Cell >> 8) & 255) * 45) / 100,
        ((c & 255) * 55 + (Cell & 255) * 45) / 100);

    /// <summary>
    /// How different two colours look (0xRRGGBB): the distance in the Lab colour space (CIE76), where about 2 is just noticeable. Two members whose colours are
    /// closer than TooClose are easily mixed up on the map and in the tables.
    /// </summary>
    public static double Difference(int a, int b)
    {
        var (l1, a1, b1) = Lab(a);
        var (l2, a2, b2) = Lab(b);
        return Math.Sqrt((l1 - l2) * (l1 - l2) + (a1 - a2) * (a1 - a2) + (b1 - b2) * (b1 - b2));
    }

    /// <summary>Colours closer than this (see Difference) are too alike for two members: two greens, two oranges, a red and a dark red.</summary>
    public const double TooClose = 30;

    static (double L, double A, double B) Lab(int rgb)
    {
        static double Linear(int c)
        {
            var v = c / 255.0;
            return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        static double F(double t) => t > 0.008856 ? Math.Cbrt(t) : 7.787 * t + 16.0 / 116;
        var r = Linear((rgb >> 16) & 255);
        var g = Linear((rgb >> 8) & 255);
        var b = Linear(rgb & 255);
        var x = F((0.4124 * r + 0.3576 * g + 0.1805 * b) / 0.95047);
        var y = F(0.2126 * r + 0.7152 * g + 0.0722 * b);
        var z = F((0.0193 * r + 0.1192 * g + 0.9505 * b) / 1.08883);
        return (116 * y - 16, 500 * (x - y), 200 * (y - z));
    }

    /// <summary>Black on a light colour, white on a dark one.</summary>
    public static int TextOn(int c) => (((c >> 16) & 255) * 299 + ((c >> 8) & 255) * 587 + (c & 255) * 114) / 1000 > 140 ? 0x000000 : 0xFFFFFF;

    /// <summary>Routes on the map: colours that cannot be mistaken for the run colours (green, orange, red, yellow).</summary>
    public static readonly int[] MapPalette =
    {
        Rgb(70, 130, 255), Rgb(210, 70, 180), Rgb(0, 170, 160), Rgb(140, 100, 220), Rgb(205, 160, 100), Rgb(90, 190, 230),
    };

    /// <summary>The colour of a worker: the one picked in the Party table, else zzoega orange, zzatvo green, the others one of the map palette by their place in the party.</summary>
    public static int WorkerColor(string name, int? picked, int partyIndex)
    {
        if (picked is int p) return p & 0xFFFFFF;
        if (SameName(name, "zzoega")) return Rgb(255, 150, 40);
        if (SameName(name, "zzatvo")) return Rgb(60, 205, 95);
        return MapPalette[Math.Max(0, partyIndex) % MapPalette.Length];
    }

    /// <summary>A run: red = all cargo lost, orange = some lost, green = completed with everything delivered, yellow = still on the way.</summary>
    public int RunColor(string lost, bool ended) => lost == "All" ? Red : lost == "Little" ? Orange : ended ? Green : Yellow;

    /// <summary>A row of the Trip Planner: red = all cargo lost, orange = some lost, green = done, yellow = on the way. Planned trips have no colour.</summary>
    public int? PlanRowColor(string level) => level switch
    {
        "all" => Red,
        "little" => Orange,
        "done" => Green,
        "active" => Yellow,
        _ => null,
    };
}
