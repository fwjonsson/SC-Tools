namespace SCTools.Tests.GameData;

/// <summary>Every spaceship of the game data, for the fleets: a name as typed or picked, a class the logs name, and the stock quantum drive.</summary>
public class ShipModelsTests
{
    [Fact]
    public void Every_ship_of_the_game_is_there_with_its_classes_and_stock_drive()
    {
        Assert.True(ShipModels.All.Length > 200);
        Assert.DoesNotContain(ShipModels.All, m => m.Classes.Any(c => c.EndsWith("_Display", StringComparison.Ordinal)));   // the display models
        var c2 = ShipModels.OfClass("CRUS_Starlifter_C2")!;
        Assert.Equal(("Crusader C2 Hercules Starlifter", 696, "Kama S3"), (c2.Name, c2.Cargo, c2.Drive));
        Assert.Equal("Aegis Gladius", ShipModels.OfClass("aegs_gladius")?.Name);
        Assert.Equal("Drake Cutlass Black", ShipModels.OfClass("DRAK_Cutlass_Black_BIS2950")?.Name);   // a Best in Show paint is the same ship
        Assert.All(ShipModels.All.Where(m => m.Drive != null), m => Assert.NotNull(QuantumDrives.Find(m.Drive)));
        Assert.Null(ShipModels.OfClass("TMBL_Cyclone"));   // a ground vehicle
    }

    [Theory]
    [InlineData("Crusader C2 Hercules Starlifter", "Crusader C2 Hercules Starlifter")]
    [InlineData("c2 hercules starlifter", "Crusader C2 Hercules Starlifter")]   // without its maker
    [InlineData("Cutlass Black", "Drake Cutlass Black")]
    [InlineData("My Carrack Expedition", "Anvil Carrack Expedition")]          // the longest name in it
    [InlineData("MOLE", "Argo MOLE")]
    [InlineData("Mystery Ship", null)]
    public void A_ship_is_found_by_its_name(string typed, string? model) => Assert.Equal(model, ShipModels.Find(typed)?.Name);

    [Fact]
    public void Every_hauler_of_the_planner_is_a_ship_of_the_game() =>
        Assert.All(Ships.All, s => Assert.NotNull(ShipModels.Find(s.Name)));
}
