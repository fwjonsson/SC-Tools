namespace SCTools.Tests.GameData;

/// <summary>A crafted quantum drive: its quality makes the drive speed up to 20 % higher or lower; and the ship of a quantum jump, by the logs' class.</summary>
public class CraftedDriveTests
{
    [Fact]
    public void Quality_1000_is_20_percent_faster_500_as_bought_and_0_20_percent_slower()
    {
        Assert.Equal(20, QuantumDrives.CraftedSpeedPercent(1000), 6);
        Assert.Equal(0, QuantumDrives.CraftedSpeedPercent(500), 6);
        Assert.Equal(-20, QuantumDrives.CraftedSpeedPercent(0), 6);
        Assert.Equal(10, QuantumDrives.CraftedSpeedPercent(750), 6);
        var ts2 = QuantumDrives.Find("TS-2 S3")!;
        Assert.Equal(611_661_360, ts2.Crafted(20).Speed, 1);   // as the game's crafting page says
        Assert.Equal(ts2.Accel1, ts2.Crafted(20).Accel1);       // only the drive speed changes
        Assert.True(ts2.Crafted(20).Seconds(50) < ts2.Seconds(50));
        Assert.Equal("+20 %", QuantumDrives.PercentText(20));
        Assert.Equal("-12.5 %", QuantumDrives.PercentText(-12.5));
        Assert.Equal("", QuantumDrives.PercentText(0));
    }

    [Fact]
    public void Fuel_quality_1000_burns_20_percent_less_500_as_bought_and_0_20_percent_more()
    {
        Assert.Equal(-20, QuantumDrives.CraftedFuelPercent(1000), 6);
        Assert.Equal(0, QuantumDrives.CraftedFuelPercent(500), 6);
        Assert.Equal(20, QuantumDrives.CraftedFuelPercent(0), 6);
        var ts2 = QuantumDrives.Find("TS-2 S3")!;
        Assert.Equal(141.68, ts2.FuelPerKm, 2);                        // the game: "Quantum Fuel Burn: 141.68 per Km"
        // Frank's crafted TS-2s (31 Oct 2026): the C2's burns 130.61 per km, the Perseus's 120.33; the fuel estimates of their routes in his log, 35,035
        // and 32,270 per Gm, are the same factor of the two
        Assert.Equal(35_035 / 130.61, 32_270 / 120.33, 0);
        Assert.Equal(0, QuantumDrives.Find("Mauler S4")!.FuelRate);    // not in the game data: no fuel per distance shown
        Assert.All(QuantumDrives.All.Where(d => d.Name != "Mauler"), d => Assert.True(d.FuelRate > 0, d.Key));
    }

    [Fact]
    public void The_ship_of_a_quantum_jump_is_found_in_a_fleet_by_its_name()
    {
        var line = "<2026-10-24T21:28:22.922Z> [Notice] <Quantum Drive Arrived - Arrived at Final Destination> [ItemNavigation][CL][28348] | NOT AUTH | ARGO_MOLE_865686068094[865686068094]|CSCItemNavigation::OnQuantumDriveArrived|Quantum Drive has arrived at final destination [Team_CGP4][QuantumTravel]";
        Assert.Equal("ARGO_MOLE", VehicleLines.VehicleOf(line));
        Assert.Null(VehicleLines.VehicleOf("<2026-10-24T21:28:22.922Z> [Notice] something else | ARGO_MOLE_865686068094[865686068094]|"));

        // zzoega's log of 28 Oct: his own ship's navigation, and a ship that came near him (the game writes one of those for every ship around)
        const string Own = "<2026-10-28T13:10:02.112Z> [Notice] <Failed to get starmap route data!> [ItemNavigation][CL][24012] | NOT AUTH | RSI_Perseus_898683539008[898683539008]|CSCItemNavigation::GetStarmapRouteSegmentData|No Route loaded! [Team_CGP4][QuantumTravel]";
        const string Near = "<2026-10-28T13:13:43.768Z> [Notice] <Local Route Guard - Server Rerouted> [ItemNavigation][CL][24012] | NOT AUTH | RSI_Polaris_904387400011[904387400011]|CSCItemNavigation::PostInitialize::<lambda_1>::operator ()|FinalStop=-1 [Team_CGP4][QuantumTravel]";
        Assert.Equal("RSI_Perseus", VehicleLines.VehicleOf(Own));
        Assert.Null(VehicleLines.VehicleOf(Near));
        var u = new LogUploads();
        Assert.True(u.Add("zzoega", "2026-10-28T12:21:23.003Z", 0, 100, new[] { Own, Near }).Ok);
        Assert.Equal("RSI_Perseus", u.Ships()["zzoega"].Vehicle);   // not the Polaris that came near
        var fleet = new[] { "RSI Perseus", "M2 Hercules Starlifter", "C2 Hercules Starlifter", "MOLE" };
        Assert.Equal(2, VehicleLines.Best("CRUS_Starlifter_C2", fleet));
        Assert.Equal(3, VehicleLines.Best("ARGO_MOLE", fleet));
        Assert.Equal(0, VehicleLines.Best("RSI_Perseus", fleet));
        Assert.Equal(-1, VehicleLines.Best("ANVL_Carrack", fleet));
        Assert.Equal("CRUS Starlifter C2", VehicleLines.Pretty("CRUS_Starlifter_C2"));
    }
}
