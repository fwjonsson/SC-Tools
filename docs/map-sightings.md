# Map sightings

Places Frank sees in the game that the map does not have yet: one line per screenshot he sends. Nothing here reaches the site by itself; a place is
added on Admin -> Map places (or in `StarMap.cs`, when it should be in the game data for everybody) only when it is picked, and most of these may never
be. The file is in the repo so the list is backed up, not because it is data the site reads.

What the list is for: the location tracker can only follow a jump to a destination the map knows. Today the admin's word on an unknown destination
is either "a place" (drawn on the map with its label) or "not a place to show" (the jump then goes nowhere the map knows). The best case Frank wants
(31 Oct 2026) is a third kind: a place the map knows the position of, so the arrow goes there and he is placed there, but that is not drawn as a stop,
so a mission's spot or a one-off outpost does not fill the map with random markers. That is not built yet.

Columns: date | system | place name | what it is (station, outpost, city, moon, marker, ...) | at / in orbit of | what the game called it (HUD or
quantum list; the log uses the quantum name) | notes (why it matters, which tool wants it) | status (new, add, skip, added).

| Date | System | Name | Kind | At / orbit of | Game text | Notes | Status |
|------|--------|------|------|---------------|-----------|-------|--------|
| 31 Oct 2026 | Nyx | QV Breaker Station BRK-304 | hidden station of the game data (the bounty "Reclusive Bounty: Burt Gearhart, Extreme Risk" sent him there) | in the asteroid cluster by QV Breaker Station BRK-204, 537 Mm from it | map label "QV BREAKER STATION BRK-304"; objective "Go to QV Breaker Station"; mission BountyHuntersGuild_ShipWaveAttack_DrawOutBoss_Nyx_Super; the jump went to quantum destination social_001_keeger_segment_rckcrk_095 (Not known yet, 31 Oct 2026) | X -45.28, Y 15.91 Gm (the game's 47.99 Gm at 160.64 deg; the debug overlay's position, 30 km from the objective). It IS in the game data (found later the same night): positions-nyx of the ScApiTool download has it, hidden (hide_in_starmap), at X -45.282, Y 15.909 Gm, 0 Mm from where he stood; the map loaded only the places that are not hidden (BRK-127, 204, 320, 425, 546). A real station: a place to add, hidden or drawn. The destination was not a mission beacon (MISSION_QT_Quantum_Beacon_LongRange_...) but the cluster's own nav point, so the map could not place it: say it is BRK-204 on Map places (537 Mm off, nothing at the map's scale) and no new stop is drawn. | add |
| 31 Oct 2026 | Nyx | Rockcracker asteroid cluster | area (the game's name for where he was) | by QV Breaker Station BRK-204 | AsteroidClusterBase_Nyx_Rockcracker_Sandbox_009 (area; last digit hard to read); social_001_keeger_segment_rckcrk_095 (quantum destination; "rckcrk" is Rockcracker, "keeger segment" a part of it) | The inventory lines name this area, so it can turn up in Not known yet. If the other Rockcracker sandboxes also sit by BRK-204, an alias AsteroidClusterBase_Nyx_Rockcracker_* to BRK-204 is worth adding; one sighting is not enough to know. The exact text social_001_keeger_segment_rckcrk_095 is BRK-204 on Map places since 31 Oct 2026 (no *). | added |
| 31 Oct 2026 | Nyx | Glaciem Ring Cluster (a mission spot in the Glaciem ring) | mission spot: bounty "Reclusive Bounty: Hideaki Antal, Extreme Risk"; the game map labels it only "Quantum Beacon" | in the Glaciem ring, 15.00 Gm from Nyx at -30.5 deg (X 12.92, Y -7.62 Gm); Transit Point Glaciem Bravo is 20 deg further round the ring | objective "Go to Glaciem Ring Cluster" / "Return to Glaciem Ring Cluster"; destination MISSION_QT_Quantum_Beacon_LongRange_864272617968 (a mission beacon); the game map says "NYX SYSTEM > 0.00 -30.53 14.99 Gm", no name; marker zone 906735902202, local position x 6242 y 1702 z 20329 m | The game data knows the spot: positions-nyx has 31 hidden "Glaciem Ring Mission Location" points on the 15 Gm ring (qt_valid), one at X 12.920, Y -7.619 Gm, 13 Mm from the game map's figure. Not known yet stays empty for this jump by design: the beacon is the mission's objective, so the tracker counts it as known ("at the mission: Return to Glaciem Ring Cluster") though it has no point to draw him at. | skip (a mission's spot), see below |

## What the game data has that the map does not use (31 Oct 2026)

The ScApiTool download of 20 Oct 2026 (`ScApiTool/ScApiData/20261020-1433/positions-nyx-001.json`, 693 entries) has every place of Nyx with
`hidden` (the game's hide_in_starmap) and `qt_valid` (a quantum destination). `StarMap` took only the ones that are not hidden. Hidden and in
the file: 31 "Glaciem Ring Mission Location" spots on the 15 Gm ring and 30 "Keeger Belt Mission Location" spots on the 48 Gm ring (both
qt_valid: where the bounties' beacons go), 61 asteroid clusters and 30 asteroid mining bases on the two rings, 45 Moraine Bases (48 Gm),
the Breaker stations BRK-235, 267, 304, 521 and 608 (48 Gm; the five drawn ones are on the same ring), 40 QV Extraction Stations spread
between 19 and 37 Gm, 20 QV Logistics Stations (15 Gm), the clinics. Pyro's file hides 19 Derelict Outposts, 4 Contested Zones, the clinics.
The "rckcrk" of the Rockcracker nav point is the Keeger belt: "Keeger Belt" and "Keeger 001" are in the locations data; "Rockcracker" is not.

Why the best case is not reached yet. A jump to a mission's beacon is followed as "at the mission: <objective>", and the objective's marker
comes in the log with a position, but that position is local to the cluster's zone (`zoneHostId`, an id that differs per server: 906735902202
here, 906735904623 for the Rockcracker one; the system itself was 906735902183), so it does not say which of the 31 Glaciem spots it is. The
game's own map knows (it showed 0.00, -30.53, 14.99 Gm); the log does not say it in any line the trackers read. The link that is missing is
zone id to spot. With the hidden places loaded as known-but-not-drawn (issue 398), a jump to a cluster's own nav point
(social_001_keeger_segment_rckcrk_095) still needs a name-to-spot link too; the positions file has no such codes, only names and uuids.

What can be done today: draw the two rings of Nyx (15 and 48 Gm) like orbit rings, so "at the mission: Return to Glaciem Ring Cluster" has a
ring to point at; load the hidden stations (the Breaker ones) as places, drawn or hidden; put a player at a mission whose text names a ring
on that ring.

## The fuel estimate is a distance (31 Oct 2026)

Every route the player sets writes `<Calculate Route> ... Successfully calculated route to <destination> fuel estimate <n>` (a `[QuantumTravel]` line, so the
trackers already send it). The estimate is linear in the distance, for one ship: the Pyro jumps of 29 and 30 Oct (Ruin to the Nyx Gateway 85.5 Gm, Patch City
to Ruin 85.9 Gm, Ruin to Orbituary 51.9 Gm, Orbituary to the Nyx Gateway 47.7 Gm) all give 35,035 per Gm within 0.03 %. The Perseus of 31 Oct gives 32,270 per Gm:
Levski to the BRK-304 spot 1,450,849 (44.96 Gm), and then Levski to the Glaciem beacon 738,770 is 22.89 Gm, exactly the distance to the Glaciem spot at
X 12.92, Y -7.62. The constant is the ship's (its drive); a jump between two known places by that ship gives it. The jump he set from the BRK-304 spot
to social_001_keeger_segment_rckcrk_095 was 17,344, 537 Mm: the nav point is at BRK-204, so the alias is exact.

So a beacon can be placed from the log alone: the start place and the fuel estimate give its distance; the game data's hidden mission spots on the rings
give the candidates; the ones at that distance are one or two (mirror images round the ring); the next route he sets from there, or the drive's travel time,
settles it. Frank's reports (the debug overlay's position) are still the way to learn spots and nav points the game data does not have.
