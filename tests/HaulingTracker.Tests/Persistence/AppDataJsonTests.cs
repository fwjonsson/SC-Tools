using System.Text.Json;

namespace HaulingTracker.Tests.Persistence;

/// <summary>The saved data survives a save and a load (the data file is this JSON).</summary>
public class AppDataJsonTests
{
    static AppData RoundTrip(AppData data) =>
        JsonSerializer.Deserialize<AppData>(JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }))!;

    [Fact]
    public void Sessions_with_their_payouts_and_locks_are_kept()
    {
        var data = new AppData();
        data.Sessions["manual-20261022230700"] = new SessionInputs
        {
            Reward2 = 1_268_250m,
            Reward4 = 2_113_750m,
            WasteReward4 = 2_113_750m,
            NextStopsLocked = true,
        };
        var back = RoundTrip(data).Sessions["manual-20261022230700"];
        Assert.Equal(1_268_250m, back.Reward2);
        Assert.Equal(2_113_750m, back.Reward4);
        Assert.Equal(2_113_750m, back.WasteReward4);
        Assert.True(back.NextStopsLocked);
    }

    [Fact]
    public void The_party_ships_and_typed_values_are_kept()
    {
        var data = new AppData();
        data.Ship["Pilot_A"] = "C2 Hercules Starlifter";
        data.TypedScu["m|dropoff_1"] = 147;
        data.StartPoints["Pilot_A"] = new StartPoint { Id = "70120", Since = new DateTime(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc) };
        var back = RoundTrip(data);
        Assert.Equal("C2 Hercules Starlifter", back.Ship["Pilot_A"]);
        Assert.Equal(147, back.TypedScu["m|dropoff_1"]);
        Assert.Equal("70120", back.StartPoints["Pilot_A"].Id);
    }

    [Fact]
    public void An_empty_data_file_has_the_defaults()
    {
        var back = JsonSerializer.Deserialize<AppData>("{}")!;
        Assert.Equal(8765, back.WebViewPort);
        Assert.Empty(back.Sessions);
    }
}
