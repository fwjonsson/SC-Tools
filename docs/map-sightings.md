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
| 31 Oct 2026 | Nyx | QV Breaker Station BRK-304 (the mission's) | mission spot: bounty "Reclusive Bounty: Burt Gearhart, Extreme Risk" | in the asteroid cluster by QV Breaker Station BRK-204, 537 Mm from it | map label "QV BREAKER STATION BRK-304"; objective "Go to QV Breaker Station"; mission BountyHuntersGuild_ShipWaveAttack_DrawOutBoss_Nyx_Super; the jump went to quantum destination social_001_keeger_segment_rckcrk_095 (Not known yet, 31 Oct 2026) | X -45.28, Y 15.91 Gm (the game's 47.99 Gm at 160.64 deg; the debug overlay's position, 30 km from the objective). Not in the game files, which have BRK-127, 204, 320, 425 and 546: a station the mission puts there, not a place to pre-map. The destination was not a mission beacon (MISSION_QT_Quantum_Beacon_LongRange_...) but the cluster's own nav point, so the map could not place it: say it is BRK-204 on Map places (537 Mm off, nothing at the map's scale) and no new stop is drawn. | skip |
| 31 Oct 2026 | Nyx | Rockcracker asteroid cluster | area (the game's name for where he was) | by QV Breaker Station BRK-204 | AsteroidClusterBase_Nyx_Rockcracker_Sandbox_009 (area; last digit hard to read); social_001_keeger_segment_rckcrk_095 (quantum destination; "rckcrk" is Rockcracker, "keeger segment" a part of it) | The inventory lines name this area, so it can turn up in Not known yet. If the other Rockcracker sandboxes also sit by BRK-204, an alias AsteroidClusterBase_Nyx_Rockcracker_* to BRK-204 is worth adding; one sighting is not enough to know. The exact text social_001_keeger_segment_rckcrk_095 is BRK-204 on Map places since 31 Oct 2026 (no *). | added |
