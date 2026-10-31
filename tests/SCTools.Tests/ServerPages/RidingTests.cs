using System.Net;
using Microsoft.Extensions.DependencyInjection;
using SCTools.Server;
using SCTools.Tests.Support;

namespace SCTools.Tests.ServerPages;

/// <summary>
/// A rider on the site (the ship tracker, Frank 31 Oct 2026): zzatvo in zzoega's C2 is in zzoega's ship with zzoega's crafted drive (the map moves him at
/// its speed), the pages say whose ship it is, and the Hauling Planner plans him no trips of his own: he is crew of the pilot's ship.
/// </summary>
public class RidingTests
{
    [Fact]
    public async Task A_rider_is_in_the_owners_ship_with_its_drive_and_flies_no_trips_of_his_own()
    {
        using var f = new ServerFactory();
        var zzoega = await f.Player(f.AddKey("zzoega"));
        var zzatvo = await f.Player(f.AddKey("zzatvo"));
        await ServerFactory.Send(zzoega, "/me", "/me/fleet/add", ("ship", "Crusader C2 Hercules Starlifter"), ("drive", "TS-2 S3"), ("quality", "1000"));
        await ServerFactory.Send(zzatvo, "/me", "/me/fleet/add", ("ship", "Crusader C2 Hercules Starlifter"), ("drive", ""), ("quality", ""));
        var start = DateTime.UtcNow.AddMinutes(-5);
        f.Upload("zzoega", new GameLogWriter().At(start).Login("zzoega").Boarded("Crusader C2 Hercules Starlifter", "zzoega").Seconds(30).QuantumSelected("Gaslight").Lines.ToList());
        f.Upload("zzatvo", new GameLogWriter().At(start).Login("zzatvo").Seconds(10).Boarded("Crusader C2 Hercules Starlifter", "zzoega").Lines.ToList());

        // his own C2 is in his fleet too: the one he is in is zzoega's
        var fleet = f.Services.GetRequiredService<FleetService>();
        var active = fleet.Active("zzatvo");
        Assert.Equal((true, "zzoega's Crusader C2 Hercules Starlifter", "zzoega"), (active.Riding, active.Text, active.Ship?.Row.Player));
        Assert.Equal((false, "Crusader C2 Hercules Starlifter"), (fleet.Active("zzoega").Riding, fleet.Active("zzoega").Text));

        // the planner: no hauler of his own, the C2's drive as zzoega crafted it; zzoega flies the C2
        fleet.SyncPlanner(DateTime.UtcNow);
        Assert.Equal(((string?)null, "TS-2 S3", 20.0), f.Live.Read(t => (t.Crew.ShipOf("zzatvo")?.Name, t.Crew.DriveOf("zzatvo")?.Key, t.Crew.DriveBonusOf("zzatvo"))));
        Assert.Equal("C2 Hercules Starlifter", f.Live.Read(t => t.Crew.ShipOf("zzoega")?.Name));
        f.Live.Refresh(DateTime.UtcNow);
        Assert.Equal((Crew.DefaultShipCapacity, 0), f.Live.Read(t => (t.Crew.FlyingCapacityOf("zzoega"), t.Crew.FlyingCapacityOf("zzatvo"))));

        var admin = await f.Admin();
        var page = WebUtility.HtmlDecode(await admin.GetStringAsync("/party/zzatvo"));
        Assert.Contains("zzoega's Crusader C2 Hercules Starlifter · TS-2 S3 +20 %", page);
        Assert.Contains("(riding: zzoega flies it; boarded", page);
    }
}
