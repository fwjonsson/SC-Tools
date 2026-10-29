using SCTools.Missions;
using SCTools.Tests.Support;

namespace SCTools.Tests.Missions;

/// <summary>What the Mission Library groups missions by: their name (the same mission at another place is the same), a hauling contract by its contract line.</summary>
public class MissionKindsTests
{
    static readonly string M1 = GameLogWriter.MissionId(1);
    const string Other = "33333333-3333-3333-3333-333333333333";

    [Fact]
    public void A_hauling_contract_is_its_name_like_any_other_mission_and_its_contract_line_without_one()
    {
        var lines = new GameLogWriter().Login("zzoega").Accepted(M1)
            .Marker(M1, string.Format(GameLogWriter.HaulingContract, 3), "pickup_aaaa_0", "900000000101", GameLogWriter.Gaslight).Lines
            .Append($"<2026-10-21T09:00:00.000Z> [Notice] <SHUDEvent_OnNotification> Added notification \"Contract Accepted: Stop Rival Attack at Asteroid Mining Base <EM4>[200 Rep] [BP]*</EM4>: \" [84] to queue. New queue size: 1, MissionId: [{Other}], ObjectiveId: [] [Missions][Comms]")
            .Append($"<2026-10-21T09:00:00.500Z> [Notice] <CSCPlayerMissionLog::MissionStartCommsNotification> MissionStart comms notification will not be sent - This mission has no MissionStart comms setup. ContractId: [26b8773e-0b96-40b4-a33a-27a4e9877e75]. MissionId: {Other} [Missions][Comms]");

        var missions = SCTools.GameLog.OldLog.Read(lines).Missions;

        var haul = missions.Single(m => m.MissionId == M1.ToLowerInvariant());
        Assert.Equal("3 To Ruin", haul.Hauling);
        Assert.Equal("senior | stellar large haul | to ruin station [50/200/250/500/1000/2000/4000 rep]", MissionKinds.KeyOf(haul.Title, haul.Hauling));   // by its name (29 Oct 2026)
        Assert.Equal("hauling:3 to ruin", MissionKinds.KeyOf(null, haul.Hauling));   // no name in any log: its contract line
        var other = missions.Single(m => m.MissionId == Other);
        Assert.Null(other.Hauling);
        Assert.Equal("stop rival attack at asteroid mining base [200 rep] [bp]*", MissionKinds.KeyOf(other.Title, other.Hauling));
    }

    [Fact]
    public void A_name_is_the_same_in_any_case_and_with_any_spaces_and_one_the_game_filled_in_wins()
    {
        Assert.Equal(MissionKinds.TitleKey("Combat Gauntlet - Scenario #7"), MissionKinds.TitleKey(" combat  gauntlet -  Scenario #7 "));
        Assert.Null(MissionKinds.TitleKey("  "));
        Assert.Equal("Stop Rival Attack at Asteroid Mining Base", MissionKinds.Better("Stop Rival Attack at ~mission(Location)", "Stop Rival Attack at Asteroid Mining Base"));
        Assert.Equal("Stop Rival Attack", MissionKinds.Better("Stop Rival Attack", "Something else"));
        Assert.Equal("x", MissionKinds.Better(null, "x"));
    }
}

/// <summary>A bounty is a kind by its kind and risk, not by its target (that is what it pays by); a target with a nickname in quotes is read whole.</summary>
public class BountyKindsTests
{
    [Theory]
    [InlineData("Reclusive Bounty: Kafu \"Vaco\" Dudley | Extreme Risk [300 Rep]", "Reclusive Bounty | Extreme Risk [300 Rep]")]   // zzatvo's log, 26 Oct
    [InlineData("Reclusive Bounty: Craig Clayden | Extreme Risk [300 Rep]", "Reclusive Bounty | Extreme Risk [300 Rep]")]
    [InlineData("Reclusive Bounty: ~mission(TargetName) | Very-High Risk [250 Rep]", "Reclusive Bounty | Very-High Risk [250 Rep]")]   // not filled in yet
    [InlineData("Reclusive Bounty: Somebody", "Reclusive Bounty")]
    [InlineData("Stop Rival Attack at Asteroid Mining Base [200 Rep] [BP]*", "Stop Rival Attack at Asteroid Mining Base [200 Rep] [BP]*")]
    public void A_bounty_is_its_kind_and_risk(string title, string kind)
    {
        Assert.Equal(kind, MissionKinds.KindTitle(title));
        Assert.Equal(MissionKinds.TitleKey(kind), MissionKinds.TitleKey(title));
    }

    [Fact]
    public void A_name_with_quotes_in_it_is_read_whole()
    {
        var reader = new MissionReader();
        reader.ProcessLine("<2026-10-26T18:16:53.811Z> [Notice] <SHUDEvent_OnNotification> Added notification \"Contract Accepted:  Reclusive Bounty: Kafu \"Vaco\" Dudley | Extreme Risk <EM4>[300 Rep]</EM4>: \" [85] to queue. New queue size: 1, MissionId: [f4dd25aa-c7d4-409f-8f3e-6c1a2b3c4d5e], ObjectiveId: [] [Team_CoreGameplayFeatures][Missions][Comms]");
        var m = Assert.Single(reader.Missions);
        Assert.Equal("Reclusive Bounty: Kafu \"Vaco\" Dudley | Extreme Risk [300 Rep]", m.Title);
    }
}
