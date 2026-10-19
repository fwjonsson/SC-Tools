# Hauling Session Tracker

Reads Star Citizen `Game.log` files and fills in your hauling session sheet.
Only **Station to Ruin** hauling contracts are used. Ruin to Station missions are ignored.

## Build and run

You need the .NET 10 SDK (Windows).

    cd HaulingTracker
    dotnet run

or open `HaulingTracker.csproj` in Visual Studio and press F5.
To make a single exe: `dotnet publish -c Release -r win-x64 --self-contained false`.

## Use

1. **Open log file(s)** to read old logs (pick several at once, for example logs split by a crash).
   Or **Watch live Game.log** while you play. The window updates every 1.5 seconds.
2. Pick the session in the drop-down. Missions less than 30 minutes apart form one session.
3. Start and End times are filled from the log. Type over them if you want. **Use log times** resets them.
4. Rename each pickup location once (click the name). It is remembered for every future session.
5. Pick the worker for each location, tick Partial pickup if needed, write notes. Payouts can be changed in the reward table.
6. **Copy table for Google Sheets** copies the pickup table, ready to paste into the sheet.

Your data is saved in `%AppData%\HaulingTracker\data.json`. On the Debts tab, **Data file…** moves it to another folder, for example in OneDrive, so it follows you between PCs. Run the tracker on only one PC at a time then, because it rewrites the whole file on every change.

## What the log can and cannot tell

- Several missions with the same pickup are grouped into one row with a count.
- The Missions column shows missions still waiting / missions with cargo at that location, so 5 missions start as 5/5 and count down as you load. Yellow = partly loaded, green = all loaded.
  The log has no SCU numbers, so it only knows loaded, partly loaded or not loaded.
- Loaded and delivered times are shown on the run board. "Copy table for Google Sheets" still fills them in per location.
- If the log misses a final "completed" push, stops of a finished mission count as loaded.
- Not in the log: location names, SCU per location, rewards. These are typed by you.

## Runs, players and two computers

- The lower table lists every run. A run ends at each hand-in at Ruin, also a partial one.
- Rewards: completed 2 stop and 3 stop contracts are counted from the log. Type the payout of each type and the totals, revenue and hourly rate calculate.
  Hand-ins less than 3 minutes apart count as one hand-in. Run #1 starts at the first accepted contract.
- Each Game.log belongs to one player (the character name in the log). The run board shows the runs of all loaded players side by side, the top table follows the player picked in the drop-down.
- **Add live source…** adds the StarCitizen folder (the one holding LIVE and HOTFIX), also on another computer through a shared folder (UNC path). The drop-down next to it picks LIVE, HOTFIX or Auto (the log written most recently). All added sources are watched at once.
- When watching starts (and when the game starts a new Game.log) the logs in the `logbackups` folder that are less than 6 hours old are read first, so a session that was already going on, for example before a crash, continues.
- **Sources…** lists every added live source, shows if it can be reached right now, and lets you remove one.
- Mission updates reach the whole party, so the logs of all accounts are merged and a shared mission counts once.
- **Party…** is the list of names in the Assigned Worker dropdown.
- A location nobody is assigned to is assigned automatically to the player the logs show picking up there. You can change it, and bildog123 (no log) has to be assigned by hand.
- The run board makes runs per worker. The Assigned Worker of a location always decides who did a pickup there. Only for locations with nobody assigned, the logs are used: only the player who takes cargo out of a freight elevator has those lines in his own log, a few seconds before the mission update (needs that account's log).
- A run is leaving the base, collecting cargo from any number of missions and handing it in at Ruin. A hand-in ends only the run of the player who did it, never somebody else's run, whatever missions it was for. Who did it is read from the logs (the loading platform lines). If no log names anybody, it is credited to a worker without a log (like bildog123).
- Any pickup signal counts a stop as picked up. Tick **Partial pickup** yourself if you had to leave cargo (the cell turns yellow) and write what you left in Notes.
- **Lost cargo** on a run is ticked by hand. It does not change the revenue.
- The reward table counts every accepted 2 and 3 stop mission, payouts start at 1,268,250 and 1,691,000.

- **Value** on the upper board is what the missions at that location are worth: every mission pays its reward split over its stops (reward / 2 for a 2 stop mission, reward / 3 for a 3 stop mission), times the stops at that location. It follows the payouts in the reward table.

## Runs, lost cargo and history

- **Lost cargo** on a run is a drop-down: None, Little or All. It is only for your own notes and never changes who owes what. A row turns red for All, yellow for Little, and green when the run ended with everything delivered.
- **End run** (last column) ends the run of that worker by hand, for example when all cargo was lost. You get an "are you sure" box. A run ended by hand shows **Undo end** to bring it back.
- **History tab:** every session is saved with its date, missions, revenue and what each worker carried, so earlier sessions stay after their logs are gone.

## Debts tab

- **Earnings:** every session saves how much each worker carried (the Value of the locations assigned to him). The table shows this session, all sessions and the net balance per worker.
- **Split:** the reward of a session is split equally between everybody in the party, but each one is owed the value he carried. So somebody who carried nothing owes his whole share to the others, and somebody who carried more than his share is owed the difference. Lost cargo changes nothing.
- **Session party…:** by default the party of a session is everyone who carried something. Use this button to add someone who was in the party but carried nothing (or to remove someone).
- **Balance:** pick a party member and see the balance with each other member. + means they owe the selected member, − means the selected member owes them. Type over a number to correct it, your correction is kept.
- **Money transfers:** `You sent <name>: <amount> aUEC` and `<name> has sent you: <amount> aUEC` are read from the logs (a transfer seen in both accounts' logs counts once). The list shows the transfers of the selected member. Tick **Payment** on the ones that pay off a debt, other transfers (like buying a ship) are ignored.
- Balances add up over all saved sessions, so earlier money you settled by hand can be fixed with the typed corrections.
