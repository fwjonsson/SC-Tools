namespace SCTools.Tests.Tracking;

/// <summary>The party: who was active when, joining and leaving, and the ship, drive and capacity of a member.</summary>
public class CrewTests
{
    static readonly DateTime T = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

    static Crew CrewWith(AppData data) => new(new TrackerState(data, _ => { }));

    [Fact]
    public void A_member_is_active_from_when_he_joined_until_he_left()
    {
        var data = new AppData { Party = new List<string> { "Pilot_A" } };
        data.PartyChanges.Add(new PartyChange { Name = "Pilot_A", TimeUtc = T, Active = true });
        data.PartyChanges.Add(new PartyChange { Name = "Pilot_A", TimeUtc = T.AddHours(2), Active = false });
        var crew = CrewWith(data);

        Assert.False(crew.IsActiveAt("Pilot_A", T.AddMinutes(-1)));   // before he joined
        Assert.True(crew.IsActiveAt("pilot_a", T.AddHours(1)));      // capitals do not matter
        Assert.False(crew.IsActiveAt("Pilot_A", T.AddHours(3)));
        Assert.Equal(new[] { "Pilot_A" }, crew.PartyAt(T.AddHours(1)));
        Assert.Empty(crew.PartyAt(T.AddHours(3)));
    }

    [Fact]
    public void Without_any_change_a_member_is_what_the_Active_tick_says()
    {
        var data = new AppData { Party = new List<string> { "Pilot_A", "Pilot_B" }, InactiveParty = new List<string> { "Pilot_B" } };
        var crew = CrewWith(data);
        Assert.True(crew.IsActiveAt("Pilot_A", T));
        Assert.False(crew.IsActiveAt("Pilot_B", T));
        Assert.Equal(new[] { "Pilot_A" }, crew.ActiveCrew());
    }

    [Fact]
    public void Joining_adds_a_member_once_with_a_ship_of_the_default_size()
    {
        var data = new AppData { Party = new List<string>() };
        var crew = CrewWith(data);

        Assert.True(crew.Join("Pilot_C"));
        Assert.Equal(new[] { "Pilot_C" }, data.Party);
        Assert.Equal(Crew.DefaultShipCapacity, crew.ShipCapacityOf("Pilot_C"));
        Assert.True(crew.IsActiveAt("Pilot_C", DateTime.UtcNow.AddSeconds(1)));
        Assert.False(crew.Join("pilot_c"));   // he is in the party already
        Assert.Single(data.Party);
    }

    [Fact]
    public void A_member_who_is_removed_is_out_of_the_party_and_not_active()
    {
        var data = new AppData { Party = new List<string>() };
        var crew = CrewWith(data);
        crew.Join("Pilot_C");
        crew.Remove("Pilot_C");
        Assert.Empty(data.Party);
        Assert.False(crew.IsActiveAt("Pilot_C", DateTime.UtcNow.AddSeconds(1)));
        Assert.Contains("Pilot_C", crew.PartyNamesAll());   // his earlier time in the party is kept
    }

    [Fact]
    public void The_batch_capacity_is_a_number_from_1_to_the_most()
    {
        var crew = CrewWith(new AppData());
        crew.SetShipCapacity("Pilot_A", " 99 ");
        Assert.Equal(Crew.MaxShipCapacity, crew.ShipCapacityOf("Pilot_A"));
        crew.SetShipCapacity("Pilot_A", "0");
        Assert.Equal(1, crew.ShipCapacityOf("Pilot_A"));
        crew.SetShipCapacity("Pilot_A", "lots");
        Assert.Equal(Crew.DefaultShipCapacity, crew.ShipCapacityOf("Pilot_A"));
    }

    [Fact]
    public void Ship_and_drive_are_set_by_their_label_and_without_a_drive_one_is_assumed()
    {
        var crew = CrewWith(new AppData());
        Assert.Null(crew.ShipOf("Pilot_A"));
        Assert.Same(QuantumDrives.Assumed, crew.QtOf("Pilot_A"));

        var ship = Ships.All[0];
        crew.SetShip("Pilot_A", ship.Label);
        Assert.Same(ship, crew.ShipOf("pilot_a"));
        crew.SetShip("Pilot_A", "");
        Assert.Null(crew.ShipOf("Pilot_A"));

        var drive = QuantumDrives.All.First(d => d != QuantumDrives.Assumed);
        crew.SetDrive("Pilot_A", drive.Label);
        Assert.Same(drive, crew.DriveOf("Pilot_A"));
        Assert.Same(drive, crew.QtOf("Pilot_A"));
    }

    [Fact]
    public void A_picked_colour_is_kept_for_the_worker()
    {
        var crew = CrewWith(new AppData());
        Assert.Null(crew.PickedColor("Pilot_A"));
        crew.SetColor("Pilot_A", unchecked((int)0xFF3366CC));
        crew.SetColor("pilot_a", unchecked((int)0xFF112233));   // the same worker: the new colour replaces the old one
        Assert.Equal(unchecked((int)0xFF112233), crew.PickedColor("Pilot_A"));
    }
}
