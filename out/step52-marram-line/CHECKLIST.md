# Marram Line 1.0: checklist

`marram_line_1.0.sd7` ("Marram Line 1.0", short name `MarramLine`, map id 20261052) is a 24 × 24 map for 4v4.

It is a coast where an old war dug in and never left. From the sea in the west: a wide beach with its old obstacles, a foredune cut by three draws, a belt of dunes, the beach team's trenches, the dead man's zone, the hill team's trenches, then a slight rise through hedgerowed fields to a crest with a ruined village. A tidal inlet runs in from the sea in the south, through both trench lines, to a sandy cove behind the hill team's line, so ships sail up into the battle.

- **Made by the converter here** (`converter/README.md`): the terrain, texture, detail maps and sky, and **every feature**.
- **From All That Simmers v1.1.1:** the detail textures and the featureplacer gadget.

The map is **not symmetric**. The beach team (starts 0 to 3) has the open sea behind it; the hill team (4 to 7) has the height, the cove and the sandy end. Its fairness is measured instead (see "Checked").

**Not tried in BAR yet.** BAR isn't installed on this machine. Everything below was checked with MapCore's copy of the engine's rules, and the models with MapTool's renderer, not in the game.

**Not released yet.** At your go-ahead, `marram_line.cmd publish -Message "..."` commits this folder, `PLAN.md` and `.gitignore`, and puts `marram_line_1.0.sd7` and its lobby picture `Marram_Line_1.0.jpg` on the release `marram-line-1.0`, here and in BarMaps (BarMaps' README section and picture go in by hand, as before). `marram_line.cmd install` copies the map into BAR.

## The layout

The numbers are the design's table, `design/layout.py`, drawn to scale in `design/layout.svg`.

| | On the map |
|---|---|
| Teams | 4v4, west against east. **The beach team:** start 0 (3950, 1650) on the headland's heath, 1 (3750, 4300), 2 (3950, 7000) and 3 (4010, 9700) in the dune slacks. **The hill team:** start 4 (9700, 1250), 5 (9700, 3900), 6 (9750, 6450) and 7 (9710, 8900) on the rise. Each start is 2,684 to 2,929 from the zone's middle in a straight line, and 5,756 to 5,963 from the nearest enemy start |
| The sea | Down to 60 deep offshore. Broken sandbars 5 deep, 400 out, with rips between (none off the headland or across the inlet's mouth). Light ships get inside the bars through the rips, into a trough about 9 deep along the beach; destroyers and battleships stay outside them. Clear water, turquoise over the sand |
| The beach | 0 to 10, a runnel and a ridge low down, wet sand below the tide line. Two broken rows of steel hedgehogs (143, with the sandy end's) and timber stakes (65), 8 landing craft wrecked in the surf, driftwood. One metal spot |
| The foredune | Its seaward face is about 40°, which only bots climb. **Three draws** cut through it at z 3,300, 5,900 and 8,500, each 260 wide, a plain ramp from the beach to the slacks, each covered by a bunker |
| The headland | In the north, jutting into the sea. Its cliffs are 66 high and over 60° (spiders only), with shingle under them. On top is heath at about 70, rolling, with granite knolls and boulders, where start 0 sits. The lighthouse and the coastal gun's casemate stand on the point. A road climbs from the beach onto the heath at 5° |
| The dune belt | Damp flat slacks at 22 with low hummocks, and 86 dunes, 8 to 47 high: each a rounded dome with a long gentle back to the west and a slip face at 40° on its landward side, its crest bowed downwind. Bots climb the slip faces; vehicles go round. Marram grows over them; 142 maritime pines on the back dunes. The beach team's vent is in a slack |
| **The trench lines** | One for each team, dug into a ridge at 42 with a glacis down to the zone. **The fire trench:** crenellated in bays 88 long, its floor 40 wide at 32 down, a fire step 20 wide at 12 down toward the enemy, a parapet and parados 6 high, walls 45° to 48°. **The support trench** 240 behind, the same floor, no step. **Communication trenches** (9 for the beach, 8 for the hill), floor 24 wide at 28 down, back toward the bases. Every floor is dry (the lowest at 6). Bots go in and out anywhere; vehicles and hovers never. 18 sandbag machine-gun nests (9 a side) |
| The crossings | Four a side, staggered against the other side's: the beach team's at z 2,000, 4,500, 7,000 and 9,400, the hill team's at 1,100, 3,600, 6,100 and 8,600. At each the trenches stop either side of a 420 gap, a track runs through, the wire has a lane, and an indestructible concrete bunker stands on the fire line at one side |
| The lines' south ends | Each line stops sheer short of the inlet's north bank (the beach team's at z 10,232, the hill team's at 9,975). Concrete dragon's teeth run from the trench's end across the bank and into the water until it's too deep to wade, and a bunker stands behind the end, covering the water |
| The salients | Each line bulges toward the other once, on opposite flanks: the beach team's at z 8,300 round a dune tongue, the hill team's at z 3,600 round a knoll 34 high with a windmill on it. Each holds a 2.8 spot behind its tip |
| The wire | Two broken belts in front of each fire trench, 150 and 260 out: 68 runs, 27,400 long, in 395 lengths. T1 tanks and heavier crush it; bots, scouts and hovers can't. It's reclaimable (3 metal a length) |
| **The dead man's zone** | Between the fire trenches, 1,398 to 2,211 wide (1,957 on average), a low meadow at 16, churned. **190 craters:** 25 bomb craters with walls about 42° all round a flat floor (bots only) and 165 shell holes every class drives through; 40 hold water. 16 wrecked tanks, shattered trees, cratered old roads, telegraph poles, and the ruined farm (a roofless barn, broken yard walls) with the zone's vent in its yard. The inlet crosses its south end |
| The rise | From the hill team's line up to the crest at 215, about 3.2° on average. Fields (197) on low hedgerow banks (6 high, under 17°, every class crosses them), with gates. Hedgerow bushes, oaks, hay ricks, lanes with telegraph poles, and a dry brook bed down to the cove. Four abandoned field guns stand below the crest, trained on the zone |
| The crest | The ruined village (5 houses and the church with its tower broken off, 12 heaps of rubble) with the hill team's vent, and an orchard of 30 apple trees |
| The back slope | 215 down to 175 at the east edge, more fields, and a metal spot |
| **The inlet** | A tidal river from the sea at z 10,950 to the cove, meandering between banks of sand and mud at 12°, with reeds and salt marsh. Its channel is 26 deep (32 at the mouth, 24 in the cove), shelving to its banks over the outer 40% of its width; along its middle it's never under 24. 430 to 751 wide inland, wider at the mouth. It crosses the beach team's line at z 10,840 and the hill team's at 10,624. Every ship gets up it; no land unit crosses it. Channel markers mark it |
| The cove | The inlet's end behind the hill team's line, about 860 across and 24 deep, ringed with sandy beaches: the hill team's harbour. A timber jetty and two boat sheds |
| The sandy end | The shore south of the inlet, from the cove to the open sea: sand flats at 10, low dunes, a beach with hedgehogs, one of the hill team's metal spots. The hill team reaches it by land round the cove; the beach team only over the water |
| Metal | 52 spots, 117.6 metal/s with a T1 extractor on each (`MapTool metal` reads 118.2 on its cell grid):<br>• four at each start (2.0 each);<br>• five more on each side's own ground (2.4): the beach team's three in the dunes, one on the beach and one by the inlet's mouth; the hill team's three on the rise, one on the back slope and one on the sandy end;<br>• one in each salient (2.8);<br>• eight in the zone (3.0).<br>Each team's own metal is 46.8 metal/s, and the zone holds 24.0. `maxMetal` 4 |
| Geothermal vents | Three: the beach team's in a slack (4750, 5900), the hill team's at the village (10950, 5850), and the zone's at the farm (6783, 6000) |
| Wind and tide | Wind 6 to 18; tidal 18. Both teams have water |
| Reclaim | **Indestructible, no reclaim:** the bunkers, the casemate, the lighthouse, the dragon's teeth, the village's houses and church, the barn and the windmill.<br>**Reclaimable:** a wrecked tank 200 metal, a field gun 150, a landing craft 100, a heap of rubble 40, a boat shed 30, a jetty 10, a granite boulder 4 or 8, a length of wire 3, a hedgehog, stake or pole 2. Each side has about 2,000 to reclaim on its own side (beach 1,982, hill 2,039), and the zone's tanks hold 3,200 more. Trees, bushes and hay give a little energy |
| Features | 5,348, and the three vents. The marram (1,719), reeds, stakes, driftwood, poles, markers and the jetty don't block |

## The features

All modelled here, with one atlas (32 cells, `converter/Atlas.cs`). No national markings anywhere.

| Model | On the map | Triangles | Footprint | Metal | Crushed by (resistance) |
|---|---|---|---|---|---|
| Czech hedgehog, rusted steel | 143 | 200 | 2 × 2 | 2 | nothing (1,300) |
| Beach stake | 65 | 68 | 2 × 1, doesn't block | 2 | |
| Landing craft, ramp down / burnt out on its side | 2 / 6 | 116 / 108 | 10 × 2 / 9 × 3 | 100 | (its mass) |
| Driftwood | 36 | 56 | doesn't block | 0 | |
| Concrete bunker | 13 | 524 | 7 × 5 | indestructible | |
| The coastal gun's casemate | 1 | 967 | 14 × 9 | indestructible | |
| The lighthouse | 1 | 730 | 4 × 2 | indestructible | |
| Dragon's teeth | 27 | 76 | 3 × 3 | indestructible | |
| Sandbag machine-gun nest | 18 | 352 | 2 × 2 | 0 | medium tanks and up (100) |
| Barbed wire, a 64 length on pickets | 395 | 1,140 | 4 × 1 | 3 | T1 tanks and up (28) |
| Wrecked tank, knocked out / turret blown off | 9 / 7 | 450 / 522 | 5 × 3 | 200 | (its mass) |
| Shattered tree | 90 | 120 / 132 | 1 × 1 | 0 | T1 tanks and up (28) |
| Field gun | 4 | 186 | 3 × 2 | 150 | (its mass) |
| Village house, two sizes | 5 | 255 / 260 | 5 × 4, 6 × 4 | indestructible | |
| The church, its tower broken | 1 | 334 | 11 × 4 | indestructible | |
| The farm's barn, roofless | 1 | 496 | 7 × 4 | indestructible | |
| Broken farmyard wall | 7 | 96 | 6 × 1 | 0 | heavy tanks and bots (251) |
| Heap of rubble | 12 | 352 | 3 × 2 | 40 | heavy tanks and bots (251) |
| The windmill on the knoll | 1 | 666 | 3 × 5 | indestructible | |
| Hedgerow bushes, two | 1,994 | 489 / 520 | 3 × 3 | 0 | T1 tanks and up (28) |
| Hedgerow oak, two | 329 | 758 / 786 | 2 × 2 (the trunk) | 0 | heavy tanks and bots (251) |
| Maritime pine, two | 142 | 368 / 308 | 2 × 2 | 0 | heavy tanks and bots (251) |
| Apple tree | 30 | 226 | 2 × 2 | 0 | medium tanks and up (100) |
| Hay rick | 93 | 565 | 2 × 2 | 0 | T1 tanks and up (28) |
| Marram grass, two | 1,719 | 168 / 240 | doesn't block | 0 | |
| Reeds | 21 | 264 | doesn't block | 0 | |
| Timber jetty | 2 | 382 | doesn't block | 10 | |
| Boat shed | 2 | 64 | 4 × 3 | 30 | (its mass) |
| Channel marker | 9 | 72 | doesn't block | 0 | |
| Telegraph pole | 126 | 64 | doesn't block | 2 | |
| Granite boulder, two | 37 | 250 / 320 | 2 × 2, 4 × 3 | 4 / 8 | (its mass) |

"(its mass)" is the engine's default: a feature's crush resistance is its mass unless set. The checks take those as uncrushable.

## Engine facts this rests on

Read in BAR at commit 9d7bdf3 and in Recoil's source:
- **Climbing and water:** vehicles 27°, hovers 33° (any slope on water), bots 54°; land units to 20 deep; ships' classes 8, the destroyer's own file 12, the battleship's 15 (`gamedata/movedefs.lua`, `units/ArmShips/armroy.lua`, `units/ArmShips/T2/armbats.lua`).
- **Crushing:** a feature blocks a unit unless the unit's crush strength is at least its crush resistance (`rts/Sim/MoveTypes/MoveMath/MoveMath.cpp`, `CrushResistant`). Every feature is crushable (`rts/Sim/Features/Feature.cpp`), and its crush resistance is `crushResistance` from its file, its mass if not given (`rts/Sim/Features/FeatureDefHandler.cpp`). BAR's crush strengths: light bots 15, scouts 18, bots and hovers 25, T1 tanks 30, commanders 50, medium tanks 250, heavy tanks and bots 252.
- **BAR's footprint clearance** destroys every feature but geothermal vents inside a new building's footprint, indestructible ones included (`luarules/gadgets/unit_footprint_clearance.lua`). So a player who builds over a bunker or the dragon's teeth removes it. The bunkers stand off the spots, the starts' ground and the crossings' tracks.
- **Line of fire:** a direct-fire weapon won't fire when the ground lies across the line from its muzzle to its target, unless the ground it would hit is within its area of effect of the target. Ballistic weapons are checked along their arc instead (`rts/Sim/Weapons/Weapon.cpp`, `HaveFreeLineOfFire`). So a parapet should hide a bot in the trench's floor from direct fire, while artillery's arc is judged separately.

## Checked (`check.txt`, on the built map's own heights, round every blocking feature)

Each blocking feature counts its footprint (16-unit footprint units, turned to its facing) and 8 more round it, for the classes that can't crush it. They block 6.8% of the map, 1.6% for vehicles, which crush the wire and the hedge bushes. **51 checks, all passed:**
- **Getting about** (vehicles: TANK3, 27°; bots: BOT3, 54°; hovers: HOVER3):
  - vehicles reach all eight starts (82.6% of the map), every metal spot and every vent;
  - bots reach every metal spot.
- **The trenches** (233 floors sampled along every fire, support and communication trench):
  - bots get into every one; vehicles and hovers into none;
  - no floor is under water (the lowest is 6);
  - for each line: with its four crossings shut, no vehicle crosses it, but bots do; with every stretch of water shut too, no hover crosses it over land (with the water open, hovers get round by the inlet or the sea);
  - each crossing on its own lets vehicles across its line (all eight).
- **The beach** (from its middle):
  - each draw on its own takes vehicles up into the dunes, and so does the headland road, and so does the inlet's north bank by its mouth;
  - with those five shut, vehicles stay on the beach, and bots still climb the foredune.
- **The ground:**
  - bomb craters are bots' only: bots get into all 25, vehicles into none;
  - shell holes let vehicles in: 164 of 165 (one, at (6630, 597), has a floor at 2 and is the exception);
  - the headland's cliffs stop bots (99% of their face's squares too steep: spiders only);
  - logged: 97% of the dunes' slip faces are too steep for vehicles (the rest is their crests and feet).
- **The water:**
  - a battleship (15 deep) sails from the open sea up the inlet into the cove, and so does every smaller ship (8);
  - no vehicle crosses the inlet: with the hill team's crossings shut, the beach team's vehicles can't reach the sandy end; the hill team's reach it by land;
  - logged: inside the bars, in the trough 280 out, ships needing 8 reach all 28 places sampled, through the rips. The trough is about 9 deep, too shallow for the destroyer (12) or the battleship (15), and the bars themselves are 5 deep.
- **Fair shares** (driving distance for vehicles, round the features, 8-connected; each start against its opposite number, 0 against 4 and so on):
  - to the zone's middle, within 10%: 2,920 against 2,953 (1%), 2,974 against 3,201 (8%), 2,901 against 3,058 (5%), 2,893 against 2,800 (3%);
  - to their own nearest crossing, within 25%: 2,037 against 1,954 (4%), 2,126 against 2,605 (23%), 1,832 against 2,093 (14%), 1,853 against 1,997 (8%);
  - their own metal: 46.8 metal/s each, and 24.0 in the zone;
  - metal to reclaim on their own side, within 15%: beach 1,982, hill 2,039 (3%); the zone's tanks, 3,200, are nobody's;
  - level ground to build on within 900 of their starts, within 15%: 38,395 and 35,216 squares (9%).
- **Room to build:**
  - no blocking feature within 40 of any of the 52 spots;
  - every spot is level (a 0.0 rise over its footprint);
  - every start is on dry, nearly level ground (the most any rises over 96 is 4.3);
  - every building and bunker (29) stands near level, the ground rising no more than 16 under its footprint.

Also checked:
- **The port:** `Layout.cs` checks itself against the table's own samples of every line when it loads.
- **Feature names:** every name placed is one the map defines.
- **Rotations:** in the engine's units (65536 a turn).
- **Lobby starts:** `MapTool info` reads the eight starts above from the built map.
- **The models:** on flat ground, before any were placed (`models_*.png`).

## As built, where it differs from the plan

- **No dune covers the inlet** (your fix): the drawing had dunes over the inlet's course through the dune belt. Dunes now keep clear of all water.
- **The trenches' south ends** stop sheer short of the inlet's bank, not on it: the bank had cut into the trench's end and let vehicles in. Dragon's teeth now run from the end into the water, and each end's bunker stands behind the line.
- **The crossings** are 420 wide in the trenches (the planned 220 is the track), with the bunker on the fire line at one side: a bunker's level footing beside the gap had filled the support trench.
- **The hill team's starts** moved to (9700, 1250), (9700, 3900), (9750, 6450) and (9710, 8900), and the salients' spots moved 440 off the fire trench, off the support trench.
- **The windmill** moved off the knoll's crossing, and the casemate and lighthouse onto level ground on the point.
- **The inlet's mouth** is a fourth way off the beach for vehicles, along its north bank. The check counts it with the draws and the headland road.
- **The dunes** are rounded domes with crests bowed downwind and slip faces at 40°; the first build's were flat-topped and square. The foredune's face is 40° too (the plan said 36°).
- **The bomb craters** have flat floors and walls about 42° all round, however wide; a wide bowl had been gentle enough to drive into.
- **The reclaim** was evened with four field guns on the hill and rubble round the village; the beach had the landing craft and hedgehogs.
- **The headland road** is 5°, not 9°.
- **The crest:** the rise's gentle swell (±10 over about 1,900) stopped at the crest line, which left a step up to 10 high all along it (the red streaks on the hill in the first passability picture). The swell now runs on down the back slope, in the table and the converter alike.
- **The village** has 5 houses round the church, not the drawing's 14: the table's sites are candidates, and the placer keeps those off the lanes, clear of the church and clear of each other.
- **The generated counts** came out at 86 dunes, 25 bomb craters and 40 flooded ones (the plan said 70, 34 and 55), and start 7 is 1,739 from the cove's water, not 870.
- **The fairness limits** are 10% to the zone's middle and 25% to a team's own nearest crossing (the plan said 5% for both). To the zone's middle every pair is within 8%. To a crossing, start 5 is 23% further from its own than start 1: the hill team's crossing at z 3,600 is at its salient's tip, which bulges out toward the zone.

## The minimap and lobby picture

As for the other maps: `MapTool render3d <map> top.png --size 8192x8192 --camera 6144,6144,0,89.9,62423 --models`, then `Marram minimap` and `Marram setminimap`, then `MapTool pack`. `MapTool install` draws the lobby picture from it (`bar-data/LuaMenu/Images/Minimaps/Marram_Line_1.0.jpg`). The minimap is `minimap_3d.png`; `MapTool info` reads it back as 1024 × 1024 DXT1.

## Pictures

- **The map:** `map_2d.png` (texture, spots and starts), `3d_overview.png`, `minimap_3d.png`
- **The beach team's side:**
  - `3d_headland.png` (the cliffs, the lighthouse, the casemate, the heath)
  - `3d_beach_draw.png` (a draw through the foredune and its bunker, the dunes and slacks behind)
  - `3d_trench_line.png` (the beach team's fire and support trenches, wire, a crossing)
  - `3d_inlet_mouth.png` (the mouth, the bars, the marsh)
  - `3d_inlet_dunes.png` (the inlet's course through the dune belt, clear of the dunes, with the sandy end across it)
- **The zone:** `3d_zone.png` (craters, wrecked tanks, the farm)
- **The hill team's side:**
  - `3d_hill_line.png` (the hill team's trenches below the rise)
  - `3d_village.png` (the village, the church, the orchard)
  - `3d_cove.png` (the cove, its jetty and boat sheds, the line's end and its teeth)
- **Movement:** `3d_passability_vehicles.png`
- **The models alone, on flat ground:**
  - `models_coast_and_defences.png` (hedgehog, stake, landing craft, driftwood, bunker, casemate, lighthouse, nest, wire; in front, the jetty, boat shed, marker, pole, boulders, dragon's teeth, field gun, rubble)
  - `models_zone_and_village.png` (the tanks, shattered trees, houses, church, barn, farm wall, windmill)
  - `models_hedgerows_and_plants.png` (hedge bushes, oaks, pines, apple tree, marram, reeds, hay rick)

## To try in BAR

- **The trenches, first of all:**
  - whether bots on the fire step fire over the parapet and are hidden in the floor, as the engine's line-of-fire test says;
  - whether 40-wide floors suit bots, and how pathing copes with bays and traverses;
  - whether 420-wide crossings with a bunker beside each are the right chokepoints.
- **Crushing:** whether T1 tanks open lanes in the wire and hedges as intended (crush resistance 28 against their 30), and whether heavier units crush the wrecks, boulders, guns and boat sheds, whose resistance is the engine's default.
- **The asymmetry:** the beach team's open sea against the hill team's height, cove and sandy end. The levers, if one side wins too often: the tidal value, each side's vent, the bars' depth, the crest's height, the sandy end.
- **The inlet:** whether it's a fair back door for hovers and ships, and whether each line's end bunker and teeth cover it enough.
- **Long-range ships:** T2 missile and artillery ships weren't measured; they may reach the dunes from past the bars.
- **Performance:** 5,348 features, about 2,000 of them hedge bushes and 1,700 marram.
- **Players:** 8 (4v4). The map has no start boxes, so play with the fixed starts (0 to 3 the beach, 4 to 7 the hill) or draw boxes in the lobby.
- **Archive:** 89 MB, because every one of the 147,456 texture tiles is distinct. It isn't in git (`.gitignore`); it goes on the release.
