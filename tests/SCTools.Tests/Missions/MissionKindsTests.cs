using SCTools.Missions;
using SCTools.Tests.Support;

namespace SCTools.Tests.Missions;

/// <summary>What the Mission Library groups missions by: their name (the same mission at another place is the same), a hauling contract by its contract line.</summary>
public class MissionKindsTests
{
    static readonly string M1 = GameLogWriter.MissionId(1);
    const string Other = "33333333-3333-3333-3333-333333333333";

    [Fact]
    public void A_hauling_contract_is_its_contract_line_and_any_other_mission_its_name()
    {
        var lines = new GameLogWriter().Login("zzoega").Accepted(M1)
            .Marker(M1, string.Format(GameLogWriter.HaulingContract, 3), "pickup_aaaa_0", "900000000101", GameLogWriter.Gaslight).Lines
            .Append($"<2026-10-21T09:00:00.000Z> [Notice] <SHUDEvent_OnNotification> Added notification \"Contract Accepted: Stop Rival Attack at Asteroid Mining Base <EM4>[200 Rep] [BP]*</EM4>: \" [84] to queue. New queue size: 1, MissionId: [{Other}], ObjectiveId: [] [Missions][Comms]")
            .Append($"<2026-10-21T09:00:00.500Z> [Notice] <CSCPlayerMissionLog::MissionStartCommsNotification> MissionStart comms notification will not be sent - This mission has no MissionStart comms setup. ContractId: [26b8773e-0b96-40b4-a33a-27a4e9877e75]. MissionId: {Other} [Missions][Comms]");

        var missions = SCTools.GameLog.OldLog.Read(lines).Missions;

        var haul = missions.Single(m => m.MissionId == M1.ToLowerInvariant());
        Assert.Equal("3 To Ruin", haul.Hauling);
        Assert.Equal("hauling:3 to ruin", MissionKinds.KeyOf(haul.Title, haul.Hauling));
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
