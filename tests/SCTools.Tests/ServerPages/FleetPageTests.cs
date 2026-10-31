using System.Net;
using System.Text.Json;
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
        await ServerFactory.Send(me, "/me", "/me/fleet/add", ("ship", "Aegis Gladius"), ("drive", ""), ("quality", ""));
        var bad = await ServerFactory.Send(me, "/me", "/me/fleet/add", ("ship", "Carrack"), ("drive", "TS-2 S3"), ("quality", "1200"));
        Assert.Equal("/me#fleet", bad.Headers.Location?.OriginalString);
        var page = WebUtility.HtmlDecode(await me.GetStringAsync("/me"));
        Assert.Contains("The quality of a crafted drive is a whole number from 0 to 1000", page);
        Assert.Contains("611.66 Mm/s (+20 %)", page);   // the TS-2 at quality 1000: 509,717,800 m/s and 20 % more
        Assert.DoesNotContain("not a hauler the planner knows", page);   // the Gladius is a ship of the game, not a hauler
        Assert.Contains("Stock: Beacon S1", page);                        // no drive picked for it: its stock drive
        Assert.Contains("Aegis Gladius", page);
        // a ship takes the drives of its stock drive's size: the Gladius lists size 1 drives, the C2 size 3, and another size is refused
        var gladius = System.Text.RegularExpressions.Regex.Match(page, "<tr[^>]*>(?:(?!</tr>).)*Aegis Gladius(?:(?!</tr>).)*</tr>", System.Text.RegularExpressions.RegexOptions.Singleline).Value;
        Assert.Contains("value=\"Beacon S1\"", gladius);
        Assert.DoesNotContain("value=\"TS-2 S3\"", gladius);
        var c2 = System.Text.RegularExpressions.Regex.Match(page, "<tr[^>]*>(?:(?!</tr>).)*Crusader C2 Hercules(?:(?!</tr>).)*</tr>", System.Text.RegularExpressions.RegexOptions.Singleline).Value;
        Assert.Contains("value=\"TS-2 S3\" selected", c2);
        Assert.DoesNotContain("value=\"Beacon S1\"", c2);
        Assert.Contains("data-size=\"3\">TS-2 S3</option>", page);   // the Add form has every drive with its size, for the script
        Assert.Contains("\"crusader c2 hercules starlifter\":3", page);
        await ServerFactory.Send(me, "/me", "/me/fleet/add", ("ship", "Aegis Gladius"), ("drive", "TS-2 S3"), ("quality", ""));
        Assert.Contains("A Aegis Gladius takes a size 1 quantum drive: TS-2 S3 is size 3.", WebUtility.HtmlDecode(await me.GetStringAsync("/me")));

        var fleet = f.Services.GetRequiredService<FleetService>();
        var ships = fleet.Fleet("zzoega");
        Assert.Equal(new[] { "Crusader C2 Hercules Starlifter", "Aegis Gladius" }, ships.Select(s => s.Row.Ship));
        Assert.Equal("A Aegis Gladius takes a size 1 quantum drive: TS-2 S3 is size 3.", fleet.Set("zzoega", ships[1].Row.Id, "drive", "TS-2 S3"));
        Assert.Null(fleet.Set("zzoega", ships[1].Row.Id, "drive", "VK-00 S1"));
        Assert.Null(fleet.Set("zzoega", ships[1].Row.Id, "ship", "Anvil Carrack"));   // a size 3 ship now: the size 1 drive goes, its stock drive until one is picked
        Assert.Null(fleet.Fleet("zzoega")[1].Row.Drive);
        Assert.Null(fleet.Set("zzoega", ships[1].Row.Id, "ship", "Aegis Gladius"));
        Assert.Equal("C2 Hercules Starlifter", ships[0].Hauler?.Name);

        // Automatic: his latest quantum jump was in a C2 (the logs name it CRUS_Starlifter_C2)
        f.Upload("zzoega", new GameLogWriter().At(DateTime.UtcNow.AddMinutes(-5)).Login("zzoega").QuantumSelected("Gaslight").Lines.ToList());
        var active = fleet.Active("zzoega");
        Assert.Equal(((int?)ships[0].Row.Id, false, "CRUS_Starlifter_C2"), (active.Ship?.Row.Id, active.Picked, active.Vehicle));
        fleet.SyncPlanner(DateTime.UtcNow);
        Assert.Equal(("C2 Hercules Starlifter", "TS-2 S3", 20.0), f.Live.Read(t => (t.Crew.ShipOf("zzoega")?.Name, t.Crew.DriveOf("zzoega")?.Key, t.Crew.DriveBonusOf("zzoega"))));
        Assert.Equal(611_661_360, f.Live.Read(t => t.Crew.QtOf("zzoega").Speed), 1);
        Assert.Equal("TS-2 S3 +20 %", f.Live.Read(t => t.Crew.DriveTextOf("zzoega")));

        // he picks the Gladius: not a hauler, and its stock drive (none set for it)
        Assert.Equal(("Beacon S1", true, "Beacon S1 (stock)"), (ships[1].Drive?.Key, ships[1].Stock, ships[1].DriveText));
        await ServerFactory.Send(me, "/me", "/me/fleet/active", ("id", ships[1].Row.Id.ToString()));
        Assert.True(fleet.Active("zzoega").Picked);
        Assert.Equal(((string?)null, "Beacon S1", 0.0), f.Live.Read(t => (t.Crew.ShipOf("zzoega")?.Name, t.Crew.DriveOf("zzoega")?.Key, t.Crew.DriveBonusOf("zzoega"))));

        // a field changed on its own (the quality), and a ship taken out: his pick goes back to Automatic
        await ServerFactory.Send(me, "/me", "/me/fleet/set", ("id", ships[0].Row.Id.ToString()), ("field", "quality"), ("value", "250"));
        Assert.Equal(-10, fleet.Fleet("zzoega")[0].Bonus, 3);
        await ServerFactory.Send(me, "/me", "/me/fleet/remove", ("id", ships[1].Row.Id.ToString()));
        Assert.False(fleet.Active("zzoega").Picked);
        Assert.Equal((int?)ships[0].Row.Id, fleet.Active("zzoega").Ship?.Row.Id);

        // the fuel efficiency quality: set like the speed's, and both shown as the game shows the drive's stats
        Assert.Equal("458.75 Mm/s (-10 %)", fleet.Fleet("zzoega")[0].SpeedText);
        Assert.Equal("141.68 per km", fleet.Fleet("zzoega")[0].FuelText);   // no fuel quality typed: as bought
        await ServerFactory.Send(me, "/me", "/me/fleet/set", ("id", ships[0].Row.Id.ToString()), ("field", "fuel"), ("value", "1000"));
        Assert.Equal("113.34 per km (-20 %)", fleet.Fleet("zzoega")[0].FuelText);

        // somebody else's ship cannot be changed
        var other = await f.Player(f.AddKey("bileh123"));
        await ServerFactory.Send(other, "/me", "/me/fleet/remove", ("id", ships[0].Row.Id.ToString()));
        Assert.Single(fleet.Fleet("zzoega"));
    }

    [Fact]
    public async Task The_qualities_start_at_500_have_arrows_and_are_saved_in_the_background_with_the_new_stats()
    {
        using var f = new ServerFactory();
        var me = await f.Player(f.AddKey("zzoega"));
        var page = WebUtility.HtmlDecode(await me.GetStringAsync("/me"));
        // the Add form: both qualities typed or clicked with arrows, 0 to 1000, 500 (as bought) to start with
        Assert.Contains("type=\"number\" name=\"quality\" value=\"500\" min=\"0\" max=\"1000\" step=\"1\"", page);
        Assert.Contains("type=\"number\" name=\"fuel\" value=\"500\" min=\"0\" max=\"1000\" step=\"1\"", page);
        await ServerFactory.Send(me, "/me", "/me/fleet/add", ("ship", "Crusader C2 Hercules Starlifter"), ("drive", "TS-2 S3"), ("quality", "500"), ("fuel", "250"));
        var bad = WebUtility.HtmlDecode(await (await ServerFactory.Send(me, "/me", "/me/fleet/add", ("ship", "RSI Perseus"), ("drive", ""), ("quality", "500"), ("fuel", "1001"))).Content.ReadAsStringAsync());
        var fleet = f.Services.GetRequiredService<FleetService>();
        var ship = Assert.Single(fleet.Fleet("zzoega"));   // the Perseus with a fuel quality of 1001 was refused
        Assert.Equal((500, 250), (ship.Row.Quality, ship.Row.FuelQuality));
        Assert.Equal("155.85 per km (+10 %)", ship.FuelText);

        page = WebUtility.HtmlDecode(await me.GetStringAsync("/me"));
        Assert.Contains(FleetService.BadQuality, page);
        // the row has what the page's script needs to show the stats at every click: the drive's speed and fuel per distance as bought
        Assert.Contains("data-id=\"" + ship.Row.Id + "\" data-speed=\"509717800\" data-fuel=\"141.68", page);
        Assert.Contains("id=\"speed-" + ship.Row.Id + "\">509.72 Mm/s</td>", page);
        Assert.Contains("id=\"fuel-" + ship.Row.Id + "\">155.85 per km (+10 %)</td>", page);
        Assert.Contains("data-autosave=\"quiet\" data-settle", page);
        Assert.Contains("name=\"field\" value=\"fuel\"", page);

        // sent in the background: JSON back with the new stats for their cells, and the planner flies the new speed at once
        async Task<JsonElement> Set(string field, string value)
        {
            var html = await me.GetStringAsync("/me");
            using var req = new HttpRequestMessage(HttpMethod.Post, "/me/fleet/set")
            {
                Content = new FormUrlEncodedContent(new[] { KeyValuePair.Create("id", ship.Row.Id.ToString()), KeyValuePair.Create("field", field), KeyValuePair.Create("value", value), KeyValuePair.Create("__RequestVerificationToken", ServerFactory.Token(html)) }),
            };
            req.Headers.Add("X-Autosave", "1");
            var res = await me.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            return JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.Clone();
        }
        var speed = await Set("quality", "1000");
        Assert.True(speed.GetProperty("ok").GetBoolean());
        Assert.Equal("611.66 Mm/s (+20 %)", speed.GetProperty("show").GetProperty("speed-" + ship.Row.Id).GetString());
        var fuel = await Set("fuel", "1000");
        Assert.Equal("113.34 per km (-20 %)", fuel.GetProperty("show").GetProperty("fuel-" + ship.Row.Id).GetString());
        var wrong = await Set("fuel", "1200");
        Assert.False(wrong.GetProperty("ok").GetBoolean());
        Assert.Equal(FleetService.BadQuality, wrong.GetProperty("said").GetString());
        Assert.Equal(1000, fleet.Fleet("zzoega")[0].Row.FuelQuality);   // the wrong value is not saved
        var empty = await Set("fuel", "");                               // emptied: as bought
        Assert.Equal("141.68 per km", empty.GetProperty("show").GetProperty("fuel-" + ship.Row.Id).GetString());
    }
}
