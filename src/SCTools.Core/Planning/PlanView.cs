namespace SCTools.Planning;

// The plan of the session as the Trip Planner shows it: the workers, their runs in progress and planned trips, and the lines of the Current Route table.

/// <summary>One line of a table: a run that is done (or on the way), or a planned trip.</summary>
public sealed class PlanRow
{
    public string Label = "";
    public string Route = "";
    public string Batches = "";
    public int BatchCount;           // batches picked up or planned
    public double Gm;
    public double Seconds;           // a run: the time it really took; a planned trip: the estimated flight time
    public string Level = "plan";    // done, little, all, active, plan
    public List<string> Stops = new();   // station ids in flying order (the pickup stops)
    public List<string> Places = new();  // every place of the route in flying order, the start and the ends included (the map draws these; stage 1b)
    public List<string> Ends = new();    // a run in progress: the places its cargo goes to, as the plan continues it
    public PlannedTrip? Trip;
    public bool Locked;              // a planned trip that is locked to its worker (his Current)
    public bool First;               // the trip the worker is flying right now (his run in progress, or his first planned trip when he has none): the whole row has his colour
    public decimal Value;            // a run: what its batches are worth; a planned trip: the batches at each stop times the average value of a batch there
    public bool OnMap;               // a run in progress that is drawn on the map by itself (Ruin, its pickups, back to Ruin)
    public bool Hidden;              // a planned trip that is shown inside the Current row (the continuation of the run in progress), not as a row of its own
    public double Flight;            // the estimated flying time of the whole route (Ruin, the stops, back to Ruin) with the drive of the worker
    public bool Solid;               // a planned trip that is flown first (solid line on the map); the later ones are dotted
    public int Passed;               // a run that is drawn on the map by itself: how many of its stops are done (the waves run on the leg after them)
    public string? From;             // a trip that continues a run in progress starts at this station
    public List<StopLine> Lines = new();   // the route one stop at a time (the Current Route table)
    public double Scu => Lines.Sum(l => l.Scu + l.WasteScu);   // everything the route carries, in SCU (the batches are a guess): the SCU column of the Queued routes (Frank, 29 Oct 2026)
    public int Here = -1;            // the line where the worker is now (his Current location): the worker colour follows it
    public bool IsPlan => Level == "plan";
}

/// <summary>One line of the Current Route table: a stop of the route, what is done there, and the leg that leads to it.</summary>
public sealed class StopLine
{
    public string Id = "";           // station id ("" = a place that is not in the distance table)
    public string Name = "";
    public List<string> Actions = new();   // Collect, Drop Waste, Collected ✓ ... (the Drop/Collect column; how many batches is in the Cargo column)
    public bool Stop = true;         // false: the start or the end at Ruin (or where he starts from): not counted as a stop
    public bool Done;                // collected already (it is on board)
    public bool Pending;             // something done here already (waste delivered, a pickup), but the plan still collects more here: he is not finished with this stop
    public bool Busy;                // done here, but he is still at it (Collecting: he has not set his next destination yet)
    public int Batches;              // batches collected, or still to collect, here
    public decimal Value;
    public double Gm;                // the leg from the line before it
    public double Seconds;           // the estimated flying time of that leg (a gateway crossing's time included)
    public bool Gate;                // the leg crosses a gateway into another system (stage 2): the Distance column says so
    public List<int> Parts = new();      // the loads from Ruin on this line (delivered here, or collected here on a run in progress), the SCU of each (0 = not known)
    public List<string> Keys = new();    // the drop-offs of those loads ("mission id|objective id"), in the same order
    public bool Typable;                 // a planned trip's drop-off stop: the hauler (or the admin) types the SCU going there on the page (Frank, 29 Oct 2026), kept for that drop-off only
    public List<string> Unknown = new();   // drop-offs on this line whose SCU nobody knows ("mission id|objective id"): clicking the Boxes cell lets you type it (the program)
    public double Scu;               // SCU picked up here: a guess, the log only knows the SCU of a whole mission, so every stop of it counts as an equal share
    public string ActionText => string.Join(" | ", Actions);
    public int Count;                // the batches picked up here, or handed in here at Ruin: "3 Batches" in the Cargo column
    public double WasteScu;          // the SCU of the cargo from Ruin collected on this line (for the total the route carries)
    /// <summary>The SCU column: the loads from Ruin on this line ("152", "? " when one is not known), or the guessed SCU of the batches picked up here ("~375").</summary>
    public string ScuText => Parts.Count > 0
        ? (Parts.Any(p => p <= 0) ? (Parts.Sum() > 0 ? Parts.Sum() + " + ?" : "?") : Parts.Sum().ToString(System.Globalization.CultureInfo.InvariantCulture))
        : Count > 0 && Scu >= 1 ? "~" + Math.Round(Scu).ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
    /// <summary>The Boxes column: the containers every load is cut into ("4×32 + 24", "?" for a load nobody knows the SCU of), or the batches picked up here ("3 Batches").</summary>
    public string BoxesText => Parts.Count > 0
        ? string.Join(" | ", Parts.Select(p => p > 0 ? DropOffs.ContainersText(p) : "?"))
        : Count > 0 ? PlanText.BatchText(Count) : "";
}

/// <summary>What was done at one line of a run in progress (for the text of its Drop/Collect column).</summary>
public sealed class DoneAt
{
    public int Taken;                  // batches of waste taken out at Ruin
    public string? TakenCargo;
    public int Delivered;              // batches of waste delivered here
    public List<int> DeliveredScu = new();   // the SCU of every one of them (0 = not known)
    public string? DeliveredCargo;
    public int Picked;                 // batches picked up here
    public DateTime Last;              // when the latest of these was done (UTC)
}

/// <summary>One worker on the Trip Planner: his ship, drive and where he is, and the rows of his tables (his run in progress, then his planned trips).</summary>
public sealed class PlanWorkerView
{
    public string Name = "";
    public int Capacity;
    public string DriveNote = "";    // only when no drive is set
    public string Drive = "";        // the quantum drive, or that none is set
    public int RunsSoFar;            // all his runs of the session, also the finished ones that are no longer listed in his table
    public string LocationId = PyroDistances.Ruin;   // his Current location (station id): where he is now
    public bool LocationByHand;      // set in the drop-down (the logs take over again at his next pickup, delivery or hand-in)
    public string AutoLocationId = PyroDistances.Ruin;   // where the logs say he is
    public List<PlanRow> Rows = new();
    public List<PlanRow> Planned => Rows.Where(r => r.IsPlan).ToList();
    public List<PlanRow> MapRows => Rows.Where(r => r.IsPlan || r.OnMap).ToList();   // the lines on the map: the planned trips, and the run in progress when the plan adds nothing to it
}

/// <summary>The plan of the session shown, with everything the Trip Planner tab and the web page show of it.</summary>
public sealed class PlanView
{
    public string Signature = "";   // what the pages show: they are only built again when it changes
    public string Summary = "";
    public string Note = "";
    public List<PlanWorkerView> Workers = new();
}

/// <summary>The trips worked out for the open batches, kept until the batches, the crew or the locks change.</summary>
public sealed class PlanCore
{
    public string Key = "";
    public List<PlannedTrip> Trips = new();
    public int Batches;
    public string Note = "";
    public HashSet<string> Settled = new();   // workers whose next trip has no pickup stop to lock (drop-off first, or nothing to do): no reason to plan again just for them
}
