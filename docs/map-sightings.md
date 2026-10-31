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
