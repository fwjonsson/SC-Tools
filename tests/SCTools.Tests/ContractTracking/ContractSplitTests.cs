using SCTools.Missions;

namespace SCTools.Tests.ContractTracking;

/// <summary>Frank's split rule of 30 Oct 2026, as ContractSplit has it: the aUEC evenly among who has the contract when it pays out, the scrip to who took it.</summary>
public class ContractSplitTests
{
    static readonly DateTime At = new(2026, 10, 30, 20, 0, 0, DateTimeKind.Utc);
    static readonly ContractReward Pays = new(90_000, 9, 3);

    static ContractHolder Took(string p, MissionOutcome? o = MissionOutcome.Complete) => new(p, true, o, o == null ? null : At);
    static ContractHolder Shared(string p, MissionOutcome? o = MissionOutcome.Complete) => new(p, false, o, o == null ? null : At.AddSeconds(1));

    [Fact]
    public void The_aUEC_is_split_among_who_completed_it_and_the_scrip_goes_to_who_took_it()
    {
        var c = new TrackedContract("m1", "stop rival attack", "Stop Rival Attack", new[] { Took("zzoega"), Shared("zzatvo"), Shared("zzoii") }, Pays);
        Assert.Equal(new ContractReward(30_000, 9, 3), c.PartOf("ZZOEGA"));
        Assert.Equal(new ContractReward(30_000, 0, 0), c.PartOf("zzatvo"));
        Assert.Equal("zzoega", c.Taker);
        Assert.Equal((MissionOutcome?)MissionOutcome.Complete, c.Outcome);
        Assert.Equal(At, c.EndedUtc);   // when it paid out
    }

    [Fact]
    public void Who_left_it_before_it_paid_out_and_who_was_never_in_it_get_nothing()
    {
        var c = new TrackedContract("m1", null, null, new[] { Took("zzoega"), Shared("zzoii", MissionOutcome.Abandon) }, Pays);
        Assert.Equal(new ContractReward(90_000, 9, 3), c.PartOf("zzoega"));
        Assert.Null(c.PartOf("zzoii"));
        Assert.Null(c.PartOf("zzatvo"));   // in the party, without the share
        Assert.Equal(new[] { "zzoega" }, c.Payees);
    }

    [Fact]
    public void Two_copies_taken_separately_each_pay_their_player_in_full()
    {
        var a = new TrackedContract("m1", "kill the pirate", null, new[] { Took("zzoega") }, Pays);
        var b = new TrackedContract("m2", "kill the pirate", null, new[] { Took("zzatvo") }, Pays);
        Assert.Equal(new ContractReward(90_000, 9, 3), a.PartOf("zzoega"));
        Assert.Equal(new ContractReward(90_000, 9, 3), b.PartOf("zzatvo"));
    }

    [Fact]
    public void A_run_whose_accept_was_not_read_is_his_when_he_was_alone_in_it()
    {
        var alone = new TrackedContract("g1", null, null, new[] { new ContractHolder("zzoega", null, MissionOutcome.Complete, At) }, Pays);
        Assert.Equal(new ContractReward(90_000, 9, 3), alone.PartOf("zzoega"));
        var shared = new TrackedContract("g2", null, null, new[] { Took("zzoega"), new ContractHolder("zzatvo", null, MissionOutcome.Complete, At) }, Pays);
        Assert.Equal(new ContractReward(45_000, 0, 0), shared.PartOf("zzatvo"));
    }

    [Fact]
    public void The_planner_s_payout_wins_and_pays_a_member_without_the_contract_in_his_log()
    {
        var payout = new PayoutRecord { MissionId = "h1", Time = At, Contract = "Carbon, 2 stops", Payout = 90_000, Members = { "zzoega", "zzoii" }, Each = 45_000 };
        var c = new TrackedContract("h1", "haul the carbon", null, new[] { Took("zzoega"), Shared("zzatvo") }, new ContractReward(500_000, null, null), payout);
        Assert.Equal(new ContractReward(45_000, null, null), c.PartOf("zzoega"));
        Assert.Equal(new ContractReward(0, null, null), c.PartOf("zzatvo"));    // he had it, the planner did not pay him (its kept payout)
        Assert.Equal(new ContractReward(45_000, null, null), c.PartOf("zzoii"));   // no scrip: not in his log
        Assert.Equal(new[] { "zzoega", "zzoii" }, c.Payees);
    }

    [Fact]
    public void The_planner_pays_who_has_it_and_without_anybody_the_one_who_took_it()
    {
        Assert.Equal(new[] { "zzoega", "zzatvo" }, ContractSplit.PlannerPayees(new[] { "zzoega", "zzoii", "ZZOEGA", "zzatvo" }, n => n != "zzoii", null, Array.Empty<string>()));
        Assert.Equal(new[] { "zzatvo" }, ContractSplit.PlannerPayees(new[] { "zzoii" }, _ => false, "zzatvo", new[] { "zzoega" }));
        Assert.Equal(new[] { "zzoega" }, ContractSplit.PlannerPayees(new[] { "zzoii" }, _ => false, null, new[] { "zzoega", "ZZOEGA" }));
    }
}
