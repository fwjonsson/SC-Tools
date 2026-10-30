using System.Globalization;
using System.Text.RegularExpressions;

namespace SCTools.GameLog;

public sealed class Mission
{
    public string Id = "";
    public string? Cargo;                 // Potassium, Carbon... (only set for the Red Wind contracts from 2 to 4 stations to one: to Ruin, or to another station)
    public int Multi;                     // number of pickup stops (2 to 4)

    /// <summary>
    /// A Red Wind contract from 2 to 4 stations to a station other than Ruin ("Master | Stellar Large Haul | to Stanton Gateway", 26 Oct 2026): the same
    /// shape as the to-Ruin ones, with the drop-off at the station its one drop-off objective says. Stage 1a of docs/planner-any-hauling.md: the sessions
    /// and the boards know it; the planner plans it with the next stage.
    /// </summary>
    public bool ToStation;
    public DateTime? Accepted;
    public DateTime? FirstSeen;
    public DateTime? EndedAt;
    public string? EndState;              // COMPLETED, ABANDON, ...
    public Dictionary<string, string> ObjZone = new();                 // objective id -> zone host id
    public Dictionary<string, List<ObjEvent>> Events = new();          // objective id -> state changes

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

    /// <summary>
    /// The contract's own name and id as the objective markers say ("RedWind_Pyro_..._Multi4ToSingle", contractDefinitionId): the Star Citizen Wiki API's
    /// debug_name and uuid of the mission (whatever names a string mod shows in the game).
    /// </summary>
    public string? Contract;
    public string? Definition;

    /// <summary>The title of the contract, as the game shows it ("RSI Disc. Month: Ling Small Haul"), when the log has it.</summary>
    public string? Title;

    /// <summary>The star system of the contract, when its name says ("..._DrawOutBoss_Nyx_Super": "nyx"), as StarMap names systems.</summary>
    public string? System;

    /// <summary>A Red Wind contract from 2 to 4 stations to one (to Ruin, or ToStation).</summary>
    public bool IsHauling => Cargo != null;

    /// <summary>A hauling contract whose drop-off is Ruin Station: what the planner plans today.</summary>
    public bool ToRuin => IsHauling && !ToStation;
    public bool Completed => EndState == "COMPLETED";
    public bool Dead => EndState != null && EndState != "COMPLETED";
}
