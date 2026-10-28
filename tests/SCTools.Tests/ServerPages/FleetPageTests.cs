using System.Net;
using Microsoft.Extensions.DependencyInjection;
using SCTools.Server;
using SCTools.Tests.Support;

namespace SCTools.Tests.ServerPages;

/// <summary>
/// The fleet on My page: a player's ships with their quantum drives (a crafted one faster or slower by its quality), the ship he flies (picked, or the one of
/// his latest quantum jump), and the Hauling Planner flying it.
/// </summary>
public class FleetPageTests
{
    [Fact]
    public async Task A_player_keeps_his_fleet_and_the_planner_flies_his_active_ship_with_its_crafted_drive()
    {
        using var f = new ServerFactory();
        var me = await f.Player(f.AddKey("zzoega"));
        Assert.Contains("Automatic: no quantum jump in your logs yet", WebUtility.HtmlDecode(await me.GetStringAsync("/me")));

        await ServerFactory.Send(me, "/me", "/me/fleet/add", ("ship", "Crusader C2 Hercules Starlifter"), ("drive", "TS-2 S3"), ("quality", "1000"));
        await ServerFactory.Send(me, "/me", "/me/fleet/add", ("ship", "RSI Perseus"), ("drive", ""), ("quality", ""));
        var bad = await ServerFactory.Send(me, "/me", "/me/fleet/add", ("ship", "Carrack"), ("drive", "TS-2 S3"), ("quality", "1200"));
        Assert.Equal("/me#fleet", bad.Headers.Location?.OriginalString);
        var page = WebUtility.HtmlDecode(await me.GetStringAsync("/me"));
        Assert.Contains("The quality of a crafted drive is a whole number from 0 to 1000", page);
        Assert.Contains("611,661,360 m/s (+20 %)", page);   // the TS-2 at quality 1000: 509,717,800 m/s and 20 % more
        Assert.Contains("not a hauler the planner knows", page);   // the Perseus

        var fleet = f.Services.GetRequiredService<FleetService>();
        var ships = fleet.Fleet("zzoega");
        Assert.Equal(new[] { "Crusader C2 Hercules Starlifter", "RSI Perseus" }, ships.Select(s => s.Row.Ship));
        Assert.Equal("C2 Hercules Starlifter", ships[0].Hauler?.Name);

        // Automatic: his latest quantum jump was in a C2 (the logs name it CRUS_Starlifter_C2)
        f.Upload("zzoega", new GameLogWriter().At(DateTime.UtcNow.AddMinutes(-5)).Login("zzoega").QuantumSelected("Gaslight").Lines.ToList());
        var active = fleet.Active("zzoega");
        Assert.Equal(((int?)ships[0].Row.Id, false, "CRUS_Starlifter_C2"), (active.Ship?.Row.Id, active.Picked, active.Vehicle));
        fleet.SyncPlanner(DateTime.UtcNow);
        Assert.Equal(("C2 Hercules Starlifter", "TS-2 S3", 20.0), f.Live.Read(t => (t.Crew.ShipOf("zzoega")?.Name, t.Crew.DriveOf("zzoega")?.Key, t.Crew.DriveBonusOf("zzoega"))));
        Assert.Equal(611_661_360, f.Live.Read(t => t.Crew.QtOf("zzoega").Speed), 1);
        Assert.Equal("TS-2 S3 +20 %", f.Live.Read(t => t.Crew.DriveTextOf("zzoega")));

        // he picks the Perseus: not a hauler, and no drive set for it
        await ServerFactory.Send(me, "/me", "/me/fleet/active", ("id", ships[1].Row.Id.ToString()));
        Assert.True(fleet.Active("zzoega").Picked);
        Assert.Equal(((string?)null, (string?)null, 0.0), f.Live.Read(t => (t.Crew.ShipOf("zzoega")?.Name, t.Crew.DriveOf("zzoega")?.Key, t.Crew.DriveBonusOf("zzoega"))));

        // a field changed on its own (the quality), and a ship taken out: his pick goes back to Automatic
        await ServerFactory.Send(me, "/me", "/me/fleet/set", ("id", ships[0].Row.Id.ToString()), ("field", "quality"), ("value", "250"));
        Assert.Equal(-10, fleet.Fleet("zzoega")[0].Bonus, 3);
        await ServerFactory.Send(me, "/me", "/me/fleet/remove", ("id", ships[1].Row.Id.ToString()));
        Assert.False(fleet.Active("zzoega").Picked);
        Assert.Equal((int?)ships[0].Row.Id, fleet.Active("zzoega").Ship?.Row.Id);

        // somebody else's ship cannot be changed
        var other = await f.Player(f.AddKey("bileh123"));
        await ServerFactory.Send(other, "/me", "/me/fleet/remove", ("id", ships[0].Row.Id.ToString()));
        Assert.Single(fleet.Fleet("zzoega"));
    }
}
