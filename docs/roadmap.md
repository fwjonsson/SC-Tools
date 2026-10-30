# SC Tools: the roadmap

What is planned, what was decided and why, tool by tool. Decided with Frank on 28 Oct 2026 and kept up to date since: the hauling tracker's server
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
- **Scrip page** (`/admin/scrip`, PR #230): what one MG scrip and one Council scrip are worth in aUEC, the price Frank thinks they sell for now; every
  tool counts scrip with these.
- **SC Tools Tracker** (`SCToolsTracker.exe`, was HaulingTrackerSender): uploads the live log, and the old backup logs the server does not have yet
  (all of them the first time, then the new ones; PR #227; streamed line by line since PR #231). Its menu shows its version, the old logs' progress and
  "Check for an update now". Old trackers update and rename themselves; the `.old.exe` an update leaves is removed when the new one starts (PR #232).
- **Hauling Planner**: the hauling tool (was Hauling Tracker), at `/hauling` with a path per tab (`/hauling/planner`...); the Windows program (gone 30 Oct 2026: Frank only uses the site and the tracker) was
  HaulingPlanner.exe.

## The tools

All the tools track at the same time, all the time. The tracking runs on the server from what the trackers upload, not in the browser page.

1. **Hauling Planner**: live at `/hauling`, with automatic parties and automatic sessions per party (#238, #239). Its contract rewards live in the
   Mission Library. Since #266 it is the one hauling app: the Other hauling board (every system) is on its Session tab with the hauler's SCU field in
   it; `/hauling/jobs` just goes to `/hauling`. The Contracts table was a tab of its own (#269) until 30 Oct 2026, when Frank put it back on the Session
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
   admin's `/admin/gauntlet` page went on 29 Oct, with his Contracts page). Top 10 fastest: picking a scenario shows it at once (no Show button). Since 29 Oct (Frank): the Session, Stats and History tabs are
   the viewer's in-game party's runs together (alone: his own); the Earnings ranking tab is **History**, with the lifetime totals moved there from
   Session and a Scrip/h column in the ranking. Since 30 Oct 2026 (Frank): Stats has the Earnings (the lifetime totals) and the Top 10 side by side,
   the Earnings per scenario (the ranking) and the times under them; History has the earlier grind sessions; Session has the game session and the
   grind session side by side; the Leaderboard is titled "All-time leaderboard"; every wide table takes the whole band.
5. **Party tool** (`/party`, PR #241): who is playing in which in-game party with the ship each one flies, everybody else with when they were last
   seen, and a page per player. `PartyService` keeps the site's party in one place for every tool. Since 29 Oct not a card on the landing page: a
   **Party** button beside the name in the top bar, with a panel (fetched when it opens) of the parties as tables like the Hauling Planner's old
   Party table (member, ship, drive, the rows in the members' colours; Frank, 29 Oct), which the Hauling Planner's page lost that day, with an
   Active tick for the admin and the party's leader (29 Oct, after the planner kept planning trips for a member who had stopped playing while the
   logs still counted him).
6. **Map tool** (`/map`, PR #242; all three systems in PR #243): Pyro, Stanton and Nyx from above, one tab per system (it opens on the system where
   most players are). The places (stars, planets, moons, cities, gateways, stations: 26 + 52 + 19) come from the Star Citizen Wiki API positions in
   Frank's ScApiTool download of 20 Oct 2026 (`StarMap` in the core; Pyro's match the Trip Planner's distances). Everybody playing is placed where
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
7. **Map places** (`/admin/places`, PR #244, Frank 28 Oct): the admin says what a quantum destination or start name the map does not know is (a
   place, or not a place to show; `Text*` for every one that starts so), renames places, and adds places the game data does not have (at a place, or
   X/Y in Gm). Saved in the database and used by the map at once.
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
    - **Wiki API** (#264; #265 for every mission of the library): the log's `contract [...]` and `contractDefinitionId[...]` are the API's
      `debug_name` and `uuid` (Frank confirmed with `701271ba-…` = `Iasi_HaulCargo_AtoB_Small_PartySupplies`), so a string mod's names do not matter.
      The contract ids are kept on the Missions rows (the old logs re-read once); the server asks `api.star-citizen.wiki/api/missions/{uuid}` about
      up to 10 contracts a minute until it has every one of the library, then only new ones; it keeps hauling orders and reward; the Wiki's reward is
      grey in the library and counts until one is typed in. A `reward_max` of 0 there means only the one end is given (#268).
    - **Next**: stage 3, route suggestions per player across systems (map distances, his drive, pickups before their drop-offs, his ship's SCU),
      planned together with the Ruin contracts in the Trip Planner; stage 4, other hauling in the session's earnings and debts.

Later, maybe: a party/crew board (who is on which shard, in which jurisdiction, in whose ship) and crash/server statistics.

Not wanted:
- A blueprint tracker. Frank's string mod already shows blueprints in the game.
- Mining scan signatures. They are not in the log (SC Toolbox's Mining Signals reads them off the screen), and Frank does not want screen capture.

## What the Game.log has (checked 28 Oct, Frank's logs of 15–26 Oct)

- **Contracts of every kind**: "Contract Accepted / Complete / Failed: <title>" with the rep, and the ContractId (one per offer, not per kind of
  mission). Every objective marker names the contract's own name and definition id (`contract [...]`, `contractDefinitionId[...]`): the Star Citizen
  Wiki API's `debug_name` and `uuid`.
- **No contract rewards and no wallet balance.** A few "Awarded 500 aUEC" notifications have no mission attached; what pays them is not known yet.
  Rewards come from the Mission Library (typed in, else the Wiki API's), with the wallet at the start and end of a session as an optional check.
- **The in-game party**: joins, leaves, the leader, disbanded (used by AutoParty).
- **Quantum travel**: the destination picked (an id: `pyro3`, `LOC_RR_S1_L2`, `rs_ext_nyx-pyro_jp1`, `levski_all-001`, `PartyMemberMarker_…`,
  `AsteroidCluster_Pyro_…`), the start in plain words ("Projected Start Location is Aberdeen", "… is Pyro System") and the arrival. An arrival with
  no destination picked: a jump at a marker looked at, or as crew (the same ship id in the pilot's log). A mission objective's quantum beacon:
  `PairWithMissionNavpoint … MISSION_QT_Quantum_Beacon_…`.
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
4. Done (28–29 Oct): the map's tabs (#243), Map places (#244), everything up to #268: the Wiki API for every mission of the library (#265), one
   Hauling Planner (#266), the read-only Mission Library and Edit Mission Info (#267, #268). In review: the Contracts tab (#269).
5. Hauling of every system: stage 3 (routes) and stage 4 (earnings).
6. Debts as a tool of its own, then the Goods for the Getting tracker (after Frank's run).
7. Objectives on planets and moons on the map.
