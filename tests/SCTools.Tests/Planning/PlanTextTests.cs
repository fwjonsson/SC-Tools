namespace SCTools.Tests.Planning;

/// <summary>The texts of the Trip Planner tables: routes, batches, the cells of a line and the totals.</summary>
public class PlanTextTests
{
    const string RodsFuel = "72645", Endgame = "70273", PatchCity = "70120";

    [Fact]
    public void A_route_goes_from_Ruin_over_its_stations_back_to_Ruin()
    {
        var trip = new PlannedTrip { Stops = { (RodsFuel, 2), (Endgame, 1) } };
        Assert.Equal("Ruin  →  Rod's Fuel  →  Endgame  →  Ruin", PlanText.RouteText(trip));

        var continued = new PlannedTrip { From = PatchCity, Stops = { (RodsFuel, 1) } };   // continues a run in progress from where he is
        Assert.Equal("Patch City  →  Rod's Fuel  →  Ruin", PlanText.RouteText(continued));

        var withWaste = new PlannedTrip { Stops = { (RodsFuel, 2) }, Drops = { (Endgame, 1) } };   // the load from Ruin goes to Endgame first
        Assert.Equal("Ruin  →  Endgame  →  Rod's Fuel  →  Ruin", PlanText.RouteText(withWaste));
    }

    [Fact]
    public void Batches_are_counted_with_their_estimated_scu()
    {
        Assert.Equal("1 Batch", PlanText.BatchText(1));
        Assert.Equal("3 Batches (~375 SCU)", PlanText.BatchText(3, 375.2));
        Assert.Equal("? SCU", PlanText.ScuGuess(0.4));
        Assert.Equal("~4×32 + 24", PlanText.ScuGuess(152));
    }

    [Fact]
    public void The_total_of_a_route_counts_its_stops_and_everything_it_carries()
    {
        var lines = new List<StopLine>
        {
            new() { Name = "Ruin", Stop = false, WasteScu = 89 },
            new() { Name = "Rod's Fuel", Batches = 3, Scu = 375, Value = 1_000_000m, Gm = 30, Seconds = 100 },
            new() { Name = "Endgame", Batches = 3, Scu = 375, Value = 500_000m, Gm = 10, Seconds = 50 },
            new() { Name = "Ruin", Stop = false, Gm = 30, Seconds = 100 },
        };
        var total = PlanText.LineTotals(lines);
        Assert.Equal(PlanText.CurrentColumns.Count, total.Length);
        Assert.Equal("Total: 2 stops", total[1]);
        Assert.Equal("Collect", total[2]);
        Assert.Equal("6 Batches + Waste (~839 SCU)", total[3]);
        Assert.Equal("70.0 Gm", total[5]);
        Assert.Equal("~4:10", total[6]);

        var cells = PlanText.LineCells(lines);
        Assert.Equal(new[] { "1", "2", "3", "4" }, cells.Select(c => c[0]));   // every line has its number, the Ruin lines too
        Assert.All(cells, c => Assert.Equal(PlanText.CurrentColumns.Count, c.Length));
    }

    [Fact]
    public void A_queued_trip_has_a_cell_for_every_column()
    {
        var row = new PlanRow { Label = "1", Route = "Ruin  →  Gaslight  →  Ruin", Stops = { "69936" }, BatchCount = 4, Value = 2_000_000m, Gm = 12.5, Flight = 300 };
        var cells = PlanText.QueuedCells(row);
        Assert.Equal(PlanText.QueuedColumns.Count, cells.Length);
        Assert.Equal(new[] { "1", row.Route, "1", "4" }, cells.Take(4));
        Assert.Equal("~5:00", cells[6]);
        var totals = PlanText.PlanTotals(new List<PlanRow> { row, row });
        Assert.Equal("Σ", totals[0]);
        Assert.Equal(new[] { "2", "8" }, totals.Skip(2).Take(2));
    }
}
