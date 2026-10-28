using SCTools.Tests.Support;

namespace SCTools.Tests.GameLog;

/// <summary>A tracker only uploads the lines the tracker reads: the server must read exactly the same from them as from the whole log.</summary>
public class LineFilterTests
{
    static readonly string W = GameLogWriter.MissionId(71);

    /// <summary>A played session with everything the reader reads: missions, waste, quantum travel, a death, money in and out, the elevator and the platform.</summary>
    static IReadOnlyList<string> Played()
    {
        var waste = string.Format(GameLogWriter.WasteContract, 2);
        return PlayedSession.Writer()
            .Minutes(1).Accepted(W)
            .Marker(W, waste, "dropoff_w_0", "900000007101", GameLogWriter.Gaslight)
            .DeliverPopup(W, "dropoff_w_0", 147, "Waste", "Gaslight at the L2 Lagrange of Pyro V")
            .Minutes(1).QuantumSelected("rs_ext_pyro3_l3").RouteStart("Ruin Station", "rs_ext_pyro3_l3").Minutes(3).QuantumArrived()
            .Minutes(1).Sent("Pilot_B", 250_000m).Minutes(1).Received("Pilot_B", 1_000m)
            .Minutes(1).BodyItem().BodyItem().BodyItem()
            .Minutes(1).Unstow().LoadingPlatform()
            .Lines;
    }

    /// <summary>The game writes a lot more: a noisy line after every line of the session (not between a transfer and its amount, the game writes those together).</summary>
    static List<string> Noisy(IReadOnlyList<string> lines)
    {
        var all = new List<string>();
        foreach (var line in lines)
        {
            all.Add(line);
            if (line.Contains("sent you:") || line.Contains("You sent ")) continue;
            all.Add("<2026-10-21T08:00:00.000Z> [Notice] <CVehicleMovementBase::ClearDriver> Releasing control token for 'CRUS_Starlifter_C2' [Team_Vehicle]");
            all.Add("short line");
        }
        return all;
    }

    /// <summary>What the reader made of a log, written out so two can be compared.</summary>
    static string Read(IEnumerable<string> lines)
    {
        var log = new HaulingLog();
        foreach (var line in lines) log.ProcessLine(line);
        var missions = log.Missions.Values.OrderBy(m => m.Id).Select(m =>
            m.Id + ":" + m.Multi + "/" + m.OutDrops + "/" + m.EndState + "/" + string.Join(",", m.ObjZone.OrderBy(kv => kv.Key).Select(kv => kv.Key + "=" + kv.Value))
            + "/" + string.Join(",", m.Events.OrderBy(kv => kv.Key).Select(kv => kv.Key + "x" + kv.Value.Count)) + "/" + string.Join(",", m.ObjScu.OrderBy(kv => kv.Key).Select(kv => kv.Key + "=" + kv.Value)));
        return string.Join("\n", new[]
        {
            "player " + log.Player,
            "last " + log.LastTime.ToString("O"),
            "missions " + string.Join(" | ", missions),
            "transfers " + string.Join(" | ", log.Transfers.Select(t => t.From + ">" + t.To + ":" + t.Amount)),
            "qt " + string.Join(" | ", log.Qt.Select(q => q.Kind + ":" + q.Destination + ":" + q.From)),
            "deaths " + log.Deaths.Count,
            "unstows " + log.Unstows.Count,
            "platforms " + log.Platforms.Count,
            "ids " + string.Join(",", log.IdNames.OrderBy(kv => kv.Key).Select(kv => kv.Key + "=" + kv.Value)),
            "members " + log.MemberEvents.Count,
        });
    }

    [Fact]
    public void The_server_reads_the_same_from_the_kept_lines_as_from_the_whole_log()
    {
        var all = Noisy(Played());
        var filter = new LineFilter();
        var kept = all.Where(filter.Keep).ToList();
        Assert.Equal(Read(all), Read(kept));
        Assert.True(kept.Count < all.Count / 2, kept.Count + " of " + all.Count + " lines kept");
        Assert.DoesNotContain(kept, l => l.Contains("ClearDriver"));   // the game's noise stays on his computer

        var full = Read(all);
        Assert.Contains("transfers >Pilot_B:250000 | Pilot_B>:1000", full);   // and there really was something to read
        Assert.Contains("deaths 1", full);
    }

    [Fact]
    public void The_amount_of_a_money_transfer_is_kept_whatever_the_line_says()
    {
        var filter = new LineFilter();
        Assert.True(filter.Keep("<2026-10-21T08:00:00.000Z> [Notice] <SHUDEvent_OnNotification> Added notification \"You sent Pilot_B: "));
        Assert.False(filter.Keep("tiny"));                                               // the reader never looks at a line like this one
        Assert.True(filter.Keep("<2026-10-21T08:00:00.000Z> 250,000 aUEC"));            // the amount
        Assert.False(filter.Keep("<2026-10-21T08:00:00.000Z> 250,000 aUEC"));           // only the line right after it
    }
}
