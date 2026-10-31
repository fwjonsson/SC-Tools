using SCTools.Missions;

namespace SCTools.Tests.Missions;

/// <summary>Every mission of a log, any kind: what kind it is (the ContractId), its title, shared or taken, and how it ended. Formats from Frank's logs of 21 Oct 2026.</summary>
public class MissionReaderTests
{
    const string M1 = "dcce75a8-aed9-42b5-9be7-840b55d185ed";
    const string M2 = "83477341-0572-4565-a9f6-4941ffc5c8ce";
    const string M3 = "563d1685-4bc9-4848-8fb5-ad252639e017";
    const string C1 = "26b8773e-0b96-40b4-a33a-27a4e9877e75";
    const string C2 = "8d8fc20d-61ad-4433-a3f1-948d8e05104a";

    static string Contract(string time, string c, string m) =>
        $"<2026-10-21T{time}Z> [Notice] <CSCPlayerMissionLog::MissionStartCommsNotification> MissionStart comms notification will not be sent - This mission has no MissionStart comms setup. ContractId: [{c}]. MissionId: {m} [Team_MissionFeatures][Missions][Comms]";

    static string Note(string time, string kind, string title, string m) =>
        $"<2026-10-21T{time}Z> [Notice] <SHUDEvent_OnNotification> Added notification \"Contract {kind}: {title}: \" [84] to queue. New queue size: 1, MissionId: [{m}], ObjectiveId: [] [Team_CoreGameplayFeatures][Missions][Comms]";

    static string End(string time, string m, string type) =>
        $"<2026-10-21T{time}Z> [Notice] <EndMission> Ending mission for player. MissionId[{m}] Player[zzoega] PlayerId[493008374737] CompletionType[{type}] Reason[Player left] [Team_MissionFeatures][Missions]";

    static IReadOnlyList<MissionSeen> Read(params string[] lines)
    {
        var r = new MissionReader();
        foreach (var l in lines) r.ProcessLine(l);
        return r.Missions;
    }

    [Fact]
    public void A_mission_has_its_kind_its_title_and_how_it_ended()
    {
        const string title = "Senior | Stellar Large Haul | to Ruin Station <EM4>[50/200/250/500/1000/2000/4000 Rep]</EM4>";
        var lines = new[]
        {
            Note("08:23:57.000", "Accepted", title, M1), Contract("08:23:57.563", C1, M1),
            Note("09:00:00.000", "Accepted", title, M1),   // the game says it again: the first counts
            Note("13:08:37.014", "Complete", title, M1), End("13:08:38.000", M1, "Complete"),
        };
        var m = Assert.Single(Read(lines));
        Assert.Equal((M1, C1), (m.MissionId, m.ContractId));
        Assert.Equal("Senior | Stellar Large Haul | to Ruin Station [50/200/250/500/1000/2000/4000 Rep]", m.Title);   // without the game's markup
        Assert.Equal((new DateTime(2026, 10, 21, 8, 23, 57, DateTimeKind.Utc), MissionOutcome.Complete), (m.StartedUtc!.Value, m.Outcome!.Value));
        Assert.Equal(new DateTime(2026, 10, 21, 13, 8, 37, 14, DateTimeKind.Utc), m.EndedUtc);
        Assert.False(m.Shared);
        Assert.All(lines, l => Assert.True(new LineFilter().Keep(l)));   // the tracker sends these lines
    }

    [Fact]
    public void Shared_abandoned_failed_and_missions_of_the_other_account()
    {
        var seen = Read(
            Note("08:00:00.000", "Shared", "~mission(ReputationRank) | ~mission(CargoRouteToken)", M2),   // not named yet
            Note("08:00:01.000", "Shared", "Combat Gauntlet - Scenario #7", M2), Contract("08:00:02.000", C2, M2), End("08:35:32.282", M2, "Abandon"),
            Note("09:00:00.000", "Accepted", "Stop Rival Attack", M3), Note("09:10:00.000", "Failed", "Stop Rival Attack", M3), End("09:10:01.000", M3, "Abandon"),
            End("10:00:00.000", "33333333-3333-3333-3333-333333333333", "Abandon"));   // never his: it has no kind
        Assert.Equal(3, seen.Count);
        Assert.Equal((true, "Combat Gauntlet - Scenario #7", MissionOutcome.Abandon, C2), (seen[0].Shared, seen[0].Title, seen[0].Outcome!.Value, seen[0].ContractId));
        Assert.Equal(MissionOutcome.Fail, seen[1].Outcome);   // failed first: the abandon after it does not change that
        Assert.Null(seen[1].ContractId);
        Assert.Null(seen[2].ContractId);
    }

    [Fact]
    public void A_share_has_no_mission_id_and_the_accept_just_after_it_is_the_shared_one()
    {
        // zzatvo's log of 29 Oct 2026: three shares from zzoega (no mission id, the contract's template for a name), then his three accepts with the real
        // missions; then a contract he took himself. The share's own mission (all zeros) is no mission
        const string Zero = "00000000-0000-0000-0000-000000000000";
        const string M4 = "5787bf8b-1111-4111-8111-111111111111";
        var template = "~mission(ReputationRank) | ~mission(CargoRouteToken) ~mission(CargoGradeToken) Haul | to ~mission(Destination|name)";
        var r = new MissionReader();
        foreach (var line in new[]
        {
            Note("19:59:24.318", "Shared", template, Zero), Note("19:59:26.009", "Shared", template, Zero), Note("19:59:27.573", "Shared", template, Zero),
            Note("19:59:29.152", "Accepted", " Senior | Stellar Large Haul | to Ruin Station", M1),
            Note("19:59:29.269", "Accepted", " Senior | Stellar Large Haul | to Ruin Station", M2),
            Note("19:59:29.367", "Accepted", " Senior | Stellar Large Haul | to Ruin Station", M3),
            Note("20:01:23.135", "Accepted", " Experienced | Stellar Large Haul | to Ruin Station", M4),
            Note("20:30:00.000", "Accepted", " Senior | Stellar Large Haul | to Ruin Station", M1),   // the game says it again at a login
        })
            r.ProcessLine(line);
        var seen = r.Missions;
        Assert.Equal(new[] { M1, M2, M3, M4 }, seen.Select(m => m.MissionId));
        Assert.Equal(new[] { true, true, true, false }, seen.Select(m => m.Shared));
        Assert.Equal(new DateTime(2026, 10, 21, 19, 59, 29, 152, DateTimeKind.Utc), seen[0].StartedUtc);   // its first accept
    }

    [Fact]
    public void His_id_what_he_saw_of_the_others_and_what_a_mission_paid_him()
    {
        // zzoega's log of 9 Aug 2026: zzatvo shared a scenario with him and left it a second later, he completed it and the game paid him 14 rewards
        const string Zero = "00000000-0000-0000-0000-000000000000";
        var r = new MissionReader();
        foreach (var line in new[]
        {
            Note("23:01:14.000", "Shared", "Combat Gauntlet - Scenario #6", Zero),
            Note("23:01:15.867", "Accepted", " Combat Gauntlet - Scenario #6", M1),
            $"<2026-10-21T23:01:16.747Z> [Notice] <PlayerLeft> Received PlayerLeft push message for: mission_id {M1} - player_id 515604414831 [Team_GameServices][Missions]",
            $"<2026-10-21T23:01:17.000Z> [Notice] <PlayerJoined> Received PlayerJoined push message for: mission_id {M1} - player_id 493008374737 [Team_GameServices][Missions]",
            $"<2026-10-21T23:05:12.426Z> [Notice] <EndMission> Ending mission for player. MissionId[{M1}] Player[zzoega] PlayerId[493008374737] CompletionType[Complete] Reason[Mission Ended] [Team_MissionFeatures][Missions]",
            Note("23:05:12.430", "Complete", "Combat Gauntlet - Scenario #6", M1),
            "<2026-10-21T23:05:13.100Z> [Notice] <SHUDEvent_OnNotification> Added notification \"You've Earned: 14 Rewards",
            "<2026-10-21T23:05:13.100Z> Access Them at Your Primary Residence's Inventory",
            "<2026-10-21T23:30:00.000Z> [Notice] <SHUDEvent_OnNotification> Added notification \"You've Earned: 3 Rewards",   // long after: not this mission's
        })
            r.ProcessLine(line);
        var m = Assert.Single(r.Missions);
        Assert.True(m.Shared);
        Assert.Equal("493008374737", m.PlayerId);
        Assert.Equal(new[] { new MissionMember("515604414831", true, new DateTime(2026, 10, 21, 23, 1, 16, 747, DateTimeKind.Utc)) }, m.Members);   // not himself
        Assert.Equal(14, m.Rewards);
    }

    const string Me = "493008374737";

    static string Left(string time, string m, string id) =>
        $"<2026-10-21T{time}Z> [Notice] <PlayerLeft> Received PlayerLeft push message for: mission_id {m} - player_id {id} [Team_GameServices][Missions]";

    /// <summary>The login line, with his id in the game (the geid) or, should the game leave it out one day, without.</summary>
    static string Login(string time, string? id = Me) =>
        $"<2026-10-21T{time}Z> [Notice] <AccountLoginCharacterStatus_Character> Character: createdAt 1791557604958 - updatedAt 1791557605400 - {(id == null ? "" : $"geid {id} - ")}accountId 6552111 - name zzoega - state STATE_CURRENT [Team_GameServices][Login]";

    [Fact]
    public void A_mission_the_game_took_from_him_at_an_exit_to_the_menu_is_abandoned_for_him()
    {
        // zzoega's log of 31 Oct 2026 (#399): the bounty accepted at 03:04, the exit to the menu at 03:25:30, the game's PlayerLeft for his own id a second
        // later and nothing more about it; he came back and took another bounty
        const string bounty = "Reclusive Bounty: Burt Gearhart | Extreme Risk <EM4>[300 Rep]</EM4>";
        var r = new MissionReader();
        foreach (var line in new[] { Login("02:30:18.076"), Note("03:04:24.709", "Accepted", bounty, M1), Left("03:25:31.720", M1, Me) })
            r.ProcessLine(line);
        var gone = Assert.Single(r.Missions);
        Assert.Equal(Me, gone.PlayerId);   // from the login line: the log has no "Ending mission" line
        Assert.Null(gone.Outcome);         // not yet: the game may give it back at once (the next test), and the library writes down what this says

        r.ProcessLine(Note("03:42:45.193", "Accepted", "Reclusive Bounty: Hideaki Antal | Extreme Risk", M2));
        gone = r.Missions.Single(m => m.MissionId == M1);
        Assert.Equal(MissionOutcome.Abandon, gone.Outcome);
        Assert.Equal(new DateTime(2026, 10, 21, 3, 25, 31, 720, DateTimeKind.Utc), gone.EndedUtc);
        Assert.Null(gone.Members);   // his own leaving is not a member event
        Assert.Null(r.Missions.Single(m => m.MissionId == M2).Outcome);
    }

    [Fact]
    public void A_mission_the_game_gave_back_two_seconds_after_its_PlayerLeft_for_him_goes_on()
    {
        // zzoega, 28 Aug 2026: an event mission ("Retake Platforms From Nine Tails") said PlayerLeft for him and "Contract Accepted" again 1.7 seconds later
        const string title = "Retake Platforms From Nine Tails";
        var seen = Read(Login("19:00:00.000"), Note("19:10:00.000", "Accepted", title, M1), Left("19:16:09.298", M1, Me), Note("19:16:10.999", "Accepted", title, M1),
            Note("19:40:00.000", "Accepted", "Something Else", M2));
        var m = seen.Single(m => m.MissionId == M1);
        Assert.Null(m.Outcome);   // still his, half an hour on
        Assert.Equal(new DateTime(2026, 10, 21, 19, 10, 0, DateTimeKind.Utc), m.StartedUtc);   // the first accept, as always
    }

    [Fact]
    public void His_own_abandon_and_another_player_leaving_are_read_as_before()
    {
        // the normal abandon: the game's PlayerLeft for him and his "Ending mission" two milliseconds later (82 of 83 in Frank's logs of Aug-Oct 2026)
        var m = Read(Login("08:00:00.000"), Note("08:10:00.000", "Accepted", "Secure Site", M1), Left("08:20:00.564", M1, Me), End("08:20:00.566", M1, "Abandon"),
            Note("09:00:00.000", "Accepted", "Something Else", M2)).Single(m => m.MissionId == M1);
        Assert.Equal(MissionOutcome.Abandon, m.Outcome);
        Assert.Equal(new DateTime(2026, 10, 21, 8, 20, 0, 566, DateTimeKind.Utc), m.EndedUtc);   // the "Ending mission" line's time, as before

        // another player left it: a member, nothing more
        var s = Read(Login("08:00:00.000"), Note("08:10:00.000", "Accepted", "Secure Site", M1), Left("08:20:00.000", M1, "515604414831"),
            Note("09:00:00.000", "Accepted", "Something Else", M2)).Single(m => m.MissionId == M1);
        Assert.Null(s.Outcome);
        Assert.Equal("515604414831", Assert.Single(s.Members!).PlayerId);
    }

    [Fact]
    public void His_id_comes_from_the_markers_that_name_him_when_the_login_line_has_none()
    {
        // every objective marker of his goes into his data bank with his name and id; a shared mission's markers name the other players too
        var bank = $"<2026-10-21T08:10:01.000Z> [Notice] <CObjectiveMarkerComponent::AddToPlayerDataBank> MissionObjectiveMarker_3846[3846] - Added to DataBank of Player: zzoega[{Me}] - ZonePos: x: 7005.295223, y: -8307.984549, z: 25017.560228, missionId[{M1}], objectiveId[96bdc0c4-1f4e-374a-82d5-4ded19399564] [Team_MissionFeatures][Missions]";
        var m = Read(Login("08:00:00.000", id: null), Note("08:10:00.000", "Accepted", "Secure Site", M1), bank.Replace($"zzoega[{Me}]", "zzatvo[515604414831]"), bank,
            Left("08:20:00.000", M1, Me), Note("09:00:00.000", "Accepted", "Something Else", M2)).Single(m => m.MissionId == M1);
        Assert.Equal(Me, m.PlayerId);
        Assert.Equal(MissionOutcome.Abandon, m.Outcome);
    }
}
