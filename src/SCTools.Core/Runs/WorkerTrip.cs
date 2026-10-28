using System.Text.RegularExpressions;

namespace SCTools.Runs;

/// <summary>One run of one worker.</summary>
public sealed class WorkerTrip
{
    public string Worker = "";
    public int Number;
    public DateTime Start;      // UTC: the previous hand-in, but never before the missions of this run were accepted
    public DateTime KeyTime;    // UTC: the start before that correction, so what you ticked on the run keeps its key
    public DateTime? End;       // UTC, null = still on the way
    public List<TripPickup> Pickups = new();
    public int HandedIn;        // missions handed in at the end of this run
    public bool Manual;         // the run was ended by hand (failed)
    public bool Left;           // the run was ended because the worker left the party
    public List<TripCollect> Collects = new();   // from-Ruin cargo (waste) he took out at Ruin in this run
    public List<TripDrop> Drops = new();         // from-Ruin cargo he delivered to stations in this run

    /// <summary>Stable key for saving things you tick on the run.</summary>
    public string Key => Worker + "|" + (KeyTime == default ? Start : KeyTime).ToString("yyyyMMddHHmmss");

    /// <summary>The first thing the worker did in this run: taking cargo out at Ruin, delivering it, or a pickup. Null when the run has nothing in it.</summary>
    public DateTime? FirstEvent
    {
        get
        {
            DateTime? first = null;
            foreach (var t in Collects.Select(c => c.Time).Concat(Drops.Select(d => d.Time)).Concat(Pickups.Select(p => p.Time)))
            {
                if (first == null || t < first) first = t;
            }
            return first;
        }
    }

    /// <summary>The latest thing the worker did in this run (not the hand-in). Null when the run has nothing in it.</summary>
    public DateTime? LastEvent
    {
        get
        {
            DateTime? last = null;
            foreach (var t in Collects.Select(c => c.Time).Concat(Drops.Select(d => d.Time)).Concat(Pickups.Select(p => p.Time)))
            {
                if (last == null || t > last) last = t;
            }
            return last;
        }
    }

    /// <summary>The places the run went to, in the order of the visits: where cargo from Ruin was delivered and where cargo was picked up (the same place twice in a row counts once).</summary>
    public List<string> VisitedZones()
    {
        var zones = new List<string>();
        foreach (var z in Drops.Select(d => (d.Time, d.Zone)).Concat(Pickups.Select(p => (p.Time, p.Zone))).OrderBy(x => x.Time).Select(x => x.Zone))
        {
            if (zones.Count == 0 || zones[^1] != z) zones.Add(z);
        }
        return zones;
    }
}
