using SCTools.Tests.Support;

namespace SCTools.Tests.Tracking;

/// <summary>
/// The hauling jobs of every system (HaulJobs): the missions with drop-offs that are not the Hauling Planner's own Pyro contracts, their stops named and
/// placed from the objectives' texts (or the zone another objective named), and their SCU from the game, a hauler or the Mission Library, split evenly.
/// The lines are copied from zzoii's logs of 10 Oct 2026 (the RSI Disc. Month missions in Stanton).
/// </summary>
public class HaulJobsTests
{
    const string AtoB = "Iasi_HaulCargo_AtoB_Small_PartySupplies";
    const string Courier = "Iasi_2H_Box_Courier_0";
    static readonly (double, double, double) InStation = (12.000012, -15.999995, -7.75);              // a marker in the station's own zone
    static readonly (double, double, double) OnPlanet = (-18119.931151, 180393.511132, -232742.694013);   // a marker in a planet's zone

    [Fact]
    public void An_A_to_B_haul_is_a_job_with_its_drop_off_named_and_the_SCU_the_game_says()
    {
        var m = GameLogWriter.MissionId(1);
        var w = new GameLogWriter().Login("zzoii").AcceptedAs(m, "RSI Disc. Month: Ling Small Haul")
            .Marker(m, AtoB, "dropoff_aaaa_0", "862738706222", InStation)
            .Marker(m, AtoB, "pickup_aaaa_0", "862737932760", OnPlanet)
            .ObjectivePopup(m, "dropoff_aaaa_0", "Deliver 0/4 SCU of Distilled Spirits to Port Tressler")
            .ObjectivePopup(m, "dropoff_aaaa_1", "Deliver 0/2 SCU of Party Favors to Port Tressler")
            .ObjectivePopup(m, "dropoff_aaaa_2", "Deliver 0/2 SCU of Souvenirs to Port Tressler")
            .Minutes(5).Objective(m, "pickup_aaaa_0", "COMPLETED").Objective(m, "pickup_aaaa_1", "COMPLETED").Objective(m, "pickup_aaaa_2", "COMPLETED");
        Assert.All(w.Lines, l => Assert.True(new LineFilter().Keep(l), l));   // the trackers send these lines
        var log = w.Read();
        Assert.Equal(4, log.Missions[m].ObjScu["dropoff_aaaa_0"]);   // a cargo of two words

        var job = Assert.Single(HaulJobs.Of(log.Missions.Values, Atlas.Default));
        Assert.Equal(("RSI Disc. Month: Ling Small Haul", 8.0, ScuFrom.Game, "stanton"), (job.Title, job.Scu, job.ScuFrom, job.System));
        var drop = Assert.Single(job.Drops);
        Assert.Equal(("dropoff_aaaa", "stanton/port-tressler", 8.0, false), (drop.Key, drop.Place?.Key, drop.Scu, drop.Done));
        var pickup = Assert.Single(job.Pickups);
        Assert.Equal(((Place?)null, 8.0, GameLogWriter.Start.AddMinutes(5)), (pickup.Place, pickup.Scu, pickup.DoneUtc));   // the game does not name it; picked up
        Assert.Equal("a place the game does not name", pickup.Shown);
        Assert.False(job.Delivered);

        // the Hauling Planner's own contracts are not jobs
        var own = GameLogWriter.MissionId(2);
        var planner = new GameLogWriter().Login("zzoega").Accepted(own)
            .Marker(own, string.Format(GameLogWriter.HaulingContract, 2), "pickup_bbbb_0", "900000000202", GameLogWriter.PatchCity).Read();
        Assert.Empty(HaulJobs.Of(planner.Missions.Values, Atlas.Default));
    }

    [Fact]
    public void A_pickup_the_game_does_not_name_is_named_by_its_zone_exactly_or_only_by_the_planet()
    {
        var (a, b, c, d) = (GameLogWriter.MissionId(1), GameLogWriter.MissionId(2), GameLogWriter.MissionId(3), GameLogWriter.MissionId(4));
        var log = new GameLogWriter().Login("zzoii")
            // Port Tressler's own zone (a marker right in it): a pickup in it is at Port Tressler
            .AcceptedAs(a, "RSI Disc. Month: Ling Small Haul").Marker(a, AtoB, "dropoff_a_0", "862738706222", InStation)
            .ObjectivePopup(a, "dropoff_a_0", "Deliver 0/4 SCU of Souvenirs to Port Tressler")
            .AcceptedAs(b, "RSI Disc. Month: Ling Small Haul").Marker(b, AtoB, "pickup_b_0", "862738706222", InStation).Marker(b, AtoB, "dropoff_b_0", "862737820988", OnPlanet)
            // ArcCorp's zone: Baijini Point and Area18 (Riker Memorial Spaceport) are in it, so a pickup in it is only "near ArcCorp"
            .AcceptedAs(c, "RSI Disc. Month: UCM Order (L)").Marker(c, "Iasi_Salvage_Large", "dropoff_c_0", "862237792652", (-771960.5625, 1000, 2000))
            .ObjectivePopup(c, "dropoff_c_0", "Deliver 0/250 SCU of Construction Salvage to Baijini Point")
            .AcceptedAs(d, "RSI Disc. Month: UCM Order (L)").Marker(d, "Iasi_Salvage_Large_0", "dropoff_d_0", "862237792652", (-748272.07809, 1000, 2000))
            .ObjectivePopup(d, "dropoff_d_0", "Deliver 0/250 SCU of Construction Salvage to Riker Memorial Spaceport")
            .Marker(b, AtoB, "pickup_e_0", "862237792652", OnPlanet)
            .Read();
        var zones = HaulJobs.ZoneNames(log.Missions.Values, Atlas.Default);
        Assert.Equal(new ZoneName("Port Tressler", StarMap.Find("stanton/port-tressler"), false), zones["862738706222"]);
        Assert.Equal(new ZoneName("near ArcCorp", StarMap.Find("stanton/arccorp"), true), zones["862237792652"]);

        var job = HaulJobs.Of(log.Missions.Values, Atlas.Default).Single(j => j.Mission.Id == b);
        Assert.Equal(new[] { ("Port Tressler", "stanton/port-tressler", false), ("near ArcCorp", "stanton/arccorp", true) },
            job.Pickups.Select(p => (p.Shown, p.Place!.Key, p.Near)));
        Assert.Equal(("a place the game does not name", ScuFrom.NotKnown, 0.0), (job.Drops[0].Shown, job.ScuFrom, job.Scu));   // ArcCorp's other zone: nobody named it
    }

    [Fact]
    public void Without_the_game_s_SCU_a_job_has_the_hauler_s_else_the_library_s_split_evenly()
    {
        var m = GameLogWriter.MissionId(1);
        var log = new GameLogWriter().Login("zzoii").AcceptedAs(m, "RSI Disc. Month: Courier Run (Exp. E)")
            .Marker(m, Courier, "pickup_f1_0", "862737938329", (-34.999981, 1, 2)).Marker(m, Courier, "pickup_f2_0", "862737946828", (4.000014, 1, 2))
            .Marker(m, Courier, "dropoff_d1_0", "862738093314", (265321.16624, 1, 2))
            .ObjectivePopup(m, "dropoff_d1_0", "Deliver Medical Supplies To RSI Event Platform")
            .ObjectivePopup(m, "pickup_f1_0", "Collect Medical Supplies From Shallow Fields Station at Crusader's L4 Lagrange point")
            .ObjectivePopup(m, "pickup_f2_0", "Collect Food Supplies From microTech Logistics Depot S4LD13 on microTech")
            .Read();
        var missions = log.Missions.Values;
        var job = Assert.Single(HaulJobs.Of(missions, Atlas.Default, haulerScu: _ => 6, libraryScu: _ => 10));
        Assert.Equal((6.0, ScuFrom.Hauler), (job.Scu, job.ScuFrom));
        Assert.Equal(new[] { ("stanton/cru-l4-shallow-fields-station", 3.0), ("stanton/microtech", 3.0) }, job.Pickups.Select(p => (p.Place!.Key, p.Scu)));   // evenly
        Assert.Equal(("RSI Event Platform", (Place?)null, 6.0), (job.Drops[0].Shown, job.Drops[0].Place, job.Drops[0].Scu));
        Assert.Equal((10.0, ScuFrom.Library), Fields(HaulJobs.Of(missions, Atlas.Default, libraryScu: _ => 10).Single()));
        Assert.Equal((0.0, ScuFrom.NotKnown), Fields(HaulJobs.Of(missions, Atlas.Default).Single()));
        static (double, ScuFrom) Fields(HaulJob j) => (j.Scu, j.ScuFrom);
    }

    [Fact]
    public void Without_anybody_s_SCU_a_job_has_the_middle_of_the_Wiki_s_hauling_orders_for_its_contract()
    {
        const string master = "RedWind_Pyro_BulkGrade_Solar_CFP_StationToStation_Potassium_CargoHauling_Multi4ToSingle";
        const string uuid = "26e80306-6190-447f-a214-634d8ed8ca7d";   // the Star Citizen Wiki API's mission (the log's contractDefinitionId)
        var m = GameLogWriter.MissionId(1);
        var w = new GameLogWriter().Login("zzoega").AcceptedAs(m, "Master Hauler Needed for Large Shipment");
        foreach (var stop in new[] { "a", "b", "c", "d" }) w.Marker(m, master, "pickup_" + stop + "_0", "86000000000" + stop.Length, OnPlanet, uuid.ToUpperInvariant());
        var log = w.Marker(m, master, "dropoff_x_0", "862738706222", InStation, uuid).Read();
        Assert.Equal((master, uuid), (log.Missions[m].Contract, log.Missions[m].Definition));   // whatever names a string mod shows

        var wiki = new WikiContract(uuid, master, "Master Hauler Needed for Large Shipment", Enumerable.Repeat(new ScuRange(100, 150), 4).ToList());
        var job = Assert.Single(HaulJobs.Of(log.Missions.Values, Atlas.Default, wiki: x => x.Definition == uuid ? wiki : null));
        Assert.Equal((500.0, ScuFrom.Wiki, new ScuRange(400, 600), "~500 (wiki 400–600)"), (job.Scu, job.ScuFrom, job.WikiRange, HaulJobs.ScuText(job)));
        Assert.All(job.Pickups, p => Assert.Equal(125, p.Scu));
        // the library (and a hauler, and the game) come first
        Assert.Equal(ScuFrom.Library, HaulJobs.Of(log.Missions.Values, Atlas.Default, libraryScu: _ => 480, wiki: _ => wiki).Single().ScuFrom);
    }

    [Fact]
    public void The_title_comes_from_the_accept_or_the_share_and_the_merged_log_keeps_texts_and_titles()
    {
        var m = GameLogWriter.MissionId(1);
        var taker = new GameLogWriter().Login("zzoega").AcceptedAs(m, "RSI Disc. Month: Important Supply Haul <EM4>[50 Rep]</EM4>")
            .Marker(m, "Iasi_HaulCargo_AtoB_Supply_SecuritySupplies", "dropoff_x_0", "862737820988", OnPlanet).Read();
        var crew = new GameLogWriter().Login("zzatvo")
            .Marker(m, "Iasi_HaulCargo_AtoB_Supply_SecuritySupplies", "dropoff_x_0", "862737820988", OnPlanet).SharedAs(m, "RSI Disc. Month: Important Supply Haul")
            .ObjectivePopup(m, "dropoff_x_0", "Deliver 80/128 SCU of Ship Decoy Countermeasures to Baijini Point").Read();
        Assert.Equal("RSI Disc. Month: Important Supply Haul [50 Rep]", taker.Missions[m].Title);
        Assert.Equal("RSI Disc. Month: Important Supply Haul", crew.Missions[m].Title);
        var merged = HaulingLog.Merge(new[] { taker, crew });
        var job = Assert.Single(HaulJobs.Of(merged.Missions.Values, Atlas.Default));
        Assert.Equal(("RSI Disc. Month: Important Supply Haul [50 Rep]", "stanton/baijini-point", 128.0), (job.Title, job.Drops[0].Place?.Key, job.Scu));
    }

    [Theory]
    [InlineData("Port Tressler", "stanton/port-tressler")]
    [InlineData("Everus Harbor", "stanton/everus-harbor")]
    [InlineData("HUR-L1 Green Glade Station", "stanton/hur-l1-green-glade-station")]
    [InlineData("NB Int. Spaceport", "stanton/new-babbage")]
    [InlineData("Teasa Spaceport", "stanton/lorville")]
    [InlineData("Riker Memorial Spaceport", "stanton/area18")]
    [InlineData("Faint Glen Station at ArcCorp's L4 Lagrange point", "stanton/arc-l4-faint-glen-station")]
    [InlineData("microTech Logistics Depot S4LD13 on microTech", "stanton/microtech")]
    [InlineData("wreck site near Hurston", "stanton/hurston")]
    [InlineData("\"Wheeler's\" on Lyria", "stanton/lyria")]
    [InlineData("Ruin Station", "pyro/ruin-station")]
    [InlineData("RSI Event Platform", null)]
    public void A_place_a_mission_names_is_a_place_of_the_map(string name, string? key) =>
        Assert.Equal(key, HaulJobs.PlaceOf(name, null, Atlas.Default)?.Key);
}
