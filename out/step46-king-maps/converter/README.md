# The king maps: the converter

How the king maps (`star_fort_1.0.sd7`, `edge_keeps_1.0.sd7`, `corner_fortresses_1.0.sd7`) are made from their drawings (`out/koth-maps/k6_star_fort.svg`, `k4_edge_keeps.svg`, `k3_corner_fortresses.svg`) and All That Simmers v1.1.1 (its plants, detail textures, featureplacer gadget and water settings). It grew from Five Rivers' converter (`out/step44-five-rivers/converter`), made general: one program for every king map, a *look* for each planet, and the access checks for each team's ramp, zigzags and passes.

## Steps (for one map; `<key>` is `star_fort`, `edge_keeps` or `corner_fortresses`)

1. `python out/koth-maps/export_geometry.py <key> out/step46-king-maps/<key>/design/geometry.json`: the drawing's geometry (bands, cliffs, cliff-tops, water, land, ramps, bridges, fords, ramparts, towers, zigzags, passes, roads, metal, vents, kings, peasants), every team's part turned into place, in the drawing's pixels; with the fold, the axis, square or disc, the ground height and the planet.
2. `MapTool extract all_that_simmers_v1.1.1.sd7 <template>`.
3. `KingMaps build <geometry.json> <template> <work>`:
   - **Heights** for every vertex (`Terrain.cs`), then the **access checks** (`Access.cs`); it stops here if any fails.
   - **Texture**, 8 texels a square, painted texel by texel by the planet's look (`Look.cs`, `Paint.cs`) and written straight into a new SMT; a new SMF with the heights, minimap and grass (`Create.cs`).
   - **Detail maps** as DDS: splat distribution, specular and normals (4096), and a sky cube in the planet's colours (`Paint.cs`, `Derive`). Each is flipped before it is written: map `.dds` files are stored upside down (PLAN.md rule 7).
   - **mapinfo.lua**: name, credits, metal, the planet's colours and weather, the look's light and water, the new maps and sky, the starts (`Program.cs`, `Maps.WriteMapInfo`).
   - **run.txt** for MapTool: the extra models the planet needs, metal spots, geothermal vents, plants, rocks and props (`Place.cs`; headings in the engine's units, 65536 a turn with 0 facing south, from the converter's degrees clockwise from north); then `<key>_1.0_stage.sd7`.
   - All That Simmers' own layout is left out: its SMF and SMT, its detail maps, its grass and feature list, its sunrise sky, two unused textures, and its map options.
4. `MapTool edit <key>_1.0_stage.sd7 <key>_1.0.sd7 @run.txt`.
5. `KingMaps check <geometry.json> <final map, extracted> <work>/run.txt`: the access checks again on the built map's own heights, the ones that units get through going round every blocking feature (its footprint and 8 more), and how alike the parts are.
6. The minimap from a straight-down 3D render, features included (`<key>/CHECKLIST.md`, *The minimap and lobby picture*), then `MapTool install <key>_1.0.sd7 <scratch maps folder>`: the lobby picture, kept in `<key>/bar-data`.
7. `king_maps.cmd publish <key>`: the map on the GitHub releases, here and in BarMaps (`../RELEASE.md`).

## The terrain (`Terrain.cs`)

- **Turned, not mirrored.** MapTool's symmetry mirrors or turns by half, so nothing here uses it. Every height and colour is made here; noise is read in each part's own frame and blended with the next part's near the lines between them (`Sym` in `Design.cs`), so the parts are the same and meet without a seam. Patterns with edges (flagstones, slabs, stripes) are read in the part's own frame only.
- **Kings' ground is raised**: a cliff-top on cliffs 40 across (over 54°: nothing but spiders climbs them).
- **Walls are terrain**: ramparts 90 above the ground beneath, flat-topped (you can build on them), with sheer faces; towers 110. No doors.
- **A ramp for everyone**: 12°, from its foot in the realm up through the gap in the ramparts. Vehicles (27°), hovercraft and the Thor (33°) and bots all climb it.
- **Zigzags for bots**: switchbacks 64 wide, flights of 38° (dropping 65 at most) with level landings of 48, cut through the ramparts where they cross them. Bots (54°) climb them; vehicles and hovercraft don't.
- **Passes**: where a ridge parts two realms, a bots-only way over it: the ground along its line is lowered to at most 60 above the higher side and no steeper than 38° anywhere.
- **The planet's ground:** its relief (dunes, hummocks, cracked yards) is kept off roads, bases, spots and stairs; under it, a look may lay a broad swell (the moor's rolling hills, ±40 at most a few degrees steep) that roads and bases follow. Dry ground never dips below the shore into puddles.
- **Water** 60 deep with gently shelving banks; isles at most 60; bridges are causeways with decks at 16; fords 10 deep.
- **Disc maps** (Star Fort): beyond the play circle, mountains on a cliff.

## The checks (`Access.cs`)

Flood fills by the engine's rules (MapCore's `PassabilityMap`: per 8-unit square, the slope of its 16-unit cell, the water depth), from the starts, with a team's ramp or zigzags shut off to show what each one is the only way through:

- **Vehicles:** every start is reached from A's first peasant (and the isle, if there is one); with its ramp shut, a king's ground can't be reached; no zigzag takes them up.
- **Hovercraft:** no zigzag takes them up; the ramp does.
- **Bots:** each zigzag on its own takes them up; with the ramp and zigzags shut, the cliffs and ramparts stop them.
- **Passes:** bots cross each one; vehicles don't (within 200 units of it).

Given MapTool's `run.txt`, the checks that something gets through (every start, the ramps, each zigzag alone, the passes) go round the features as well. Each blocking feature blocks a square of its footprint and 8 more round it, so a way through is at least 3 squares wide:
- 64 for factory wrecks;
- 44 for other wrecks;
- 32 for crates and trucks;
- 12 for rocks and cairns;
- 8 for trees;
- nothing for ferns.

The checks that something doesn't get through stay on the heights alone.

## The planets (`Look.cs`, `Place.cs`)

| Map | Planet | Ground | Features |
|---|---|---|---|
| Star Fort | Dune | Rippled dunes, sandstone strata on the cliffs, flagstone courts in the bastions, block ramparts | Oases of palms and ferns at each ramp's foot; desert rocks on the ridges and passes and as scree under the cliffs; half-buried solar and wind wrecks |
| Edge Keeps | Heath | Rolling hills (`Swell`): heather on the rises, moor grass and green flushes in the hollows, small peat hags, bracken on the slopes, granite and lichen on the cliffs, turf on the keeps with heather at the rims, flag plazas, granite-sett causeways | Pinewoods thinning to lone firs, birch copses by the loch and in the hollows, bracken; granite tors on the crests, scree, boulders; cairns on the rims; mossy rocks on the isle |
| Corner Fortresses | Forge | An estate's lots of cracked, broken concrete between gravel waste ground gone to weeds; rust and oil; worn, patched asphalt roads with kerbs; big-slab fortress yards with hazard stripes; a brick market; acid crust round the lakes; panel ramparts | Ruined works (a wrecked factory or moho mine with storage tanks and metal makers round it, rubble), smaller wrecks, container yards, crates in the fortress yards, trucks by the roads, crane trucks by the market, weeds |
