# The Route Planner for every hauling mission

Design notes, 30 Oct 2026, for Frank to read and comment on before the code is written. Frank's wish of 29 and 30 Oct: "make the Hauling Planner
work with all missions", every system, interstellar ones too, and the Other hauling board of the Session tab folded into the Route Planner instead
of staying a list of its own. The roadmap points here (`docs/roadmap.md`, "Hauling of every system"); README.md says how the code works today.

The short version: every hauling mission becomes a set of **parcels** (cargo waiting at one place that has to go to another place), the planner
plans parcels instead of batches to Ruin, a trip starts where the worker is and ends where his last parcel is delivered, and the distances come
from the map's places, through the gateways when a parcel crosses a system. The Pyro contracts to and from Ruin are parcels like any other, so
nothing changes for them: the existing scenarios are the yardstick for every step.

## What Frank asked for

- The Route Planner plans any hauling mission of any system, interstellar ones included (a pickup in one system and the drop-off in another, through
  the gateways), with the same tables it has today: the Current Route and the Queued routes per worker, the map, and on the Session tab the Stops,
  Finished stops, Cargo runs and Contracts.
- The Other hauling board (every mission with drop-offs that a party member has going, in any system, with its SCU from the game, the hauler, the
  Mission Library or the Wiki) is merged into the Route Planner rather than kept as a list of its own.
- The Pyro contracts to and from Ruin Station are the common case and the tests cover them: they keep working through the change.

## What the data gives today

### The missions in the logs

zzoega's 342 log backups on kino, 4 Aug to 29 Oct 2026. A contract's own name comes from its objective markers (`contract [...]`); it is the Star
Citizen Wiki API's `debug_name`, so Mr Kraken's string mod does not matter. The count is in how many logs the contract shows up.

| Shape | Contract names (as the markers say) | A title (as the game shows it) | Logs | Today |
|---|---|---|---|---|
| Pyro, 2 to 4 stations to Ruin | `RedWind_Pyro_..._StationToRuin_<Carbon or Potassium>_CargoHauling_Multi<2-4>ToSingle` | Senior \| Stellar Large Haul \| to Ruin Station | about 165 of the 40 newest | the planner's contracts (`HaulingLog`, `SessionBuilder`, `TripPlanner`) |
| Pyro, Ruin to 2 to 4 stations, and the direct ones | `..._RuinToStation_Waste_CargoHauling_SingleToMulti<2-4>`, `..._RuinToStation_Region<X>_Waste_CargoHauling_AtoB` | Experienced \| DIRECT Stellar Large Haul \| Ruin Station > Gaslight | about 25 of the 40 newest | the planner's loads (`DropOffs`) |
| Pyro, 2 to 4 stations to another station | `..._StationToStation_<Carbon, Potassium or Waste>_CargoHauling_Multi<2-4>ToSingle` | Master \| Stellar Large Haul \| to Stanton Gateway | 10 (19 to 26 Oct) | the Other hauling board only (`HaulJobs`) |
| Stanton, every shape | `HaulCargo_<AToB, SingleToMulti<2-4> or Multi<2-3>ToSingle>_<cargo>_Stanton<1 or 4>_<SmallGrade or SupplyGrade>` | (Red Wind, Hurston and microTech) | about 35, mostly August | board only |
| Interstellar | `HaulCargo_AToB_Interstellar_Bulk_...` (direct), `HaulCargo_Multi<2 or more>ToSingle_Interstellar_<Small, Medium or Large>_...` | Rookie \| DIRECT Interstellar Bulk Haul \| Baijini Point > Ruin Station; Ling Family Rookie Haul - Small Scale \| to Bueno Ravine | 10 (September and October) | board only |
| RSI Disc. Month hauls and couriers, other couriers, recover-cargo missions | `Iasi_HaulCargo_AtoB_...`, `Iasi_2H_Box_Courier...`, `FTL_Courier_...`, `ORS_MA_HaulingMedium`, `EchhartSecurity_..._RecoverCargo`, `HeadHunters_Pyro_..._RecoverCargo` | RSI Disc. Month: Ling Small Haul | about 45 | board only |

Every hauling mission, whatever its shape, has objectives named `pickup_<guid>_<k>` and `dropoff_<guid>_<k>`: the guid is the stop, k the
commodity at it (the direct interstellar haul of 16 Oct had three drop-off objectives at Ruin, one each for Hydrogen Fuel, Scrap and Cobalt, 288 +
96 + 40 SCU). Three shapes cover everything seen: several pickups to one drop-off, one pickup to several drop-offs, and one to one. No mission had
several pickups and several drop-offs.

### What the log says about a mission

- **Markers**: `Creating objective marker: missionId, contract, contractDefinitionId, objectiveId, zoneHostId, position`. In Pyro the stations sit in
  open space, so a marker's position is in the system's frame and `StationIds` knows every station by it (the two gateways share one position and are
  told apart by the game naming one of them later). In Stanton and Nyx the position is in the frame of the zone, a planet's or a station's, and says
  nothing by itself; `HaulJobs` reads a marker within 10 km of its zone's origin as the place's own zone and names the zone by what another objective
  says about it. A zone id is different on every shard.
- **Popups**: `Contract Accepted: <title>`, where the title is `<rank> | <kind> | to <place>`, or `| <A> > <B>` for a direct haul, which names both
  ends. `New Objective: Deliver 0/N SCU of <cargo> to <place>` for every drop-off objective: the SCU and the place as the game writes it ("Ruin Station
  above Pyro VI", "Dudley & Daughters at the L4 Lagrange of Pyro VI", "Stanton Gateway", "Port Tressler", "Shallow Fields Station at Crusader's L4
  Lagrange point"); for the Nyx Gateway the game writes no name at all ("to : ", 45 times). The Red Wind pickups have no popup; the RSI Disc. Month
  couriers say "Collect X From Y".
- **Interstellar**: while the player is in the other system the drop-off popup says "to Pyro System"; when he arrives the markers are created anew
  with the real name ("to Ruin Station above Pyro VI"). The 16 Oct haul: Baijini Point (Stanton) to Ruin, picked up 16:46, the area tags of his
  inventory lines show him at the jump point on the Stanton side at 17:06 (`Location[RR_JP_StantonPyro]`) and on the Pyro side at 17:10
  (`Location[RR_JP_PyroStanton]`), at Ruin (`RR_P6_LEO`) 17:18, delivered 17:27. The log has no quantum travel line for the crossing itself; the two
  area tags are the only trace. Over all of zzoega's logs there are 80 such crossings (both ways), 3.4 minutes from one side's tag to the other's
  in the middle, three quarters of them between 3.2 and 4.1 minutes, the rest up to 19 (a stop at the gate on the way).
- **Pickups and drop-offs**: `ObjectiveUpserted ... COMPLETED` (INPROGRESS for a part of a pickup). Who did it: the freight elevator and loading
  platform lines of his own log a few seconds before (`WorkerTripBuilder.ActorAt`), helped by who has the mission and where his jumps had him.
- **Rewards**: never in the log. The Mission Library pays every kind what Frank typed, else the Wiki's reward (`Earnings.KindReward`); the Red Wind
  table by stops is the fallback for the planner's own contracts.
- **The Wiki API** (`api.star-citizen.wiki/api/missions/{uuid}`, checked 30 Oct for the two contracts above): `hauling_orders` with the commodity,
  min and max SCU and the container size; `reward_min` and `reward_max` (none for either); `star_systems` (`["Stanton"]` for the interstellar bulk haul,
  empty for the Pyro station-to-station one); `time_to_complete_minutes` (38 and 100). No places.
- **The map's data**: `StarMap` has the 97 places of Pyro, Stanton and Nyx with positions (Pyro's are `PyroDistances`'), the gateways in pairs
  (Pyro's Stanton Gateway and Stanton's Pyro Gateway, and so on for Nyx), and the admin's Map places add names and places of his own. Outposts,
  settlements and mission spots are not in it: a stop at one of them resolves to its planet or moon at best.

### Where the Ruin bias lives

| Where | What assumes Ruin, or Pyro |
|---|---|
| `GameData/PyroDistances.cs` | The 13 stations with the distances from Ruin as read in the game; `Run(stops)` is Ruin, the stops, Ruin; the quantum destinations of Pyro. |
| `GameLog/HaulingLog.cs`, `Mission.cs` | Two regexes: `_StationToRuin_..._Multi<n>ToSingle` (IsHauling, Cargo, Multi) and `_RuinToStation_...` (IsOut, OutCargo, OutDrops). Anything else is a job for the board. |
| `Sessions/SessionBuilder.cs`, `AutoSessions.cs` | A session is the to-Ruin missions (`IsHauling`); a hand-in is a drop-off event; a session starts with a to-Ruin or from-Ruin contract. |
| `Planning/TripPlanner.cs` | Every trip starts and ends at Ruin (`Cheapest` returns to Ruin, `d0` is the leg from Ruin), stops are Pyro station ids, the loads come from Ruin for the first stop, capacity in batches of an average SCU. |
| `Tracking/PlanService.cs` | The plan's input: pickup rows to Ruin and drop chunks from Ruin; the Current Route lines (Ruin first and last, `AddRuinLine`); the locks by station id; `From == Ruin` is "nowhere special". |
| `Planning/PlanText.cs`, `PlanView.cs` | `RouteText` is "Ruin → A → B → Ruin"; a worker's location is a Pyro station id. |
| `Tracking/CargoRuns.cs`, `Runs/WorkerTripBuilder.cs` | A run ends at the hand-in at Ruin; `FlightOf` and `RunDistance` go Ruin to Ruin; a stop's SCU is the mission's SCU over `Multi`; the loads are "Collects at Ruin". |
| `Tracking/DropOffs.cs`, `Whereabouts.cs`, `Places.cs` | The loads start at Ruin; a worker is at Ruin unless the logs say otherwise; headings and stations are Pyro ids. |
| `Tracking/Earnings.cs` | The reward fallback by `Multi` (to Ruin) and `OutDrops` (from Ruin); a stop is worth reward over `Multi`. |
| `View/WebSnapshot*.cs`, `wwwroot/hauling.js` | The Stops board's drop-off is Ruin for every pickup; `planner.stations` are the Pyro ids with their place keys; the map is Pyro alone; the routes are drawn as `['Ruin', ...stops, 'Ruin']`. |
| `Server/MapService.cs` (`PlannerPositions`), `WorkerPosition` | The map hands the planner where everybody is as Pyro station ids (null anywhere else). |
| `Persistence/AppData.cs`, `SessionInputs.cs` | `StartPoints` and `LockedTrips` hold Pyro station ids. |

## The design

### 1. One model for every hauling mission: parcels

A **parcel** is cargo waiting at one place that has to go to another: its mission, its pickup stop and its drop-off stop (each a `HaulStop` as the
board has them today: the stop's key, its zone, the place of the map or null, its name as the game says it, done or not), its SCU (exact when the game
said it for that stop, else a share of the mission's), the commodity, who may carry it (the party members who have the mission, as `Contracts.HasMission`
says; everybody when they all have it) and where it is: waiting at the pickup, aboard a worker's ship since his pickup, or delivered.

Every hauling mission (every mission with a `dropoff_` objective, as `HaulJobs.IsJob` reads them, the planner's own contracts included) is read into one
`HaulJob` with its parcels:

- several pickups to one drop-off (the to-Ruin contracts, the Pyro station-to-station ones, the interstellar and Stanton Multi-to-single ones): one
  parcel per pickup, going to the one drop-off, each a share of the mission's SCU (the drop-off popup's N over the pickups, as `CargoRuns.StopScu` does);
- one pickup to several drop-offs (the from-Ruin waste, the Stanton Single-to-multi ones): one parcel per drop-off, from the one pickup, with that
  drop-off's own SCU from its popup (exact), else the library's over the drop-offs (as `DropOffs.OpenDrops` does);
- one to one (the direct hauls, the RSI Disc. Month hauls, the couriers): one parcel, with the SCU of all its drop-off objectives added up (the
  three commodities of the 16 Oct haul are one parcel of 424 SCU).

A batch on the boards is a parcel at its pickup place; a load (the from-Ruin cargo of today) is a parcel waiting where the worker already is. The SCU
order stays Frank's of 29 Oct: the game, the hauler's number for the mission, the Mission Library, the Wiki, else 500 SCU over the stops for a Red Wind
contract and nothing for the rest. The reward of a parcel is its share of the mission's: reward over parcels, which is what reward over stops and reward
over drop-offs are today.

Where a stop is, in this order: a Pyro station by its marker's position (`StationIds`); the place its own text names ("to Port Tressler", "From Shallow
Fields Station at Crusader's L4 Lagrange point", `HaulJobs.PlaceOf`); the zone another objective of the last day named (`HaulJobs.ZoneNames`, exactly or
"near ArcCorp"); the ends of the title of a direct haul ("Baijini Point > Ruin Station"), new; the admin's word on Map places; and, for a stop that is
done, where the carrier's jumps had him when he did it (the map's `Follow`), new, which also teaches the site the zone for the rest of that shard's day.
A stop the game names nowhere is shown by what the game says and left out of the routes, with a line in the planner's note, as a station not in the
distance table is today.

`Mission.IsHauling` (to Ruin) and `IsOut` (from Ruin) stay as what they are, the Red Wind shapes, for the reward fallback and the contract names;
the general predicate is "has a drop-off objective".

### 2. Distances between any two places

A static `StarRoutes` in `GameData` beside `StarMap`: `Legs(from, to)` gives the quantum jumps of the way from one place to another and the gateway
crossings in it; `Gm(from, to)` and `Seconds(drive, from, to)` add them up.

- In one system: one jump, straight between the places (`StarMap.Distance`); between two of Pyro's stations the table's distance as the game reads it
  (`StarMap.JumpGm` does this already, so the Pyro legs do not change by a metre).
- Between two systems: from the start to the gateway of its system that leads to the other, the crossing, then from the arrival gateway to the end.
  Every pair of systems has its pair of gateways in `StarMap` (Pyro's Stanton Gateway and Stanton's Pyro Gateway; Pyro's Nyx Gateway and Nyx's Pyro
  Gateway; Stanton's Nyx Gateway and Nyx's Stanton Gateway), so no way goes through a third system.
- The crossing takes a fixed time, `GateSeconds`, counted in the flight time and shown as "jump" in the Distance column (it has no Gm): 3.4
  minutes to begin with, the middle of the 80 crossings in zzoega's logs from one side's area tag to the other's (every crossing in everybody's logs
  shows up in the inventory lines the trackers already upload, so the server can keep measuring it, and the Map tool already follows a player through a
  gateway).
- The time of a jump is the drive's `QtDrive.Seconds(gm)`, as today; the start place of a route is free (he is there), every later place is a landing.

### 3. A planner without a hub

What the planner does today, in parcel words: it divides the parcels waiting to be picked up over the trips of the crew, each trip a ship of so many SCU,
so that the crew finishes at about the same time while landing as little as possible, because a landing (land, leave the seat, load, back in the seat,
take off) is the work and quantum travel is close to free. That stays. What goes is the hub:

- A **trip** starts where the worker is (his Current location, as `Whereabouts` has it) and ends where its last parcel is delivered; his next trip
  starts there. For the to-Ruin contracts that is Ruin both times, as today. A trip is over when the ship is empty: everything aboard is delivered.
- A trip's **route** visits its places in the cheapest order such that every parcel's pickup comes before its drop-off and the cargo aboard never goes
  over the ship (the plan's 90 % while a parcel's SCU is an estimate, the full ship when every parcel aboard is exact, as a direct load fills a C2 today).
  Up to twelve landings this is exact (the same subset search as `Cheapest`, with the precedence and the cargo aboard as part of the state); beyond, the
  order they were added. A place where a trip drops off and picks up is one landing.
- The **cost** of a plan is what it is today, counted on landings: every arrival at a place is a landing (the hand-in at Ruin is one of them, so the
  trip's hand-in landing `TripSeconds` and the saving of delivering where the trip picks up anyway `SyncSeconds` are not terms any more; they fall out of
  counting landings per place), the flight time at its quarter weight, the top-up, fill-now, leftover and lone-batch rules on the trip he is flying, and
  the waste-wait rule, which becomes: a pickup at a place where a parcel waits that only a trip starting elsewhere can bring, on a trip that cannot bring
  it, costs the landing that parcel will need later.
- **Loads** are parcels waiting where the trip starts: picking them up costs no landing. Today's rules for them stay in the first step: they ride
  along on the trips planned for the pickups, at most one commodity to one place aboard at a time (the game cannot tell one mission's waste from
  another's, so waste for two stations never shares a ship; whether that holds for every commodity is a question below), never adding a landing, and a
  trip just for a load is the last resort. Planning loads and pickups in one search is a later refinement, if the plans ask for it.
- **Who may carry** a parcel is who has its mission (the `limits` of today, by mission instead of by station), a run in progress is parcels aboard
  with their drop-offs still to make (the worker's first trip continues it from where he is), a **locked** next stop is the first landing of his first
  trip (a place key instead of a station id), and the claims of pickups no log names stay by mission and objective.
- The **queue** order of a worker's trips: his trips are flown in the order they were planned, since each starts where the one before it ends; the
  reordering `PlanService.Queue` does today (waste first, one-stop trips, the most valuable, three stops last) is kept for trips that start and end at
  the same place, where the order changes no leg, which is every trip of a to-Ruin session.

The output, `PlannedTrip`, becomes a list of landings (the place, the parcels picked up there, the parcels delivered there) with the start place, the
Gm and the seconds (gates included), and the flags it has today (special, locked, fill, room). `PlanText.RouteText` writes the places in order
("Rat's Nest → Orbituary → Stanton Gateway ⇢ Pyro Gateway → Baijini Point", ⇢ for a crossing); the Current Route table has a line per landing with
Collect and Deliver in the Job column, the SCU and the boxes as today, and a line for a gateway crossing with "jump" in the Distance column.

### 4. The Session tab's boards

- **Stops**: a row per pickup place and destination, as the from-Ruin rows are today (Ruin as the pickup, the station as the drop-off): Pickup
  Location, Batches (parcels picked up of parcels there), SCU, Boxes, Value, Drop-off Location. The to-Ruin rows keep Ruin as the drop-off, the
  station-to-station rows say their station, a Stanton row says Port Tressler or "near ArcCorp", an interstellar row says the other system's place. A
  row whose place is not known shows the game's words and is not planned.
- **Finished stops**: a pickup place whose parcels are all delivered (today: handed in at Ruin), with the worker who carried them.
- **Cargo runs**: a run from its first pickup (or the load taken out where it started) to the delivery of the last parcel aboard; its places in order,
  its Gm and flight time by the general legs, its value the parcels' shares.
- **Contracts**: every hauling mission of the session by its kind's name as the Mission Library has it, the to-Ruin and from-Ruin lines first and the
  rest by name, with pickups and drop-offs ticked off (`ContractStopsText` already does this for both directions).
- **Other hauling**: the rows whose stops the planner can place move onto the Stops board with their drop-off; the board keeps only the missions with a
  stop nowhere, with the SCU field as today, and goes when the last stage leaves it empty.

### 5. Runs and sessions

- A **run** (`WorkerTripBuilder.Build`) is a worker's pickups in time order until everything he picked up is delivered: a drop-off event of a mission
  he carries takes those parcels off his ship, and the run ends when none is left (today: the hand-in at Ruin of a mission he carries). A run with a
  to-Ruin pickup and a station-to-station pickup goes on past the station to Ruin, as it does in the game. The loads taken out where he starts are
  pickups like any other, so the three lists of a run today (pickups, collects, drops) become its steps in order.
- Who picked up and who delivered comes from the elevator and loading platform lines as today, with the mission's holders and the station he was at
  deciding between two players at the same moment (30 Oct).
- A **session** is the party's hauling of every kind: `SessionBuilder` and `AutoSessions.HaulingActivity` take every hauling mission, not only the
  to-Ruin ones, so a session starts when a party member accepts any hauling mission and keeps going on its pickups and drop-offs and the jumps while one
  is open (the rules of 30 Oct). Whether a courier run alone should start a hauling session is a question below.
- The **leaderboard** counts parcels delivered as batches and the general legs as distance; the **Debts** tab's payouts come from every mission
  completed in the session, at the library's reward for its kind (stage 4).

### 6. The map

- The Route Planner's map draws the routes between any places of the map: `planner.stations` (Pyro ids with place keys) becomes `planner.places`
  (the key, the name and the system of every place a route or a worker touches), and the routes are lists of place keys with no Ruin added at the ends
  (`hauling.js` draws `[r.from].concat(r.stops)` and rings every place a route touches, as it does since 29 Oct).
- The map engine draws one system at a time; the Route Planner gets the Map tool's row of systems (Pyro, Stanton, Nyx) when a route or a worker is in
  more than one, and opens on the system of the viewer's party. A leg that leaves the system is drawn to the gateway with its arrow and the other
  system's name at it; the rest of the route is on that system's tab. `/hauling/map.json` sends every system (the Map tool's whole snapshot) instead of
  Pyro alone.
- The map and the planner keep telling each other where everybody is (29 Oct): `WorkerPosition` carries place keys for any system, and the planner's
  own pickups and deliveries place a worker anywhere a parcel's place is known.

### 7. The Pyro-to-Ruin contracts through the change

- `PyroDistances` stays as the Pyro table (the in-game readings are the best data) behind `StarRoutes`; the Pyro stations keep their ids inside
  `StationIds` and `PyroDistances`, mapped to place keys by `StarMap.PyroStations` everywhere else.
- The scenarios of `TripPlannerTests`, `PlanServiceTests` and `tools/PlanTest` are the yardstick: every one of them keeps its plan, and the planner's
  output (`tools/PlanTest`, printed by CI) is compared by eye before and after the planner changes. New scenarios come from the real missions in the
  logs (the station-to-station haul of 26 Oct, the Baijini Point haul of 16 Oct, a Stanton Single-to-multi).
- A worker nobody knows the place of starts at the session's hub, the place most open parcels go to (Ruin in a to-Ruin session, as today), else at his
  first pickup with no leg counted.
- The saved data reads as it is: a `LockedStop` or `StartPoint` with a Pyro station id is read as that station's place key.
- `data.json` keeps every table; `planner.stations` goes and `planner.places` comes; `hauling.js` is the only reader since the Windows program went.

## Gaps and open questions for Frank

1. **The station-to-station contract of 26 Oct** ("Master | Stellar Large Haul | to Stanton Gateway", 520 SCU of Waste from Megumi, Dudley, a gateway
   and Gaslight) was abandoned after three minutes. Because the planner did not know it, or for another reason? Should these be planned in the same
   session as the to-Ruin contracts, mixed in the same trips?
2. **What to get right first** after Pyro: the logs since August show Stanton HaulCargo contracts (Aluminium, Stims, Waste around Hurston and
   microTech), the interstellar bulk hauls (Baijini Point to Ruin), the RSI Disc. Month hauls and the couriers. Which of these do you haul most now?
3. **Cargo that looks the same**: the plan never puts waste for two stations in one ship. Does the same go for Carbon to Orbituary and Carbon to Ruin
   in one ship (two contracts, one commodity), or can those boxes be told apart?
4. **The gateway crossing**: 3.4 minutes in the middle of 80 crossings in your logs, from one side's area tag to the other's (3.2 to 4.1 for most);
   does that match what you see, and is there a queue or a wait at the gate the plan should count on top?
5. **A pickup the game does not name** (most Stanton pickups): is "near ArcCorp" (the planet) good enough for the plan, or do you want to type the
   place in, as the SCU?
6. **Log lines**: an interstellar haul with several pickups done end to end (the one in the logs was abandoned), and a Stanton Single-to-multi done
   end to end, so the hand-in order and part deliveries can be checked. The dates are enough if the server has those logs; else copy-sc-logs.
7. **Sessions**: with every hauling mission counted, a courier run alone starts a hauling session and shows on the Session tab. Fine, or only the
   cargo contracts?
8. **The Route Planner's map** with workers in two systems: a row of system tabs like the Map tool's, or one system following the viewer's own party?

### Frank's answers (30 Oct 2026, on the pull request)

1. He abandoned the station-to-station contract to summon another one in its place. These contracts are planned with the same priorities as before,
   side by side with the to-Ruin contracts in the same trips: the planner has to solve that mix.
2. He hardly hauls outside Pyro. Now and then an interstellar run from Stanton to Pyro: a Covalex master mission of about 100 SCU with several
   drop-offs in Pyro. So after Pyro the interstellar legs (stage 3) matter more than hauling within Stanton (stage 2); the order of those two stages
   is swapped where the work allows it.
3. Carbon for two destinations can share a ship: they load it so that they remember where each part is. So the plan may put the same commodity for
   two places in one ship; the rule that a trip takes at most one load from where it starts goes, the plan only keeps such loads apart in the Current
   Route's lines so the hauler sees what goes where.
4. 3.4 minutes for a gateway crossing is about right, the calibration included: `GateSeconds` starts at that.
5. "Near ArcCorp" is the mission's description only; the marker has coordinates, or he fills the place in on the admin's Map places page. So a stop the
   game does not name is placed by its marker's position where that is in the system's frame, else by a place the admin adds, and it is listed as not
   placed until then.
6. The log lines of an end-to-end interstellar haul and a Stanton Single-to-multi come later.
7. A courier mission counts as hauling: it starts a session too.
8. The Route Planner's map gets a row of system tabs like the Map tool's.

Known limits that stay for now: the payout of a part delivery (`docs/partial-submits.md`), outposts and mission spots that are not places of the map
(their planet or moon stands in), and the ships' real box sizes (the Boxes column keeps guessing 32s).

## The stages

One pull request per stage against main, each with its tests (xUnit, the server's pages through `ServerFactory`, the planner scenarios of
`tools/PlanTest` where the plan changes) and README.md and `docs/roadmap.md` updated with it. The roadmap's numbering: these are its "stage 3, routes"
and "stage 4, earnings" of the Hauling of every system, split up.

- **Stage 0** (this pull request): the design.
- **Stage 1, Pyro places**: every hauling mission whose stops are Pyro stations, planned with the to-Ruin and from-Ruin contracts. Two pull requests,
  each one thing:
  - **1a, the model and the boards**: `HaulJob` reads every hauling mission with its parcels (the to-Ruin and from-Ruin contracts included), the
    station-to-station contracts are recognised, the sessions take every hauling mission, the Stops, Finished stops, Contracts and Cargo runs boards show
    them with their drop-off places, the Other hauling board keeps the rows with a stop nowhere. The planner plans the to-Ruin and from-Ruin parcels as
    today and names the rest in its note ("not planned yet"). Tests from the log lines of the real missions.
  - **1b, the planner without a hub**: trips start where the worker is and end at his last drop-off, parcels to any Pyro station, the cargo aboard
    checked along the route, runs that end when the ship is empty, locks and start points by place key, the map drawing any Pyro route, the Current
    Route lines per landing. Every existing scenario keeps its plan; new ones for the station-to-station haul and a mixed session.
- **Stage 2, interstellar legs** (before the other systems, Frank's answer 2): routes through the gateways with the crossing's time, a mission with
  its ends in two systems (the drop-off named "Pyro System" until arrival, taken from the title; the Covalex master mission from Stanton with several
  drop-offs in Pyro), a worker's run across systems, the Route Planner's map with the Map tool's row of system tabs and the legs drawn per system with
  the jump marked, workers placed anywhere by the map.
- **Stage 3, the other systems**: `StarRoutes` within Stanton and Nyx, a stop named by the texts, the zones, the title, the marker's position and the
  admin's Map places; a party hauling in Stanton gets its plan.
- **Stage 4, earnings and debts**: every hauling mission in the session's earnings, the runs' values, the leaderboard and the Debts tab's payouts; the
  Other hauling board goes.

Later, if the plans ask for it: loads and pickups planned in one search; the site learning which zone is which place from where the carriers were;
the payout of a part delivery.
