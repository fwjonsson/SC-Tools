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
    public void The_ship_of_a_quantum_jump_is_found_in_a_fleet_by_its_name()
    {
        var line = "<2026-10-24T21:28:22.922Z> [Notice] <Quantum Drive Arrived - Arrived at Final Destination> [ItemNavigation][CL][28348] | NOT AUTH | ARGO_MOLE_865686068094[865686068094]|CSCItemNavigation::OnQuantumDriveArrived|Quantum Drive has arrived at final destination [Team_CGP4][QuantumTravel]";
        Assert.Equal("ARGO_MOLE", VehicleLines.VehicleOf(line));
        Assert.Null(VehicleLines.VehicleOf("<2026-10-24T21:28:22.922Z> [Notice] something else | ARGO_MOLE_865686068094[865686068094]|"));
        var fleet = new[] { "RSI Perseus", "M2 Hercules Starlifter", "C2 Hercules Starlifter", "MOLE" };
        Assert.Equal(2, VehicleLines.Best("CRUS_Starlifter_C2", fleet));
        Assert.Equal(3, VehicleLines.Best("ARGO_MOLE", fleet));
        Assert.Equal(0, VehicleLines.Best("RSI_Perseus", fleet));
        Assert.Equal(-1, VehicleLines.Best("ANVL_Carrack", fleet));
        Assert.Equal("CRUS Starlifter C2", VehicleLines.Pretty("CRUS_Starlifter_C2"));
    }
}
