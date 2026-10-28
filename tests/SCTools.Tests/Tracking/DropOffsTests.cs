using SCTools.Tests.Support;

namespace SCTools.Tests.Tracking;

/// <summary>From-Ruin contracts: the loads still to deliver, cut into containers and into loads the planner can take along, and the SCU you typed.</summary>
public class DropOffsTests
{
    static readonly string W = GameLogWriter.MissionId(41);
    const string GaslightZone = "900000004201";
    const string PatchZone = "900000004202";

    /// <summary>A from-Ruin contract with two drop-offs: Gaslight (76 SCU, still to do) and Patch City (already delivered).</summary>
    static GameLogWriter WasteLog()
    {
        var contract = string.Format(GameLogWriter.WasteContract, 2);
        return new GameLogWriter().Login("Pilot_A")
            .Minutes(1).Accepted(W)
            .Marker(W, contract, "dropoff_w_0", GaslightZone, GameLogWriter.Gaslight)
            .Marker(W, contract, "dropoff_w_1", PatchZone, GameLogWriter.PatchCity)
            .DeliverPopup(W, "dropoff_w_0", 76, "Waste", "Gaslight at the L2 Lagrange of Pyro V")
            .Minutes(30).Objective(W, "dropoff_w_1", "COMPLETED");
    }

    static Tracker Loaded(AppData? data = null)
    {
        var t = new Tracker(data ?? new AppData { Party = new List<string> { "Pilot_A" } }, _ => { });
        t.Load(new[] { WasteLog().Read() });
        return t;
    }

    [Fact]
    public void A_load_is_cut_into_the_biggest_containers()
    {
        Assert.Equal("4×32 + 16 + 2 + 1", DropOffs.ContainersText(147));
        Assert.Equal("32", DropOffs.ContainersText(32));
        Assert.Equal("152 SCU: 4×32 + 24", DropOffs.ScuText(152));
        Assert.Equal("", DropOffs.ScuText(0));
        Assert.Equal("152 SCU: 4×32 + 24 | ? SCU", DropOffs.LoadText(new[] { 152, 0 }));
        Assert.Equal("? SCU", DropOffs.LoadText(new[] { 0, 0 }));
    }

    [Fact]
    public void The_drop_offs_still_to_do_are_the_ones_not_delivered()
    {
        var t = Loaded();
        var open = Assert.Single(t.DropOffs.OpenDrops());
        Assert.Equal("dropoff_w_0", open.Objective);
        Assert.Equal(PlayedSession.Gaslight, open.Station);
        Assert.Equal(76, open.Scu);
        Assert.Equal("Waste for Gaslight", t.DropOffs.DropLabel(W + "|dropoff_w_0"));
    }

    [Fact]
    public void A_load_for_a_station_goes_to_the_planner_as_one_chunk()
    {
        var t = Loaded();
        var chunk = Assert.Single(t.DropOffs.DropChunks(t.DropOffs.OpenDrops(), new List<string> { "Pilot_A" }, 3));
        Assert.Equal(PlayedSession.Gaslight, chunk.Station);
        Assert.Equal(1, chunk.Batches);
        Assert.Equal(76, chunk.Scu);
        Assert.Equal(new[] { W + "|dropoff_w_0" }, chunk.Keys);
        Assert.Empty(chunk.Allowed);   // everybody who flies has the contract
        Assert.Empty(t.DropOffs.DropChunks(t.DropOffs.OpenDrops(), new List<string> { "Pilot_A" }, 0));   // nobody has room
    }

    static readonly string D = GameLogWriter.MissionId(42);

    /// <summary>The waste contract above, and a direct one for Gaslight as well (one drop-off, 421 SCU), as zzatvo's log of 23 Oct 15:09 writes it.</summary>
    static Tracker WithDirect()
    {
        var w = WasteLog().Minutes(1).Accepted(D)
            .Marker(D, GameLogWriter.DirectContract, "pickup_d_0", "900000004204", GameLogWriter.RuinStation)
            .Marker(D, GameLogWriter.DirectContract, "dropoff_d_0", "900000004203", GameLogWriter.Gaslight)
            .DeliverPopup(D, "dropoff_d_0", 421, "Waste", "Gaslight at the L2 Lagrange of Pyro V");
        var t = new Tracker(new AppData { Party = new List<string> { "Pilot_A" } }, _ => { });
        t.Load(new[] { w.Read() });
        return t;
    }

    [Fact]
    public void A_direct_contract_is_a_drop_off_to_do_like_the_waste()
    {
        var t = WithDirect();
        var direct = t.DropOffs.OpenDrops().Single(d => d.Mission.Id == D);
        Assert.True(direct.Mission.IsDirect);
        Assert.Equal(PlayedSession.Gaslight, direct.Station);
        Assert.Equal(421, direct.Scu);
        Assert.Equal("Direct From Ruin", Contracts.ContractName(direct.Mission));
    }

    [Fact]
    public void A_direct_load_goes_out_with_the_waste_for_the_same_station_as_far_as_a_ship_holds_them()
    {
        var t = WithDirect();
        var together = Assert.Single(t.DropOffs.DropChunks(t.DropOffs.OpenDrops(), new List<string> { "Pilot_A" }, 3, 741));
        Assert.Equal(PlayedSession.Gaslight, together.Station);
        Assert.Equal(2, together.Batches);
        Assert.Equal(76 + 421, together.Scu);
        Assert.Equal("Waste", together.Cargo);
        Assert.Equal(new[] { W + "|dropoff_w_0", D + "|dropoff_d_0" }, together.Keys);

        var apart = t.DropOffs.DropChunks(t.DropOffs.OpenDrops(), new List<string> { "Pilot_A" }, 3, 450);   // a smaller ship: 76 + 421 does not fit
        Assert.Equal(new[] { 1, 1 }, apart.Select(c => c.Batches));
    }

    [Fact]
    public void Typed_scu_fills_in_what_the_game_did_not_say_and_the_game_wins()
    {
        var data = new AppData { Party = new List<string> { "Pilot_A" } };
        var t = Loaded(data);
        t.DropOffs.SetTypedScu(W + "|dropoff_w_1", 120);
        t.DropOffs.SetTypedScu(W + "|dropoff_w_0", 999);
        t.Load(new[] { WasteLog().Read() });
        var m = t.State.Log!.Missions[W];
        Assert.Equal(120, m.ObjScu["dropoff_w_1"]);
        Assert.Equal(76, m.ObjScu["dropoff_w_0"]);   // the game's own number

        t.DropOffs.SetTypedScu(W + "|dropoff_w_1", 0);
        Assert.False(data.TypedScu.ContainsKey(W + "|dropoff_w_1"));
    }

    [Fact]
    public void The_drop_off_cell_of_a_station_says_what_to_bring_and_how_big_it_is()
    {
        var waste = new Mission { OutCargo = "Waste" };
        var drops = new[] { new DropBatch { Mission = waste, Scu = 76 }, new DropBatch { Mission = waste, Scu = 76 } };
        Assert.Equal("Waste ×2 (152 SCU: 4×32 + 24)", DropOffs.StationText(drops));
        Assert.Equal("Cargo ×1", DropOffs.StationText(new[] { new DropBatch { Mission = new Mission() } }));
    }
}
