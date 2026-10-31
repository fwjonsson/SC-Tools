# SC Tools

Star Citizen tools that read the game's own `Game.log`: the site **sc-tools.fwjonsson.dev** (the Companion, the Hauling Planner, the Session, Mission Library,
Gauntlet Tracker, Party and Map tools) and the **SC Tools Tracker** on every gaming PC that sends the site what the tools need. It started as the **Hauling
Planner** Windows program, which read the logs and filled in a hauling session sheet; the program went on 30 Oct 2026 (Frank only uses the site and the
tracker), and its logic lives on in the core, where the site runs it.

Renamed from Hauling Tracker on 28 Oct 2026 (the repository was SCHaulingTracker, the site haulingtracker.fwjonsson.dev and then sct.fwjonsson.dev, the
programs HaulingTracker.exe and HaulingTrackerSender.exe): the old addresses send you to the new ones, and a tracker from before renames itself when it starts (the downloads under the old names went with the
program on 30 Oct 2026).

This file says how it all works. `docs/roadmap.md` says what is planned, what was decided and why; `docs/partial-submits.md` has the test notes on
what a hauling contract pays when it is handed in short; `docs/map-sightings.md` is the list of places seen in the game that the map does not have yet; `CLAUDE.md` says how we work on it (branches, CI, migrations, what never to commit).

## Build and run

You need the .NET 10 SDK.

    dotnet run --project src/SCTools.Server      the site on your own computer (see "Running the server on your own computer" under The web app)
    dotnet run --project src/SCTools.Tracker     the SC Tools Tracker
    dotnet test tests/SCTools.Tests              the tests

or open `SCTools.slnx` in Visual Studio. The exes come from the Build workflow, a single exe each (see Updates).

## How the code is organised

    src/
      SCTools.Core/            the logic, without any user interface (runs anywhere .NET runs, so it can be tested on any machine)
        GameLog/               reading Game.log: missions, objectives, quantum travel, money transfers (HaulingLog), and the files
                               that are read, opened by hand or followed live with their backup logs (LogSources); for the web app:
                               the lines a tracker uploads (LineFilter), its sending side (LogSender, HttpLogServer) and the server's
                               side (LogUploads)
        Sessions/              which missions belong to a session, the pickup locations (SessionBuilder)
        Runs/                  the runs of every worker: pickups, deliveries, hand-ins (WorkerTripBuilder)
        Planning/              the route planner (RoutePlanner), the loads from Ruin (DropChunk), and the plan as the tab shows it
                               (PlanView, PlanRow, StopLine) with the texts of its tables (PlanText)
        GameData/              the Pyro stations and distances, quantum drives, ships
        Debts/                 who owes whom (DebtCalc)
        Parties/               the party tracker: who plays with whom, now and before, from the party lines of everybody's log (PartyTracker,
                               its answer PartyBook with the history of every party, PartyRecord), for every tool
        Locations/             the location tracker: where every player is, since when, and whether he is on his way somewhere, from every
                               clue of the logs and the Hauling Planner (LocationTracker, its answer LocationBook), for every tool
        ContractTracking/      a contract as the contracts tracker knows it (TrackedContract: who had it, how it ended, what it pays)
                               and how its money is split (ContractSplit), for every tool
        Persistence/           the saved data (AppData and the classes in it) and where it is saved (Storage)
        Tracking/              the logic of the tabs, working from what the server knows now (TrackerState: the merged log, the
                               sessions, the session shown with its stops and runs). Tracker makes the services and wires them:
                               Places, Crew, Contracts, Earnings, DropOffs, CargoRuns, Whereabouts, SessionFigures, Payouts, Leaderboard,
                               PlanService (the plan of the session, the locks) and StatusFeed (the team status line); LiveTracker
                               works it all out on the server
        View/                  the Hauling Planner's page as data (WebSnapshot: data.json, every tab with its tables and colours)
        Updates/               the self-update from the GitHub release (the tracker)
      SCTools.Server/          the site (ASP.NET Core MVC): the trackers upload their logs to it, everybody watches the tools; the Hauling Planner's
                               page is Views/Hauling with wwwroot/hauling.js and hauling.css, and wwwroot/systemmap.js is the map engine every map draws with
      SCTools.Tracker/         the SC Tools Tracker every player runs on his gaming PC (SCToolsTracker.exe, a tray icon)
      ScApiTool/               the separate Star Citizen Wiki API tool
    tests/SCTools.Tests/       the tests (xUnit): dotnet test tests/SCTools.Tests
    tools/PlanTest/            prints the plans the trip planner makes for situations from real sessions

Every area has its own namespace (`SCTools.Planning`, `SCTools.Tracking`, ...), one class per file.
GitHub builds the tracker and the API tool for every pull request and every merge to main (Build, Windows), runs the tests and the planner scenarios (Tests, Linux),
and puts the site on the server after a green Tests run on main (Deploy: only of a push to this repository's main, never of a pull request, not even one
from a fork's own branch called main). A push to a branch in a pull request runs Build and Tests once, as the pull
request; the NuGet packages are kept between runs.

## Updates

The SC Tools Tracker updates itself from the [latest release](https://github.com/fwjonsson/SC-Tools/releases/tag/latest), the "latest" release the
build makes from `main`: see **The SC Tools Tracker** under The web app. The Windows program, which updated itself the same way, went on 30 Oct 2026.

## The hauling logic (from the program)

The rules below came from the Windows program and are the core's now, which the site runs: where they name a button, a menu or a window of the
program, the site has the same thing on its pages (see The web app: the Hauling Planner's page, the admin's pages, the Party panel, My page).

### What the log can and cannot tell

- Several missions with the same pickup are grouped into one row with a count.
- **From Ruin** (right of the location name) is the distance in Gm from Ruin Station to that pickup, taken from the Pyro distance table. It stays empty for a location the table does not know.
- The pickup table only shows missions that are not completed yet. While you still have open missions, completed ones are gone from it, so after you finish a batch of contracts and accept new ones you only see the new ones. When everything is completed and you have not accepted anything new, the finished locations stay on the table until you start adding new missions. Nothing else forgets them: the run board, both leaderboards, the totals (Batches Delivered, Missions Completed, aUEC) still cover the whole session. The worker and notes you typed for a location are kept and come back if the location shows up again.
- The Missions column shows missions still waiting / missions with cargo at that location, so 5 missions start as 5/5 and count down as you load. Yellow = partly loaded, green = all loaded.
- **Cargo** (right of Batches) is the estimated SCU still to pick up there (*~835 SCU*): every stop that is not picked up yet is its mission's SCU divided by its stops (the "Deliver 0/490 SCU" popup; a mission the game has not said the SCU of counts as 500), the same estimate as the Cargo of the Trip Planner. Empty when everything there is picked up.
  The log has no SCU numbers, so it only knows loaded, partly loaded or not loaded.
- If the log misses a final "completed" push, stops of a finished mission count as loaded.
- Not in the log: location names, SCU per location, rewards. These are typed on the site (the Mission Library, Map places, the admin's pages).
- Quantum travel: the log has when a destination is picked and when the drive arrives, not when the jump starts (only a passenger's game sometimes logs an
  error when the pilot spools). The time in flight is worked out from the distance and the drive.
- The in-game party is in the log (who joins, leaves and leads it, whose game goes out and comes back), as the game's notifications; the web app's
  party tracker reads it (see "The party tracker" under The web app).

### Runs and players

- The lower table lists every run. A run ends at each hand-in at Ruin, also a partial one.
- Rewards: completed 2, 3 and 4 stop contracts are counted from the log. The Mission Library has the payout of each contract, and the totals, revenue and hourly rate follow.
  Hand-ins less than 3 minutes apart count as one hand-in. Run #1 starts at the first accepted contract.
- Mission updates reach the whole party, so the logs of all accounts are merged and a shared mission counts once.
- **Station names** are found by the position of the station in the log, not by its id, because the id is different on every server. So the stations keep their names from one day and one server to the next. The Nyx and Stanton Gateways have the same position, so they are told apart by their ids (Nyx has the lower one of the two; with only one of them seen, it stays unknown). Clicking into a name cell and leaving it again no longer saves the name as one you typed; a saved name that is just another station's name is ignored when the station is known. The names are the full ones the game gives (Megumi Refueling, Dudley & Daughters, Rod's Fuel 'N Supplies, Starlight Service Station, Nyx Gateway, Stanton Gateway; Frank, 30 Oct 2026: not the shortened ones of before, and not names typed by hand, which went with the Stops table of the admin's Session page). A station that is not in the table shows as `Unknown ...id`.
- When all your missions are completed and you then accept new ones, the locked Current trips of the Trip Planner are cleared (a clean slate for the new set). The old runs keep their workers and the leaderboards do not change. Notes stay. (The old **Assigned Worker** column is gone from the Stops table; sessions from before it was removed keep what was assigned in them.)
- A location nobody is assigned to is assigned automatically to the player the logs show picking up there. You can change it, and bildog123 (no log) has to be assigned by hand.
- The run board makes runs per worker. **There is no Assigned Worker any more**: the worker of a pickup is the player the logs show taking the cargo out of the freight elevator (needs that account's log); if no log shows it, the credit the Trip Planner decided and saved when the batch was picked up (the worker whose locked stop it was, then the worker whose planned trip needed a batch there, in the order of the plan, and otherwise the only member without a log), which never changes again, also not when the server restarts; and only if there is none, the player who handed that mission in. Nobody is credited with more batches than his ship holds. Only runs get credit, a run is from a worker's first pickup to his hand-in. Pickups from before this change keep the old rule. The finished stops show who carried from them.
- A run is leaving the base, collecting cargo from any number of missions and handing it in at Ruin. A hand-in ends only the run of the player who did it, never somebody else's run, whatever missions it was for. Who did it is read from the logs (the loading platform lines; only a holder of the mission can be it, and of two players at platforms at the same moment the one whose own jumps had him at that station, 30 Oct 2026; at Ruin too for the hand-in of a to-Ruin contract, since that day 03:37 UTC, when zzatvo's hand-in at Ruin went to zzoega, who was landing at Rat's Nest, and zzatvo's route stuck at "Delivering"). If no log names anybody, it is credited to a worker without a log (like bildog123).
- Any pickup signal counts a stop as picked up. If you had to leave cargo, write it in Notes.
- **Column widths.** On the Session tab the first columns of the Stops, Finished Stops and Cargo Runs boards are as wide as the matching columns of the Session Leaderboard (#, Worker, Completed Runs, ...), so they line up; Notes (and Handed In) take what is left. The web page does the same. There the Party, Contracts and Session tables also use four equal columns (the two columns of Session are two of them, so the middle line is straight), and a party member's whole row has his colour instead of a colour column. The side with them is a narrow column, so Stops and Finished Stops get the width, and the Notes column.
- **Cargo Missing.** When a mission has a pickup signal at every stop, something was handed in after the last pickup, nobody is still flying cargo of it and the hand-in is more than 5 minutes old, but the game does not complete the mission, some cargo is gone (a lost or forgotten box, for example). The mission is marked **Cargo Missing**: its stops in Finished Stops say *Cargo Missing* (in orange) instead of the hand-in time, and Active Missions says how many are missing. The log does not say which stop or trip lost the box, only that the mission is short. A mission with missing cargo no longer keeps the session clock running and does not hold up the clean slate for the next set of missions. If the cargo turns up and the mission completes, the marker goes away by itself.
- The reward table counts every accepted contract: **2 / 3 / 4 To Ruin** (the hauling missions with 2, 3 or 4 pickups) and **2 / 3 / 4 From Ruin** (the from-Ruin contracts, by their number of drop-offs) and **Direct From Ruin** (the "DIRECT Stellar Large Haul" contracts: one drop-off). Payouts start at 1,268,250 (2 stops), 1,691,000 (3 stops) and 2,113,750 (4 stops, Master rank), the same for To Ruin and From Ruin, and 795,750 for a direct one (the log never says what a contract pays); the Mission Library's reward wins over the built-in payout. The From Ruin payouts give the from-Ruin loads their value: a drop-off is worth the reward / drop-offs of its contract, on the Ruin line where the load is collected in the Current Route, in the trip totals, in the Value of Cargo Runs (with the waste the run delivered), in Total aUEC Earned (contracts that are done) and in the session revenue. The Debts tab still only counts the pickups.

- **Value** on the upper board is what the missions at that location are worth: every mission pays its reward split over its stops (reward / 2 for a 2 stop mission, reward / 3 for a 3 stop mission, reward / 4 for a 4 stop mission), times the stops at that location. It follows the payouts in the reward table.

### Runs, lost cargo and history

- **Lost Cargo** on a run is a drop-down: None, Little or All (All also marks the run as failed, see below). A run row is yellow while the run is on the way, green when it is completed with everything delivered, orange for Little lost and red for All lost.
- **Completed** is the time the run ended, at the hand-in at Ruin Station. It stays empty while the run is on the way. **Missions** is how many missions were handed in then. **Pickup Location & Time** lists where the run went and when: the pickups (*Rod's Fuel 15:36*) and the waste of a from-Ruin contract that was taken out at Ruin (*Waste from Ruin 13:19*) and delivered to a station (*Waste → Rat's Nest 15:55*), in the order it happened. A run also starts when a worker leaves Ruin with waste: delivering it is a run on the way before anything is picked up. Who took the waste out and who delivered it comes from the logs (only the player who does it has the freight elevator and loading platform lines), so a mission that was shared with someone is only counted for the one who delivered the load, and a step that no log shows is not counted. The time columns sit together after Missions: **Started**, **Completed** and **Time** (the run time, with seconds). A run starts at the previous hand-in. The first run starts when the first mission of the batch was accepted (whichever order the trips are flown in), and when everything was handed in and new missions are accepted, the clock starts again with that acceptance, so a break is not run time. For a run that is on the way, Time counts up from Started while you watch live. The **Total** row's Time is the time of the whole set of missions: it counts from the moment the first mission of the set was accepted (the finished runs are no longer listed in the table, so it cannot be added up from the rows) and stops when everything is handed in. **In flight** (right after Time) is the estimated flying time of the run: Ruin, the stations it went to (waste delivered, pickups) in the order of the visits, and back to Ruin, with the worker's quantum drive. It is the same estimate as the In flight of the Trip Planner, so you can compare it with the Time.
- **aUEC/h** (right of In flight) is what the run earned per hour: its Value over its Time (a run on the way: so far, ticking with the clock).
- **Submit** (Frank, 30 Oct 2026): a button at the right end of the **Current Route** title while a worker has a run on the way, for the worker himself
  (logged in) or the admin. After "Are you sure?" the run ends there and is counted as it is: what it picked up is its batches, nothing is failed, and the
  plan goes on from where he is (`/hauling/trips/end`, `CargoRuns.SubmitRun`, a `ManualEnd` with `Submitted`). For when the game or the logs leave a
  route hanging (a delivery the log did not show, a route the plan should let go of).
- **Lost Cargo** is a drop-down: None, Little or All. **All** marks the run as failed: a run still on the way ends there, it does not count as a run, and its value is shown as lost. Pick None or Little again to bring it back.
- **History tab:** every session is saved with its date, missions, revenue and what each worker carried, so earlier sessions stay after their logs are gone.
- **Leaderboard tab** (Frank, 30 Oct 2026): the **Session leaderboard** (under the Session tab's stops until then) and the **All-time leaderboard** (on the History tab until then). The Session tab has the stops, the contracts (where the Session leaderboard was) and the Cargo runs.
- **Completed Contracts and the # columns** (Frank, 31 Oct 2026): the Session tab's contracts table is **Completed Contracts**: only the contracts the session completed, by kind, with their reward, how many and what they paid, and the Total (no Active column: the contracts going on are the Active Contracts above it). Every table of the Session, Debts, History and Leaderboard tabs has a **#** column first (a Total row and one player's accounts added up have none); a player sees his own rows of the Debts and History tables numbered 1, 2, 3 (`DataViews.Rows`). Every # column of the page is 36 pixels wide, the Companion's width, also in the tables that size themselves (`.idx` in hauling.css, set by `table()` and the Active Contracts table).

### Trip Planner


**From-Ruin contracts (alpha).** The "Single to Multi" contracts (cargo is loaded at Ruin and dropped off at 2 to 4 stations) are recognised from the log, and so are the direct ones (*Experienced | DIRECT Stellar Large Haul | Ruin Station > Gaslight*, about 400 to 600 SCU to one station; contract *Direct From Ruin*). **The loads for one station go out together**: a direct contract and a waste drop-off for the same stop are one load, as far as a ship holds them (the plan's 90% of the biggest ship; what does not fit is a load of its own), so take the direct contract for the station of your next stop and it rides along on that trip. All of them are waste, and the waste of different missions looks the same, so the plan is one such contract per account at a time. Every drop-off objective that is not completed yet counts as one batch for now (the log has not shown what happens when one is delivered, so this is the part to check). The **Stops** board has a **Drop-off** column (gold) with what has to be brought out to every station (*Waste ×1*); a station that only has drop-offs gets a row of its own. **The pickups come first**: the planner works out the trips for the pickups (to Ruin) on their own, and the waste only rides along on those trips. A trip leaves Ruin with the load of one mission for its **first stop**, unloads it there (a ship never carries outgoing and incoming cargo at the same time) and then picks up. **A load never adds a stop**: it only rides along on a pickup trip that picks up at its station anyway (the landings are what costs the time, and unloading and loading at the same station saves one); that station becomes the trip's first stop, and among such trips the load goes where it adds the least flying (the Queued routes table says *Ruin → Rod's Fuel → Endgame → Ruin*: the load is collected at Ruin, the drop station comes first and the rest of the trip is the cheapest way from there; the Current Route table says what is done at every stop: *Collect Waste* at Ruin (Cargo: *152 SCU: 4×32 + 24*), *Deliver Waste | Collect ×2* at Rod's Fuel, *Collect* (Cargo: *1 Batch*) at Endgame and *Deliver* at Ruin). Drops at two stations go in different trips, every trip takes at most one load, and the trips with a load come first in the list of a worker, so he is rid of his cargo as soon as possible and can take the next from-Ruin contract. **A trip only to drop off waste is the last resort**: a worker only gets one when he has nothing at all to pick up and no run in progress. A load that no pickup trip can take along (no trip picks up at its station, every such trip has a load already, or nobody who has that mission has one) waits until one can (it does not show in the planner: the line above the map is gone, to give the tables more room). After a drop-off the worker's next trip starts at that station (he cannot pick up more waste there): the planner counts his flight home either way, so it fills that flight with pickups on the way instead of sending him back to Ruin first; and when his next trip does have to start at Ruin, his Current Route begins with the station where he is, so the flight back to Ruin is in the distance and time. A worker only gets the load of a mission he has (the Contracts tab decides who has what), and a worker who is flying a run does not get a load. The game tells how many SCU every drop-off is (the "Deliver 0/147 SCU of Waste" popup), and the program shows it with the containers to load, as big as possible (sizes 32, 24, 16, 8, 4, 2 and 1): 147 SCU is *147 SCU: 4×32 + 16 + 2 + 1*. It is in the Drop-off column and in the Cargo column of the Current Route table, only on the Ruin line where the load is collected (not again where it is dropped) (*147 SCU: 4×32 + 16 + 2 + 1*). The game does not always write that popup when you accept (it can come an hour later, or not at all): then the Cargo cell says *? SCU (click to type)*. Click it, type the SCU from the contract, and the program works out the containers and remembers it (saved with your data); the game's own number wins once it writes it, and 0 clears what you typed. A load is only given to a worker whose **Ship** (Party table) holds that many SCU, with the room off grid of the C2 (+128) and the Hermes (+168); a load the ship holds in full rides along, whatever the plan's 90 % says (it is unloaded before anything is picked up; Frank, 29 Oct 2026: a direct contract of 650 SCU in a C2 waited and the planner sent him elsewhere). A load only some workers can take, at a station none of them was planned to pick up at, takes those pickups over to a free trip of one of them, so it rides along instead of waiting. A drop-off the game gave no SCU of has the Mission Library's SCU for the mission's kind (typed in there, else the suggestion, else the Star Citizen Wiki's orders), split over the mission's drop-offs. The from-Ruin contracts are on the **Contracts** tab too (Contract *3 From Ruin*, Cargo *Waste*, and the drop-offs with their SCU and a ✓ when delivered): the boxes there decide who can take a load (the planner only gives a load to someone who has the mission), and **Gone** takes a contract out of the plan. Not done yet: they are not on the web page. Their payout is what you type in the From Ruin rows of the reward table (see above).

**Active Contracts.** On the Session tab under the stops (Frank, 30 Oct 2026): a copy of the Map tool's table of the missions going on, with who has each and what the Mission Library says about it, from the map's data (`/hauling/map.json`, fetched while the Session tab is shown too).

**Contracts.** On the Session tab under the Active Contracts, over the whole width like the Debts tables (Frank, 30 Oct 2026; a tab of their own between the Trip Planner and Debts from #269 until then, and `/hauling/contracts` still opens the Session tab): every mission that is still going, from the oldest down, with its Contract (the number of stops), the Cargo, its **Pickups** (the stops it has, a ✓ behind the ones that are picked up, so you can match a row with a mission in your game) and one box per party member. A green ✔ means the member has the mission (it was shared with him), a red ✖ that he has not; click a box to change it. The member who took the contract has a gold box, which can be changed like the others (he may have lost the mission while the others still have it). The logs tell the program who has what: a member with a log has a mission when his own log has it (a mission that one account abandoned or never got is a red ✖ there, until it is shared with that account again: then it is accepted again in his log and the box turns green); a member without a log (bileh123) shows a green ✔ while the party tracker has him in the in-game party (not the Active tick, which only says who gets routes), unless the others were told that he left the mission (an inactive member is a red ✖ unless the game told that he joined it), and the game also tells when somebody joins a mission (shared with him), so a share is seen when it is made. The taker is the account whose log shows the contract accepted first, so a contract that a member without a log took and shared shows the first account that got it as the taker. Click a box and the choice is kept (it wins over the logs until it is the same as what the logs say). A mission that is not in the game any more, but that the logs never ended (the game sometimes drops a mission without saying so, so the app still waits for a batch that is not there), has a **Gone** button at the end of its row: the mission then counts as abandoned everywhere (the Trip Planner stops planning its batch, the Session tab and the payouts forget it) and its row turns grey with **Bring back** to count it again. The Trip Planner only plans a worker for batches of missions he has (if one of the three batches at Rat's Nest belongs to a mission only zzatvo has, zzoega is planned for the other two, and the note above the tables says which batches only some of the crew can take). The Mission Payouts of the Debts tab are shared between the players who have the mission when it pays out (the contracts tracker's rule, `ContractSplit.PlannerPayees`: a member with a green ✔, and anybody else whose log still has it, also one who was marked out of the party; a mission that already has its payout kept keeps its members, so earlier sessions do not change); if nobody is known to have it, the one who took it gets it (before 30 Oct 2026 the active members shared it then).

**Distances and flying time.** The distances between the Pyro stations are calculated from the real positions of the stations in the game data (they match the distances you read in the game within 0.1 Gm). Pick each worker's **QT Drive** in the Party table (all 57 drives of the game data) and the planner estimates the flying time of every jump like the star map does: the drive speeds up with its stage 1 acceleration until the engage speed, then with its stage 2 acceleration up to the top speed, flies at the top speed and slows down the same way (a short jump never reaches the top speed). The spool time is not part of the time. The planner plans so the crew finishes at about the same time (a worker with a faster drive gets more of the flying). **The ships are filled by SCU, not by batch count.** A batch is about the SCU of its mission divided by its stops (the "Deliver 0/N SCU" popup; when the game has not written it, a mission counts as 500 SCU), so a batch of a 2 stop mission is about 250 SCU and one of a 4 stop mission about 125. Every ship holds what its **Ship** in the Party table holds (a C2: 696 SCU on the grid plus 4 × 32 SCU off grid = 824 SCU; no ship set: its batches times 232 SCU), and the plan fills it to 90% of that (a C2: 742 SCU): the size of a batch is an estimate (missions are 500 SCU give or take, and not split evenly over their stations), so there is headroom. The planner also tries putting two whole trips together when the ship has room for both, so the last scattered batches become one tour (Ruin → Stanton GW → Rod's Fuel → Endgame → Ruin) instead of one trip each. **No small top-ups on a nearly full ship:** a stop with little cargo (under a quarter of the ship) is not added to a trip that the rest of its stops (or a run in progress) fill to 75% or more: it counts 10 minutes extra there, so it goes to a later trip with other small stops. A ship that is only half full does fill up with a stop on the way home when that is quicker than another trip. The planner also moves and swaps whole stops (all the batches of a station at once) between trips, not just single batches. A trip carries one load from Ruin at most, so two stations with waste waiting are not put in the same trip (the second one would be stranded). **tools/PlanTest** runs the planner on situations from real sessions and prints the plans (the "Planner scenarios" run on GitHub, whenever the planner changes). The queue starts with the one-stop trips that (nearly) fill the ship (75% of the planned room or more), the fullest first, then the other trips that fill at least half of it, fewest stops first. Trips that fill less than half the ship go to the back of the queue, so new missions can still make them worth flying. The Cargo column of the Current Route says it for every stop and for the hand-in: *3 Batches (~375 SCU)*. **The landings are the work, not the flying.** Setting the destination, flying in, asking for a hangar, landing, loading and taking off again is what takes the time; quantum travel is close to free (with two accounts you play the other one meanwhile). So when the planner compares plans (not in the times shown), every stop counts as 15 minutes, so does the hand-in landing at Ruin that ends every trip (a run of 38 minutes: 9 minutes of flying, 3-4 minutes loading waste at the elevator, the rest two landings of 13-15 minutes each), and the flying only for a quarter of its time: it plans as few landings as it can and makes every one of them worth it (a station with a full ship of batches is a one-stop trip, a station is only split over several trips when it has to be). A pickup at a station where waste from Ruin is waiting is kept for a trip from Ruin that brings that waste along (one landing does both), not given to a run in progress that cannot take the waste: that would cost the waste a landing of its own later. Every row of the Trip Planner tables has an **In flight** time (the last column; Cargo Runs has it too): the estimate for that whole route (Ruin, the stops, back to Ruin) with the drive of its worker, also for a run; in the Current Route table every line has the time of the leg that leads to it, so they add up to the Total. How long a run really took is the **Time** of Cargo Runs. The drive is no longer written in the title of a table (only a note when none is set). Loading time is not included. Without a drive set, an Odyssey (S2) is assumed. The calculation was checked against SPViewer with a TS-2 (S3, 12 jumps from 1 to 150 Gm, all within 2 seconds) and an XL-1 (S2) and a VK-00 (S1) (1, 15 and 100 Gm, all within 1 second); the other drives of those sizes use the same formula. Size 4 drives (Allegro, Mauler) are only approximate: an Allegro is up to 10 seconds off.
The **Trip Planner** tab divides the open batches over the whole crew, so everybody flies as little as possible and you finish at about the same time.
- The crew is the active party (the Party panel's Active tick, which means one thing only: the planner gives this member routes; the logs set it, and
  the admin or the party's leader unticks a member who plays but does not haul), and a member's capacity is his ship (My fleet; a ship
  the site does not know counts as a C2).
- The plan keeps the stops of everybody's planned trip for him (see "Kept stops" below; the Next Stops button went on 30 Oct 2026). The plan also follows the batches you pick up while you play. The program shows one page with everybody (no tabs): a square **map of Pyro** on the left, in a frame with the team status line (like on the web page), with a short, read-only copy of **Cargo Runs** under it, and the tables of everybody on the right. The tables of the workers go down the whole right side, so there is room for more workers, and the page only scrolls when they do not fit. The map is always a square (its size comes from the size of the window only, so it keeps its size when the plan is updated). The **Next Stops: Unlocked / Locked** button is in the top bar of the window, level with Options and Stop Session, while the Trip Planner tab is shown; hold the mouse over it for the explanation. There is no title and no summary line. The web page shows the same single page. Only a warning (stations that are not in the distance table) is shown under the summary. On the map every planned trip is a line from Ruin through the stops and back (arrows show the direction); every worker has his own colour, his current trip is a bold line and the later trips are dotted, also on the web page. Each worker has two tables, the **Current Route** and the **Queued routes** (see below), and in the Current Route the worker's colour is on the step he is doing: the stop where he is still busy (*Collecting*), and once that is done (*Collected ✓* (*2 Batches*), or *Delivered Waste ✓* as soon as the waste is sent down the elevator) the next stop, the one he is on his way to. The short copy of **Cargo Runs** under the map has the **#**, the **Worker**, the **Value**, **Started**, **Completed**, **Time**, **In flight** and **aUEC/h** (the value of the run over its time; a run on the way: so far) of every run (not the locations, batches or lost cargo) and keeps the runs in view while you plan; the Session tab has the whole, detailed table. The web page has the same layout (the map in its frame with the short Cargo Runs under it, the tables on the right); there all tables have the same narrow columns and a long Route column. The page keeps its height and its scroll position while the planner is drawn anew (every few seconds while somebody plays), so a reader who has scrolled down to the Cargo Runs stays there (Frank, 30 Oct 2026: the page jumped back up).

- **Kept stops and runs in progress.** The plan is sticky by itself (Frank, 30 Oct 2026; until then a **Next Stops: Unlocked / Locked** button on the admin's Session page locked the next stop of everybody, and only that one, and Frank did not use it): the stops of every worker's planned Current trip are kept for him, in their order, so planning again when somebody else picks up or hands in never takes a stop away from him (zzoega's Megumi stop went to zzatvo's plan when zzatvo picked up at Endgame). A kept stop goes when its batches are picked up (by him or by somebody else) or are gone, when his ship has no room for it any more, or when he picks up somewhere else (he went his own way: his trips are planned anew); a station set by hand releases them all. What the planner puts after his kept stops is kept too, so a trip settles; a worker whose run is handed in, or whose kept stops are used up, gets the trip planned for him and keeps that. A worker flying a run with room left keeps the stops of its continuation. Nothing is marked in the tables: the planner just keeps those stops for him (`SessionInputs.LockedTrips`, `WorkerStart.Keep`, `TripPlanner`'s kept stops in the first trip). A trip that only has a drop-off from Ruin first has no pickup to keep.
- Each worker has two tables. The **Current Route** (on the left) is the run on the way or, when he has no run in progress, his first planned trip: one numbered line per stop, in flying order from Ruin back to Ruin (Ruin is the first and the last line). **Stops** is just the station, and **Deliver/Collect** says what is done there: *Collect* (Cargo: *2 Batches*) (pick up the batches there), *Deliver Waste* (unload a load from Ruin, see below) and, for the run you are flying, what is done already with a tick: *Collected Waste ✓* on the Ruin line (the waste taken out there in this run; the game completes every pickup of the contract at once however much you take, so there is no count, and the Cargo is that of the loads delivered since), *Delivered Waste ✓* where the waste was delivered (at once when it is sent down the elevator: nothing is left to do there) and *Collected ✓* (*1 Batch*) for the cargo on board. **Cargo** says what is collected there, only on the lines where it is collected (*3 Batches (~375 SCU)*; the Deliver/Collect column only says what is done; delivering is everything on board, so it has no amount; on the line he is on his way to, *Deliver* says *Delivering*, like *Collecting*), and the **Total** line says everything the route carries, the batches to Ruin and the waste from Ruin together (*4 Batches + Waste (~839 SCU)*), and for a load from Ruin the SCU first, then the containers to use, like *198 SCU: 6×32 + 4 + 2*. A load that goes out from Ruin has its SCU and the containers to use, as big as possible (the sizes are 32, 24, 16, 8, 4, 2 and 1), like *152 SCU: 4×32 + 24*: only on the Ruin line where it is collected (one such part for every drop-off if there are more of them; *? SCU* when the game never said how big the load is). The batches you pick up have no SCU or containers in the Cargo column: they all go to Ruin, so the number of batches at a stop (*Collect* (Cargo: *2 Batches*)) is all that is needed (the code that guesses it, *~5×32 + 2 + 1*, is still there, switched off by `ShowPickupScu`, for a later version) (the log only tells the SCU of a whole mission, *Deliver 0/490 SCU of Carbon*, so it would be an equal share per stop anyway). The lines that are done say the same as the lines that are planned. While a worker is still busy at the place where he is (he has pulled the cargo out of the elevator, but not selected his next quantum destination yet) the line says *Collecting* without the tick, and turns into *Collected ✓* (*2 Batches*) when he selects the next destination. That needs the Game.log of that worker (the game writes the quantum destination only in his own log); without it the tick comes at once, like before. Everything done in the run stays in the table, in the order it happened, until the run is handed in at Ruin; what the plan still adds comes after it, and Ruin is the last line. A trip with a load starts with the Ruin line *Collect Waste ×3* (Cargo: *152 SCU: 4×32 + 24*) (once he has brought the waste up from the elevator at Ruin it says *Collecting Waste* while he is still at it (the same words as the pickups: *Collect* planned, *Collecting* busy, *Collected ✓* done), and *Collected Waste ✓* once he has set his next quantum destination; his colour then moves on to the drop-off) and ends with the Ruin line *Deliver* (the batches handed in). The **Queued routes** (under the Current Route, as wide as it) are the trips after that, the one-stop trips first (after a trip that takes cargo out from Ruin): while you fly those, missions get completed and new ones come in, and the trips with more stops are planned again with the new missions, one line each, numbered 1, 2, 3, ..., with its **Route** (like *Ruin → Rod's Fuel → Endgame → Ruin*; the map draws them as dotted lines), how many **Stops** it has and its **Batches** (the total of that trip only); the Total line is marked Σ. Every worker's name (in capitals and in his colour), location and two tables sit in a frame of their own, so it is clear where one worker ends and the next begins. Both tables have the **Value** (a run: what its batches are worth, like in Cargo Runs; a planned trip: the batches at each stop times what an average batch at that stop is worth, reward / stops of its mission, from the rewards in the Session tab), the **Distance** in Gm and **In flight** (the estimated flying time). The tables are the same for every worker, so they line up, and a row is one line high unless the window is so narrow that a text has to wrap. A run that is finished is taken out of the tables as soon as it is handed in; the whole history stays in **Cargo Runs** below.
- **Total.** Both tables of a worker end with a **Total** line: the number of stops, the batches (*Collect* (Cargo: *3 Batches*) in the Current Route) with their SCU (*~392 SCU* under Cargo), the value, the distance and the flying time (In flight) of all the rows above it. The title above the tables of a worker is just his name.
- **Current location.** Every worker has a **Current location** setting on his My page (the admin opens anybody's from his Players page) (the Trip Planner shows it beside his name): where he is now, and where the plan starts his next trip. It follows the logs by itself and then says *Automatic: Starlight* (the station): after a pickup he is at that station, after he delivers the load of a from-Ruin mission he is at that drop-off (only the worker whose own log shows him at the loading platform; when a mission is shared with him and somebody else delivers the load, he does not move), and after a hand-in (or before anything has happened) he is at Ruin Station. The line of the Current Route where he is has his colour (the station of his last pickup while he flies a run, the first line of a trip that has not started). A run in progress continues from his last pickup, and what is left at the station where he stands (that he has the missions for) is taken first, so he picks up right where he is before the plan sends him anywhere else (the Current Route then starts with the station he is at, and a queued trip starts with that station instead of Ruin). You can also set it by hand, for example for everybody at the start of a session, when someone sits at a station with an empty ship, or when something strange happens and the logs missed a pickup: pick the station in the drop-down and the box says *set by you*; the plan starts from there at once, also for a worker who is flying a run (it then continues the run from that station). Picking a station releases his locked next stop, so the plan is free to change. A choice by hand holds until the logs show him picking up, delivering a load from Ruin, handing in, taking waste out at Ruin or selecting a quantum destination (his own log) after that; from then on the box follows the logs again. Pick *Automatic* to give it back to the logs yourself (Ruin Station by hand is a choice too, for a worker who is back at Ruin after a delivery elsewhere). The drop-downs are only in the program; the web page shows the result in the title above the tables of every worker (*zzoega – Current location: Patch City*). If the plan ever cannot be worked out or shown, the program says so above the tables (and tries again on the next update) instead of leaving an old or empty page.
- The map is drawn on black with the orbit rings of Pyro I, Monox, Bloom, Pyro V and Terminus, the planets and the sun (the site's map engine draws the same). On the line from where a worker is to his next stop (the first leg of a trip, or for a run that is full the way home after his last pickup), slow dark waves move in the direction of the arrow, so you can see at a glance where everybody is heading; the legs of a run to the stops that are done are gone, so the leg with the waves is the first one drawn, and the leg home is gone too once the map has him at Ruin Station, before he hands in (Frank, 30 Oct 2026: the arrow with the waves stayed on the map after he had arrived); they run only while the Trip Planner is on screen; the live web view has them too. Distances between stations are calculated from the real 3D positions of the stations (Star Citizen Wiki API) and match the distances read in the game. **On the web page the map is the Map tool's** (since 29 Oct 2026, one map engine, see "The Map tool" under The web app): the same Pyro with all its places, every worker as an arrow in his colour with a ring pulsing out from it, moving along his route at his drive's speed while he is in quantum, the objectives of the missions going on as gold diamonds (with how many, when several are at one place), and under every worker's name where he is and what he does with his speed meter while he flies; the planned routes, bold and dotted, with their arrows and the waves, are drawn on it, and the stations a route touches have a ring (Ruin like any other: the planner is to lose its Ruin bias). The Windows program keeps its own map (PyroMapView).
- **Team status line.** Under the map is a small line of text that changes every ten seconds or so (on the web pages it is at the right end of the gameplay tools' row of tabs: the Hauling Planner, the Companion, the Gauntlet Tracker and the Map, `ticker.js`; under the Route Planner's map until 31 Oct 2026). What just happened comes first and is shown at once: *zzatvo is traveling towards Rat's Nest*, *zzatvo arrived at Rat's Nest*, *zzoega is loading Carbon at Starlight*, *zzatvo delivered Waste to Rat's Nest (187 SCU)*, *zzoega handed in 3 missions after 22m 41s*, *zzatvo sent zzoega 125,000 aUEC*, *zzoega died*. In between come the clock of the run a worker is flying (*bileh has spent 23m 12s on this trip*, it counts on while it is shown), stats of the session (runs, missions, average run time, Gm flown, fastest and longest run, quantum jumps and time spent in quantum travel, the ship somebody flies, the death count) and some fun. It only knows what the logs show: the quantum travel, the deaths and the money lines come from the Game.log of the player himself, so a member whose log is not read (not added, or the other computer is offline) is only in the lines that come from the runs. The game no longer writes kill lines, so a death is recognised by the burst of item recovery lines the game writes for the body of the player (shown a few seconds after the death). The destinations of Gaslight and Endgame are guessed from the pattern of the other names; if one is not right the line just says the worker picked a new destination.
- The plan ignores who is assigned to a location now. A location can be split over several trips and several workers: take what fits and fill the last seats with a small pickup that is close by, or come back for the rest.
- The plan is the one where the longest worker total is as short as possible, with a smaller weight on the combined Gm. It tries many ways of dividing the batches, and the suggestion is always the same for the same situation. It follows the batches you pick up while the tab is open.
- It uses the Pyro distance table and map positions, so a location that is not in it is left out and named under the summary. Only batches that are not picked up yet are planned.

### Leaderboards

The **Session Leaderboard** has one row per worker who started a cargo run in the session, and a total row, the highest earner on top (marked with a star on each side of the name and a gold row; the other names are indented to line up):
- **Avg Batches/Run** and **Avg Run Time**: averages over the completed runs (ended by a hand-in, not marked as failed). Batches/run counts the batches picked up at each location, like the Batches column of the run board.
- **Distance Travelled**: the distance in Gm flown in the worker's completed runs. A run counts Ruin to the first pickup, between the pickups in the order they were made, and back to Ruin. The distances are a fixed table for the Pyro stations (`Distances.cs`: positions from a Pyro map, Ruin legs read in the game). A pickup location the table does not know (a station you named differently, or one that is not in the table) is skipped. Sessions saved before this column existed get their distance filled in when you open them. **Gm / Hour** is that distance divided by the session duration (the same hours as Run Value / Hour).
- **Completed Runs** and **Perfect Runs** (completed runs with lost cargo None).
- **Delivered Batches**: the batches of the worker's finished runs. Nothing counts (also not the earned aUEC) until the run is done.
- **Total Run Value**: the value of the batches in the worker's finished runs (reward / stops of each mission), counted as soon as the run is done. Run counts (Completed Runs, Perfect Runs, Delivered Batches) also count at once; a perfect run stays perfect whatever happens to the server afterwards.
- **Total Earned aUEC**: the part of the run value whose mission is completed. **Lost Value**: run value minus earned, shown once the session has been quiet for 6 hours (until then the rest is just queued). Failed or abandoned missions never pay. **Run Value/Hour** is the run value divided by the session duration, **Share of Run Value** is his part of the total run value. The History leaderboard has the same columns over all saved sessions.
- The clocks only run while you have a contract that is not completed: **Session Time**, the per-hour numbers that use it, and the **Time** of a run that is still on the way stop as soon as everything is completed (the session time freezes at the last activity).
- **Debts follow who has the contract.** When a mission pays out, its reward is split equally between the players who *have the mission* at that moment: the one who took it and everybody who accepted the share and still had it (the contracts tracker's rule, Frank 30 Oct 2026; being in the party without the share gets nothing, and a player whose log still has it gets his share also when he was marked out of the party). A member with a log has it when his log has it, one without a log is taken to have it while the party tracker has him in the in-game party (never the Active tick: a member unticked because he does not haul still gets his share, and owes the haulers what he did not carry) unless the others were told he left it, and the boxes of the Contracts tab override the logs. Everybody is owed the value of the batches he carried. So if Bileh joins, flies two runs and no mission pays out while he is in the party, you owe him for his runs. If a payout comes while he is there, his share of it is already counted as paid to him. Missions that paid out before this table started tracking times keep the old split (the whole session party); a **Session Party…** typed in the Debts tab still overrides everything for that session.
- The two boards on the left only show **active missions** (not completed yet). **Stops** lists the pickup locations with cargo still to deliver; when every batch of a location is handed in, the location moves to **Finished Stops** below it (shown when there is something in it). When a mission is completed it leaves both boards. **Cargo Runs** at the bottom keeps every run since the session started, so it is the log.
- The start/end table shows **Session Time** (ticks every second while you watch live), **Batches Delivered** (finished runs / all batches), **Active Missions** (accepted, not completed yet), **Missions Completed** (exactly the number completed in the session), **Total aUEC Earned** (what completed missions actually paid) and **aUEC/Hour** (earned per session hour).

The **History** tab has the same table for **all saved sessions** (with an extra Sessions column). Under it a line shows the best session (most revenue) and the best aUEC/hour. It adds up what each session saved, so it keeps working after the logs are gone. Sessions saved by an older version have earnings but no run counts until their logs are opened once.

### Debts tab

- **Earnings:** every session saves how much each worker carried (the Value of the locations assigned to him). The table shows this session, all sessions and the net balance per worker.
- **Split:** the reward of a session is split equally between everybody in the party, but each one is owed the value he carried. So somebody who carried nothing owes his whole share to the others, and somebody who carried more than his share is owed the difference. Lost cargo changes nothing.
- **Session Party…:** by default the party of a session is everyone who carried something. Use this button to add someone who was in the party but carried nothing (or to remove someone).
- **Balance:** pick a party member and see the balance with each other member. + means they owe the selected member, − means the selected member owes them. Type over a number to correct it, your correction is kept.
- **Money transfers:** `You sent <name>: <amount> aUEC` and `<name> has sent you: <amount> aUEC` are read from the logs (a transfer seen in both accounts' logs counts once). The list shows the transfers of the selected member. Tick **Payment** on the ones that pay off a debt, other transfers (like buying a ship) are ignored.
- On the Debts tab the **Balances** and the **Earnings** tables are side by side, under them are the **Money Transfers** (left) and the **Mission Payouts** (right). Mission Payouts lists every finished mission, newest first: when it completed, its payout, the players who had the mission at that moment and shared it, and how much each of them got. The list is saved in the data file, so it stays after the logs are gone. The **Payout** box can be typed in: a mission that was handed in short pays less than its contract, so type what it really paid (it turns gold); the shares, the *Each* column and the debts then follow that amount, spread over the batches that were carried. Empty the box to use the reward of the contract again. A positive sum (somebody owes the selected member) is green and a negative one (he owes them) is red, in Balance and in Net Balance. The Meaning has the colour of its Balance.
- Balances add up over all saved sessions, so earlier money you settled by hand can be fixed with the typed corrections.
- **Combined accounts** (Frank, 30 Oct 2026): on his Account page the admin ticks his other accounts (zzatvo and zzoii with zzoega), and the money tables
  (the Session and All-time leaderboards, the Earnings table of the Debts tab, the Gauntlet Tracker's leaderboard) get a row that adds them up with his
  (`zzoega + zzatvo + zzoii`, in grey, after the players, not numbered; the hours of a leaderboard row are the longest of the accounts, as they play at the
  same time). Only visual: the debts, the payouts and the balances stay each account's own, since the work is done on each (`AppData.CombinedAccounts`,
  `/account/combine`).
- **Balances are pair by pair**, from what really happened: in every session the members who carried more than their share of its payouts are owed by
  those who carried less (each in proportion to how much less), and a ticked payment counts between exactly the two who made it. So paying one member
  never moves a debt to somebody else (it used to show the fewest payments that settle everybody, which could route one member's debt through another),
  and the balances of a member add up to his Net Balance. Two accounts of one player are two members: the work on each is counted.

## The web app (being built)

The tracker is moving to a server, so the website is always up and everybody with a tracker is tracked, also when Frank's computer is off. The plan is
`claude/webapp-plan.md` in the project: the server and the live page (done), the trackers (done), the database (done), the logins and the editing pages of the
admin (done), then going live.

- **The SC Tools Tracker** (`SCToolsTracker.exe`, in `SCToolsTracker.zip` of the latest release; its page on the site, `/tracker`, has the download and
  the steps, and the landing page links to it) is the tracker of every tool: a tray icon on every gaming PC. It was `HaulingTrackerSender.exe`: a
  tracker from before the rename renames itself `SCToolsTracker.exe` when it starts (and moves Start with Windows along, `AppName`); the release carried
  `HaulingTrackerSender.zip` under the old name until 30 Oct 2026, when it went with the Windows program (every tracker runs as SCToolsTracker since the rename). Its settings are the
  server (`https://sc-tools.fwjonsson.dev`; a tracker with an old address, haulingtracker or sct, saved moves to it by itself, `SiteAddress`; the settings are in `%AppData%\SCTools\tracker.json`), your **key** (Frank gives every player his own; **Test** asks the server whose key it is), the Star Citizen folder and the log channel. Every
  3 seconds it uploads what is new in the Game.log, and the backup logs of the last day, from where the server is, so nothing is sent twice (also after a
  restart of the tracker or the game). When the server cannot be reached it waits longer between tries, twice as long after every failure in a row (6, 12,
  24 seconds... up to two minutes), and a minute when the server does not know the key, until a try works or the settings change (`RetryDelay`; the menu
  says how long). A tick with nothing new opens one file, the Game.log, and reads nothing from it: a backup log sent to its end is never opened again.
  While the game is closed (its Game.log has not grown for 3 minutes) it looks every 15 seconds, and every 3 again at the game's first new line; the
  ticks run off the tray's thread. It only sends the lines the
  tracker reads (the kinds in `HaulingLog.LineKinds`: missions, quantum travel, money transfers, the freight elevator and the loading platform, and the
  game's "You've Earned: N Rewards" after a completed mission, since 31 Oct 2026); the rest
  of the game's log stays on the PC. It can start with Windows, and it updates itself (it looks when it starts and every half hour, so a new kind of line
  the site reads comes from every tracker soon after it is merged); the download of an update is removed once the new exe is in place, and the downloads
  of earlier updates still in Temp go when the tracker starts (`Updater.RemoveOldDownloads`); an update that fails leaves nothing behind either. The
  release is made again only when the tracker changed: its version is the last merge to main that changed `src/SCTools.Tracker` or the Core folders it is
  made of (GameLog, Persistence, Updates), so a merge that changed only the site does not make every tracker download 45 MB and restart. The API tool is
  built when it changed, or by hand (Run workflow).
  Every request says its version (`X-Tracker-Version`: the commit it was built from), so the site knows whether it updated: the `/tracker` page shows a
  logged in player his own tracker's version (the newest or not, and when it was last in touch), the admin's Keys page everybody's (`TrackerVersions`;
  the newest build is the "latest" release on GitHub, asked at most every 10 minutes).
- **The old logs.** The tracker also sends the backup logs the server does not have yet (`HistorySender`): the first time all of them, after that only the
  new ones (it asks the server which it has: `api/history/missing`; it looks when it starts and every six hours). Only the lines it reads, in pieces of
  whole lines (`api/history`). The server keeps them apart from the live logs, so the Hauling Planner never sees them: their lines in `/var/lib/sctools/history`
  (gzip, a folder per player) and a row per log in `LogFiles` (when the game ran, its build); the Gauntlet runs in them go to the database at once
  (`HistoryStore`). A log of another player on the same PC is noted ("zzatvo's game") and not read. When the tracker reads more kinds of lines,
  `HistorySender.Version` goes up and the server asks for every log again (3 since 31 Oct 2026: the "You've Earned" lines, so the old contracts
  get them too). The tracker says its version with every piece and the server keeps the log at that version, so a tracker that did not update yet is
  asked again once it has. The tracker's menu says how far it is ("Old logs: sending 3 of 140"), its
  version (up to date, or updating), and **Check for an update now**; after an update it says "Updated to version ...".
- **SCTools.Server** keeps the uploads (one log per player, also on disk, so a restart reads them again), works the session out every two seconds
  with the core's logic, and serves the Hauling Planner's page to everybody. The two-second refresh makes `data.json` anew only when something in it can
  have changed (a piece of log, a save, half a minute gone by: `LiveTracker.Refresh`), and the page polls it every 3 seconds with the ETag it got last
  (`If-None-Match`): the same data again is a 304 with no body, so an idle server and an idle page do next to nothing. Cloudflare compresses the answer and
  calls its ETag weak (`W/"..."`); the browser sends it back that way, and the server takes it as the same tag (`PageController.Matches`). The team
  status line has an address of its own, the site's and not the Hauling Planner's (`/status.json`, `SiteStatusController`; `/hauling/status.json`
  until 31 Oct 2026), asked every few seconds by `ticker.js` on the gameplay tools' pages: it changes every few seconds and has clock lines, so in
  `data.json` it made all the data again for one sentence. Every running clock on the pages counts up by the
  browser's clock from when it started, set at once when it is drawn; the map takes the server's clock against the browser's when its data comes. The page draws
  only the tab shown, and in it only the tables whose data changed; a browser tab nobody looks at does not poll until it is shown again. The Mission
  Library and the Gauntlet Tracker look at the logs again only when a piece came in (`LogUploads.Pieces`), and the database runs in write-ahead mode
  (WAL), so a write does not make the readers wait. The server's log says how the refresh goes: a tick over a second at once, and once an hour how
  many ticks there were, how long they took and how often `data.json` was made. An upload without a known key is refused. The admin makes the keys on the
  **Players** page (`/admin/players`; the Tracker keys page until 31 Oct 2026): a new key is shown once (the database keeps only a hash of it), and a key can be taken back (that tracker stops,
  and its player's browser is logged out within a minute). Keys in the settings (`SCTools:Trackers`: key to player) are still added when the server
  starts.
- **Logins** (ASP.NET Core Identity, its tables in the same database). Watching needs no login, but what the players do is only for the players it is about (Frank, 30 Oct 2026; `Audience`, `DataViews`): everybody sees every tool shown on the Tools page; a **visitor** (not logged in) sees no player data, apart from the public leaderboards (the Hauling Planner's all-time leaderboard and the Gauntlet Tracker's Leaderboard tab), and the other pages ask him to log in; a **player** sees his own data and his party's (the in-game party he is in now and the group he hauls with in the session): his party's session, stops, runs, contracts, routes and map, his own debts, money transfers and saved sessions, his own Gauntlet runs and his party's on the Session and History tabs, while the Gauntlet Tracker's Stats tab (the Top 10 fastest with it) is his alone; the **admin** sees everybody's, and with the top bar's View pick exactly what that player sees. data.json is cut to the viewer from the one the refresh makes (each viewer's cut has an ETag of its own); map.json the same. The **admin** logs in at `/login` with his name and
  password; his login is made when the server starts and has no admin yet, from `SCTools:Admin:UserName` (default `admin`, not a player's name)
  and `SCTools:Admin:Password` (at least 12 characters). Those are never in `appsettings.json` (it is in the repository): on your own computer
  right-click SCTools.Server, **Manage User Secrets**, and put in
  `{ "SCTools": { "Admin": { "UserName": "admin", "Password": "..." } } }`; on the server they are in its environment. The settings are only read to make
  the admin the first time: after that the name and the password are changed on the **Account** page (`/account`). A forgotten one: add `"ResetPassword": true` next to
  the password in the settings, start the server (it sets that password again and lifts a lockout), then remove it. A **player** is logged in by his tracker: **Open my page** in its menu asks the server for a link that
  works once, within two minutes, so the key is never in an address. The admin can have his own player's name (Frank is zzoega): with the password he is
  the admin, and his tracker logs him in as the player only, so a tracker key never opens the admin pages. A colour too close to another member's (two
  greens, a red and a dark red) is not taken, on My page. **My page** (`/me`) is his own Current location and colour, and his
  **fleet** (`FleetService`, the tables `FleetShips` and `ActiveShips`); nobody else's. Five wrong passwords lock the login for ten minutes.
- **The Party tool** (`/party`, `PartyService`, `PartyToolController`): the party of the site in one place, for every tool. Who is playing now and in
  which in-game party (the party tracker's: anybody playing, not only the Hauling Planner's party; a player alone is a party of one), with the
  ship each one flies; everybody else the site knows (the tracker keys, the Hauling Planner's party, the fleets, the logs) with when he was last seen.
  A player's page (`/party/{name}`): playing or last seen, his party, the ship he flies and its drive (picked, or his last quantum jump), where the
  Hauling Planner has him, his tracker, and his fleet. Only for the admin until it is shown on the Tools page. It is not a card on the landing page and
  not in the row of tools (Frank, 29 Oct 2026; `SiteTools.Cards`): a **Party** button beside the viewer's name in the top bar (`TopBar.PartyButton`, on
  the tools' own pages too) opens a panel under it with the same parties, each a table like the Hauling Planner's old Party table (Frank, 29 Oct 2026:
  Member, Ship, QT Drive, every member's row in his colour, `Edits.ColorOf`; the names link to the players' pages; for the admin, and for the leader of
  a party for his members, an **Active** tick per member of the Hauling Planner's party, `/party/active`, sent in the background: off when somebody
  stopped playing and the logs still count him, so the planner plans no trips for him, the same as In and Out on the admin's Players page, holding until
  the logs change their mind), and "Everybody" to the Party
  page. The panel is fetched when it opens (`/party/panel`, a fragment: `_Panel.cshtml`), so a page costs nothing for it until somebody looks; without
  JavaScript the button is a link to the Party page. The button is marked in the tool's colour on the Party pages, like a tool in the row. A member
  who runs no SC Tools Tracker (no log of his own and no tracker key: only the others' logs show him) has a small **no tracker** tag after his name
  on the panel and the Party pages (his own page says Tracker: None), and his last seen is the last time the others' logs showed him (Frank, 31 Oct
  2026; `PartyService.WithoutTracker`).
- **The admin's Map** (`/admin/map`, a tab of his pages since 31 Oct 2026, one tab with Map places since later that day, Frank: out of the top bar, the landing page and the Tools
  page, where it was the Map tool at `/map`, so his bar has the same tools as a player's; `MapController`, `MapService`, `wwwroot/map.js`): Pyro, Stanton and
  Nyx from above (the map engine's buttons, one per system, in the map's top left corner, as on every map of the site since 31 Oct 2026, `SystemMap.systems`;
  not a row of tabs under the admin's; it opens on the system where most players are, `#stanton` in the
  address opens that one; no texts under it and no team status line), with everybody playing where the location tracker has him (below) and the objectives of
  the missions going on (`map.json`, every 3 seconds, with who the viewer is: `me`, as the Companion's feed has it, null for a visitor; for the map tools that are his, such as measuring a distance with his own ship, #408). The places are `StarMap` (Core): the stars, planets, moons, cities, gateways and stations of
  the three systems with their positions from the Star Citizen Wiki API (`/api/locations/positions`, downloaded with ScApiTool on 20 Oct 2026;
  Pyro's are the same as `PyroDistances`'), drawn with the system's Y to the right and its X down, like the Trip Planner's Pyro. A moon, city or
  station within 10 pixels of its planet is drawn at the planet (its tooltip lists them; the planet's label names the cities and stations at it).
  With the mouse over a station (or a planet with stations at it) a card says what it has, as the game's starmap does: its services (Refinery, Cargo deck,
  Trade terminal, Shops, Clinic, Food, Refuel, Repair, Habitation, Loading dock, Docking port, Freight elevator, Armistice zone) and its shops by name, from
  UEX (`StationServices` in the core, generated by a script from ScApiTool's UEX download of 20 Oct 2026, `space_stations`, `cities` and `terminals`,
  matched to the map's places by name; `/map/services.json`, game data the engine fetches once per page; the footer credits UEX; Frank, 31 Oct 2026, #409).
  Left clicks on the map measure a trip, stop by stop: every click adds a stop (a click near a dot snaps to it), every leg is the straight line between
  its stops in Gm and a quantum jump of its own, speeding up and slowing down each time, timed with the viewer's own drive (`mine` in the feeds: his
  fleet's active ship and the drive he flies, also while he is not playing; a visitor gets the distances alone). One leg says its distance and time between
  the stops; more legs say each one's at its middle, number the stops, and put the trip's total (jumps, Gm, time) at the last stop. A right click clears the
  trip (the browser's own menu stays away on the map). The stops live in his browser alone and go nowhere (#408; Frank, 31 Oct 2026). Where somebody died in the last quarter of an hour a skull sits with his
  name in gray, fading as the minutes pass (`deaths` in map.json: the location tracker's answer for that moment, from his jumps, inventories and areas up
  to it, `LocationTracker.DeathAge`; a player sees his party's alone, like the players; #410).
  The log says when a player picks a destination ("Projected Start Location is Ruin Station for route to destination rs_ext_pyro5_l2"), when he
  goes from one area into another (`<Update Inventory Location> Player [zzoega] is changing location. Landing [2026442305] -> [0]. Location
  [2026442305] -> [685899972]`: the game's numbers for the landing area and the area around him, `AreaChange`; the trackers send it from the update
  of 31 Oct 2026) and when he arrives. In the station's hangar he is **Landed** at the place; out of the hangar and still in the station's own area he
  is **Taking off** (the card says "Landed at Ruin Station", "Taking off from Ruin Station", with ", leaving for Gaslight" when he picked a destination;
  `hangar` in map.json). A jump picked at a station starts when he leaves the station's area into space, not when he leaves the hangar (Frank, 31 Oct
  2026: "no one really flies out from a station's area without quantum jumping"; in the three accounts' logs a player sometimes flew about inside the
  station's area for minutes after taking off). The landing area of the lines is the station's hangar: one number per station for all its hangars,
  the same in every player's log (Ruin's 2026442305, Levski's 3723364946), and moving the ship about inside the hangar writes nothing (Frank's
  hangar landings of 30 Oct 2026: leaving it is leaving the instanced hangar for the station). Which area is a station's own is not in the numbers: `StationAreas` knows the ones of the three
  accounts' logs (the numbers are the same for every account) and learns more from the logs on the server (a hangar lets out into it, and he leaves it
  again within half a minute, three times or more and in at least three of ten take-offs); where the hangars let out straight into open space (Levski,
  Orbituary, the Pyro Gateway, Everus Harbor) taking off is leaving. Replayed on 4,549 jumps: 2,553 start later than before (10 seconds is the median),
  1,838 the same. The next area, well into the jump, is where he
  jumped to: he is there, a few seconds before the arrival line (`LocationTracker.JumpTimes`). Measured on zzoega's logs: from leaving to coming in, a
  route takes the same time to about 2 seconds (Ruin to Rod's Fuel 281 to 287 s, seven times), from the pick it varied by a minute. Picked out in
  space there is no area to leave: it is taken to start 10 seconds after the pick (`LocationTracker.JumpDelay`). The page
  moves him along his route by his drive (`Crew.QtOf`: his fleet's active ship, crafted as it is; the same motion as `QtDrive.Seconds`), with a
  speed meter (Mm/s, in c, against his drive's top speed; it stays at 0 Mm/s with his drive's top speed while he is not in quantum, so his card keeps its size,
  on the Trip Planner too; `top` in map.json; Frank, 29 and 30 Oct 2026). `LocationTracker.Follow` walks his jumps: the start name sets his system and place
  (`StarMap.OfName`: "Pyro System" is the system alone; a name two systems have, like "Nyx Gateway", is the one he is in, and after a gateway the
  system it leads to), the destination is a place (`StarMap.OfQuantumTarget`: the Pyro stations, "pyro3" the third planet, "LOC_RR_S1_L2" HUR-L2,
  "rs_ext_nyx-pyro_jp1" Nyx's Pyro Gateway, else a place whose name is in it, like "levski_all-001"), and the arrival puts him there. The game's own
  code for a station or a gateway is read in every one of those (`Atlas.OfCode`: "RR_P6_L5" and "PYR6 L5" are Megumi Refueling, planet 6 and
  Lagrange point 5 like "rs_ext_pyro6_l5", "RR_P6_LEO" is Ruin Station in low orbit; in Stanton by the planet's three letters, "RR_CRU_L1" and
  "CRU L1" CRU-L1 Ambitious Dream Station, "RR_HUR_LEO" Everus Harbor; "RR_JP_PyroStanton" the Stanton Gateway in Pyro; RR is Rest & Relax's, which
  the game puts on every station, Frank 31 Oct 2026). Coming near a
  place puts him there too: the game names the area he is in with his inventory lines (`<RequestLocationInventory> Player[zzoii] requested inventory for
  Location[Nyx_Levski]`, `Location[RR_P6_LEO]` around Ruin Station: the space where a hangar can be called, not where he opened an inventory, Frank 30 Oct
  2026; the system by the start of the id, the place by the rest, `Atlas.OfLocation`; the trackers send these lines from their next update), also after
  a jump to somewhere the map does not know; an area the map does not know is on Map places, where Frank says what each one is. A jump with no destination picked (he jumped at a marker he looked at, or
  he is crew) is a jump too, not nothing: a crew's arrival gets the destination the pilot picked (another player of the site with the arrival of the
  same ship, by its entity id, within 10 seconds, the members of his party asked first: `LocationTracker.WithPilots`). Before the arrival, a crew member
  flies with the pilot too: the ship's area lines are in everybody's log aboard, into the same area within a second or two, so a player with no jump of
  his own who went into an area the pilot went into since he picked his jump is on that jump (`LocationTracker.AboardJump`; Frank, 31 Oct 2026: zzatvo
  stayed at Ruin on the map while he rode in zzoega's ship). An arrival seconds after the inventory line of a place is that arrival (zzatvo's log had the
  Nyx Gateway's inventory line 47 ms before his arrival line, and the map took the arrival for a jump of its own, "off the grid"). When no area line came
  before the pick (a tracker that just updated, a game just started), the first one after it says whether he was in a hangar when he picked; else it is to the objective of a mission going on then (below); else he
  is "Off the grid: out of comm-array range" since then, with the last place the map had him at ("last ping at Levski"), where his marker stays, faded
  (`lastPingAt` in map.json; Frank, 31 Oct 2026: a rough overview beats no marker). An arrival with no destination while the area lines have him landed
  is the jump that brought him there, written late (zzoega's log had it 81 s after he landed at Levski), not a jump off the grid. A player the map has not
  placed yet has "No signal yet"; an area the map does not know (a mission's spot) leaves him where it had him (Frank, 29 Oct 2026: zzatvo
  fell to "No signal yet" at Ruin). The Hauling Planner's pickups, deliveries and hand-ins place a member of its party too (`Whereabouts.OwnLocationOf`,
  `LastMoveOf`), when they are newer than anything his jumps say; his card says so ("the Hauling Planner"). The other way too (Frank's list of 29 Oct 2026: the map and the planner learn from each other):
  a worker's Current location and the station he is flying to take the location tracker's place when it is newer than his last pickup or delivery
  (`Whereabouts`, `TrackerState.Locations`): a crew member's jumps through the pilot's log, a destination or an area named on Map places, a mission's
  objective. Both sides are one answer since #388 (the location tracker, below): until then the server copied the map's places to the planner every
  six seconds and the map took the planner's back. Not in the middle of a run, though: while he flies a run his
  trip is planned from his last pickup, and while he carries waste out of Ruin it is planned from Ruin, whatever the map says (Frank, 30 Oct 2026:
  the map put him at Patch City before he had delivered the waste, and the Ruin "Collect Waste" row and the "Deliver Waste" of the Patch City
  row vanished from his Current Route until he had; every row of a route stays until the route is done). The players' cards stand beside the map (under
  it on a narrow window). The objectives list beside them and the **Active Contracts** under the map went to the Companion on 30 Oct 2026 (#380, below),
  where they are the player's own, and the map got the room: 1,060 px wide (820 before). map.json still has the objectives (the map draws them) and the
  missions going on (`missions`: who has each, its open objectives, the Mission Library's columns for its kind and its `key` in the library), which the
  Companion and the Hauling Planner's Session tab show. The objectives he can jump to are the ones the game paired with a quantum beacon (`<PairWithMissionNavpoint>
  MissionObjectiveMarker_21517[21517] - Paired with NavPoint: MISSION_QT_Quantum_Beacon_LongRange_…`, `HaulingLog.Beacons`, with the objective's
  text from its popup, "New Objective: Go to Asteroid Cluster", and the contract's system from its name, `…_DrawOutBoss_Nyx_Super`): a destination
  that is a beacon, or an arrival with no destination while one is going on (the latest; until a minute after it was done), puts him "at the
  mission: Go to Asteroid Cluster" (at the place its text names, when it names one). A mission's beacon or marker is never a place to list on Map
  places. **A beacon is placed by its route's fuel estimate** (#403, `BeaconRanging`; Frank, 31 Oct 2026): every route the game works out writes
  "Successfully calculated route to … fuel estimate 738770.25" (`QtEvent.FuelEstimate`, a `[QuantumTravel]` line the trackers already sent), and the
  estimate is the drive's fuel burn per km (as the game shows it in Vehicles) times the distance in Gm times 268.2 (his C2 and Perseus agree to 0.03 %).
  So the estimate over the ship's estimate per Gm is how far the beacon is from where he starts; the ship's estimate per Gm is learnt from his own
  jumps between two known places in that ship (the latest), else it is his fleet's fuel burn for that ship (My page, `FleetService.FuelPerKmOf`) times
  268.2. The beacon is the hidden spot of the game data at that distance, within 0.3 % (`HiddenSpots`: the 310 places the game hides in its starmap
  but a jump can go to, from the ScApiTool download: Nyx's 31 Glaciem ring and 30 Keeger belt mission spots, the hidden Breaker stations BRK-235,
  267, 304, 521, 608, the Moraine Bases, the QV Logistics Stations, Pyro's Derelict Outposts and Contested Zones, Stanton's hidden places; not
  found by a name or a destination of the log). That night's two bounties came out at BRK-304 and at a Glaciem ring spot, 1.2 and 0.4 Mm off, the
  next spot over 370 Mm away. The map knows the spots but draws none of them: map.json's systems carry a spot only while a player is at it or flies
  to it (`hidden`; the engine places him there and draws nothing for it), so he flies there and stands there "at the mission". At such a spot the
  game names only the system as the start of his next route ("Projected Start Location is Nyx"): he is still there. A beacon with no spot at that
  distance, or a ship whose fuel is not known, stays "at the mission" with no point, as before. **Its objective is there too**: once anybody has
  set a route to a beacon (picking it in the starmap is enough, no jump needed), the objective's gold diamond is at that spot for everybody with the
  mission (`PlayerLocation.Ranged`, by the beacon's id). And every objective whose marker is in the same zone (the marker's `zoneHostId`: the game's
  id of the cluster it is in, the same for every objective there on that server; zzoega's log of 31 Oct 2026 had the bounty's "Go to" and "Return
  to Glaciem Ring Cluster" in one zone) is at that spot without a route of its own. Before anybody set a route, the objective is in the list only:
  the marker's own position is the same offset in every cluster, so it cannot tell which one. A jump whose start is not known shows as a ring at the destination with his name and an arrow. A player is an arrow in his colour (pointing along his
  route in quantum, up when he is at a place) with a ring pulsing out from it and his name beside it in his colour; players at the same place
  are one under the other. Objectives: at a station when their zone is one of the Pyro stations
  (`StationIds`), where they are when the marker is in the system's own frame (in the system the player is in), at the place their text names
  ("Go to a debris field above Daymar": `LocationTracker.NamedIn`), else only in the list, by what they say and the contract's system (a bounty's marker
  is at 0, 0, 0). Only for the admin until it is shown on the Tools page.
  **One map engine** (Frank, 29 Oct 2026): the map itself is `wwwroot/systemmap.js`, a file of the site both the Map tool's page and the Hauling
  Planner's page load before their own script (with its version in the address, so a browser keeps it until it changes). The Trip Planner's map is the Companion's size (820 px wide, the Map tool's until #380; Frank, 29 Oct 2026), smaller on a
  narrower window so the workers' tables keep their columns. `SystemMap.create(box)` draws a system (`show`: the places of map.json's
  `systems`, drawn anew only when they change), the objectives and players of map.json on it (`draw`, with a filter by name), gives the caller a layer
  between the system and the objectives and the anchors of the places (`layer`, `at`) for his own drawing, and makes the line and the speed meter of a
  player by name (`meter`: what he does, `describe`; in quantum his speed every frame). The Hauling Planner's Trip Planner tab draws Pyro with it:
  `/hauling/map.json` is the same data for Pyro alone (`MapService.Snapshot(now, "pyro")`), for whoever sees the Hauling Planner, every 3 seconds while
  the tab is shown; a page outside the site (none since 30 Oct 2026) has no such address (404: the poll stops) and shows the routes alone, on `planner.map` of data.json (Pyro
  from `StarMap`, `Atlas.SystemData`) with every planner station's place (`planner.stations[].key`). The page draws the planner's routes on the engine's
  layer, the players and objectives of the workers shown (an objective has `who`: everybody with the mission, a shared one placed once), and the
  engine's meter under every worker's name. The planner's and the map's "done" agree: a pickup the planner credits (a COMPLETED or an INPROGRESS event,
  `SessionBuilder.PickedUp`) is off the map at once, and an objective one holder's log says is done is done for everybody with the mission.
- **The Companion** (`/companion`, `CompanionService`, `CompanionController`, `wwwroot/companion.js`; #380, Frank 30 Oct 2026): a player's own page to keep
  open while playing, made as a copy of the Map tool (no texts under its map and its contracts since 31 Oct 2026, Frank: what the Game session numbers are
  is the tooltip of its table). **Three tabs** since 31 Oct 2026 (Frank), in its row under the bar with the team status line at the end, each with its
  own address and switched without a reload (one page: the map, the polls, the admin's toast and his cards go on whichever is shown; `companion.js`):
  **Overview** (`/companion`, `/companion/overview`: the map, the Players, the objectives where the game session was, and the Active Contracts),
  **Session** (`/companion/session`: the game session) and **Contracts** (`/companion/contracts`: the Active Contracts again, and under them the
  Completed Contracts, which were on the one page before). **The Gauntlet Tracker's Session tab** has the same map with the Players beside it, and beside them the Game Session with the Grind Session under it (both tables always there, nought while nothing is counted; Frank, 31 Oct 2026) (`/gauntlet/map.json`,
  `CompanionService.Map`; `map.js` with the feed named on the map, `data-src`, as the admin's Map; Frank, 31 Oct 2026). Personal (`Audience.PartyOf` of the viewer: the player the admin looks as with View as, else who is
  logged in; the admin's own view, View as: Admin, has everybody on the map and everything else his own player's, and View as his own player is a
  player's Companion, Frank 30 Oct 2026): the map with him and his party (map.json cut by `DataViews.MapFor`, with `me`),
  the Players list, the objectives beside it, and under the map the party's **Active Contracts** and **Completed Contracts**, both with who in the party
  has each. Completed Contracts are the contracts the party ended in the viewer's current game session (`MissionLibrary.Ended`: completed, failed or
  abandoned, newest first), with how each ended and when in place of Open, and the same columns. Beside the players is the **game session**: the Session
  tool's top table except Games (time played, crashes, missions completed, aUEC, MG and Council scrip, earned, per hour; his own) for the current game
  session only (`GameSession` in the core: from when the game started until an hour after it was closed, crashed or quit and not started again; a game
  started again within that hour is the same session; when it is over, the last one is shown, marked ended, until the next game; Frank's answers). The
  earnings are his part, as on the Session tool (below); a player with **combined accounts** (the Account page, `AppData.CombinedAccounts`: Frank's
  zzoega, zzatvo and zzoii) sees the aUEC, the scrip, the Earned and the per hour of all of them together over his own game session, per hour of his own
  time played (Frank, 30 Oct 2026; the missions completed stay his own). Its
  data is `/companion/session.json`, polled every 5 seconds with its ETag (304 when nothing changed; made again when a piece of a log came in, the library
  or the scrip's value changed, or a minute went by): no running time in it, the time played and the per hour count on in the browser from the running
  game's start (the clock rule). The map opens on the system he is in (else the address's, else where most of his party is), and while it shows the
  system he is in it follows him when he jumps to another one; a system he picked otherwise stays. The **admin** gets Edit Mission Info's fields in the
  contracts' rows (name, faction with the list of factions, SCU, reward, MG and Council scrip; `ContractEdit`), sent in the background to
  `/admin/missions/set` as soon as one changes (`autosave.js`), also while he looks as a player; a table is not drawn again while he types in it. A
  visitor sees the map's places and a note to log in. Only for the admin until it is shown on the Tools page; in the top bar and on the landing page it is
  left of the Hauling Planner.
- **Map places** (Admin → Map, `/admin/map`: the map in the top left, **Not known yet** to its right with the players under it, the rest under
  them; the Map places tab of its own, `/admin/places`, until 31 Oct 2026; its changes still send to `/admin/places/…`; `MapPlaces`, tables `MapPlaces` and `MapAliases`): the admin's changes to the map's places,
  used at once (the map reads them as an `Atlas`: `StarMap`'s places with his changes). **Not known yet** lists the quantum destinations and start
  names in the logs on the server that the map cannot place (`LocationTracker.Follow` reports them), with how often, when last, who and the system;
  for each he says what it is: a place, or *not a place to show* (a mission's marker, an encounter), and a text ending in * is every one that
  starts so (`AsteroidCluster_Pyro_*`). His word comes before the game data's rules; a party member's marker is never a place. **Places** lists
  every place with its name and short name (the label on the map; the full name since 30 Oct 2026, when Frank wanted "Dudley & Daughters" and
  "Rod's Fuel 'N Supplies" written out on the maps as the Hauling Planner has them again, instead of "Dudley", "Rod's Fuel", "HUR-L1"), which he can change (empty: the game's again; the game's name still finds
  the place). **Add a place**: its system, name, short name, what it is, and where: at a place of that system (in its orbit when it is a
  planet or a moon) or X and Y in Gm; optionally for a text of the log, which is then that place (the *add it* link of a Not known row fills
  it in). **The admin's notice of unknown places** (#414, Frank 31 Oct 2026; `UnknownPlaceAlerts`, `MapPlaces.Sightings`, `wwwroot/alerts.js`, loaded for
  the admin alone on every page): a toast in the top right with a short sound ("Map places: unknown place needs registration"; a click opens the Map tab's Not known yet)
  when somebody else meets an unknown place, the first time anybody does, and when one of his combined accounts comes to one that is still unknown, once
  per visit (sightings less than 10 minutes apart are one visit). Being there is an area he came into, a start name, or a quantum destination he arrived
  at (not one he only picked). On his Companion a card under the map for every unknown place his accounts were at in the last half hour, with Map places'
  form (Save is sent in the background: he stays on the Companion, the card says what was done) and **Later**, which hides it until his next visit there. The page asks `/admin/places/unknown.json` every 5
  seconds; sightings from before the server started are not news (a new version of the site does not announce them all again), and the Laters are kept
  in memory. A browser plays the sound only after the page was clicked once since it loaded; the toast's "Sound" switches it off in that browser.
- **The fleet.** On My page a player keeps every ship he owns, the quantum drive in each, and the two qualities of a crafted drive, 0 to 1000 with one
  decimal (a crafted drive's quality is not a whole number: Frank's C2 is 786.6 and 695.3, 31 Oct 2026) and 500 as bought, in a straight line between: the speed quality (1000 makes the drive speed 20 % higher, 0 20 % lower) and the fuel quality (1000 burns 20 %
  less fuel per distance, 0 20 % more; the game's Crafting Lab, a TS-2 at 0.1417 /m, 0.11 at 1000 and 0.17 at 0; Frank, 31 Oct 2026). The game shows a
  crafted drive's stats, not its qualities, so each quality is a number field with arrows (a tenth a click) that starts at 500, and the table shows the drive speed and
  the **quantum speed** and the **fuel burn** as the game writes them under the ship in Vehicles ("Quantum Speed: 509.72 Mm/s", "Quantum Fuel Burn:
  141.68 per Km": the game data's `fuel_rate` times a billion, `QtDrive.FuelPerKm`) changing at every click (the page's script works them out as `FleetShip.SpeedText` and `FuelText` do): he clicks until they are the game's. The
  qualities are saved in the background once the clicks stop (autosave.js `data-settle`, the JSON back has the new stats), the rest of a ship's fields
  load the page again. The fuel quality is not used yet: it is for placing a mission's quantum beacon by the fuel estimate of the route line, which is
  the drive's fuel burn times the distance times one factor for every ship (Frank's C2 at 130.61 per km and Perseus at 120.33 gave the same factor, 31
  Oct 2026; see `docs/map-sightings.md`). The ships to pick from are every
  spaceship of the game data (`ShipModels`: 231, from the Star Citizen Wiki's vehicles in the ScApiTool download of 20 Oct 2026, with their classes,
  cargo grid and stock quantum drive; ground vehicles, display and concept models left out); a ship without a drive picked flies its stock drive
  ("Beacon S1 (stock)"); a ship takes the drives of its stock drive's size, so only those are listed and another size is refused (Frank, 29 Oct 2026;
  a ship the site does not know lists every drive). His **active ship** is the one he picks, or Automatic: the ship of his latest quantum jump (every quantum travel line names it
  by the game's class, `CRUS_Starlifter_C2`, `VehicleLines`; only the lines about his own ship: he picked a destination, the route, the arrival, his
  ship's navigation; not "Local Route Guard - Server Rerouted", which the game writes for every ship that comes near him), the ship of his fleet that is that class's ship (`ShipModels.OfClass`), else the one whose
  name fits the class best; a class that is not in his fleet shows by the game's name ("Crusader C2 Hercules Starlifter"). The Hauling Planner flies the active ship: its name (when it is a hauler the planner knows: a name like "Crusader C2
  Hercules Starlifter" is the C2), its drive and how much faster the drive is (`AppData.DriveSpeedBonus`, `Crew.QtOf`) go to the planner every few
  seconds (`FleetService.SyncPlanner`), so the capacities and the flying times follow it. The Trip Planner, called **Route Planner** on the page since 30 Oct 2026, is the first tab (Frank, 29 Oct
  2026: `/hauling` opens it; its choice of Everyone, a party or Only me sits at the right end of the logged in player's own route card, and a card's heading says where he
  is once, with the map's "since" or his jump after the station, instead of the map's line under the name; the route cards sit under a **Players**
  heading beside the map, as on the Companion, and with nobody of the party playing the heading stays with "Nobody of your party is playing now.",
  31 Oct 2026), and the tabs are a row under the top bar in the tool's colour like the admin's pages (30 Oct 2026; the Gauntlet
  Tracker's tabs and the Map's systems too, through the layout's `subnav` section; the tools' pages have no title of their own, the bar says where you are; the live text sits in the top bar left of Tracker on every page (`live.js`: green "● live" while the site answers; a page that polls its own
  data says how its poll went, any other asks `/live` every 15 seconds; the time the data was made went on 31 Oct 2026), the team status line at the right
  end of the tabs' row (the session's name was there until 31 Oct 2026: the Session tab says it), and the Session tab has no line saying whose session any more, 30 Oct 2026), the Σ line of its Current Route table is like the Queued routes' total, and the Time of a run on the way counts up under
  the map too. The clocks count up from when they started (`runsLive`, `since` in the data: UTC) by the browser's clock, like the Session
  tool's clock: the text in the data was made when the data was made, which can be a while before the page fetched it, so counting on from it made
  the clocks lag and jump (Frank, 30 Oct 2026). On the Session tab the admin picks the Lost Cargo of a run in the Cargo runs table (`/hauling/runs/lost`, sent in the background; his
  Session page, where it was too, went on 31 Oct 2026). The **Stops** board (`WebSnapshot.Stops`) reads the from-Ruin drop-offs the same way as the pickups (Frank, 29 Oct
  2026): a pickup location is a row with its batches, its cargo, its value and Ruin as the **Drop-off Location** (the station, for the stops of a contract
  to another station: a row of their own, stage 1a, 30 Oct 2026), and every station that drop-offs go
  to is a row of its own with Ruin as the pickup, how many drop-offs, the cargo with its SCU in containers, their value and the station as the drop-off
  (in gold); the most valuable row on top. The SCU and the boxes are two columns, **SCU** and **Boxes**, on the Stops, Finished stops and Current Route
  tables (Frank, 29 Oct 2026), and the Queued routes have an SCU column beside Batches (everything the trip carries). On the Current Route the drop-off
  of a planned trip has its SCU and boxes on the line of the stop it goes to, and the hauler of that trip (or the admin) types the SCU there
  (`/hauling/trips/scu`, sent in the background): it is kept for that drop-off only (`AppData.TypedScu`; the admin's Session page, where he typed them too, went on 31 Oct 2026), not in the Mission Library, the plan is worked out again with it, and the game's own number wins once it writes it. The planner
  leaves a station with a single batch out of the trip he is flying when it can (`RoutePlanner.LoneBatchSeconds`; Frank, 30 Oct 2026: the biggest stops
  first, a lone batch later, when new missions may have filled the station up). The Party button's panel in the top bar shows every member's
  ship and drive ("TS-2 S3 +20 %"); the Hauling Planner's page has no Party table of its own since 29 Oct 2026 (Frank).
- **SC Tools.** The site, `sc-tools.fwjonsson.dev`, is a landing page with the title over a screenshot of the crew's ship and a link for every tool (`/`, `Views/Home`); the Hauling Planner's page is at
  `/hauling`, every tab at an address of its own (`/hauling/session`, `/hauling/planner`, `/hauling/debts`, `/hauling/history`, `/hauling/leaderboard`; the program's own page
  keeps them after a #, and the old `#plan` links still work; the live page's tables keep to 1,900 px in the middle of an ultrawide screen, with the bar at the top
  from edge to edge as on every page). The old addresses `haulingtracker.fwjonsson.dev` and `sct.fwjonsson.dev` are gone (Frank took them
  out of the Cloudflare tunnel; their redirect went from the server on 30 Oct 2026, #375): one site, one login. A tracker that still has an old address
  saved moves to the site's by itself (`SiteAddress`). The plan of the tools is `claude/sc-tools-roadmap.md` in the
  project. The landing page (Frank, 30 Oct 2026, his pick of five mock-ups) has the title and the slogan centred over a screenshot of the crew's ship
  in space (`wwwroot/img/landing.jpg`, a shade over it so the text reads) and under them a row of links to the tools in the order of the bar, each with
  a dot in the tool's colour and "Only you see this" on the admin's hidden ones; the mouse over a link opens a panel with what the tool does and how
  it is used (`SiteTool.Text`) and what it shows now (the hauling session, your gauntlet runs this week, the missions left to fill in); the orbit rings
  and the cards went that day. Every page has the same bar at the top
  (`TopBar.cs`, `topbar.css`), the tools' own pages too: **SC Tools** (the only way back to the cards), the tools the viewer sees, and on the right
  **Tracker** (its page: download and setup; the tools come in the order Frank set on 30 Oct 2026, `SiteTools.Bar`: Hauling Planner, Gauntlet Tracker, Map,
  Session, Mission Library, and the Companion left of the Hauling Planner since #380, the landing page's links in the same order), who is logged in, for the admin **View as:** (a pick of a player, the first choice **Admin** for everybody's at once; the page and its tab stay as they are when the view changes; Frank 30 Oct 2026: the `sct-pov` cookie set by `/admin/pov`, `PointOfView`; the
  Hauling Planner's page, the Gauntlet Tracker, the Session tool and the landing page's links then show that player's view, and the Hauling Planner's
  own pick of a party on its Session tab went with it; what a page lets the viewer change still goes by who is really logged in), his **Party** button (the panel of who is playing, see the Party tool), My page, Admin and
  Log out (or Log in); on a phone all of it is behind a menu button.
  The part of the site you are in is marked in its own colour: a tool (by its address, `/gauntlet/stats` too) in the colour of its card, My page in
  green, Admin in red on the admin's pages and his account. The page's tables (their heads) and its buttons are in that colour too (`TopBar.ColorOf`
  sets `--header`, the colour mixed into the dark as the site's blue is; Frank, 31 Oct 2026: every tool's tables in its colour), and a # column is as narrow
  as the Hauling Planner's on every tool (`th.n`, `td.n`: the Gauntlet Tracker's Stats and Leaderboard, Session History's Games). The colours since
  31 Oct 2026: the Hauling Planner blue, the Companion yellow (pink before), the Gauntlet Tracker orange (the Map tool's colour
  before it became an admin page; yellow before, not red: the admin's pages are), Session History teal and the Mission Library grey (the other way round before), the Party purple. On an ultrawide screen every page keeps to a band of 1,900 px in the middle: the bar, the
  admin's row and the pages' content (`topbar.css`, `site.css`), like the Hauling Planner's live page (Frank, 29 Oct 2026). Every page and tab has the same room
  under the row of tabs (14 px, the Route Planner's: `main`'s padding, and the first thing on a page brings no margin of its own on top; Frank, 31 Oct
  2026), and the maps of the Route Planner, the Companion and the Gauntlet Tracker are the same size at every width (half the page's width since 31 Oct 2026, Frank: 820 px and 520 at
  1600 px or less before; 340 at 1200, the whole width at 900; `.maptool`'s `--mapw` and `.planleft`), the outermost places 16 px from the map's edge (46 before, `ROOM` in `systemmap.js`), with the Players and the next column's title level with the map's top. A page with a wide table (the
  Mission Library, the Session tool's games, the Gauntlet Tracker) takes the whole band too since 30 Oct 2026 (Frank; 1,400 px before), and the Mission
  Library numbers its rows in a # column on the left, on through its pages. The pages of the site end with a footer: the tracker's download, how to set it up, and the
  disclaimer. The admin's pages have a second row under it with his pages (Players, Scrip Value, Map, Tools, Account; Admin in the top bar opens Players), tinted in Admin's red with a red line on top, so it is plain that it belongs to Admin.
- **The admin's pages** change the data with the core's logic (`Editing/Edits.cs`, each change saved and worked out again at once):
  the **Session** page (`/admin`) went on 31 Oct 2026 (Frank): the sessions start and stop by themselves (below), the lost cargo of a run is picked in
  the Hauling Planner's Cargo runs table, the SCU of a drop-off the game did not tell is typed on its Current Route, and the **start time** is a row of
  every tool's session for the admin: the Hauling Planner's Start Time in its Session table (`/hauling/session/start`, `Edits.SetStartTime`), the
  Companion's Start time on its Session tab (`/companion/start`, in the browser's time zone) and the Gauntlet Tracker's on its Game Session and Grind
  Session tables (`/gauntlet/start`, in UTC as the page's times). The Companion's and the Gauntlet's sessions are worked out from the logs, so the
  admin's start is kept by the start the logs give the session (`SessionStarts`, in `SiteValues`); the games, missions and runs count from it, and empty
  gives the logs' start back. **Players** (`/admin/players`, in place of the Party and Tracker keys pages since 31 Oct 2026: every
  registered player and every member of the Hauling Planner's party, with when he registered, when he last played, his tracker, Active (Auto, In or
  Out, sent as soon as it is changed), his keys with Take back, Make a key, and Into and Out of the party; his name opens his **My page** as he sees it
  (`/me?player=`), where the admin sets his location, colour and fleet for him; the party follows the game's parties, see below). The admin's Debts page went on 31 Oct 2026: he changes
  the debts on the Hauling Planner's **Debts** tab, below.
- **Sessions start and stop by themselves** (`Sessions/AutoSessions` in the core): a session starts when a mission of the tool is accepted and goes on
  while anything happens with its missions; after an hour with nothing (also a crash, or the trackers off) it ended 5 minutes after the last thing that
  happened. The Hauling Planner: one session for the party (everybody's hauling contracts to and from Ruin: accepted, picked up, delivered, ended; and
  while a contract is still going, everybody's quantum jumps and the latest log line count too, since a party on long routes can go an hour without a
  pickup or a drop-off: on 29 Oct 2026 the session ended at 22:45 with nine contracts going; and a session never ends while a contract is still open,
  `AutoSessions.Apply(going)`, Frank 30 Oct 2026: a break of an hour or more at a station or a loading of twenty minutes is still the session; an
  open contract holds it for three hours after the last line in anybody's log, `OpenContractHolds`, so somebody who went to bed with one does not
  keep a session going for ever),
  kept in `ManualSessions` (`Auto`) so it keeps its key and what was typed for it; one that stopped goes on again when a log that came late shows it did
  not end, and it never gets shorter (`SCTools:FollowSessions` = false gives the Start / Stop Session button back). The Gauntlet Tracker: a grind
  session per player from his runs (`GauntletService.GrindSessions`), the earlier ones too.
- **The Gauntlet Tracker** (`/gauntlet`, `Gauntlet/` in the core, `GauntletService`) reads Gilly's Combat Gauntlet from the uploaded logs and keeps
  every run in the database. **Session**, **Stats** and **History** are the viewer's in-game party's (everybody in it now, from the logs, `PartyService`;
  alone or not playing: his own runs; Frank, 29 Oct 2026): the game session from the first of the party's games, one grind session for the party
  (`GauntletService.GrindSessions` of several players; the game session and the grind session side by side on Session, together as wide as the wide
  tables, Frank, 30 Oct 2026), and on **Stats** the **Earnings** (the lifetime totals; top left), the top 10 fastest of a scenario, which says whose
  run it is (top right), the **Earnings per scenario** (total aUEC per hour at the average time, the MG scrip per hour alone, at the fastest time,
  lifetime earned; "Earnings ranking" on History until 30 Oct 2026) and the times by scenario. **History** has the earlier grind sessions (on Session
  until then). A
  mission shared in the party is in every member's log, and each member's run earned him his part of it (the contracts tracker, Frank 30 Oct 2026: the
  aUEC split evenly among who completed it, the MG scrip only to the one who accepted it first; before, it counted in full for each), on every tab and
  on the leaderboard, so a party's or one player's combined accounts' earnings count it once. It is timed from each member's own accept (the one who
  gets a share has an accept line too, seconds later: Frank's logs of 29-30 Oct 2026), so a shared run is timed once per member. What a scenario pays (the Stats tab's per hour) stays its whole reward. The old address `/gauntlet/ranking` goes to Stats. The **Leaderboard** ("All-time leaderboard") is everybody's: total earned, aUEC,
  MG scrip, total aUEC per hour over the timed runs, completed and completion rate, fastest run, favourite and best scenario, last played. The rewards
  are the same for everybody: the Mission Library's "Combat Gauntlet - Scenario #n", typed in the library's rows (the admin's Gauntlet page and his
  Contracts page went on 29 Oct 2026).
- **What scrip is worth** is the admin's **Scrip** page (`/admin/scrip`, `ScripValues`, the table `SiteValues`): aUEC for one MG scrip and for one Council
  scrip, the price he thinks they sell for now. Every tool counts scrip with these, the past included (a new price changes old totals too).
- **The contracts tracker** (`ContractTracker` on the server, `ContractTracking/` in the core; #383, Frank 30 Oct 2026) is the one place that knows
  every contract and from which every tool gets its contract money. For every contract (one mission: two copies of the same contract taken separately
  are two) it knows, from what is kept for good, who took it, who accepted the share, how and when it ended for each of them (the Mission Library's
  missions, every player's own row; the Gauntlet Tracker's runs), what it pays (the Mission Library's typed reward, else the Star Citizen Wiki's; the
  admin's payout override on the Debts tab wins) and the Hauling Planner's payout of it (`AppData.Payouts`, kept with the sessions). The split
  (`ContractSplit`): the aUEC evenly among the players who have the contract when it pays out (the one who took it and everybody who accepted the share
  and still had it; being in the party without it gets nothing), the MG and Council scrip only to the one who took it (when he abandoned it, to the one
  who accepted the share first of the ones who still had it: the game's rule), and a contract the planner paid
  out keeps the planner's payout (so a past session's debts never change). **Who took it** is the one whose accept was not a share. The game's
  "Contract Shared" line, only in the log of the one who gets the share, has no mission id (all zeros) and often the contract's template for a name;
  a second to half a minute later his log says "Contract Accepted" with the real mission, as if he took it. So `MissionReader` marks an accept as a
  share when a share came in the half minute before it (each share is one accept), and the Mission Library keeps a mission shared with a player
  (marked Shared; before #385 it left it to the sharer's log) and keeps the mark once any of his logs says so (the old logs are read once more for it:
  `HistoryReadVersion` 3). When the logs do not tell one taker, the one who accepted first does (the PCs' clocks can be seconds apart, so only then).
  The game says "Contract Accepted" again for every contract at a login, so the library keeps the earliest accept. **When the taker abandoned it**,
  its scrip goes to the share accepted first of the ones who still had it: the one with the longest from his accept to the completion, both by his
  own PC's clock. Checked against the game's own "You've Earned: 14 Rewards" lines after the completions in Frank's, zzatvo's and zzoii's logs of
  Aug-Oct 2026: right for 386 of 388 shared gauntlet runs (the other two were accepted within a second of each other). **The game's own clues**
  (Frank, 31 Oct 2026: right also with players he does not know, whose logs the server never sees): the Mission Library keeps, for every player's
  mission, his id in the game (his login line's geid, the objective markers' "Player: zzoega[493008374737]", his "Ending mission" lines), what his log
  said about the other players (the game's "PlayerJoined" and "PlayerLeft" with their ids: `MissionMember`) and the game's "You've Earned: N Rewards"
  right after he completed it. A reward line with the scrip's amount says who got the scrip, whoever took it; a holder whose own log never says how it
  ended (it ends with a crash, or is not on the server) left it when another holder's log saw him leave before that one's completion; and the shares
  are in the order one log saw them all join (one clock: the taker's), else by the time from accept to completion. **A mission the game took from him at
  an exit to the menu** (Frank, 31 Oct 2026, #399: a bounty he had was gone when he came back twenty seconds later) leaves only a "PlayerLeft" with his
  own id in his log, a second after the exit, and no "EndMission" (the normal abandon has both, two milliseconds apart). Both readers end the mission
  for him on that PlayerLeft as abandoned (`HaulingLog` for the Companion's Active Contracts, the map's objectives and the Session tab, `MissionReader`
  for the library and the contracts tracker), unless the game says "Contract Accepted" for it again (an event mission gave it back to him two seconds
  later, 28 Aug 2026; the library waits ten seconds of log for that accept, since what it writes down stays). The old logs on the server are read once
  more for the ids and the joins (`HistoryReadVersion` 4) and once more for the missions lost at an exit (`HistoryReadVersion` 5), and
  every tracker sends its old logs once more with the reward lines (`HistorySender.Version` 3), so the old contracts' scrip comes out right too. A player's combined accounts are added up where a tool shows his totals
  (the Companion, the Gauntlet leaderboard). It is asked by the Session tool and the Companion (`ContractTracker.Earned`), the Gauntlet Tracker (each
  run's part, `GauntletRun.Paid`) and the Hauling Planner's payouts (`Payouts.ComputePayouts` picks who is paid with `ContractSplit.PlannerPayees`).
  Worked out again on every question, so a reward typed in later counts for the past. The tests `tests/SCTools.Tests/ContractTracking` show what every
  tool shows for one evening of three players.
- **A contract screenshot read into the Mission Library** (#437, Frank 31 Oct 2026; the admin's only): every mission row has a 📷 button for the admin
  (the library's rows, the Companion's Active and Completed Contracts on both its tabs, the Hauling Planner's Session tab: Active Contracts by the
  mission's key, Completed Contracts by its name, `MissionLibrary.EntryOf`). It opens a little box beside the row, right where he is
  (`wwwroot/screenshot.js`): he presses Ctrl+V with a screenshot of that contract's card in the mobiGlas (or drops the file), checks what was read beside
  what the library has, and saves. Only for that mission: never one guessed by its name (a similar name could be another mission) and never one added (a
  misread name would make a row no log ever joins); a screenshot whose name does not look like the mission gets a note. Tesseract, a free text reader,
  reads it in his browser (tesseract.js 5.1.1 from the jsDelivr CDN, about 10 MB the first time; no AI, nothing to pay; about 8 seconds): the name, the
  Reward, Contracted By and the description, each from its own box (in pixels of a 3840x1600 screenshot; another size is scaled by its height around its
  middle, as the mobiGlas is, and a smaller picture's boxes are enlarged to that size, all but the reward's, whose digits read best as they are). The
  reward is read in two passes: freely, where the currency sign before it is a word of its own, then only the number with digits only (read with digits
  only, the sign was a 1: 35,000 became 135,000). The description is read twice, as it is and without the soft light of the hangar behind it (a
  blurred copy taken away): on a smaller picture a glow swallowed "7 MG Scrip". The server (`EditMissionsController`, `ContractCardReader` in the core)
  makes values of it: the reward only when its commas are every three digits and it is at least 1,000, the MG or Council scrip of "a bonus of 18 MG
  Scrip" in the description, the XP of "Reputation Awarded: 250" (else the "[250 Rep]" of the name), the faction of his list nearest to the one read; a
  reward outside the Star Citizen Wiki's range gets a note. Tried on Frank's screenshots: every reward of the clear ones exact (35,000 to 1,370,500),
  every scrip found, also on a picture at half the size with a light behind the scrip; the one with a lamp on the reward was not read, and says so.
- **The admin's lists and drop-downs** (Frank, 31 Oct 2026): Faction and Category are picked from his lists, kept in the box above the Mission Library's
  table on the right (add one, or × to take one out; `LibraryChoices`, `MissionLibrary.Choices`); they started with what the library had then (typed in,
  and the Wiki's). A mission that has one taken out keeps it (its drop-down still shows it). Legality is Legal or Illegal. Category, XP and Legality can
  be filled in like the reward (the kinds' `Category`, `Xp`, `Legality`): one filled in wins over the Wiki's, in grey until then. The same drop-downs are
  in the Companion's contract tables, and the 📷 box's Faction.
- **The Mission Library** (`/missions`, `Missions/MissionReader` and `Missions/MissionKinds` in the core, `MissionLibrary`) has every kind of mission
  the players took, by itself: the missions of the live logs and of the old logs are kept for good (`Missions`), grouped by their name as the game shows
  it (`MissionKinds`; the game's ContractId is one per offer, so the same mission at another place has another one). The contracts the Hauling Planner
  reads are kinds by their names like every other mission (Frank, 29 Oct 2026; before, by their contract line, 2 To Ruin... Direct From Ruin: those
  kinds go to the kinds by name when the server starts, with what was typed for them), so an Experienced contract and a Senior one with as many stops
  are two rows with their own rewards; a contract whose name no log showed is still grouped by its contract line. With every kind is what the log
  does not say: the aUEC, MG scrip and Council scrip it pays, its cargo in SCU (it wins over the suggestion in grey: the last SCU a hauler typed in for
  one of these missions on his Current Route, or what the game said for one; the boards use it; else the Star Citizen Wiki's in
  grey), another name, the faction (the Giver field, as it was called until 29 Oct 2026). With it is what the **Star Citizen Wiki** says about the
  mission, in grey: the **Faction**, the **Category** (the Wiki's type: Hauling, Bounty Hunter, Security...), the **Reward** (a reward in another
  currency says which), the **XP** (the reputation it gives) and the **Legality**; one the admin filled in wins over each. The server reads the
  Wiki's list of every mission once a day (`WikiMissions.LearnAll`, 1,800 missions, 200 to a page, `SiteValues` says when), so every kind can show
  them: a kind is the Wiki's mission with its contract id (from the logs' markers), else the one with its name (`MissionLibrary.LinkWiki`; the rep and
  blueprint marks a string mod adds to a name do not count). The page everybody sees is **read-only** (`MissionsController`; for everybody when the tool is shown on the Tools
  page, like the Hauling Planner's live page, else only for the admin): a library to look things up in, not a list of what was played last. The viewer
  chooses what to see (a search of the names, factions and categories, one faction, with or without rewards, only the kinds he played) and the order (click a column's
  header: names A to Z, numbers the biggest first, again for the other way round; the kinds without a value in the column last; `LibraryFilter` keeps
  it all in the address), 20 to a page. What is played is his own: **Failed / Completed** (how often he failed and completed it, hover for taken and
  abandoned) and **Last played** (sort by it for the latest he played; a mission he is playing now, taken in the last six hours and not ended, says so
  and has a green row), never anybody else's, and a visitor not logged in sees no plays at all. The admin changes the library **in its own rows** (`/missions`: for him a field in every cell; Edit
  Mission Info, his page of its own at `/admin/missions`, went on 31 Oct 2026, Frank; the fields send to `/admin/missions/set`, `EditMissionsController`): the same list, choices and order with a field in every cell (the faction to type, or to pick from every faction so far: a datalist, a
  new one is in it as soon as it is saved; each field saved as soon as it changes, in the background; the Notes column is gone, what was typed in it
  stays in the database). Both pages keep themselves up to date (`missions.js` asks the page's `version` address every few seconds and gets the list
  again when it changed, not while you type in it). A mission the player took himself is in it also when the log never says which offer it is (most
  missions with comms, like bounties, do not); one shared with him whose offer never showed is the other account's. The scrip columns are narrow (a
  mission pays two figures at most, their headers on two lines), so the mission and the giver have the room. The other tools count with it: the
  Session tool (all of it; its "no reward yet" line links the player to the kinds he played without rewards, the admin to filling them in), the
  Gauntlet Tracker (the scenarios) and the Hauling Planner (every contract pays the aUEC of its kind, else the Star Citizen Wiki's reward for the
  contract, else the built-in table by stops: `Earnings.KindReward` with the tracker's `JobBook`; the Contracts tab lists the session's contracts by
  name with what they pay). The first time it runs it reads the missions of the old logs already on the server; what was typed in the
  library when it went by ContractId is carried over to the names once.
- **The Session History tool** (`/history`; the Session tool at `/session` until 31 Oct 2026, renamed so the Companion's Session tab is not mixed up with it, Frank; `Play/PlayStats` in the core, `Playtime`, `SessionController`): your own play. Time played today, this week (from
  Monday), this month and in all, the games and the crashes (a log without the game's quit line, `SystemQuit`, crashed), the missions you completed
  and what they earned (aUEC, MG and Council scrip as the Mission Library says they pay; the scrip at the Scrip Value page's value), and per
  hour; the clock of the game you play now; a list of your games. Worked out again on every visit, so a reward typed in later counts for the past.
  What a mission earned is your part of it (the contracts tracker's `ContractTracker.Earned`, Frank 30 Oct 2026; before, every member of the party
  counted the whole reward of a contract they all completed): the aUEC is split evenly among the players who completed the same mission, a Hauling
  Planner contract pays what its payout split gives you (`AppData.Payouts`, the Debts tab's Mission payouts, also when you shared the payout without
  the contract in your own log), and the MG and Council scrip go only to the player who took the contract (not to one it was shared with). The
  games are kept for good in `LogFiles`: the old logs (now with the quit line: `HistorySender.Version` 2, so they are sent once more) and the games of
  the live logs while they run (version 0, so their backup log still comes and takes over).
- **Which tools everybody sees** is the admin's **Tools** page (`/admin/tools`, `SiteTools.cs`, the table `Tools`): a tick per tool, saved as soon as it
  changes. A tool that is not ticked is only for the admin: its link on the landing page (where he sees "Only you see this") and its page (a visitor gets
  the login, a player "not for you"; the hauling tracker's `data.json` too). A tool never set is as `SiteTools` says: the hauling tracker shown, the tools
  still being made (Session, Mission Library, Gauntlet Tracker, Map, Companion) only for the admin. The trackers' uploads do not depend on it.
- **The party tracker** (`Parties/PartyTracker.cs` in the core, #389): the one place that knows the parties, now and before. It reads the party lines
  of everybody's log once per refresh (`LiveTracker.Parties`: again when a piece of log comes in, when the members change, or after half a minute;
  else every page gets the same answer, `PartyBook`), and every tool asks it: the Party tool and its panel, the Gauntlet Tracker, the Companion, who
  may see whom (`Audience`: the party now and the session's hauling groups, `PartyBook.Together`), the Hauling Planner's Active ticks (below), the
  payouts and the contracts (who was in the in-game party when a contract paid out). `PartyService` keeps the site's players, fleets and tracker
  versions and asks it about parties. It keeps the history of every party (`PartyRecord`: who joined and left and when, who led it and when that
  changed, disbanded or not; a relog or a new log file shorter than fifteen minutes is not a change), served to the tools in the background (no page
  of its own, Frank 31 Oct 2026). A party made from an invite names nobody in either log ("You have created party" in one, "You have joined party"
  in the other, seconds apart since the PCs' clocks differ), so two of those within fifteen seconds are one party. Its leader is the one who made it:
  the game names a leader only when it changes, and the one who accepted an invite has "Party Invite Received: Accept Invitation?" in his log just
  before (the one who made it has none; right for 175 of 176 makers and 264 of 270 joiners in the three accounts' logs; Frank, 1 Nov 2026: the site
  said "zzoega's party" for a party zzatvo had made, since of two who come in together the PCs' clocks can put the joiner first). "X connected." and "X
  disconnected." (a party member's game came in or went out) are read too: "connected" puts a player without a tracker in the party of the log
  that says it (he may have been there before that player came), and changes nothing for a player with a tracker (his own log says when he plays).
- **The location tracker** (`Locations/LocationTracker.cs` in the core, #388): the one place that knows where every player is, since when, and whether
  he is on his way somewhere. It reads everybody's log once per refresh (`LiveTracker.Locations`: again when a piece of log comes in, the logs are worked
  out anew, the data is saved, the parties or the Map places change, or after half a minute; else every page gets the same answer, `LocationBook`)
  and takes in every clue: his quantum picks and arrivals, the areas he goes into (when a jump starts and ends), the inventories he opens, his pilot's
  destination when he rides as crew (the same ship id in another player's log, his party's logs first), the mission objectives he can jump to, the
  admin's Map places (the `Atlas`), and the Hauling Planner's own reading (`PlannerClue`: the station of his last pickup, delivery or hand-in, where it
  is newer than his jumps). Everybody asks it: the Map tool, the Hauling Planner's Route Planner and the Companion draw the same answer (map.json;
  `LocationTracker.Now` says where the map draws him now with his drive: at a place, on a jump with its UTC start for the moving marker, heading,
  at a mission, or off the grid), and the planner's Current location, My page and the Party pages take it through `Whereabouts` (as station ids,
  `PlannerPlace`). The plan is made anew only when what the planner takes from it changes (`LocationBook.PlannerKey`), and nothing in it moves with
  the clock (the clock rule). Before it (until 31 Oct 2026) the map and the planner each worked it out and the server copied between them every six
  seconds (`PlannerPositions`); `LocationPagesAsBeforeTests` recorded what every page showed then, and the tracker shows the same.
- **Who is active comes from the logs** on the server (the party tracker, and `Tracking/AutoParty.cs` writes it into the Active ticks), not from a tick
  set by hand. The game writes the in-game party in its notifications
  ("zzatvo has joined the party.", "Bileh123 has left the party." also when he quits or crashes, "zzoega is now party leader.", "The party has been
  disbanded."), and the trackers already send those lines. A tracker also sends the time of the last line of every piece, so the server knows how long each
  game ran (the log grows at least every few minutes while the game runs; quiet for fifteen minutes = the game ended, about as long as the game keeps a
  crashed player in his party; a game quit the normal way, with the quit line in its log, ended at once: `GameRun.Quit`). A member is active while his game
  runs, or while somebody who is playing has him in his party, so a player whose tracker is off is still seen through the others' logs. **The party follows
  the game's parties** (Frank, 29 Oct 2026): whoever the logs show in an in-game party with a member becomes a member by himself (`LiveTracker.Party`:
  joined with the settings every member has, active while the logs show him playing); a player alone, or in a party of strangers, is not (he does not
  haul with the party). Gone for less than
  fifteen minutes
  (a crash, a relog) does not count. A player's party only changes when the game says so (he joins or makes a party, somebody joins or leaves it, it is
  disbanded; when another log says he left, he is out of it in his own log too): a crash or a relog keeps his party, because the game puts him back in it
  and does not always say so (zzatvo's crash on 28 Oct: nothing about the party in any log afterwards). Only a game started more than an hour after his last
  one ended (`PartyTracker.Rejoin`) starts alone until the game says otherwise. It is written to the party changes with its time, so payouts and runs go by it as they went by the tick. The Players page
  shows who is in the game and with whom ("zzoega's party: zzatvo, zzoega"; the leader is the one the game named, else the one who made the party) and has
  **Auto / In / Out** for every member: In or Out holds until the logs show something new about him (In until he leaves, Out until he plays); a member
  the logs put out of the party is not in its table (Frank, 30 Oct 2026: gone from the page when he leaves; a line under the table names them, and he
  is back when the logs show him in a party with a member again, or when he is set In or added). It starts the
  first time the server runs it (the ticks before stay in the history); `SCTools:FollowParty` = false turns it off.
- **Your own party.** With more than one party (the ones in the game, "zzoega's party", named by its leader, and the parties that hauled in this session
  and are not playing now), the Session and Leaderboard tabs of the live page show the viewer's own party by themselves (the page knows who is logged in):
  the stops, contracts, session, leaderboard and runs of that party only (the missions its players have by their own logs, and their runs;
  `Tracker.Focus`); a viewer in none of them (or not logged in) sees all of them together. A line under the tabs says whose it is. The admin alone
  has a list after the tabs (the page knows he is the admin: `<meta name="admin">`) to pick whose session the tabs show: All parties, or one of them,
  his own first (remembered in the browser); everybody else just sees his own (Frank, 29 Oct 2026). The Trip Planner can still show Everyone, one
  party in the game, or Only me (the plan is the same; only whose routes are drawn and listed changes). The **Party** table (Member, Ship, QT Drive)
  sits beside the title and the tabs, level with the title, with the Session table under it; the Ship column has the room for a long name ("Crusader
  C2 Hercules Starlifter"), and a longer one wraps. The Stops and Finished stops boards have no From Ruin and Notes columns.
- **Hauling of every system** (`Tracking/HaulJobs`, `JobBook` in the core). Besides its own contracts, the planner takes every mission with drop-offs
  (`dropoff_…` objectives) that a member of the party has going, taken in the last 12 hours of the logs and not ended (with a party picked, only
  theirs): *RSI Disc. Month: Ling Small Haul*, courier runs, the cargo of a salvage order. Until 30 Oct 2026 they were on an **Other hauling** board of
  their own on the Session tab; since then they are rows of the Stops board like the contracts (stage 4a below), and the board is gone.
  - **To another station** (stage 1a of `docs/planner-any-hauling.md`, 30 Oct 2026): the Red Wind contracts from 2 to 4 stations to a station other
    than Ruin (`_StationToStation_…_Multi<n>ToSingle`, *Master | Stellar Large Haul | to Stanton Gateway*; `Mission.ToStation`) are the planner's own
    contracts, not jobs of this board: they are in the session, on the Stops board with their station in the Drop-off Location column (a row of their
    own beside the to-Ruin stops of the same location, `LocationRow.ToRuin`, `DropZone`, `DropName` from the "Deliver 0/520 SCU of Waste to Stanton
    Gateway" popup), in the Contracts tab (*2 To Station*) and in the Cargo runs (the hand-in at the station ends the run, `WorkerTrip.EndZone`, and he
    is at that station then, `Whereabouts`).
  - **The planner without a hub** (stage 1b, 30 Oct 2026; `Planning/RoutePlanner`): a second planner that plans these contracts with the to-Ruin
    ones. A batch is cargo at a station for a place (`RoutePlanner.GroupKey`: a station id alone is "to Ruin", `station>destination` is to another
    station; the kept stops have the same keys), a trip starts where the worker is (his run in progress, where he stands, or where his trip before it
    ended: `RoutePlanner.Chain`) and ends at the last place it delivers at (`PlannedTrip.Ends`; Ruin for the contracts to Ruin, so their plans are what
    the old planner's are, scenario for scenario), its route is the cheapest way through its stations and then its ends, and a station where batches
    for two places wait is one landing. The Current Route has a line per place it delivers at, the Queued routes say where each trip ends (*Ruin →
    Gaslight → Orbituary*), the map draws every route from its start to its end (`places` in data.json), and a run's distance and flight go to where
    it was handed in. A worker whose trips all start and end at Ruin keeps the queue order (waste first, one-stop trips, the value); one with a trip
    that ends elsewhere flies his trips in the planner's order, since each starts where the one before it ended. A contract to a station the distance
    table does not know stays out of the plan, with a note.
  - **One planner** (Frank, 30 Oct 2026, evening): the new planner was tried out for a day on a tab of its own, *Route Planner (new)*, beside the old one,
    each with kept stops of its own; then Frank made it the Route Planner. The old `TripPlanner` (every trip from Ruin and back), the second tab, its tools on
    the Tools page and the `FreeLockedTrips` column are gone, and so is the address `/hauling/planner-new`. `Tracker.Plan` is the one `PlanService`.
  - **Soft kept stops, 20-minute landings, the flying at a tenth** (Frank, 30 Oct 2026; the new planner only): the stop a hauler is flying to stays first in
    his trip, but its batch count is free, so he takes as many there as fit. The stops kept after it are preferred, not fixed (`RoutePlanner.KeepSeconds`, a
    third of a landing for a kept stop that leaves his trip): they give way when that saves a landing or fills the ship better, and never on a tie. His trip
    A ×2, B ×1, C ×1: a new batch at B makes it A ×2, B ×2 and C goes back; at A he takes 3 and keeps the nearer of B and C; at a stop D elsewhere that now
    has two, the trip becomes A ×2, D ×2. A landing counts 20 minutes in the comparison (`StopSeconds`; the lone-batch, leftover and fill-now rules follow
    it) and the flying a tenth of its time (`FlightWeight`), so the plan goes a long way for a fuller stop; the order of the stops within a trip is still the
    shortest route, and the times shown are the real ones. A batch his planned trip loses without a replacement costs a landing, so the trip is never emptied
    to save its landings, and a landing for a lone batch in a later trip counts half (`LaterLoneShare`: new missions may fill that station before it is flown),
    which is what lets a stop elsewhere with two batches take the place of two single stops. Half the search's restarts start from his kept trip, the other
    half free, so such a swap is found. `tools/PlanTest` prints where the old planner's plans differ.
  - **Interstellar legs** (stage 2, 30 Oct 2026; `GameData/StarRoutes`): the planner without a hub flies between any two places of the map. A place is a
    planner id: a Pyro station's id, or a place key of the map (`stanton/baijini-point`). Within a system a leg is one jump (between two Pyro stations the
    distance table's, as the game reads it, so nothing changes there); between two systems it is the jump to the gateway that leads to the other system,
    the crossing, and the jump from the arrival gateway (`StarRoutes.Legs`); a crossing takes 3.4 minutes (`GateSeconds`, the middle of 80 crossings in
    zzoega's logs, Frank's answer 4), counted in the flight time, "+ jump" in the Distance column, a dashed arrow (⇢) in the route. The **hauling jobs of
    the Other hauling board** whose stops the map has places for are planned on the Route Planner (new) tab with the Pyro contracts (`PlanService.JobParcels`):
    a parcel per pickup to the one drop-off, or per drop-off from the one pickup (Frank's Covalex master mission: one pickup in Stanton, several drop-offs in
    Pyro), each a batch of its own group with the stop's SCU and a share of the mission's reward (the library's, else the Wiki's), only for those who have
    the mission. A job with a stop the map has no place for (or only the planet) stays out with a note; one whose cargo is picked up already is noted and
    left to stage 4 (the runs do not follow jobs yet); several pickups to several drop-offs are not planned (the game does not say which goes where). The
    map's place for a worker in any system is where his trip starts (`Whereabouts.LocationOf(name, anyPlace)`; the old planner keeps to the Pyro stations).
    The map on the planner tabs has the **systems' buttons** in its corner, the engine's as on every map, for every system with how many of the players shown
    are in each (since 31 Oct 2026, Frank: only when the routes touched more than one before, Frank's answer 8; `data.freePlanner.maps`, `places` with the
    system of every place); it opens on Pyro, or on the system the routes are in when they are not in Pyro; a leg through a gateway is drawn to the gateway on the system it leaves, with the far end's name, and from the
    gateway on the system it enters; `/hauling/map.json` has every system.
  - **The other systems** (stage 3, 30 Oct 2026): any hauling mission starts a session, a courier too (`AutoSessions.HaulingActivity`, Frank's answer 7),
    so a party hauling in Stanton alone has a session and a plan on the Route Planner (new) tab. A stop the texts and the zones do not place is placed by
    its marker's position when that is in the system's frame (metres from the star, as the Stanton pickups' are: the nearest place of the map within 0.05
    Gm, `HaulJobs.AtPosition`; Frank's answer 5), else by the ends of a direct haul's title (*Baijini Point > Ruin Station*). A worker the logs and the map
    place nowhere starts at the session's **hub**, the place most of the open batches go to (Ruin in a to-Ruin session, so nothing changes there;
    `Whereabouts.LocationOf(name, anyPlace, fallback)`); the old planner starts everybody at Ruin.
  - **The jobs in the session** (stage 4a, 30 Oct 2026): the sessions take every hauling mission (`SessionBuilder.InSession`), so a job of another kind
    is on the **Stops** board (its pickup as the map names it, its drop-offs in the Drop-off Location column, `LocationRow.Job`, `PickupName`), in the
    **Completed Contracts** table, once completed, by its name with what the Mission Library says it pays (else the Wiki, else nothing), in the **Cargo runs** when a party member
    picks it up and delivers it (the delivery ends the run, like a hand-in), and in the session's earnings, the leaderboard and the Debts tab's payouts.
    What a part of a mission is worth is reward over its **parcels** (`Mission.Parcels`: the stops of a Red Wind contract, the drop-offs of a from-Ruin
    one, a job's pickups when it has one drop-off, else its drop-offs), which is what reward over stops was. A job's zones are named as the map has their
    places everywhere a table names a location (`TrackerState.JobZoneNames`). The planner plans the job itself (not its rows a second time).
  - **The SCU of a job, typed on the route** (Frank, 30 Oct 2026, evening): the Other hauling board and its SCU field are gone. The hauler types the SCU
    of a job's drop-off in the **SCU column of his Current Route**, on the line of the place the cargo goes to, exactly as for a load from Ruin
    (`PlanService.JobParts`, `HaulJobs.DropKey`, `/hauling/trips/scu`, `AppData.TypedScu`): the job has it, the boxes follow, the Stops board shows it,
    and the Mission Library gets it as the suggestion for the kind like a number the game said. A run in progress with a job's cargo aboard has the
    field on its Deliver lines too (`CargoRuns.LoadedJobDrops`). The admin may type anybody's; watchers see the SCU as text.
  - **A job's cargo aboard** (stage 4b, 30 Oct 2026): a run with a job's parcels goes on to the job's drop-offs still to make (`CargoRuns.LoadedDestinationsOf`:
    their places as the map has them, in any system), so the Route Planner (new) continues the run there and its Current Route ends with a Deliver line at
    each; a job with several drop-offs ends the run at the last of them, the ones before take a parcel off the ship (`WorkerTripBuilder`). On the new tab a
    run's lines start at the session's hub (a party in Stanton: where its cargo goes), on the old one at Ruin as always.
  - **Stops:** one per objective group (`pickup_<guid>_0.._2` is one stop with three commodities), ✓ when every objective of it is completed.
  - **Names:** from the objectives' texts ("Deliver 0/4 SCU of Distilled Spirits to Port Tressler", "Collect … From Shallow Fields Station at
    Crusader's L4 Lagrange point" is CRU-L4, "… on microTech", the spaceports: NB Int. is New Babbage, Teasa Lorville, Riker Memorial Area18, August
    Dunlow Orison; the admin's Map places aliases first). A pickup the game does not name (most "A to B" hauls) is named by its zone when another
    objective named a place in the same zone: exactly when a marker is right in the zone's middle (the place's own zone), else only "near ArcCorp"
    (the zone is the planet's: Baijini Point and Area18 share ArcCorp's).
  - **SCU, the first that has it:** what the game's "Deliver" popups add up to, or the SCU a hauler typed in for a drop-off on his Current Route (the
    SCU column; it lands in the mission's drop-off like the game's number); the Mission Library's for its kind (typed in there, else the suggestion: the
    last SCU a hauler typed in for one of them, or what the game said for one, every minute, `MissionLibrary.SuggestFromGame`); the Star Citizen
    Wiki's hauling orders for the contract (the middle of their ranges: "~500 (wiki 400–600)" for a Master contract of 4 × 100–150 SCU). It is split
    evenly over the pickups and over the drop-offs the game did not give the SCU of (~ on the board), like the planner's own contracts.
  - **The Star Citizen Wiki API** (`WikiMissions`, table `WikiMissions`): every objective marker of the log has the contract's own name and id
    (`contract [RedWind_Pyro_…_Multi4ToSingle], contractDefinitionId[26e80306-…]`, `Mission.Contract` and `Definition`), which are the API's
    `debug_name` and `uuid` of the mission, whatever names a string mod shows in the game. The library keeps every mission's contract id
    (`Missions.Definition`, from the live logs and the old ones; a server that read the old logs before reads them once more, `missions-read.txt` in the
    history folder). Every minute the server asks `api.star-citizen.wiki/api/missions/{uuid}` about up to 10 contracts of the library's missions it did
    not ask about yet, the newest first, until it asked about every one; after that only about a contract it has not seen (a new kind of mission taken).
    One the API does not know is asked again after a week; when the API cannot be reached, the next minute. It keeps their debug name, title, hauling
    orders (`min_scu`–`max_scu` of every order, one per stop) and reward. The list of every mission (`api/missions?limit=200&page=n`), read once a day,
    gives the Mission Library the faction, category, reward, XP and legality of every mission, but no hauling orders: a mission only the list was read
    for (`ListOnly`) has its own page asked for when a log has the contract.
  - **Value:** the Mission Library's aUEC for the kind.
  - The server gives the tracker what it knows besides the logs as a `JobBook` (the haulers' SCU, the library's kinds, the map's places), made new
    whenever the library changes, so the tracker never waits for the library.
- **The Payment tick on the live page** (Frank, 29 Oct 2026): on the Debts tab, the player who sent a money transfer ticks whether it pays off a debt
  (sent in the background to `/hauling/debts/payment`, `DebtsPayController`; `data.json` says where, `debtsPay`, and whose every transfer is,
  `transfersEdit`); only the sender can, and the admin, everybody else sees yes or nothing. The admin changes the rest of the debts right there too
  (Frank, 31 Oct 2026: his Debts page went; `/hauling/debts/balance`, `/payout`, `/party`, admin only): a **balance** typed over (+ they owe the member,
  − he owes them), what a **mission really paid** (empty: the reward of its contract; typed ones in gold; `payoutsEdit`), and the **Session party**
  ticks (who shares the session's reward; `sessionParty`). Each is sent as soon as it changes, and a player's `data.json` has neither list.
- **Parties never owe each other.** Who carried a mission and who shared its payout are one party (`SessionInputs.Groups`, worked out with the payouts),
  so two parties that hauled in the same session are two groups, and the Debts tab works out who owes whom inside each of them only (`DebtCalc.Pairs`). A
  session from before has no groups and stays as it was.
- **The database** (`App_Data/tracker.db`, SQLite, EF Core in `SCTools.Server/Data`) has everything the core keeps in its data file (`AppData`): the party and
  every member's ship, colour and location, the sessions, the transfers and payouts, what was typed on the Contracts and Debts tabs, and the tracker keys.
  The first time the server starts with an empty database, a data file in `App_Data/data.json` goes into it, when there is one; after that the database is the data.
  What the tracker changes is written at most every two seconds, and a copy of the database is made every day in `App_Data/backups` (the newest 30 are kept).
- **Running the server on your own computer**: put an `AppData` data file in `src/SCTools.Server/App_Data/` as `data.json` (or none: an empty database starts fresh) (the folder
  is ignored by Git, so it never ends up in the repository). In Visual Studio set SCTools.Server as the startup project and start it (the page
  opens at `http://localhost:8780`), or run `dotnet run --project src/SCTools.Server`. To import the data file again, stop the server and delete
  `App_Data/tracker.db`.
- **Changing the tables** (a new field in the data, a new table): change the classes in `Data/Rows` and `TrackerDb`, then commit with
  `[migration: AName]` in the commit message (for example `[migration: AddMemberNotes]`). GitHub makes the migration (`dotnet ef migrations add AName`)
  and commits it to the same branch; pull it before you go on. The server brings its database up to date when it starts. With the .NET SDK on your own
  computer: `dotnet tool install -g dotnet-ef`, then `dotnet ef migrations add AName --project src/SCTools.Server --output-dir Data/Migrations`. A
  test fails when the tables changed without a migration.

### Going live (the server)

The site runs on a Hetzner server (Ubuntu, Arm64, host name SC-Tools) as the service `sctools`; Cloudflare's tunnel brings sc-tools.fwjonsson.dev to it (no
open ports but SSH). GitHub puts every new version of main on it (the **Deploy** workflow). Once:

1. **Set up the server.** Log in (`ssh root@SERVER-IP`) and run
   `curl -fsSL https://raw.githubusercontent.com/fwjonsson/SC-Tools/main/deploy/setup.sh -o setup.sh && bash setup.sh`. It makes the users, the
   folders, the settings file, the service and the key GitHub logs in with, and prints three secrets.
2. **The secrets.** In GitHub: the repository, **Settings**, **Secrets and variables**, **Actions**, **New repository secret**: `DEPLOY_HOST`,
   `DEPLOY_KNOWN_HOSTS` and `DEPLOY_KEY`, each pasted exactly as printed. Then delete the key's copy on the server: `rm /root/github-deploy`.
3. **The admin's password.** `nano /etc/sctools/sctools.env`, type it after `SCTools__Admin__Password=` (at least 12 characters),
   Ctrl+O, Enter, Ctrl+X. The name is already `zzoega`.
4. **The data.** Close the program on the PC (so its data file is final), then in PowerShell on the PC:
   `scp "PATH\TO\HaulingPlanner-data.json" root@SERVER-IP:/var/lib/sctools/data.json`.
5. **The first version.** GitHub, **Actions**, **Deploy**, **Run workflow**. When it is green the app runs; `systemctl status sctools` and
   `journalctl -u sctools -n 50` on the server say how it goes (the import of the data file is in the log).
6. **Look at it before everybody does.** `ssh -L 8780:127.0.0.1:8780 root@SERVER-IP` in PowerShell, and open `http://localhost:8780` on the PC (while that
   window stays open). Log in as zzoega and check the Debts and History tabs.
7. **The tunnel.** In Cloudflare: Zero Trust, **Networks**, **Tunnels**, the tunnel, **Configure**. Add a connector for **Debian**, **arm64** and run the
   commands it shows on the server. Add the published application sc-tools.fwjonsson.dev with the service `http://127.0.0.1:8780`. Remove the
   connector on the PC (an administrator PowerShell: `cloudflared service uninstall`), so every visit goes to the server.
8. **The trackers.** On the **Players** page make a key for zzoega, zzatvo and bileh123, and put each in the SC Tools Tracker on his PC
   (Server `https://sc-tools.fwjonsson.dev`, the default).

After that every merge to main that changes the server is deployed by itself, once its Tests run came out green (a red run deploys nothing); a version
that does not start is replaced by the one before it. The
settings are in `/etc/sctools/sctools.env` (`sudo systemctl restart sctools` after a change), the data in `/var/lib/sctools`
(the database, the uploaded logs, the daily copies, the keys of the login cookies), the versions in `/opt/sctools/releases`.

**After the rename (28 Oct 2026).** The server has the new names; the settings file may still have the old lines from before, which nothing reads any more
once they are gone: `sed -i '/^HaulingTracker__/d' /etc/sctools/sctools.env && systemctl restart sctools` (the one-time `deploy/rename-server.sh` went on
30 Oct 2026).

## ScApiTool (Star Citizen Wiki API)

A separate small program, `ScApiTool.exe` (in the same Releases download, `ScApiTool.zip`). It asks the [Star Citizen Wiki API](https://api.star-citizen.wiki) for game data (locations, missions, commodities, ships, items, and the API's own list of addresses) and saves the raw answers as `.json` files in a `ScApiData` folder next to it, with a `summary.txt` and a zip of everything.
- Double-click it with no arguments for the standard set. `--all` removes the page limit. Give it an address like `ScApiTool.exe /api/missions` to download anything else the API has.
- **UEX** (uexcorp.space): create an app on the UEX site (My Apps) and copy its access token. Put the token alone on the first line of a file called `uex-token.txt` next to `ScApiTool.exe` (or set the environment variable `UEX_TOKEN`). The tool then also downloads the UEX places, terminals, commodities and prices, saved as `uex-....json`. `ScApiTool.exe --uex` does only UEX, `ScApiTool.exe uex:/terminals` any other UEX address. The token is a secret: it is sent only to the UEX site and is never written to the results, so the zip is safe to hand over. Never put the token in the repository.
- Pages are asked for by number, so a list is never downloaded twice (the API's own "next" link repeated a page and filled the mission list with the same 270 missions).
- The data comes from fan sites that are not affiliated with Cloud Imperium Games. Credit api.star-citizen.wiki and uexcorp.space if you publish anything made from it.

**Gateways.** The Nyx and Stanton Gateway have exactly the same position in the log, so the core tells them apart by the order of their location ids once both have been seen. It now also uses what the game says itself: when you plan a quantum route a few minutes after picking something up at a gateway, the log says *Projected Start Location is Stanton Gateway*, and that names the gateway you were at (and, when only one of the two was seen, the other one is the other gateway). It only works when a quantum route is planned from the gateway; otherwise the gateway stays *Unknown* until both have been seen, and you can still type its name in the Stops board.
