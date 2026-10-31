# SC Tools: notes for Claude Code

`README.md` says how everything works, in detail (start with "How the code is organised" and "The web app"). `docs/roadmap.md` says what is
planned, what was decided and why, tool by tool. The to-do list is GitHub Issues (the backlog). This file says how we work.

## How we work

- Frank (GitHub fwjonsson, zzoega in the game; his other accounts are zzatvo and zzoii) asks for one thing at a time, in Swedish or English. One
  branch and one pull request per request (`claude/<topic>`); Frank reviews and merges. `main` is protected: the Build and Tests checks must be green.
- CI is what counts: Build (Windows: the tracker and the API tool), Tests (Linux: `dotnet test tests/SCTools.Tests` and the planner scenarios of
  `tools/PlanTest`), Deploy on every merge to `main` (the site), Migration on a commit whose message has `[migration: Name]`.
- Database changes: never write an EF Core migration by hand. Put `[migration: Name]` in the commit message, the Migration workflow adds a migration
  commit on the branch, pull it. The Tests run of that first commit fails (the model change is pending); the migration commit's run is the one to check.
- The backlog is GitHub Issues (`gh issue list -R fwjonsson/SC-Tools`): every idea, bug and chore, one issue each, with labels `idea`, `bug`,
  `new tool`, `tech`, `waiting` (needs logs, a test in the game or a decision first) and one per tool (`hauling`, `gauntlet`, `map`, `mission library`,
  `session`, `party`, `debts`, `tracker`, `site`). A pull request that does an issue says `Fixes #n` in its body, so the merge closes it. An idea Frank
  mentions that is not done in that session becomes an issue (ask him first). A big one (a new tool) is one issue until Frank decides to build it; then
  its plan and decisions go in `docs/roadmap.md` and the issue gets sub-issues for the steps. Merged branches are deleted by GitHub.
- Stacked pull requests: say the merge order in the PR body.
- Every change comes with tests (xUnit, `tests/SCTools.Tests`). The server's pages are tested through `ServerFactory` (the real server in memory, the
  admin and players logged in); the game log lines come from `GameLogWriter`; the live page's JavaScript is checked by rendering it (Playwright) when
  the page itself changes.
- The style of the code: one class per file, a namespace per area, comments in full sentences that say what a thing is and why ("the planner only
  gives a load to someone who has the mission"), English everywhere. Commit messages and PR bodies are plain sentences too: what it does and why,
  not a list of files. README.md is updated in the same pull request as the code it describes, and `docs/roadmap.md` when a plan or a decision changes.
- The Hauling Planner's page is an ordinary page of the site (`Views/Hauling/Index.cshtml`, `wwwroot/hauling.js` and `hauling.css`, the map engine
  `wwwroot/systemmap.js`); its tabs have addresses (`/hauling/<tab>`), `PageController` lists them. The Windows program went on 30 Oct 2026 (Frank only uses the site and
  the tracker); its logic is the core's, which the site uses.

## Never

- Commit secrets or data: `uex-token.txt`, `loginData.json`, tracker keys, the data folder, logs. The UEX token and the tracker keys are shared privately.
- Force-push, rewrite history, or delete a branch that is not merged.
- Deploy by hand: a merge to `main` deploys. Server commands are Frank's to run (he gets them written out).
- Read the clipboard or capture the screen: Frank does not want tools that do. (A screenshot the admin pastes himself into the Mission Library's paste
  box is his own doing, Frank 31 Oct 2026: the page reads only what he pastes.)

## Where things are

- The site: **sc-tools.fwjonsson.dev** on Frank's Hetzner server (Arm), service `sctools`, settings `/etc/sctools/sctools.env`, data `/var/lib/sctools`
  (the SQLite database and the history folder with everybody's old logs). The old addresses `haulingtracker.` and `sct.fwjonsson.dev` redirect.
- The logs come from the SC Tools Tracker on each gaming PC (kino: zzoega, tsoi: zzatvo, bydlo: zzoii; the PCs reach each other's game folders over
  the network). Frank copies logs by hand with his copy-sc-logs script when a tool needs lines the tracker does not send yet.
- The trackers upload only the kinds of lines the tools read (`HaulingLog.LineKinds`); a new tool adds kinds, never the whole log, and the players are
  told before that happens.
- The Star Citizen Wiki API (`api.star-citizen.wiki/api/missions/{uuid}`): the uuid is the log's `contractDefinitionId`, the `debug_name` the log's
  `contract [...]`. `src/ScApiTool` downloads the game data (stations, drives, ships; the map's positions).
- Frank plays with Mr Kraken's string mod, so the mission names in his logs are not the game's defaults; the Mission Library groups by name, and the
  Wiki link goes by the contract id, so that does not matter.
