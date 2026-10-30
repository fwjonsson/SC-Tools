using SCTools.Tests.Support;

namespace SCTools.Tests.Tracking;

/// <summary>The names of the places: the stations known by their position, with the full names the game gives them.</summary>
public class PlacesTests
{
    static Places PlacesWith(AppData data) => new(new TrackerState(data, _ => { }));

    static string Known(string zone, (double X, double Y, double Z) at)
    {
        StationIds.Register(zone, at.X, at.Y, at.Z);
        return zone;
    }

    [Fact]
    public void A_station_found_by_its_position_has_its_name_and_its_distance_from_Ruin()
    {
        var zone = Known("900000004101", GameLogWriter.Gaslight);
        var places = PlacesWith(new AppData());
        Assert.Equal("Gaslight", places.LocationName(zone));
        Assert.Equal("69936", places.StationOf(zone));
        Assert.Equal(PyroDistances.Between(PyroDistances.Ruin, "69936").ToString("N1", System.Globalization.CultureInfo.InvariantCulture) + " Gm", places.DistanceText(zone));
    }

    [Fact]
    public void The_stations_have_the_games_full_names_and_typed_names_do_not_count()
    {
        // Frank, 30 Oct 2026: the full names back ("Rod's Fuel 'N Supplies", not "Rod's Fuel"), and the names he typed on his Session page, kept in the
        // data file from before, are not used any more
        var known = Known("900000004103", GameLogWriter.RodsFuel);
        var data = new AppData();
        data.LocationNames[known] = "Rod's Fuel";
        var places = PlacesWith(data);
        Assert.Equal("Rod's Fuel 'N Supplies", places.LocationName(known));
        Assert.Equal("72645", places.StationOf(known));
        Assert.Equal("Rod's Fuel 'N Supplies", PyroDistances.NameOf("72645"));   // the planner's tables say the same
    }

    [Fact]
    public void An_unknown_place_is_named_by_the_end_of_its_id()
    {
        var places = PlacesWith(new AppData());
        Assert.Equal("Unknown ...54321", places.LocationName("900000054321"));
        Assert.Null(places.StationOf("900000054321"));
        Assert.Equal("", places.DistanceText("900000054321"));
    }
}
