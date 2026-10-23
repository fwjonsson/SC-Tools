using HaulingTracker.Tests.Support;

namespace HaulingTracker.Tests.Tracking;

/// <summary>The names of the places: the stations known by their position, the names you typed, and the old names in the data file.</summary>
public class PlacesTests
{
    static Places PlacesWith(AppData data) => new(new TrackerState(data, _ => { }));

    static string Known(string zone, (double X, double Y, double Z) at)
    {
        StationIds.Register(zone, at.X, at.Y, at.Z);
        return zone;
    }

    [Fact]
    public void A_station_found_by_its_position_has_its_short_name_and_its_distance_from_Ruin()
    {
        var zone = Known("900000004101", GameLogWriter.Gaslight);
        var places = PlacesWith(new AppData());
        Assert.Equal("Gaslight", places.LocationName(zone));
        Assert.Equal("69936", places.StationOf(zone));
        Assert.Equal(PyroDistances.Between(PyroDistances.Ruin, "69936").ToString("N1", System.Globalization.CultureInfo.InvariantCulture) + " Gm", places.DistanceText(zone));
    }

    [Fact]
    public void Your_own_name_wins_until_you_take_it_away()
    {
        var zone = Known("900000004102", GameLogWriter.PatchCity);
        var data = new AppData();
        var places = PlacesWith(data);

        Assert.False(places.Rename(zone, "Home Base"));
        Assert.Equal("Home Base", places.LocationName(zone));
        Assert.Equal("70120", places.StationOf(zone));   // still the same station

        Assert.True(places.Rename(zone, "Patch City"));   // the name the program knows itself is no name of your own
        Assert.False(data.LocationNames.ContainsKey(zone));
        places.Rename(zone, "Home Base");
        Assert.True(places.Rename(zone, ""));
        Assert.Equal("Patch City", places.LocationName(zone));
    }

    [Fact]
    public void Names_saved_by_older_versions_are_shown_as_they_are_now()
    {
        var known = Known("900000004103", GameLogWriter.RodsFuel);
        var unknown = "900000004199";
        var data = new AppData();
        data.LocationNames[unknown] = "Rod's Fuel 'N Supplies";   // the long name an earlier version saved
        data.LocationNames[known] = "Megumi";                     // the name of another station, saved by clicking into the cell
        var places = PlacesWith(data);
        Assert.Equal("Rod's Fuel", places.LocationName(unknown));
        Assert.Equal("Rod's Fuel", places.LocationName(known));
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
