# SC Tools: the roadmap

What is planned, what was decided and why, tool by tool. The to-do list itself is GitHub Issues (the backlog, see CLAUDE.md): an idea is an issue first,
and gets its plan here once Frank decides to build it. Decided with Frank on 28 Oct 2026 and kept up to date since: the hauling tracker's server
became a site with several Star Citizen tools (one server, one tracker on every PC, the same logins). The same text is in the claude.ai project
(`claude/sc-tools-roadmap.md`); change both. How the code works is README.md, how we work is CLAUDE.md.

## The site

- **Names** (renamed 28 Oct, PR #225): repo `fwjonsson/SC-Tools`, site **sc-tools.fwjonsson.dev**, server host SC-Tools with the service `sctools`
  (`/opt/sctools`, `/var/lib/sctools`, `/etc/sctools`). `haulingtracker.` and `sct.fwjonsson.dev` send every page to the new address and still take
  uploads from trackers that have not updated; their Cloudflare routes go once every tracker has updated. Then remove the old setting lines:
  `sed -i '/^HaulingTracker__/d' /etc/sctools/sctools.env && systemctl restart sctools`.
- **Landing page** (PR #229): orbit rings and centred tool cards; the tracker download in the top bar and the footer; the disclaimer in the footer
  (data from the Star Citizen Wiki, our own logs, and what we fill in ourselves). Hamburger menu on mobile.
- **Top bar**: the same on every page, the tools' own pages too: SC Tools (the only way back to the cards), the tools, and on the right the user, his
  Party button (Frank, 29 Oct: the Party is not a card on the landing page but a button beside the name with a panel under it), My page, Admin, Log
  out. The admin pages are a second row under it.
- **Tools page** (`/admin/tools`): Frank ticks which tools everybody sees; an unticked tool is admin-only (its card and its page). Default: Hauling
  Planner shown, the others only for Frank.
- **Settings page** (`/admin/settings`; the Scrip page `/admin/scrip`, PR #230, until 31 Oct 2026): what one MG scrip and one Council scrip are worth
  in aUEC, the price Frank thinks they sell for now (every tool counts scrip with these), and his lists of factions and categories.
- **SC Tools Tracker** (`SCToolsTracker.exe`, was HaulingTrackerSender): uploads the live log, and the old backup logs the server does not have yet
  (all of them the first time, then the new ones; PR #227; streamed line by line since PR #231). Its menu shows its version, the old logs' progress and
  "Check for an update now". Old trackers update and rename themselves; the `.old.exe` an update leaves is removed when the new one starts (PR #232).
- **Hauling Planner**: the hauling tool (was Hauling Tracker), at `/hauling` with a path per tab (`/hauling/planner`...); the Windows program (gone 30 Oct 2026: Frank only uses the site and the tracker) was
  HaulingPlanner.exe.

- **Who sees what** (Frank, 30 Oct 2026): every shown tool is open to everybody, logged in or not; no player data for a visitor except the public leaderboards (Hauling all-time, Gauntlet Leaderboard); a player sees his own and his party's; the Gauntlet Stats page is personal; the admin sees everybody's and uses the top bar's View pick to see a player's view.

## The tools

All the tools track at the same time, all the time. The tracking runs on the server from what the trackers upload, not in the browser page.

1. **Hauling Planner**: live at `/hauling`, with automatic parties and automatic sessions per party (#238, #239). Its contract rewards live in the
   Mission Library. Since #266 it is the one hauling app: the hauling of every system is in it (an Other hauling board on the Session tab until 30 Oct
   2026, rows of the Stops board since, with the SCU typed on the Current Route; the address `/hauling/jobs` is gone). The Contracts table was a tab of its own (#269) until 30 Oct 2026, when Frank put it back on the Session
   tab under the stops (over the whole width) and gave the two leaderboards a Leaderboard tab of their own.
2. **Session tool** (`/session`, PR #233): playtime kept for good (`LogFiles`: old logs and live games), totals today / this week / this month / all
   time, games, crashes (no SystemQuit line), a live clock, and what the completed missions earned at the Mission Library's rewards (worked out again
   every visit, so a reward typed in later counts for the past; since #264 the Star Citizen Wiki's reward where none is typed in).
3. **Mission Library** (`/missions`): every mission the players take, one row per mission name (the game's ContractId is one per offer, so it cannot
   group them); the contracts the Hauling Planner reads are one row per contract line (2 To Ruin ... Direct From Ruin), named as the game names their
   missions once one is seen (#268; since 29 Oct they are kinds by name like every mission, and the planner pays each contract what its kind says,
   else the Wiki's reward, else the old table by stops: an Experienced contract is not a Senior one). The Session tool, the Gauntlet Tracker (the "Combat Gauntlet - Scenario #n" rows) and the Hauling Planner (the
   aUEC of the Hauling rows, the SCU of the others) all count with it. The old Wiki lookup by ContractId was dropped; since #264 a kind is linked to
   the Wiki API's mission by the log's contract id (see 11).
   - **A contract screenshot** (#437, Frank 31 Oct 2026, the admin's only): a mission row's 📷 (the library, the Companion's contract tables, the Hauling
     Planner's Session tables) opens a little box beside it; he pastes a screenshot of the contract's card and the page reads the reward, the faction,
     the MG or Council scrip and the XP with a free text reader (Tesseract in his browser; Frank did not want an AI that costs tokens), he checks it and
     saves. The reward is read with digits only, after the currency sign is cut off, and checked (commas, at least 1,000, the Wiki's range). Only for that
     mission: never one guessed by its name or added (Frank: a similar name could be another mission, a new one would not be joined to the logs').
   - **Drop-downs and the admin's lists** (Frank, 31 Oct 2026): Faction and Category are picked from lists he keeps on his Settings page (add, take
     out; a box above the library for a day), Legality is Legal or Illegal; Category, XP and Legality can be filled in, and win over the Wiki's. He edits
     a mission wherever it is shown, as in the library: the Companion's contract tables and the Hauling Planner's Active and Completed Contracts too.
   - **Read-only for everybody, editable for Frank** (Frank, 29 Oct; #267, #268): `/missions` is a library to look things up in (everybody when the
     tool is shown, a visitor too; else only Frank): no fields, a search, one giver, with/without rewards, only the kinds the viewer played, sorted by
     a clicked column (names A to Z, numbers biggest first, the other way on the second click; the choices in the address). Not ordered by last
     played: what is played is bound to the account looking (Failed / Completed and Last played, his own; a green row for a mission he plays now; a
     visitor sees no plays). **Edit Mission Info** (`/admin/missions`, in the admin row) is the same list with a field in every cell: name, giver,
     SCU, aUEC, MG and Council scrip, autosaved. Only Frank edits the library; a hauler's SCU goes in on the Hauling Planner's board and becomes the
     library's suggestion. Nothing under the names.
   - **The Wiki's columns** (Frank, 29 Oct, later): the server reads the Star Citizen Wiki API's list of every mission once a day
     (`api/missions`, 1,800 missions), so every kind shows the Wiki's **Faction** (the Giver column became Faction; one typed in wins), **Category**
     (the Wiki's type), **Reward** (was aUEC; typed in wins), **XP** (the reputation it gives) and **Legality**. A kind whose contract id no log showed
     is linked to the Wiki's mission by its name.
4. **Gauntlet Tracker** (`/gauntlet`, PRs #224, #226): everybody sees only his own runs; a Leaderboard of everybody (total earned, aUEC/h over timed
   runs, completion rate, fastest run, favourite and best scenario, last played). Rewards from the Mission Library (typed on Edit Mission Info; the
   admin's `/admin/gauntlet` page went on 29 Oct, with his Contracts page); each run earns its player his part (the contracts tracker, #383). Top 10 fastest: picking a scenario shows it at once (no Show button). Since 29 Oct (Frank): the Session, Stats and History tabs are
   the viewer's in-game party's runs together (alone: his own); the Earnings ranking tab is **History**, with the lifetime totals moved there from
   Session and a Scrip/h column in the ranking. Since 30 Oct 2026 (Frank): Stats has the Earnings (the lifetime totals) and the Top 10 side by side,
   the Earnings per scenario (the ranking) and the times under them; History has the earlier grind sessions; Session has the game session and the
   grind session side by side; the Leaderboard is titled "All-time leaderboard"; every wide table takes the whole band. Since 31 Oct 2026 (Frank): the
   Earnings/Scenario and the Scenario Times sit under the Earnings, as wide as it, and the Top 10 Fastest has the right half to itself, level with the
   Earnings; History's table is "Saved Sessions" with a # column; the Game Session's and the Grind Session's row titles are as wide as each other, with
   no status line under the Grind Session's title.
5. **Party tool** (`/party`, PR #241): who is playing in which in-game party with the ship each one flies, everybody else with when they were last
   seen, and a page per player. `PartyService` keeps the site's players in one place for every tool, and asks the party tracker (14) about the
   parties. Since 29 Oct not a card on the landing page: a
   **Party** button beside the name in the top bar, with a panel (fetched when it opens) of the parties as tables like the Hauling Planner's old
   Party table (member, ship, drive, the rows in the members' colours; Frank, 29 Oct), which the Hauling Planner's page lost that day, with an
   Active tick for the admin and the party's leader (29 Oct, after the planner kept planning trips for a member who had stopped playing while the
   logs still counted him).
6. **Map tool** (`/map`, PR #242; all three systems in PR #243): Pyro, Stanton and Nyx from above, one tab per system (it opens on the system where
   most players are). The places (stars, planets, moons, cities, gateways, stations: 26 + 52 + 19) come from the Star Citizen Wiki API positions in
   Frank's ScApiTool download of 20 Oct 2026 (`StarMap` in the core; Pyro's match the Trip Planner's distances). Each system is turned as the game's
   starmap shows it: Pyro a quarter turn clockwise, Stanton and Nyx not, although the three are in one frame (Frank's screenshots, 31 Oct 2026: the
   site had drawn Stanton and Nyx turned like Pyro). Everybody playing is placed where
   their quantum jumps put them, through gateways into the next system too, moving along the route at their drive's speed (crafted bonus included)
   with a speed meter. The jump start is a guess (10 s after picking the destination); the arrival line corrects it. Objectives at stations, in
   space, at the place their text names, else listed by text and system (#258). A jump with no destination picked: to the mission's objective, to
   where the pilot picked (crew), else "Off the grid" with the last ping (#258, #260).
   - **One map engine** (Frank's TODO of 29 Oct: Map 3 and the Trip planner tab; branch `claude/map-engine`): the Map tool's map is the one engine
     (`systemmap.js`, since 29 Oct a file of the site both pages load), and the Hauling Planner's Trip Planner tab draws Pyro
     with it: the players as arrows moving along their routes, the objectives as diamonds with a count, the speed meter under every worker's name,
     with the planner's routes (bold, dotted, arrows, waves, legend) on top, from `/hauling/map.json` (Pyro alone). The planner's and the map's
     "done" agree: a pickup the planner credits is off the map at once, for everybody with the mission. Ruin is a station like any other on it
     (Frank, 29 Oct: the planner is to work with every kind of hauling and lose its Ruin bias). The Windows program went on 30 Oct 2026. The map's
     place for a worker never moves the start of a run he is in the middle of (30 Oct 2026: the rows of his waste trip vanished while he flew it).
     The plan is sticky since 30 Oct 2026: the stops of a worker's planned trip stay his until they are picked up, gone or he picks up elsewhere, and
     the Next Stops button of the admin's Session page went (Frank: not used; better logic instead of a button).
   - **Over the map** (Frank, 31 Oct 2026; #408, #409, #410, on top of #411): a card over a station with what it has (UEX's services and shops,
     `StationServices`, `/map/services.json`); left clicks measure a trip stop by stop, every leg a jump of its own, with its flight time with the viewer's
     own ship, and a right click clears it (Frank, 31 Oct 2026, after trying the two-click version: "as long as you left click it keeps adding stops"; his
     alone, nothing leaves the browser); a skull with the name in gray where somebody died, for a quarter of an hour. All three in the map engine and the
     feed, so the Map tool, the Companion and the Hauling Planner's map have them; a player sees his party's skulls alone, like the players.
   - **One look for every map** (Frank, 31 Oct 2026): the star systems are the engine's buttons in the map's top left corner on every map (`systems()`;
     a row of tabs under the bar, then a row over the map, before), the Gauntlet Tracker's map is the Companion's size (820 px, not the admin's Map's
     1060), and the Route Planner's map box lost its background.
7. **Map places** (`/admin/places`, PR #244, Frank 28 Oct): the admin says what a quantum destination or start name the map does not know is (a
   place, or not a place to show; `Text*` for every one that starts so), renames places, and adds places the game data does not have (at a place, or
   X/Y in Gm). Saved in the database and used by the map at once. The maps label every place with its full name since 30 Oct 2026 (Frank: "Dudley &
   Daughters", "Rod's Fuel 'N Supplies", like the Hauling Planner again); the short name on Map places is the full name unless he shortens it.
8. **Fleet** (My page, PR #240): ships with quantum drive and crafted quality (1000 = +20 % drive speed, 500 = as bought, 0 = −20 %, linear); active
   ship picked or Automatic (the ship class in the latest quantum travel line); the Hauling Planner flies the active ship, and its Party table shows
   Ship instead of Active.
9. **Debts as a tool of its own** (Frank, 28 Oct, later): the Debts tab of the Hauling Planner becomes its own tool with a card on the landing page,
   so the other tools can put debts in it too.
10. **Goods for the Getting tracker** (Frank, 28 Oct, later): aUEC per hour, the common locations, a countdown of each location's cooldown until the
    mission is offered there again, and connected to the debts (one player sells all the gold for the party).
    - **Spec (Frank, 28 Oct 15:50):** a simple version of the Hauling Planner. Most important: count when the group accepts the missions, picks up the
      gold and sells it. The gold's price is not part of the mission, so a completed mission counts as the gold got away with. If possible, a map of
      the RAB/RMB asteroid bases the gold is taken from.
    - **The mission** (Star Citizen Wiki): Bit Zeros, Pyro, costs 33,000 aUEC to accept and pays nothing itself; a loaded Freelancer locked in an
      asteroid base's hangar (debug name `UnafilliatedCFP_Pyro_RegionD_M_2steroidBase_Criminals_ShipHeist`).
    - **The bases**: the ScApiTool download of 20 Oct has all of them with positions in Pyro: 29 RAB (`Asteroid_ValidQT`, quantum targets) and 86
      RMB (`Asteroid`).
    - **Waiting (28 Oct)**: Frank will run the mission and sell the gold, then run copy-sc-logs; the lines for the base, the pickup and the
      commodity-terminal sale are not known yet. The Mission Library has a "Goods for the Getting" row (no reward: the mission pays nothing itself).
11. **Hauling of every system** (Frank, 29 Oct: "make the Hauling Planner work with all missions"; he chose every system).
    - **SCU order (Frank)**: the game's "Deliver 0/N SCU" popup for the mission; else the hauler's number for that one mission (the board's field;
      also saved as the library's suggestion for the kind); else the Mission Library (Frank's override typed in there, else the latest suggestion
      from a hauler or the game); else the Star Citizen Wiki API's hauling orders (the middle of their ranges). Split evenly over the stops, like the
      planner's own contracts.
    - **Stage 1** (#262): `HaulJobs` reads every mission with drop-offs that is not one of the planner's own contracts; stops named from the
      objective texts (spaceports, Lagrange points, "on/near X") or by the zone another objective named (exactly, or "near ArcCorp" when the zone is
      the planet's); titles from Contract Accepted/Shared; "Deliver 0/4 SCU of Distilled Spirits" (two-word cargo) is read now.
    - **Stage 2** (#263): the Other hauling board on the Session tab; a hauler's SCU; suggestions in the library (grey); the game's SCU becomes the
      suggestion every minute. The tracker gets it all as a `JobBook` (lock-free). One planner, not two apps (#266): the SCU field is in the board.
    - **Stage 1a of the design** (30 Oct; `docs/planner-any-hauling.md`): the Pyro station-to-station contracts (`_StationToStation_…`, "Master |
      Stellar Large Haul | to Stanton Gateway") are the planner's own: in the sessions, on the Stops board with their station as the drop-off (a row
      of their own), in the Contracts tab and the runs; the planner names them in its note until stage 1b (the planner without a hub) plans them.
    - **Stage 1b of the design** (30 Oct): the planner without a hub for Pyro: a batch is cargo at a station for a place, a trip starts where the
      worker is (or where his trip before it ended) and ends at the last place it delivers at, the contracts to another station are planned in the
      same trips as the to-Ruin ones (Frank's answer 1), a run ends where it is handed in, the map draws every route from its start to its end. The
      to-Ruin plans are unchanged scenario for scenario.
    - **Two planners side by side, then one** (Frank, 30 Oct): the new planner (`RoutePlanner`) was tried out for a day on a tab of its own beside the old
      one (`TripPlanner`), with kept stops of its own and a switch for each tab on the Tools page; in the evening Frank made it the Route Planner and the
      old planner, the second tab, the switches and `FreeLockedTrips` went.
    - **Frank's rules of 30 Oct, evening**: the first stop
      of a trip is fixed, its batch count and the other kept stops are soft (a kept stop gives way for a landing saved or a fuller ship, a third of a
      landing as the penalty); a landing counts 20 minutes; the flying a tenth.
    - **The Other hauling board goes** (30 Oct, evening): the jobs are rows of the Stops board, and the SCU of a job's drop-off is typed in the SCU column
      of the Current Route like a load from Ruin (the job has it, the boxes follow, the library gets the suggestion). Left: several pickups to several
      drop-offs.
    - **Stage 2 of the design, interstellar legs** (30 Oct): `StarRoutes` gives the way between any two places of the map, through the gateways between
      systems with a 3.4 minute crossing; the planner without a hub plans the Other hauling board's jobs whose stops the map has places for (a parcel per
      pickup or per drop-off; Frank's Covalex mission from Stanton to Pyro) with the Pyro contracts, starts a worker where the map has him in any system,
      and its map has the Map tool's row of systems with the legs through a gateway drawn to and from the gateway. Not yet: the runs of a job (its
      cargo picked up is noted, not planned), several pickups to several drop-offs, and planning within Stanton without a Pyro session (stage 3).
    - **Stage 3 of the design, the other systems** (30 Oct): any hauling mission starts a session (a courier too, Frank's answer 7), so a party in
      Stanton gets its plan on the new tab; a stop is placed by its marker's position in the system's frame or by the title's ends ("Baijini Point >
      Ruin Station", Frank's answer 5); a worker nobody can place starts at the session's hub (where most open batches go). Not yet: the runs of a job,
      the jobs on the Stops, Finished stops and Contracts boards, their earnings (stage 4).
    - **Stage 4a of the design, the jobs in the session** (30 Oct): the sessions take every hauling mission, so a job is on the Stops board with its
      places, in the Contracts table by its name at the library's reward, in the Cargo runs (its delivery ends the run) and in the earnings, the
      leaderboard and the payouts, at reward over its parcels (`Mission.Parcels`).
    - **Stage 4b, a job's cargo aboard** (30 Oct): a run with a job's parcels goes on to the job's drop-offs still to make, in any system, and the new
      planner continues it there; a job with several drop-offs ends the run at the last of them. Left: the SCU field on the Stops board and the Other
      hauling board going, several pickups to several drop-offs, and the new planner replacing the old one when Frank says so.
    - **Wiki API** (#264; #265 for every mission of the library): the log's `contract [...]` and `contractDefinitionId[...]` are the API's
      `debug_name` and `uuid` (Frank confirmed with `701271ba-…` = `Iasi_HaulCargo_AtoB_Small_PartySupplies`), so a string mod's names do not matter.
      The contract ids are kept on the Missions rows (the old logs re-read once); the server asks `api.star-citizen.wiki/api/missions/{uuid}` about
      up to 10 contracts a minute until it has every one of the library, then only new ones; it keeps hauling orders and reward; the Wiki's reward is
      grey in the library and counts until one is typed in. A `reward_max` of 0 there means only the one end is given (#268).
    - **Design** (30 Oct; `docs/planner-any-hauling.md`): how the Route Planner plans every hauling mission of every system, interstellar ones
      included, and loses its Ruin bias: every mission is parcels (cargo at one place for another place), a trip starts where the worker is and ends
      at his last drop-off, the distances come from the map's places and through the gateways, the Other hauling board folds into the Stops board.
      What the logs give today (zzoega's 342 logs: the Red Wind to-Ruin and from-Ruin contracts, Pyro station-to-station ones, Stanton HaulCargo,
      interstellar hauls, the RSI Disc. Month hauls and couriers), the gaps, and Frank's questions are in it.
    - **Next**: the design's stages, one pull request each: stage 1 Pyro places (1a the model and the boards, 1b the planner without a hub), stage
      2 the other systems, stage 3 the interstellar legs, stage 4 earnings and debts (the roadmap's stage 3 routes and stage 4 earnings, split up).
12. **Companion** (`/companion`, #380, Frank 30 Oct 2026): a player's own page to keep open while playing, made as a copy of the Map tool. The map
    with him and his party (personal; the admin's own view, View as: Admin, has everybody on the map and the rest his own player's, and View as shows a
    player's, his own player's too; Frank, 30 Oct 2026), the Players list, the
    objectives, the party's **Active Contracts** and under them **Completed Contracts** (the same columns, with how each ended, Completed, Failed or
    Abandoned, and when, in place of Open), both with who in the party has each; and a table with the Session tool's figures except Games for the
    **current game session** (from when the game started until an hour after it was closed, crashed or quit and not started again; a game started
    again within that hour is the same session). Frank's answers of 30 Oct 2026: Completed Contracts go back to the start of the game session; they say
    how each ended; the admin's fields are Edit Mission Info's (name, faction, SCU, reward, MG and Council scrip), right in the contracts' rows; when
    the session is over, the table keeps the last one, marked ended, until the next game. While the map shows the system he is in, it follows him when
    he jumps to another system; another system he picked stays. In the top bar and on the landing page it is left of the Hauling Planner. The Map
    tool lost its Objectives list and its Active Contracts to it, and its map got bigger (1,060 px, was 820).
    - **Earnings are your part** (Frank, 30 Oct 2026, after the Companion showed 18,501,500 aUEC for a session he hauled with zzatvo: every member
      counted every shared contract in full): the aUEC is split evenly among the players who completed the same mission, a Hauling Planner contract
      pays its payout split (the Debts tab's), and the scrip goes only to the one who took the contract; the Session tool counts the same way. With
      combined accounts the Companion adds them up over the viewer's own game session, per hour of his own time played (Frank's answer). Since #383
      it asks the contracts tracker (13).
13. **Contracts tracker** (#383, Frank 30 Oct 2026): contract money was worked out in four places with different rules (the planner's payouts by the
    party at payout time, the Session tool and the Companion by who completed it, the Gauntlet Tracker in full for every member). Now one place knows
    every contract (who took it, who accepted the share, how and when it ended for each, what it pays) and every tool gets its money from it.
    **The split rule (decided):** the aUEC is split evenly among the players who have that same contract when it pays out (the one who took it plus
    everyone who accepted the share and still had it); being in the party without accepting the share gets nothing; two copies of the same contract
    accepted separately are two contracts, each paying its own player in full. MG and Council scrip go only to the one who took it; when he abandoned
    it, to the one who accepted the share first of the ones who still had it when it paid out (the game's rule, Frank 30 Oct 2026). Combined accounts
    are added up where a tool shows a player's totals. The admin's payout overrides on the Debts tab still win, and a payout already kept with a
    session keeps its members (debts are real money between friends: a past session's numbers do not change).
    - What changed with it: the Gauntlet Tracker counts a scenario shared in the party once (each member his share of the aUEC, the scrip to who
      accepted it), and the Session tool and the Companion count the shared scenario for the one it was shared with too; the planner pays a player
      whose log still has the contract when it pays out, also when he was marked out of the party, and when nobody is known to have it, the one who
      took it (before: the active party).
    - **Who took it, and who gets the scrip** (#385, from the logs of all three accounts, Aug-Oct 2026): the "Contract Shared" line, only in the log
      of the one who gets the share, has no mission id (all zeros) and often the contract's template for a name; his "Contract Accepted" with the real
      mission follows within half a minute. That accept is marked as a share; the taker is the one whose accept was not (the first accept only when the
      logs cannot tell: two PCs' clocks were up to 9 seconds apart). When the taker abandons it (Frank's gauntlet routine: take it on zzatvo, share it
      to zzoega, abandon it on zzatvo to escape the cooldown), the scrip goes to the share accepted first of the ones who still had it, judged by the
      time from each one's accept to the completion on his own clock. The game's "You've Earned: N Rewards" line after a completion agreed in 386 of
      388 shared gauntlet runs. The game says "Contract Accepted" again for every contract on every login, so the library keeps the earliest accept.
      The offer line (ContractId) is in few logs (one mission in ten).
    - **The game's own clues** (Frank, 31 Oct 2026: right also with players he does not know): the trackers send the "You've Earned" line (a new
      line kind: its first line has no "Mission" in it), and the library keeps every player's id in the game and what his log said about the others
      ("PlayerJoined", "PlayerLeft"). A reward line with the scrip's amount says who got the scrip; a holder whose own log is silent left when another
      log saw him leave before the payout; the shares are in the order one log saw them join. The old logs already on the server have the joins and
      leaves (read once more); the reward lines come with every old log, which every tracker sends once more (Frank: "so the statistics is correct").
    - Still to look at: the Gauntlet Tracker times a shared run for everybody who accepted it (the one who got the share has an accept line too), so
      the timings and the Top 10 count a shared run once per member; and which member a party's Top 10 names for it is not sure to be the taker.
14. **Party tracker** (#389, Frank 30-31 Oct 2026): "the party" was worked out in four places (the in-game party by AutoParty, in LiveTracker and
    again on every page; the Hauling Planner's party and its Active ticks; who hauled together per session; who a player may see). Now one place in
    the core (`SCTools.Parties`: `PartyTracker`, its answer `PartyBook`) reads the party lines of everybody's log once per refresh
    (`LiveTracker.Parties`), and every tool asks it: the Party tool and its panel, the Gauntlet Tracker, the Companion, who may see whom (`Audience`,
    which adds the session's hauling groups), the planner's Active ticks (`AutoParty.Apply`), the payouts and the contracts. **Decided:**
    - It keeps a full history (`PartyRecord`): who joined and left, when, who led and when that changed, disbanded or not. A gap of less than fifteen
      minutes (a relog, a new log file) does not end a party or make a member leave. **No page for it** (Frank, 31 Oct 2026: "this tracker should be
      working in the background serving information to the other tools").
    - **The Active tick stays and means one thing: the planner gives this member routes.** It never decides money (Frank, 30 Oct 2026): the payouts
      and the contracts ask the party tracker who was in the in-game party, so a member unticked because he does not haul still gets his share of a
      contract he had, and the debts (what each carried minus what each received) make him owe the haulers. Before the logs took over
      (`AutoPartySince`), the tick is what there is, as before.
    - **What the logs say about the party** (the three accounts' logs of 4 Aug to 30 Oct 2026, 700 log files): "X has joined / left the party.", "X is
      now party leader.", "Initiated by party leader X." (a launch), "The party has been disbanded.", "X connected." / "X disconnected." (a party
      member's game came in or went out; a disconnect is followed by "has left" about six minutes later unless he comes back), and "You have created
      / joined party". The last one names nobody: a party made from an invite is only seen as one player creating and one joining within seconds
      (343 of 384 such pairs under ten seconds apart; the PCs' clocks differ), so two of those within fifteen seconds are one party now (before:
      two players alone). Its leader is the one who made it, told by the invite line ("Party Invite Received: Accept Invitation?") the one who
      accepts has just before his "party"; the maker has none (175 of 176 makers, 264 of 270 joiners). Frank, 1 Nov 2026, after the site said
      "zzoega's party" for zzatvo's: of two who come in within seconds, the PCs' clocks can put the joiner first. "You have left the party." and "You have been kicked from the party." have their words on a line the trackers do not send
      (a kick in these logs is always the game moving a player to the party whose invite he accepted, and the others' logs say he left), and
      "Join Match ... party[id]" (the game's own id of the party) is rare (five times in zzatvo's 250 games): not used.
    - **Players without the SC Tools Tracker** (Bileh123): the others' logs say he is in the party, when he joined, left and leads, when his game went
      out and came back, the missions shared with him (by his player id), and the money sent to him. Not known: his ship, where he is, his quantum
      jumps, his own missions, his deaths and his money. Their logs also have "Player Bileh123[783855402306] entered instance" (his name with his player
      id) and "Quantum Travel Calibration Started By Bileh123" (he flies the ship they sit in), on lines the trackers do not send: they could name his
      shared missions surely, if Frank wants a new line kind. Shown (Frank, 31 Oct 2026): a "no tracker" tag after his name on the Party panel and
      pages, and his last seen from the others' logs.
    - Checked by replaying the three accounts' logs through the old and the new code at 1,488 moments: the same active times for every player with a
      tracker; the differences are parties the new code joins (invites) and tracker-less members a "connected" line shows in the party.
15. **Location tracker** (#388, Frank 31 Oct 2026): where a player is was worked out in two places, the map (`MapService`: quantum jumps, areas,
    inventories, a crew member through his pilot's log, mission objectives, Map places) and the Hauling Planner (`Whereabouts`: pickups, deliveries,
    hand-ins, a station set by hand), with a bridge copying between them every six seconds both ways (`PlannerPositions`). Now one place in the core
    (`SCTools.Locations`: `LocationTracker`, its answer `LocationBook`) takes in every clue once per refresh (`LiveTracker.Locations`) and answers
    where every player is, since when, and whether he is on his way; the map, the Route Planner, the Companion, the planner's Current location, My page
    and the Party pages ask it, and the bridge is gone. **Decided:**
    - The same answer as before for every page (recorded first, `LocationPagesAsBeforeTests`), except two changes Frank asked for (31 Oct 2026): a
      player at a station is **Landed** (in the station's hangar) or **Taking off** (out of the hangar, still in the station's own area), and a jump
      starts when he leaves the station's area, not the hangar. The landing area of the lines is the station's hangar (one number per station, the
      same in every log; moving the ship inside it writes nothing). A station's own area is learned (`StationAreas`: from the three accounts' logs, and the logs on
      the server); where the hangars let out straight into space (Levski, Orbituary) taking off is leaving. The Nyx update of late 2026 may change the areas
      (Frank). The planner's own rules
      stay the planner's: a run in progress is planned from its last pickup and waste from Ruin, whatever the tracker says, and a station set by hand
      holds until the logs show him moving. The planner's Active tick decides routes only.
    - A crew member in a pilot's ship still goes by the ship's id (the arrival of the same ship in another log within 10 seconds); the logs of his
      in-game party (the party tracker) are asked first.
    - The clock rule: the book changes only with a new clue (or half a minute on, when a game that went quiet ends), the plan is made anew only when
      what the planner takes from it changes, and the map's moving markers count from the jump's UTC start in map.json.
    - The Stanton station codes of the inventory lines (`RR_HUR_L1`, `RR_CRU_LEO`) are read since #394.
    - **Open space** (Frank, 31 Oct 2026): a jump picked inside any area (a station, a mission's encounter site, near a planet) starts when he leaves into
      the system's open space (`OpenSpace`, learned like the station areas), and one picked out in open space 47 seconds after the pick (was 10: rather
      late than early). A hop that never goes into open space (between a planet's orbital markers, to its moon) is not drawn: the arrival puts him there.
      A quantum drop-out mid-route is not in the pilot's log at all, and a resume only now and then in a passenger's (a drive-state error naming the
      pilot); "Quantum Travel Calibration Started By" is only in the logs of party members in other ships: not used.
    - Not yet, ideas for #388: new line kinds (a tracker update, the players told first): the jurisdiction, armistice and monitored-space
      notifications, the ATC contact, the elevators and shuttles, "X has started to QT to your current location", the party markers streamed in.

16. **Ship tracker** (Frank 31 Oct 2026): the location tracker lost riders (zzatvo and zzoii ride in zzoega's Perseus as gunners nearly every time):
    their logs have the ship's arrival line only three jumps in four, and a jump picked out in space has no area line until it arrives. The logs of
    Aug-Oct 2026 (the three accounts, ~700 files) showed the ships' voice channels name who boards whose ship ("You have joined channel 'RSI Perseus :
    zzoega'.", 1,567 times). One place in the core (`SCTools.Boarding`: `ShipTracker`, its answer `ShipBook`) says who is in whose ship and who flies it,
    once per refresh. **Decided:**
    - A rider goes with the ship (its id) and whoever flies it, not with its owner: zzoega leaves the Perseus to steal a Freelancer and zzatvo flies it on.
    - The ship's drive is its owner's (his fleet, his crafted speed and fuel): the map's timing and a beacon's fuel ranging use it for the pilot and the
      riders alike, because that is the ship's.
    - The pages say whose ship it is ("zzoega's RSI Perseus") on the map's cards, the Party panel and a player's page (riding: who flies it).
    - In the Hauling Planner a rider is crew of the pilot's ship: one ship, one set of trips, no trips of his own (the payouts are the party's as before).
    - A full ships tracker (every ship's state, hangars, insurance) is not worth it: the logs say little about ships beyond the jumps and the channels.
    - The trackers send the channel lines from their next update (a new line kind; the text is on the notification's first line, which they did not
      send before).

Later, maybe: a party/crew board (who is on which shard, in which jurisdiction) and crash/server statistics.

Not wanted:
- A blueprint tracker. Frank's string mod already shows blueprints in the game.
- Mining scan signatures. They are not in the log (SC Toolbox's Mining Signals reads them off the screen), and Frank does not want screen capture.

## What the Game.log has (checked 28 Oct, Frank's logs of 15–26 Oct)

- **Contracts of every kind**: "Contract Accepted / Complete / Failed: <title>" with the rep, and the ContractId (one per offer, not per kind of
  mission). Every objective marker names the contract's own name and definition id (`contract [...]`, `contractDefinitionId[...]`): the Star Citizen
  Wiki API's `debug_name` and `uuid`.
- **No contract rewards and no wallet balance.** A few "Awarded 500 aUEC" notifications have no mission attached; what pays them is not known yet.
  Rewards come from the Mission Library (typed in, else the Wiki API's), with the wallet at the start and end of a session as an optional check.
- **The in-game party**: joins, leaves, the leader, disbanded, a member's game going out and coming in (used by the party tracker, 14).
- **Quantum travel**: the destination picked (an id: `pyro3`, `LOC_RR_S1_L2`, `rs_ext_nyx-pyro_jp1`, `levski_all-001`, `PartyMemberMarker_…`,
  `AsteroidCluster_Pyro_…`), the start in plain words ("Projected Start Location is Aberdeen", "… is Pyro System") and the arrival. An arrival with
  no destination picked: a jump at a marker looked at, or as crew (the same ship id in the pilot's log). A mission objective's quantum beacon:
  `PairWithMissionNavpoint … MISSION_QT_Quantum_Beacon_…`. When the jump starts and ends: the area lines (`<Update Inventory Location> … Landing
  [a] -> [b]. Location [c] -> [d]`, numbers, not names): off the pad and out of the station's area is the start, the next area the end; the same
  route takes the same time to about 2 seconds that way (sent and read since 31 Oct 2026, the Map tool). There is no line for the spool itself: the
  "Quantum Travel Calibration Started By X" notification is only for somebody else in the party. No "Exited … Jurisdiction" either, only "Entered".
- **Who boards whose ship** (checked 31 Oct 2026, Aug-Oct logs): "You have joined channel 'RSI Perseus : zzoega'." in the log of the one who boards
  (the ship's name and owner), "You have left the channel '…'" about half the time; the owner's log names nobody who boarded, only "Member Left …
  zzatvo has left the channel '…'". No seat lines (gunner or passenger); the pilot leaving his seat writes "releasing control token for
  'RSI_Perseus_<id>'", sitting down writes nothing. Used by the ship tracker (16).
- **Deaths and ships lost**: "[ActorState] Dead … ejected from zone RSI_Perseus … destroyed vehicle"; "Local Emergency Services Are En Route" when downed.
- **Clean exit or crash**: a clean exit writes SystemQuit; a log that just stops is a crash. Disconnects show as "Hub disconnected".
- **Shard**: "Join PU … shard[pub_euw1b_12881860_110]".
- **Where**: armistice zones, monitored space, jurisdictions (UEE, People's Alliance, Rough & Ready…), ship channels ("You have joined channel 'RSI
  Perseus : zzoega'": whose ship he is in), and the area he is in ("Location[Nyx_Levski]", "Location[RR_P6_LEO]": the game's area tags that come with
  the inventory lines, the space around a place; Frank, 30 Oct 2026, not inventories).
- **Blueprints received**: "Received Blueprint: …".
- **Shop purchases**: `<CEntityComponentShopUIProvider::SendShopBuyRequest> … shopName[…] client_price[…] itemName[…] quantity[…]` and the result
  (`RmShopFlowResponse … result[Success]`).

The trackers upload only the kinds of lines the tools read (HaulingLog.LineKinds). Every new tool adds kinds, still never the whole log; the players
should know before that happens.

## Order

1. Done (merged 28 Oct): landing page, rename, Tools page, top bar, SC Tools Tracker with old logs, Gauntlet Tracker, Mission Library (by name, with
   the Gauntlet and hauling rewards in it, #235), Scrip page, Session tool (#233), `.old.exe` cleanup (#232), Gauntlet Top 10 dropdown (#234),
   tracker version on /tracker and /admin/keys (#236), Hauling Planner top bar fix (#237).
2. Done: **automatic sessions** (#238) and the Party dropdown on the Hauling Planner's Session tab with debts kept apart per party (#239; the dropdown
   went on 29 Oct, Frank: the Session and Contracts tabs show the viewer's own party by themselves, everything together to a viewer in none, and the
   Party table sat beside the tabs with room for the ship names, until the Party button's panel took it over later that day). Decided
   28 Oct, replacing the earlier plan of 3 h / 30 min with buttons as overrides:
   - **Hauling Planner**: one session for the party (a player alone is a party of one; who joins the party joins its session). It starts when
     someone accepts a contract the planner reads (2–4 to Ruin, 2–4 from Ruin, Direct from Ruin). Two separate groups hauling at the same time would
     share one session (the planner shows one session at a time).
   - **Gauntlet Tracker**: a grind session per player, starting when a Combat Gauntlet scenario is accepted; worked out from his runs, so the earlier
     ones are there too.
   - **Activity**: only those missions count: accepted, completed, failed or abandoned (for hauling also their pickups and drop-offs; and since 30 Oct,
     while a hauling contract is still going, everybody's quantum jumps and the latest log line, after a session ended under the party on 29 Oct).
     A hauling session never ends while a contract is still open (Frank, 30 Oct: breaks of an hour or more at a station, loadings of twenty minutes);
     an open contract holds it for three hours after the last line in anybody's log.
   - **End**: after 1 hour without such activity, the session ends 5 minutes after the last activity. A crash, or a tracker off for more than an
     hour, ends it the same way.
   - **No buttons**: the hauling Start/Stop Session and the Gauntlet's "Start grind session" are gone ("if there are issues we will work them out");
     `SCTools:FollowSessions=false` brings the hauling button back. The Windows program went on 30 Oct 2026.
3. Done: fleet (#240), Party tool (#241), Map tool (#242).
   Done (30 Oct): the Companion (#380), the contracts tracker (#383).
4. Done (28–29 Oct): the map's tabs (#243), Map places (#244), everything up to #268: the Wiki API for every mission of the library (#265), one
   Hauling Planner (#266), the read-only Mission Library and Edit Mission Info (#267, #268). In review: the Contracts tab (#269).
5. Hauling of every system: the Route Planner for every hauling mission, by the stages of `docs/planner-any-hauling.md` (the design, 30 Oct):
   1 Pyro places, 2 the other systems, 3 the interstellar legs, 4 earnings and debts.
6. Debts as a tool of its own, then the Goods for the Getting tracker (after Frank's run).
7. Objectives on planets and moons on the map.
