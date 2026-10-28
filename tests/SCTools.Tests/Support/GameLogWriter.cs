using System.Globalization;

namespace SCTools.Tests.Support;

/// <summary>
/// Writes Game.log lines the way the game writes them, for the tests. The formats are copied from real logs; the names, ids and numbers are made up.
/// </summary>
sealed class GameLogWriter
{
    public static readonly DateTime Start = new(2026, 10, 21, 8, 0, 0, DateTimeKind.Utc);

    public const string HaulingContract = "RedWind_Pyro_BulkGrade_Solar_HH_StationToRuin_Carbon_CargoHauling_Multi{0}ToSingle";
    public const string WasteContract = "RedWind_Pyro_BulkGrade_RegionD_HH_RuinToStation_Waste_CargoHauling_SingleToMulti{0}";
    public const string BountyContract = "BountyHuntersGuild_ShipWaveAttack_DrawOutBoss_Nyx_Super";   // "Reclusive Bounty: ... | Extreme Risk", in Nyx
    public const string DirectContract = "RedWind_Pyro_BulkGrade_RegionD_HH_RuinToStation_RegionC_Waste_CargoHauling_AtoB";   // "Experienced | DIRECT Stellar Large Haul | Ruin Station > Gaslight"

    // positions of stations as the game writes them in the objective markers (StationIds knows them)
    public static readonly (double X, double Y, double Z) Gaslight = (-19125581, 10432691, 17665446);
    public static readonly (double X, double Y, double Z) PatchCity = (2281205, -14897444, 2752554);
    public static readonly (double X, double Y, double Z) RodsFuel = (-21100600, -9275477, -9518769);
    public static readonly (double X, double Y, double Z) RuinStation = (1000, 2000, 3000);   // Ruin is not a pickup station: StationIds does not know it

    readonly List<string> _lines = new();
    DateTime _time = Start;

    public DateTime Now => _time;

    public GameLogWriter At(DateTime time)
    {
        _time = time;
        return this;
    }

    public GameLogWriter Minutes(double minutes) => At(_time + TimeSpan.FromMinutes(minutes));
    public GameLogWriter Seconds(double seconds) => At(_time + TimeSpan.FromSeconds(seconds));

    string Ts => "<" + _time.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture) + "Z>";

    GameLogWriter Add(string line)
    {
        _lines.Add(line);
        return this;
    }

    static string F(double v) => v.ToString("0.000000", CultureInfo.InvariantCulture);

    public GameLogWriter Login(string name) =>
        Add($"{Ts} [Notice] <AccountLoginCharacterStatus_Character> Character: createdAt 1791557604958 - updatedAt 1791557605400 - geid 100000000001 - accountId 1000001 - name {name} - state STATE_CURRENT [Team_GameServices][Login]");

    public GameLogWriter Accepted(string mission) =>
        Add($"{Ts} [Notice] <SHUDEvent_OnNotification> Added notification \"Contract Accepted:  Senior | Stellar Large Haul | to Ruin Station <EM4>[50/200/250/500/1000/2000/4000 Rep]</EM4>: \" [9] to queue. New queue size: 1, MissionId: [{mission}], ObjectiveId: [] [Missions][Comms]");

    public GameLogWriter Marker(string mission, string contract, string objective, string zone, (double X, double Y, double Z) at) =>
        Add($"{Ts} [Notice] <CLocalMissionPhaseMarker::CreateMarker> Creating objective marker: missionId [{mission}], generator name [RedWind_Hauling], contract [{contract}], contractDefinitionId[d8c129e3-b995-4a44-82ea-c21694da0260], objectiveId [{objective}], markerEntityId [1474], zoneHostId [{zone}], position [x: {F(at.X)}, y: {F(at.Y)}, z: {F(at.Z)}] [Team_MissionFeatures][Missions]");

    /// <summary>The marker of an objective the game gives no position (a bounty's: 0, 0, 0), with the marker's entity id.</summary>
    public GameLogWriter MissionMarker(string mission, string contract, string objective, string marker) =>
        Add($"{Ts} [Notice] <CLocalMissionPhaseMarker::CreateMarker> Creating objective marker: missionId [{mission}], generator name [BountyHuntersGuild_ShipWaveAttack], contract [{contract}], contractDefinitionId[d071209e-85e2-4946-99e4-8c1d9714dd06], objectiveId [{objective}], markerEntityId [{marker}], zoneHostId [862311775534], position [x: 0.000000, y: 0.000000, z: 0.000000] [Team_MissionFeatures][Missions]");

    /// <summary>The popup of a new objective: what it says.</summary>
    public GameLogWriter ObjectivePopup(string mission, string objective, string text) =>
        Add($"{Ts} [Notice] <SHUDEvent_OnNotification> Added notification \"New Objective: {text}: \" [12] to queue. New queue size: 2, MissionId: [{mission}], ObjectiveId: [{objective}] [Team_CoreGameplayFeatures][Missions][Comms]");

    /// <summary>The game pairs an objective's marker with its quantum beacon (the destination a player jumps to).</summary>
    public GameLogWriter BeaconPaired(string marker, string navPoint) =>
        Add($"{Ts} [Notice] <CObjectiveMarkerComponent::PairWithMissionNavpoint> MissionObjectiveMarker_{marker}[{marker}] - Paired with NavPoint: {navPoint}[865476577647] [Team_MissionFeatures][Missions]");

    public GameLogWriter Objective(string mission, string objective, string state) =>
        Add($"{Ts} [Notice] <ObjectiveUpserted> Received ObjectiveUpserted push message for: mission_id {mission} - objective_id {objective} - state MISSION_OBJECTIVE_STATE_{state} - created 0 - flags=ShowInLog| [Team_GameServices][Missions]");

    public GameLogWriter MissionEnded(string mission, string state) =>
        Add($"{Ts} [Notice] <MissionEnded> Received MissionEnded push message for: mission_id {mission} - mission_state MISSION_STATE_{state} [Team_GameServices][Missions]");

    public GameLogWriter Abandoned(string mission, string player) =>
        Add($"{Ts} [Notice] <EndMission> Ending mission for player. MissionId[{mission}] Player[{player}] PlayerId[100000000001] CompletionType[Abandon] Reason[Player left] [Team_MissionFeatures][Missions]");

    public GameLogWriter DeliverPopup(string mission, string objective, int scu, string cargo, string place) =>
        Add($"{Ts} [Notice] <SHUDEvent_OnNotification> Added notification \"New Objective: Deliver 0/{scu} SCU of {cargo} to {place}: \" [12] to queue. New queue size: 1, MissionId: [{mission}], ObjectiveId: [{objective}] [Missions][Comms]");

    /// <summary>He takes cargo out of a freight elevator (only in his own log).</summary>
    public GameLogWriter Unstow() =>
        Add($"{Ts} [Notice] <CEntityComponentFreightElevatorUIProvider::FillUnstowRequest> [FreightElevatorKioskUIProvider] FreightElevatorKiosk_FreightElevator_Util_HangarLarge_Rund[894041133967] - Processed bindings into transfer request - Entities: 0, Location: 2026442305 - RequestId: 1, ItemBank: 0 [Team_CoreGameplayFeatures][Cargo][Inventory][Missions]");

    /// <summary>He uses a loading platform: a hand-in at Ruin (only in his own log).</summary>
    public GameLogWriter LoadingPlatform() =>
        Add($"{Ts} [Notice] <CSCLoadingPlatformManager::OnLoadingPlatformStateChanged> [Loading Platform] Loading Platform Manager [LoadingPlatform_FreightElevator_Util_Exterior_Manager_Pyro] Platform state changed to ClosedIdle [Team_CoreGameplayFeatures][Cargo]");

    public GameLogWriter QuantumSelected(string destination, string ship = "CRUS_Starlifter_C2", string shipId = "891702459996") =>
        Add($"{Ts} [Notice] <Player Selected Quantum Target - Local> [ItemNavigation][CL][33716] | NOT AUTH | {ship}_{shipId}[{shipId}]|CSCItemNavigation::OnPlayerSelectedQuantumTarget|Player has selected point {destination} as their destination, routing locally [Team_CGP4][QuantumTravel]");

    public GameLogWriter RouteStart(string from, string destination) =>
        Add($"{Ts} [Notice] <Calculate Route> [ItemNavigation][CL][33716] | NOT AUTH | CRUS_Starlifter_C2_891702459996[891702459996]|CSCItemNavigation::CalculateRoute|Projected Start Location is {from} for route to destination {destination} [Team_CGP4][QuantumTravel]");

    /// <summary>The ship's quantum drive arrived: in the log of everybody aboard (the pilot's has the destination he picked before it).</summary>
    public GameLogWriter QuantumArrived(string ship = "CRUS_Starlifter_C2", string shipId = "891702459996") =>
        Add($"{Ts} [Notice] <Quantum Drive Arrived - Arrived at Final Destination> [ItemNavigation][CL][33716] | NOT AUTH | {ship}_{shipId}[{shipId}]|CSCItemNavigation::OnQuantumDriveArrived|Quantum Drive has arrived at final destination [Team_CGP4][QuantumTravel]");

    public GameLogWriter Sent(string to, decimal amount) =>
        Add($"{Ts} [Notice] <SHUDEvent_OnNotification> Added notification \"You sent {to}: ").Add($"{Ts} {amount.ToString("N0", CultureInfo.InvariantCulture)} aUEC");

    public GameLogWriter Received(string from, decimal amount) =>
        Add($"{Ts} [Notice] <SHUDEvent_OnNotification> Added notification \"{from} has sent you: ").Add($"{Ts} {amount.ToString("N0", CultureInfo.InvariantCulture)} aUEC");

    /// <summary>One item of his body for the item recovery: a burst of these lines is a death.</summary>
    public GameLogWriter BodyItem() =>
        Add($"{Ts} [Notice] <Adding non kept item [CSCActorCorpseUtils::PopulateItemPortForItemRecoveryEntitlement]> Item 'armor_test_123 - Class(armor_test)' [Team_CoreGameplayFeatures]");

    /// <summary>The lines written so far, as they would be in Game.log.</summary>
    // ------------------------------------------------------------------ the in-game party (notifications: the title, then the text as it goes to the queue, then its echoes)

    int _note;

    GameLogWriter Note(string title, string text)
    {
        var id = _note++;
        return Add($"{Ts} [Notice] <SHUDEvent_OnNotification> Added notification \"{title}")
            .Add($"{Ts} {text}: \" [{id}] to queue. New queue size: 1, MissionId: [00000000-0000-0000-0000-000000000000], ObjectiveId: [] [Team_CoreGameplayFeatures][Missions][Comms]")
            .Add($"{Ts}    \"{title}")
            .Add($"{Ts} {text}: \" [{id}]")
            .Add($"{Ts} [Notice] <UpdateNotificationItem> Notification \"{title}")
            .Add($"{Ts} {text}: \" [{id}], Action: Next [Team_CoreGameplayFeatures][Missions][Comms]");
    }

    public GameLogWriter PartyCreated() => Note("You have created", "party");
    public GameLogWriter PartyJoined() => Note("You have joined", "party");
    public GameLogWriter MemberJoined(string name) => Note("New Member Joined", name + " has joined the party.");
    public GameLogWriter MemberLeft(string name) => Note("Member Left", name + " has left the party.");
    public GameLogWriter NewLeader(string name) => Note("New Party Leader", name + " is now party leader.");
    public GameLogWriter PartyLaunch(string leader) => Note("Party Launch", "Initiated by party leader " + leader + ".");
    public GameLogWriter Disbanded() => Note("Party Disbanded", "The party has been disbanded.");
    public GameLogWriter ChannelJoined(string name) => Note("New Member Joined", name + " has joined the channel 'RSI Perseus : zzoega'.");   // a ship's channel, not the party

    /// <summary>He opens the inventory of the place he is at (its id: "Nyx_Levski").</summary>
    public GameLogWriter Inventory(string player, string location) =>
        Add($"{Ts} [Notice] <RequestLocationInventory> Player[{player}] requested inventory for Location[{location}] [Team_CoreGameplayFeatures][Inventory]");

    /// <summary>The game is quit the normal way (a log without this line crashed).</summary>
    public GameLogWriter Quit() =>
        Add($"{Ts} [Notice] <SystemQuit> CSystem::Quit invoked with - cause=30016, reason=Quit via console command, exitCode=0, thread id=25072, main thread id=25072 [Team_Unknown][System]");

    /// <summary>A line the trackers do not send (the game writes lines like this all the time).</summary>
    public GameLogWriter Noise() =>
        Add($"{Ts} [Notice] <StatObjLoad 0x800 Format> 'data/objectcontainers/pu/loc/mod/pyro/station/ext_hangar.cgf' - File exists in P4K [Team_Graphics][CoreTech]");

    public IReadOnlyList<string> Lines => _lines;

    public HaulingLog Read()
    {
        var log = new HaulingLog();
        foreach (var line in _lines) log.ProcessLine(line);
        return log;
    }

    /// <summary>A made-up mission id (a GUID like the game's).</summary>
    public static string MissionId(int n) => $"00000000-0000-4000-8000-{n:000000000000}";
}
