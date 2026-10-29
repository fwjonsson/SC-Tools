using System.Globalization;

namespace SCTools.Planning;

/// <summary>The texts of the Trip Planner tables (the program and the web page show the same): the routes, the cells of every line and the totals.</summary>
public static class PlanText
{
    /// <summary>The columns of the Current Route table, in the order of the cells of LineCells and LineTotals: the header and where the text sits.</summary>
    public static readonly IReadOnlyList<(string Header, string Align)> CurrentColumns = new[]
    {
        ("#", "center"), ("Stops", "left"), ("Collect/Deliver", "left"), ("SCU", "right"), ("Boxes", "left"), ("Value", "right"), ("Distance", "right"), ("In flight", "right"),
    };

    /// <summary>The columns of the Queued routes table, in the order of the cells of QueuedCells and PlanTotals.</summary>
    public static readonly IReadOnlyList<(string Header, string Align)> QueuedColumns = new[]
    {
        ("#", "center"), ("Route", "left"), ("Stops", "center"), ("Batches", "center"), ("SCU", "right"), ("Value", "right"), ("Distance", "right"), ("In flight", "right"),
    };

    /// <summary>
    /// The route of a planned trip as a short tag for the Queued routes table: just the stations, with Ruin as the first and the last stop
    /// (<c>Ruin → Rod's Fuel → Endgame → Ruin</c>). A trip that continues from a station (<c>From</c>) starts there. What is done at every stop is in the Current Route table.
    /// </summary>
    public static string RouteText(PlannedTrip trip)
    {
        var parts = new List<string>();
        var hasDrop = trip.Drops.Count > 0;
        var drop = hasDrop ? trip.Drops[0] : default;
        var firstStop = trip.Stops.Count > 0 ? trip.Stops[0].Id : null;
        if (trip.From == null) parts.Add("Ruin");
        else if (firstStop != trip.From) parts.Add(PyroDistances.NameOf(trip.From));   // where he is now
        if (hasDrop && firstStop != drop.Id) parts.Add(PyroDistances.NameOf(drop.Id));   // a stop with only the drop-off
        foreach (var (id, _) in trip.Stops) parts.Add(PyroDistances.NameOf(id));
        parts.Add("Ruin");
        return string.Join("  \u2192  ", parts);
    }

    /// <summary>How many batches, for the Boxes column: "1 Batch", "3 Batches".</summary>
    public static string BatchText(int n, double scu = 0) => n + (n == 1 ? " Batch" : " Batches") + (scu >= 1 ? " (~" + Math.Round(scu).ToString(CultureInfo.InvariantCulture) + " SCU)" : "");   // the SCU is an estimate: mission SCU / its stops

    /// <summary>A sum of SCU for an SCU column: "~839" (a guess when batches are in it), "152", or nothing below one.</summary>
    public static string ScuSum(double scu, bool guess = true) => scu < 1 ? "" : (guess ? "~" : "") + Math.Round(scu).ToString(CultureInfo.InvariantCulture);

    /// <summary>A guessed amount of SCU as containers, like "~12×32 + 8".</summary>
    public static string ScuGuess(double scu) => Math.Round(scu) < 1 ? "? SCU" : "~" + DropOffs.ContainersText((int)Math.Round(scu));

    /// <summary>The Total line of the Queued routes table: the number of stops, batches, value, distance and flight time of the trips above it.</summary>
    public static string[] PlanTotals(List<PlanRow> rows)
    {
        var stops = rows.Sum(r => r.Stops.Count);
        var gm = rows.Sum(r => r.Gm);
        var flight = rows.Sum(r => r.Flight);
        return new[]
        {
            "\u03a3",
            "",
            stops.ToString(CultureInfo.InvariantCulture),
            rows.Sum(r => r.BatchCount).ToString(CultureInfo.InvariantCulture),
            ScuSum(rows.Sum(r => r.Scu)),
            ValueText(rows.Sum(r => r.Value)),
            gm > 0 ? Gm(gm) : "",
            flight > 0 ? "~" + FlightSpan(flight) : "",
        };
    }

    /// <summary>The cells of the Current Route table: the lines one by one (every line has a number, the Ruin lines too), the stop, what is done there, and the leg that leads to it.</summary>
    public static List<string[]> LineCells(List<StopLine> lines)
    {
        var cells = new List<string[]>();
        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            cells.Add(new[]
            {
                (i + 1).ToString(CultureInfo.InvariantCulture),
                l.Name,
                l.ActionText,
                l.ScuText,
                l.BoxesText,
                ValueText(l.Value),
                l.Gm > 0 ? Gm(l.Gm) : "",
                l.Seconds > 0 ? "~" + FlightSpan(l.Seconds) : "",
            });
        }
        return cells;
    }

    /// <summary>The Σ line of the Current Route table (as the Queued routes have it, Frank 29 Oct 2026): the stops, the batches collected, the value, the distance and the flight time.</summary>
    public static string[] LineTotals(List<StopLine> lines)
    {
        var stops = lines.Count(l => l.Stop);
        var batches = lines.Sum(l => l.Batches);
        var scu = lines.Sum(l => l.Scu);
        var waste = lines.Sum(l => l.WasteScu);
        var gm = lines.Sum(l => l.Gm);
        var flight = lines.Sum(l => l.Seconds);
        // everything the route carries: the batches to Ruin and the cargo from Ruin, in SCU together (the batches are an estimate, so ~) and in words
        var carried = (batches > 0 ? BatchText(batches) : "") + (waste > 0 ? (batches > 0 ? " + " : "") + "Waste" : "");
        return new[]
        {
            "\u03a3",
            stops + (stops == 1 ? " stop" : " stops"),
            batches > 0 ? "Collect" : "",
            ScuSum(scu + waste, scu > 0),
            carried,
            ValueText(lines.Sum(l => l.Value)),
            gm > 0 ? Gm(gm) : "",
            flight > 0 ? "~" + FlightSpan(flight) : "",
        };
    }

    /// <summary>The cells of a trip in the Queued routes table: its number, the route, the batches, the value, the distance and the flight time.</summary>
    public static string[] QueuedCells(PlanRow r) => new[]
    {
        RowLabel(r),
        r.Route,
        r.Stops.Count.ToString(CultureInfo.InvariantCulture),
        r.BatchCount.ToString(CultureInfo.InvariantCulture),
        ScuSum(r.Scu),
        ValueText(r.Value),
        r.Gm > 0 ? Gm(r.Gm) : "",
        FlightText(r),
    };

    /// <summary>The text of the # cell of a queued trip: its number.</summary>
    public static string RowLabel(PlanRow r) => r.Label;

    /// <summary>The estimated flying time of the route, spool time and jumps with the drive of the worker, no loading.</summary>
    public static string FlightText(PlanRow r) => r.Flight <= 0 ? "" : "~" + FlightSpan(r.Flight);
}
