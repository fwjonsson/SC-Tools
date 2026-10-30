using SCTools.Tests.Support;

namespace SCTools.Tests.Sessions;

/// <summary>
/// A session started by hand with one player: a 2-stop mission picked up at Gaslight and Patch City and handed in at Ruin, then a second mission
/// picked up at Gaslight and still on the way. The sessions, the pickup locations and the runs are built from that log.
/// </summary>
public class SessionAndRunTests
{
    static readonly string M1 = GameLogWriter.MissionId(21);
    static readonly string M2 = GameLogWriter.MissionId(22);
    const string GaslightZone = "900000002101";
    const string PatchZone = "900000002102";
    const string RuinZone = "900000002103";

    static (HaulingLog Log, GameLogWriter Writer) PlayedLog()
    {
        var contract = string.Format(GameLogWriter.HaulingContract, 2);
        var w = new GameLogWriter().Login("Pilot_A")
            .Minutes(1).Accepted(M1)
            .Marker(M1, contract, "pickup_m1_0", GaslightZone, GameLogWriter.Gaslight)
            .Marker(M1, contract, "pickup_m1_1", PatchZone, GameLogWriter.PatchCity)
            .Marker(M1, contract, "dropoff_m1_0", RuinZone, GameLogWriter.RuinStation)
            // he takes the cargo out of the elevator at Gaslight, then at Patch City, a few seconds before the game completes the pickup
            .Minutes(19).Unstow().Seconds(5).Objective(M1, "pickup_m1_0", "COMPLETED")
            .Minutes(15).Unstow().Seconds(5).Objective(M1, "pickup_m1_1", "COMPLETED")
            // and hands it in at Ruin (the loading platform)
            .Minutes(15).LoadingPlatform().Seconds(5).Objective(M1, "dropoff_m1_0", "COMPLETED").Seconds(1).MissionEnded(M1, "COMPLETED")
            // a second mission, picked up at Gaslight, still on the way
            .Minutes(1).Accepted(M2)
            .Marker(M2, contract, "pickup_m2_0", GaslightZone, GameLogWriter.Gaslight)
            .Marker(M2, contract, "pickup_m2_1", PatchZone, GameLogWriter.PatchCity)
            .Marker(M2, contract, "dropoff_m2_0", RuinZone, GameLogWriter.RuinStation)
            .Minutes(19).Unstow().Seconds(5).Objective(M2, "pickup_m2_0", "COMPLETED");
        return (HaulingLog.Merge(new[] { w.Read() }), w);
    }

    static SessionGroup Session(HaulingLog log) =>
        Assert.Single(SessionBuilder.Build(log, new[] { new ManualSession { StartUtc = GameLogWriter.Start } }));

    [Fact]
    public void A_hand_in_at_Ruin_goes_to_the_holder_whose_jumps_had_him_at_Ruin()
    {
        // zzatvo's log of 30 Oct 2026, 03:37 UTC: a shared 2-stop contract, zzoega picked up at Gaslight and zzatvo at Patch City; zzatvo jumped to Ruin and
        // handed in (his platform line 22 s before the game completed the drop-off), zzoega jumped to Rat's Nest and was landing there (a platform line 3 s
        // before). The nearer line was zzoega's and the hand-in went to him: zzatvo's run never ended and his route stuck at "Delivering". Who was there
        // counts at Ruin too now: zzatvo's run ends with the hand-in, zzoega's goes on
        var contract = string.Format(GameLogWriter.HaulingContract, 2);
        GameLogWriter Both(string name) => new GameLogWriter().Login(name).Minutes(1).Accepted(M1)
            .Marker(M1, contract, "pickup_m1_0", GaslightZone, GameLogWriter.Gaslight)
            .Marker(M1, contract, "pickup_m1_1", PatchZone, GameLogWriter.PatchCity)
            .Marker(M1, contract, "dropoff_m1_0", RuinZone, GameLogWriter.RuinStation);
        var zzoega = Both("zzoega")
            .Minutes(10).Unstow().Seconds(5).Objective(M1, "pickup_m1_0", "COMPLETED")   // his pickup at Gaslight
            .Minutes(15).Objective(M1, "pickup_m1_1", "COMPLETED")                       // zzatvo's at Patch City, in his log too
            .Minutes(5).QuantumSelected("rs_ext_pyro5_l5").Minutes(6).QuantumArrived()   // to Rat's Nest
            .Minutes(3).Seconds(57).LoadingPlatform().Seconds(3).Objective(M1, "dropoff_m1_0", "COMPLETED").Seconds(1).MissionEnded(M1, "COMPLETED");
        var zzatvo = Both("zzatvo")
            .Minutes(10).Objective(M1, "pickup_m1_0", "COMPLETED")
            .Minutes(15).Unstow().Seconds(5).Objective(M1, "pickup_m1_1", "COMPLETED")
            .Minutes(5).QuantumSelected("rs_ext_pyro6_leo").Minutes(6).QuantumArrived()   // to Ruin
            .Minutes(3).Seconds(38).LoadingPlatform().Seconds(22).Objective(M1, "dropoff_m1_0", "COMPLETED").Seconds(1).MissionEnded(M1, "COMPLETED");
        var log = HaulingLog.Merge(new[] { zzoega.Read(), zzatvo.Read() });
        var runs = WorkerTripBuilder.Build(Session(log), (_, _) => "Unassigned", log, null, (DateTime.MinValue, new Dictionary<string, string>()));
        var his = Assert.Single(runs, r => r.Worker == "zzatvo");
        Assert.NotNull(his.End);   // the hand-in is his: his run ends
        Assert.Equal(1, his.HandedIn);
        Assert.Null(Assert.Single(runs, r => r.Worker == "zzoega").End);   // zzoega still has his Gaslight batch on board
    }

    [Fact]
    public void Both_missions_belong_to_the_running_session()
    {
        var (log, _) = PlayedLog();
        var g = Session(log);
        Assert.Equal(new[] { M1, M2 }, g.Missions.Select(m => m.Id).OrderBy(x => x));
        var summary = SessionBuilder.Summarize(g);
        Assert.Equal(1, summary.Completed);
        Assert.Equal(1, summary.Active);
    }

    [Fact]
    public void The_open_pickup_locations_count_the_stops_still_waiting()
    {
        var (log, _) = PlayedLog();
        var rows = SessionBuilder.Locations(Session(log), openOnly: true);
        var gaslight = rows.Single(r => r.Zone == GaslightZone);
        var patch = rows.Single(r => r.Zone == PatchZone);
        Assert.Equal((1, 1), (gaslight.Missions, gaslight.Loaded));   // M2 picked up at Gaslight
        Assert.Equal((1, 0), (patch.Missions, patch.Loaded));         // M2 still waiting at Patch City
        Assert.Equal(1, patch.Stops2);
    }

    [Fact]
    public void A_hand_in_ends_the_run_and_the_next_pickup_starts_the_next_one()
    {
        var (log, w) = PlayedLog();
        var runs = WorkerTripBuilder.Build(Session(log), (_, _) => "Unassigned", log, null, (DateTime.MinValue, new Dictionary<string, string>()));
        Assert.Collection(runs.OrderBy(r => r.Number),
            first =>
            {
                Assert.Equal("Pilot_A", first.Worker);
                Assert.Equal(1, first.Number);
                Assert.NotNull(first.End);
                Assert.Equal(1, first.HandedIn);
                Assert.Equal(new[] { GaslightZone, PatchZone }, first.Pickups.OrderBy(p => p.Time).Select(p => p.Zone));
            },
            second =>
            {
                Assert.Equal(2, second.Number);
                Assert.Null(second.End);   // still on the way
                Assert.Equal(GaslightZone, Assert.Single(second.Pickups).Zone);
            });
    }
}
