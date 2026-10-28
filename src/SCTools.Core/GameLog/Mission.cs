using System.Globalization;
using System.Text.RegularExpressions;

namespace SCTools.GameLog;

public sealed class Mission
{
    public string Id = "";
    public string? Cargo;                 // Potassium, Carbon... (only set for Station -> Ruin hauling contracts)
    public int Multi;                     // number of pickup stops (2 or 3)
    public DateTime? Accepted;
    public DateTime? FirstSeen;
    public DateTime? EndedAt;
    public string? EndState;              // COMPLETED, ABANDON, ...
    public Dictionary<string, string> ObjZone = new();                 // objective id -> zone host id
    public Dictionary<string, List<ObjEvent>> Events = new();          // objective id -> state changes
    public List<DeliverNote> Notes = new();

    /// <summary>The players (characters of the logs) who have this mission running: the logs that know it and have not ended it. Only set on the missions of the merged log.</summary>
    public HashSet<string> Holders = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The player who took the contract: the first log that shows it accepted (the others got it shared later). Only set on the missions of the merged log.</summary>
    public string? Taker;

    /// <summary>The mission was marked as gone on the Contracts tab (it is not in the game any more, but the logs never said so). Only set on the missions of the merged log.</summary>
    public bool Dropped;

    /// <summary>
    /// A from-Ruin contract: the cargo is loaded at Ruin and dropped off at 2 to 4 stations ("Single to Multi"), or at one ("DIRECT Stellar Large Haul", A to B).
    /// Its cargo and the number of drop-offs.
    /// </summary>
    public string? OutCargo;
    public int OutDrops;
    public bool IsOut => OutDrops > 0;
    public bool IsDirect => OutDrops == 1;

    /// <summary>The SCU of every objective, from its "Deliver 0/147 SCU of Waste to ..." popup (objective id to the total SCU). For a from-Ruin contract that is the load for that drop-off.</summary>
    public Dictionary<string, int> ObjScu = new();

    /// <summary>Where every objective is (objective id to its position in its zone, ObjZone, as the marker says; metres). For the map.</summary>
    public Dictionary<string, (double X, double Y, double Z)> ObjPos = new();

    /// <summary>What every objective says, from its popup ("New Objective: Go to Asteroid Cluster": objective id to "Go to Asteroid Cluster").</summary>
    public Dictionary<string, string> ObjText = new();

    /// <summary>The star system of the contract, when its name says ("..._DrawOutBoss_Nyx_Super": "nyx"), as StarMap names systems.</summary>
    public string? System;

    public bool IsHauling => Cargo != null;
    public bool Completed => EndState == "COMPLETED";
    public bool Dead => EndState != null && EndState != "COMPLETED";
}
