# Meridian Landfall 1.0: checklist

`meridian_landfall_1.0.sd7` ("Meridian Landfall 1.0", short name `MeridianLandfall`, map id 20261125) is a 32 × 36 map for 8v8.

Starship Meridian has come down on an asteroid. The two fleets fight port against starboard: inside the ship as on Meridian, and outside on the cratered moonrock round it, where void pits blasted through the rock let nothing but aircraft across.

- **The ship is Starship Meridian's own:** the converter compiles Meridian's layout, heights, ship paint, placement and checks from `../step50-starship-meridian/converter`, unchanged. It moves the ship by (2048, 2048) and raises it by 400, so its deck is at 640. Only two things differ, and a check proves it, vertex by vertex: the hangars' bay doors, and the walls beside the bridge-level floors (see "Made better on the way").
- **Made by the converter here** (`converter/README.md`): the asteroid's terrain, texture, detail maps and sky, the moonrock models, the landing struts, and the void's invisible rubble.
- **From All That Simmers v1.1.1:** the featureplacer gadget.
- **The plan** is `DESIGN.md`, with its to-scale drawing (`design/layout.svg`, from `design/layout.py`, which writes the `layout.json` the converter reads).

The map is **symmetric**: the starboard half is the port half's mirror image (x = 8192), heights and features alike (a check).

**Not tried in BAR yet.** BAR isn't installed on this machine. Everything below was checked with MapCore's copy of the engine's rules, and the looks with MapTool's renderer, not in the game.

**Not released yet.** At your go-ahead, `meridian_landfall.cmd publish -Message "..."` commits and pushes this folder, then puts the map and its lobby picture `Meridian_Landfall_1.0.jpg` on the release `meridian-landfall-1.0`, here and in BarMaps. BarMaps' README section and picture are added by hand. `meridian_landfall.cmd install` copies the map into BAR.

## The layout

| | On the map |
|---|---|
| Teams | 8v8, port (west) against starboard (east). Port is 0 to 7:<br>• Meridian's four in the ship: 0 the forward hangar, 1 the crew deck, 2 the main hangar, 3 the cargo hold;<br>• four outside: 4 the fore flank (1800, 3600), 5 the forward bay (1500, 7700), 6 the aft bay (1500, 12000), 7 the aft flank (1650, 16150).<br>Starboard's 8 to 15 are their mirror images |
| The ship | Meridian, its deck at 640, its hull from x 2948 to 13436 and z 2448 (the bow's tip) to 16048 (the engine housings' ends). Its eight start rooms, five crossings, 46 spots and 4 vents are as on Meridian. The hull's sheer side rises 140 from the moonrock to the rim |
| The bay doors | Each hangar's outboard wall opens through the rim in a bay door 700 wide, level with the deck, onto an apron of moonrock 900 × 1,300, kept level. Hazard chevrons cross the threshold, tracks run out across the apron, and cargo is set down at its corners, clear of the way in |
| The moonrock | A plain at the deck's height, swelling gently away from the hull (under 3°), pale grey regolith pocked with micro-craters. Each outside start sits on a mare: a darker, smoother, mottled plain 900 in radius, its edge ragged, kept level |
| The flanks | Between the hull's long side and the asteroid's edge: flat ground 2,100 to 2,450 wide, then the edge's worn slope (its upper part still easy going). Two outside starts sit by the bay doors, with a void pit between each pair of starts and the flank's own metal beside them |
| The fore field | North of the bow. Start 4, a big bowl (bots only) with rich metal on its floor, a void pit, contested metal, a vent on the centreline, and small craters |
| The aft field | Behind the stern, round the engine housings, whose engines hang out over the moonrock and have scorched it in fans behind them. Start 7, the aft flank's bowl (rich metal), a bowl with a vent on its floor, a void pit, contested metal, small craters |
| The asteroid's edge | Ragged at every scale, the edge's line 120 to 660 from the map's border, in bays and bites, shallower along the flanks than at the bow and the stern. **A slope worn down over ages,** as if water had run off it for years:<br>• it starts from a rounded shoulder 360 to 480 inside the edge's line;<br>• it averages about 23°, and is steepest (about 35°) where it meets the void, which is cut 170 below the plain;<br>• gullies run straight down it, meandering a little and deepening as they go (up to 55), with spurs between them;<br>• fine rills score its upper part;<br>• boulders that have weathered out of it lie on it |
| Lanes | The ship's five crossings, and two outside: round the bow and round the stern. Each team's bay doors join its outside players to its hangars |
| The fronts | By the shortest drive, crew deck to crew deck is 5,114; forward bay start to forward bay start 13,569; fore flank to fore flank 13,738 |
| Metal | 90 spots, 213.2 metal/s with a T1 extractor on each (`MapTool metal` reads 214):<br>• the ship's 46, as on Meridian;<br>• three round each outside start (2.0) and four on each flank (2.4);<br>• four contested in the fore and aft fields on each side (3.0);<br>• one on the floor of each of the two flank bowls a side (3.6, bots only).<br>Each team has 72.0 metal/s of its own; 54.8 is contested and 14.4 lies in the bowls. `maxMetal` 4 |
| Geothermal vents | Seven: the ship's four, one on the centreline at the bow, and one on the floor of each aft bowl (bots only) |
| Wind and tide | None |
| Reclaim | The ship's, as on Meridian (4,460 metal in each team's rooms, 930 in the shared ones), and the cargo at the aprons: 540 metal a side. The moonrock, the struts and the void's rubble are landmarks: indestructible, nothing to reclaim |
| Light | Meridian's: a nearby star's hard white light, deep shadows, low blue ambient, no fog |

## Craters and void pits

| Kind | How many, how big | Who gets in |
|---|---|---|
| Small craters | 62 a side (generated, seed 51; kept clear of the edge's slope), 70 to 300 in radius, 20 to 85 deep, low rims | **Bots only.** Their walls are about 39° |
| Bowls | 3 a side, 380 to 560 in radius, 150 to 240 deep, rims raised 60 to 100 | **Bots only.** Their walls stay between 38° and 45° all round (a check), steeper than vehicles (27°) and hovers (33°) climb, gentler than bots (54°). There are no ramps in or out. Rich metal (3.6) or a vent lies on the floor |
| Void pits | 4 a side, 330 to 450 in radius, each blasted into a different rock (see "The look") | **Aircraft only.** A broken rim rings the outline: ridged rock crossed by cracks, its width wavering, with hummocks of ejecta beyond. The scarp below falls in three steps (steeper faces, gentler ledges), 130 to 300 wide, its face roughened. The texture is cut 110 below the plain, so the broken faces drop straight into the starfield. Invisible rubble covers the floor |

- **Bots only means the bot band.** Amphibious tanks (armcroc, corseal, armbeaver) and armamph climb 54° too, so they get into the craters as bots do (the checks log it: all 130).
- **No circle anywhere but the ship.** Every bowl's and small crater's outline wavers. Every pit's is fractal (39 harmonics, rough at every scale), with a few notches where a chunk blew out or a buttress stayed. Each strays from its mean radius by 6% or more (a check). The edge's line is rough at every scale too. Nothing on the asteroid is a straight line or a box.
- **The void's rubble:** 1,702 invisible blockers, one over every 128 × 128 cell with void in it. Their footprints block every ground unit, spiders included, and nothing can be built or unloaded there. Each one's collision box is 1 × 1 × 1, buried 600 under the ground, so no shot meets it.

## The features

2,779 placed:
- **The ship's own 191,** as on Meridian.
- **Moonrock (856):**
  - shards (58) and broken slabs (44) on the pits' broken rims: faceted, lumpy blocks, not boxes;
  - boulders in four sizes (754): thrown round the pits, weathered out onto the edge's slope, on the bowls' and small craters' rims, and scattered over the plains, with eight huge ones.
  - They're kept off the aprons, the mares, the spots and vents, the ship and the void.
- **Six landing struts,** three on each long side of the hull, between the bay doors.
- **Cargo at the aprons (24):** containers, crates, pallets and a tug at each apron's corners.
- **The void's rubble (1,702).**

## The look

- **The ship's texels** are Meridian's own painter's, unchanged, and so is its hull's sheer side. The bay doors have hazard chevrons.
- **Regolith:** pale grey, mottled at two scales, speckled with pebbles, pocked with micro-craters (a darker pan inside a brighter lip).
- **Mares:** darker, smoother, mottled, their edges ragged.
- **Craters:** their floors darker, a bright ejecta blanket round each, and bright rays round the bowls and pits.
- **The void pits, each its own rock and ore:**
  - **Iron:** warm basalt with rust-red seams.
  - **Copper:** grey-green rock with malachite-green seams.
  - **Ilmenite:** dark blue-grey rock with silvery seams that glint.
  - **Anorthosite:** pale rock with gold-yellow seams.
  - **Their walls:** layers 16 deep, each its own tone, with soft partings between them, darker the deeper they lie. Some layers carry a seam of the pit's ore 2.5 to 6 thick, pinching out and back in along the wall.
  - **The rims:** bared along their ridges, fading out under the regolith, cracked (the cracks warped, not tiled), with dust on the ledges.
  - **Thrown out:** stones lie about, pale on their sunward side, with their shadows, and round flecks of the pit's ore.
- **The edge, worn:**
  - layered rock (bands 22 deep, laminated, lighter and browner than the pits' fresh rock) bared where the slope steepens;
  - dust on its rounded shoulder;
  - the gullies' floors darker and streaked down the slope, the spurs between them paler;
  - scree gathering toward the foot.
- **The steepest faces** keep only broad tones. The ground's texture is cast straight down, so fine detail there would smear into vertical stripes.
- **The ship's landing:** soot from the landing burn along the hull, the engines' blast fanned out behind the stern, and tracks out of each bay door.
- **Metal and vents outside:** ore in a dark, glittering patch; a dark throat in a ring of frost.
- **The moonrock's models** are on their own small atlas: regolith-coated rock, dark basalt, fresh-broken faces, dust. The slabs and shards are faceted, each face dust, basalt or fresh-broken by how it lies.

## Checked

`check.txt` is the checks on the built map's own heights, round every blocking feature's footprint. All 48 pass:
- **The ship is Meridian's:** raised 400, it differs from Meridian's own heights at 9,157 vertices, all in the bay doors or the walls beside the bridge-level floors.
- **The halves:** the heights match (0.000 apart), and every feature has its mirror image.
- **Getting about:**
  - vehicles reach all 16 starts and every spot and vent but those on the bowls' floors;
  - bots reach them all;
  - each outside start reaches both its team's bay doors by vehicle without going round the bow or the stern.
- **The lanes:**
  - each of the seven alone (the five crossings, the bow, the stern) lets vehicles across;
  - with all seven shut, vehicles, bots, hovers and amphibious units can't cross. Spiders still cross over the ship's bulkheads, as on Meridian (logged).
- **The void:** no ground unit reaches it, spiders included, the pits and the edge alike.
- **The ship's walls and solid tops:** nothing but spiders reaches them from outside.
- **Craters:**
  - vehicles and hovers get into none of the 130;
  - bots get into every one;
  - the bowls' walls stay between 38.3° and 44.9°;
  - no outline is a circle.
- **Doors and room to build:**
  - every door is at least 500 wide (the bay doors 700), and none has a feature in it or in the way across an apron;
  - the ship's start rooms keep 95% to 98% of their floor level and free;
  - each mare keeps 98% to 100%, and each apron at least 95%.
- **Standing true:** every machine, prop and strut stands level (the most 0.1). The moonrock lies as it fell.
- **Metal:** no blocking feature within 40 of a spot; every spot, vent and start is level; each team's own metal matches.

## Made better on the way

1. **A flaw in Meridian's ship, fixed here.** The new check that nothing reaches the ship's solid tops found that Meridian caps its bulkheads at 340, which is below the top of its bridge ramps (380 down to 240). A vehicle on the upper ramp can drive sideways onto the wall beside it, then out over the bow's hull top. It can't reach the other side that way, so Meridian's crossing checks passed, but it can park on the hull. Here every wall within 48 of a floor above 280 (the bridge's, its landings' and the ramps' upper parts) stands at least 60 over that floor. Every other wall is Meridian's. **Starship Meridian 1.0, already released, still has it.**
2. **Bowl walls near their floors:** the outlines' waver was first applied as an offset to the radius, which bunched the contours near the floor (walls up to 64°). The whole profile now scales with the outline, and the waver is calmer.
3. **The edge and the craters:** the edge's bays first reached up to 880 in, crowding the narrow flanks and running into three craters. The sides are now shallower than the bow and the stern, and the craters and starts were moved clear, with every clearance measured.
4. **Spots inside pits:** two of the flank's spots in the first drawing fell inside void pits; they were moved.
5. **The edge and the pits, reworked (at your request: "map end and pit edges and geometry needs more work, there can be alot more detail put into the shapes and textures to make it look natural, rectanguar extusions are not seen much"):**
   - **The rims:** they were flat-topped Voronoi plates at stepped heights, which read as rectangular extrusions. They're now continuous ridged rock, crossed by cracks, their width wavering, with hummocks of ejecta beyond. The hummocks stay off the spots, mares and aprons.
   - **The walls:** they were one straight plunge. They now fall in three steps, their width wavering (the edge's into buttresses and gullies), their faces roughened.
   - **The outlines:** they were built from straight-sided wedges. They're now fractal: a pit's from 39 harmonics with soft notches, the edge's from eight octaves of waves with soft bites. Both are kept in lookup tables, so painting stays fast.
   - **The textures:** they were plates of fresh rock. They're now layered basalt, dust on the ledges, cracks at two scales, and stones with lit sides and shadows. The steep faces keep only broad tones.
   - **The slabs and shards** were a box and a pyramid. They're now faceted lumps.
   - All 48 checks still pass.
6. **The edge worn down, and the pits given their own rock and ore** (at your request: "the pits need more texture and a difference, add some ore seems into the layers and the map edges need to be less sheer up and down, almost a weather worn slope like if water was running off for years"):
   - **The edge** was a cliff and is now the worn, gullied slope described above. It is about 23° on average, where the cliff ran up to 74°.
   - **To keep clear of the slope,** the bowl in the aft flank, the aft field's bowl, all four pits, six spots and the small craters near the edge were moved in. Two pits were made smaller so the flank's way past them stays at least 870 wide.
   - **Each pit** got its own rock and ore, its layers their own tones, and seams in some layers.
   - **The ore flecks** thrown out are round and sparse. The pebbles on the plains, once 3-unit squares, are round now, lit on one side.
   - All 48 checks still pass.
7. **The look:** the mares were flat, dark discs and are now mottled with ragged edges. The asteroid's spots and vents were unmarked and are now marked. The engines' blast was stronger, and the slabs brighter, than they should be; both are fixed.

## The minimap and lobby picture

The map's minimap is the map seen straight down in 3D, with heights, light and features, the starfield showing through the void. That minimap is what shows in game, and `MapTool install` draws the lobby's picture from it.
1. `MapTool render3d <map> top.png --size 8192x8192 --camera 8192,9216,0,89.9,93635 --models`. The camera is 5.08 map-lengths up (the 36), as for the other maps.
2. `Landfall minimap top.png minimap.png 3231 3123 4961 5070 1024` cuts out the map's corners at the deck's height, projected through the camera. It scales them to 1024 square (unevenly: the map is 32 × 36) and stars the void.
3. `Landfall setminimap <extracted map> minimap.png`, then `MapTool pack`.

The minimap is `minimap_3d.png`. `MapTool info` reads it back as 1024 × 1024 DXT1.

## Pictures

- **The map:** `map_2d.png` (texture, spots and starts), `3d_overview.png`, `3d_ship.png`, `minimap_3d.png`
- **Outside:**
  - `3d_fore_field.png`
  - `3d_bow.png` (the bow lane and the bridge)
  - `3d_flank.png` (two pits, a mare, a bay door's apron)
  - `3d_bay_door.png`
  - `3d_aft_field.png` (the engines, their blast, a bowl with its vent, a pit)
- **The pits, each its own rock:** `3d_pit_iron.png`, `3d_pit_copper.png`, `3d_pit_ilmenite.png`, `3d_pit_gold.png`
- **The edge, worn:** `3d_edge.png` (its slope and gullies by the iron pit), `3d_edge_gullies.png` (side on), `3d_stern_edge.png` (the south edge behind the engines)
- **Close:** `3d_bowl.png`
- **Movement:** `3d_passability_vehicles.png` and `3d_passability_bots.png` (the craters red for vehicles, open to bots)
- **The models alone:** `models_moonrock.png` (boulders, slabs, shards, a strut, a tug and a container for scale)

## To try in BAR

- **The void's rubble:**
  - whether invisible blockers with buried collision boxes leave shots and the D-gun alone;
  - how the pathfinder copes with 1,702 of them;
  - whether units refuse to unload into the void.
- **`voidGround` again:** whether the stars show through the pits and round the edge as in the pictures.
- **8v8 on 32 × 36:** whether the outside lanes (about 2,000 wide round the bow and stern) and the flanks (2,100 to 2,450 of flat ground) are roomy enough, and whether the outside starts get into the fight soon enough.
- **The bowls:** whether bot-only metal in the flanks' bowls is worth the trip.
- **No wind:** as on Meridian.
- **Players:** 16. The map has no start boxes, so draw them in the lobby or play with the fixed starts (0 to 7 port, 8 to 15 starboard).
- **Archive:** 91 MB (294,912 distinct texture tiles). It isn't in git (`.gitignore`). It goes on the release, as the other maps do.
