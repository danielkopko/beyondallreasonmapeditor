# Red Dust Open Cut 1.0: checklist

`red_dust_open_cut_1.0.sd7` ("Red Dust Open Cut 1.0", short name `RedDustOpenCut`, map id 20261104) is a 24 × 24 map for 2v2.

It is an outback open-cut mine, left only days before the war: the machines stand where the crews left them, an ore train waits half-loaded at the loadout, and everything is filmed in red dust.

- **Made by the converter here** (`converter/README.md`): the terrain, texture, detail maps and sky, and **every feature**. That covers the bucket wheel excavator, the haul trucks and excavators, the railway and its train, the ore's way to it, the tanks, the site's buildings and smaller machines, and the outback's plants and rocks.
- **From All That Simmers v1.1.1:** the detail textures and the featureplacer gadget.

The map is **not symmetric**. The west is the plant side and the east the dump side. Between them the pit runs from the middle to the map's south edge, and the railway crosses the north lane. Its fairness is measured instead (see "Checked").

**Not tried in BAR yet.** BAR isn't installed on this machine. Everything below was checked with MapCore's copy of the engine's rules, and the models with MapTool's renderer, not in the game.

**Download:** one file, [red_dust_open_cut_1.0.sd7](https://github.com/danielkopko/BarMaps/releases/download/red-dust-open-cut-1.0/red_dust_open_cut_1.0.sd7), from the public BarMaps release [red-dust-open-cut-1.0](https://github.com/danielkopko/BarMaps/releases/tag/red-dust-open-cut-1.0), with its lobby picture `Red_Dust_Open_Cut_1.0.jpg`. The same files are on [this repository's release](https://github.com/danielkopko/beyondallreasonmapeditor/releases/tag/red-dust-open-cut-1.0) (signed in as danielkopko), and BarMaps' README has its section and `LuaMenu/Images/Minimaps/Red_Dust_Open_Cut_1.0.jpg`. After rebuilding, `red_dust_open_cut.cmd publish -Message "..."` pushes the notes and replaces both releases' files (BarMaps' README section and picture are updated by hand); `red_dust_open_cut.cmd install` copies the map into BAR.

## The layout

| | On the map |
|---|---|
| Teams | 2v2, west against east. Start 0 (2200, 2850) and start 1 (1800, 7100) are in the west; start 2 (10500, 4600) and start 3 (10400, 8500) are in the east. Each team's two starts sit beside their own haul roads |
| The plain | Red outback at 380, nearly level in the bases, on the pads and hardstands, and flat across the roads. Haul roads link the starts, pads, ramp tops and dumps round the pit. A road runs onto a pad and ends at its edge (the pad is the open yard), and no road is painted under a building, tank or structure, nor over a stockpile, a dumped heap, the tailings dam's wall, a breakaway or the magazine's bund (a check) |
| The natural ground | Away from man's works (kept at least 200 to 450 clear of the pit, the roads, pads, hardstands, dumps, dam, railway, conveyor, bases, spots, magazine and breakaways):<br>• **swells:** ±10 over a couple of thousand;<br>• **small mounds:** on about half the cells of a 380 grid, 90 to 230 across, 6 to 18 high, their shoulders under about 17°. They're real heights, so in BAR they slow what crosses them; every vehicle still climbs them. Their tops show paler wind-sorted sand, and spinifex hummocks grow on them;<br>• **dry washes:** meandering pale beds of sand and pebbles with darker banks. The main ones are cut 5 deep, and the fine rills are paint only. They run right up to a road's edge, where the road crosses them flat, as a floodway does. Saltbush grows along them, with the odd ghost gum |
| The pit | From the middle of the map to its south edge: 26.8 million square units inside the rim, about 1.9 times the first build's oval. It isn't round. The old pit, an oval about 5,800 long tilted 14° off north-south, narrows at a waist into a broad southern lobe. The lobe's walls waver, and it is cut back at both corners where it meets the edge. The pit is 370 deep everywhere: eight benches about 46 high, each with a 96-wide berm and a 22-wide face (about 65°, which nothing climbs). The floor is at 10. Two drill rigs stand on a fresh drill pattern at the north end, and an excavator works the lobe's east face with its trucks |
| The lobe's bucket wheel excavators | Two more bucket wheel excavators work the lobe's walls just south of the waist, one on each wall, staggered (the west one north, the east one south) so the floor between them is open for armies to fight across. Each is the north one's model scaled so it stands only just higher than the pit is deep: its mast tops 30 over the rim, about 0.93 of the north one's size. Each stands on the floor in a round bite its wheel has cut into its wall: within its slewing arc the wall is cut back to the floor as far as the wheel reaches, with a steep curved face beyond, and the buckets are just into that face. The crawlers sit in the bite (footprint 17 × 12), so only the counterweight booms reach out over the floor. Indestructible, uncrushable, no reclaim |
| The pool | Green water against the map's south edge, in the pit's lowest corner: the water at 0, its bed at −46, too deep for land units, 0.75 million square units. The first build's lake in the middle of the floor is gone |
| The haul ramps | Four, 160 wide, each down its wall at one grade, then level onto the floor:<br>• **west:** from the north-west rim to the old floor's west side, 3,212 long at 6.5°;<br>• **east:** from the east rim to the old floor's north-east, 3,308 long at 6.3°;<br>• **south-west:** from the lobe's west rim toward the edge, 2,223 long at 9.4°;<br>• **south-east:** from the lobe's east rim toward the edge, 2,014 long at 10.4°.<br>The new ones are shorter and steeper, still well inside the 27° vehicles climb. Each team has two: the west ramps are the west's, the east ramps the east's |
| The south | The pit runs into the map's south edge, so there is no way round its south end: a check proves nothing crosses there. The sides now meet only through the north lane's three gaps, or down into the pit by its ramps and across its floor. Roads 8 and 11 now run to the tops of the new ramps |
| The slide | On the north-east wall, a failed section: one 40° slope from the rim to the floor. Bots climb it; vehicles can't |
| The terrace | At the north end, one bench down and 460 deep. The bucket wheel excavator stands on it, its wheel over the benches below |
| **The railway** | A line due north-south across the north lane, east of the pit's north end (x 6700), on a dead-level ballasted bed. It runs from the north edge to the **loco shed** at its end, 270 short of the pit's rim, where a spare loco stands against the buffer stop. A passing siding runs east of it, joined by turnouts at both ends. Road 7 crosses it on a level crossing |
| **The train** | Three locos at its head by the north edge, heading north. Behind them are 8 loaded wagons, one under the **loadout** being filled, then 5 empty wagons, stopping 160 short of the level crossing. Another 15 empty wagons stand on the siding. In all: 4 locos and 28 wagons |
| **The wall** | Wagons coupled on a north-south line tile into a solid wall, because footprints are squared to quarter turns. So the train, the loadout and the shed wall the north lane off, but for three gaps:<br>• in front of the locos at the north edge, 240 wide;<br>• the level crossing, 616 wide;<br>• between the shed and the rim, 270 wide.<br>The train blocks shots as well as movement. Both teams drive the same distance to the crossing (4,479 and 4,475) |
| **The ore's way** | A **primary crusher** on the ROM pad's north edge: trucks tip into its concrete ROM bin, with a rock breaker over the hopper. An **overland conveyor** on trestles runs north from it to a **transfer tower**, then due east to the loadout's incline. Units drive under it |
| The west (the plant side) | **The ROM pad:** a rounded rectangle 1,240 × 880 where three roads meet. Its three ore stockpiles stand in the gaps between them. A truck backs its load onto one, an excavator stands idle at it, a dozer waits by another, and a wheel loader faces the third. The crusher is at the pad's north edge.<br>**The plant pad:** site offices, rainwater tanks, a diesel tank, utes, a truck, a service truck and a crew bus. Round it are a diesel tank farm, a large water tank, a water tower, a communications tower, the power station (three generator sets), containers and tyres. A power line runs to the crusher.<br>**The tailings dam:** 2,300 × 2,400, a 44-high embankment round a cracked beach and its dried-out decant (cracked mud ringed with salt crust, in a shallow dip: the map has one water level, the pit's pool at 0, so the dam, high on the plain, holds none), with pipe stacked by it. Its outer wall is too steep for vehicles; bots climb it.<br>**Also:** a water cart and a grader by the roads, a windmill, two breakaways |
| The east (the dump side) | **Two waste dumps,** each two 60-high lifts (120 above the plain) with scalloped tip-head edges and a graded haul ramp up. Half of each top is paddock-dumped in rows of truck loads, a few loads missing; the other half is dozed flat, with a dozer left on the north dump.<br>**The workshop:** its heavy-vehicle shed (three roller doors, one open), two trucks on the go-line, a wheel loader, a service truck, a crew bus, offices, containers, tyre stacks and light towers. Behind it are a fuel farm (two large diesel tanks in bunds, two small ones, drums) and a large water tank.<br>**Also:** a water cart and a grader by the roads, a windmill, one breakaway |
| Out on the plain | A third windmill in the south-east, each windmill with a rainwater tank and a trough. The explosives magazine stands in its earth bund in the south-west corner, south of the tailings dam |
| Breakaways | Low mesas, 60 to 70 high, with dark ferricrete caps, white kaolin scarps (42°, bots only) and gullied outlines. Two are in the west and one in the north-east corner |
| Creeks | Two dry creeks with sand beds, ghost gums along them: one down the west edge, and one down the east side, from the east edge in the north-east to the south edge. Roads cross creeks; none runs along one |
| Haul roads | 12 roads and the two dumps' ramps, all graded: none climbs more than 5.7° or leans more than 2.7° across. Road 1 starts at the ROM pad. Road 6 runs straight on into the south dump's ramp, which sweeps round the dump's west end and up onto its top. Each pit ramp's top has a road meeting it in line with the ramp, from outside the pit:<br>• road 7 starts at the west ramp's top, and road 1 joins it just north;<br>• road 8 ends at the south-west ramp's top;<br>• road 11 passes the south-east ramp's top;<br>• road 12 is a short spur into the east ramp's top, making a hairpin with road 11.<br>A road that joins another ends inside it. A ramp's cutting, where it drops below the plain before the rim, shows the same red oxide as the pit's top bench |
| Metal | 30 spots, 74.2 metal/s with a T1 extractor on each:<br>• four at each start (2.0 each);<br>• two forward of each team toward the pit (2.6 each): on the ROM pad in the west, between the workshop and the rim in the east;<br>• one halfway down each of a team's two ramps, beside it (2.8);<br>• six on the pit floor, shared (3.4 each): four on the old floor, two on the lobe's.<br>Each team's own metal is 26.8 metal/s, and the floor holds 20.4. `maxMetal` 4 |
| Geothermal vent | One, on the pit floor |
| Wind and tide | Wind 6 to 18; no tidal |
| Reclaim | **Indestructible, no reclaim, uncrushable:** the three bucket wheel excavators, the train, the loadout, the loco shed, the crusher, the conveyor, the transfer tower, the workshop shed and the track. The walls they make can't be reclaimed away.<br>**Reclaimable:** a truck holds 300 metal, an excavator 600, a water cart 300, a grader 200, a dozer 400, a wheel loader 400, a drill rig 300, a service truck 150, a crew bus 120. Each side has 3,370 to reclaim, and the pit's 4,200 is shared. The tanks give energy (a large diesel tank 600), and the plants a little |
| The outback | 2,034 spinifex, 1,101 saltbush, 490 ghost gums (thickest along the creeks and washes), 83 termite mounds, 33 dead trees, and 126 ironstone boulders (on steep ground and fallen onto the pit's berms). With everything else, 4,085 features. Spinifex, saltbush, the track and the power poles don't block. The plants are kept off the roads, ramps, railway, conveyor, pads, hardstands, spots and bases |

## The features

The ones asked for:
- **Bucket wheel excavators:** one on the north terrace, and two working the southern lobe's walls in round bites cut by their wheels.
- **The train:** diesel-electric locomotives, blue with a white stripe and hazard-striped ends (4); ore wagons, rusted red, loaded with ore (8) and empty (20); track on concrete sleepers (49 sections of 128); a buffer stop; the loco shed at the line's end.
- **Tanks:** large diesel tanks, each in a concrete bund with a ladder and a handrail round its roof (3); small diesel tanks (3); large corrugated water tanks (2); rainwater tanks on timber stands (6); a water tower (1).

And 21 more, for the site's look and to make the lanes more interesting:
1. the heavy-vehicle workshop shed;
2. the primary crusher, its ROM bin and rock breaker;
3. the overland conveyor (28 sections on trestles);
4. the conveyor transfer tower;
5. the train loadout;
6. water carts (2);
7. motor graders (2);
8. track dozers (2);
9. wheel loaders (2);
10. blast-hole drill rigs (2), on a drill pattern painted on the pit floor;
11. service trucks (2);
12. crew buses (2);
13. shipping containers in three colours (6);
14. windmills with their troughs (3);
15. a communications tower, banded orange and white;
16. a power line of timber poles with their wires (10 poles);
17. generator sets, the plant's power station (3);
18. stacks of worn-out haul-truck tyres (3);
19. the explosives magazine in its earth bund;
20. pallets of drums (4);
21. stacks of polyethylene pipe (2).

## The machines' look

One atlas (2048 × 4096, 32 cells) for every model, painted by `converter/Atlas.cs`:
- **Paint:**
  - mining yellow on the trucks, the smaller machines and the generators;
  - orange on the excavators;
  - grey steel and rust on the bucket wheel excavator;
  - blue and white on the locos and the site gear;
  - rust on the wagons.
  - Each colour is worn to bare metal at the edges and filmed in red dust wherever it faces up.
- **Parts:**
  - tyre treads and wheel faces;
  - tracks;
  - lattice bays;
  - windows, grilles and chrome;
  - hazard stripes;
  - the lamps, which glow through texture 2;
  - the buckets, and ore in the loaded bodies and wagons.
- **The site:** corrugated iron, concrete and sun-greyed timber (the power poles, pallets and tank stands). The timber is in the cell that used to be the test cell.
- **The outback:** ghost gum bark, sage leaves, spinifex, saltbush, ironstone and termite mound earth.

No makers', railways' or companies' names, marks or model numbers anywhere.

Triangles: the bucket wheel excavator 10,224; the loadout 3,862; the crusher 2,382; the communications tower 2,450; a loco 1,334; a wagon 704 to 972; a truck 1,206 to 1,552; an excavator about 1,320. Everything else is under 2,000.

## Checked (`check.txt`, on the built map's own heights, round every blocking feature)

Each blocking feature counts its footprint (16-unit footprint units, turned to its facing) and 8 more round it. They block 2.1% of the map. 33 checks, all passed:
- **Getting about** (vehicles: TANK3, 27°; bots: BOT3, 54°):
  - From the north-west start, vehicles reach all four starts; 84.9% of the map is open to them.
  - Vehicles reach every metal spot and the vent.
- **Each of the four ramps alone** takes vehicles to all six floor spots.
- **With all four ramps shut**, vehicles can't get down the benches, but bots get down by the slide.
- **With the slide shut too**, nothing gets down the benches.
- **Fair shares:** driving distance from each team's starts, round the features, within 10%:
  - to the nearest pit-floor spot: west 5,991, east 5,827 (3% apart);
  - to all six pit-floor spots, on average: west 6,816, east 6,695 (2%);
  - to their own first ramp's spot (the old pit's): west 4,643, east 4,488 (3%);
  - to their own second ramp's spot (the lobe's): west 4,425, east 4,099 (8%);
  - to the enemy's nearest start: 8,988 both ways;
  - their own metal: 26.8 metal/s each;
  - machines to reclaim on their side: 3,370 metal each;
  - level ground to build on within 900 of their starts: 19,825 and 19,834 squares.
- **The north lane, across the railway.** With the pit and everything south of its middle shut, only the north lane joins the sides:
  - vehicles and bots cross it, through the train's gaps;
  - with the three gaps shut too, the train, the loadout and the shed wall it off, and nothing crosses;
  - each gap on its own lets vehicles across: the north edge (240 clear), the level crossing (616) and between the shed and the rim (270).
- **The south edge:** with the pit and everything north of its middle shut, neither vehicles nor bots get from the west's southern start to the east's. The pit leaves no strip along the edge.
- **Roads:** no road's paint (out to its windrows, 86 from its centre line) lies under any building, tank, structure or piece of site clutter. Nor does any lie over a stockpile, a paddock-dumped heap, the tailings dam's wall, a breakaway or the magazine's bund. Every model's footprint and every one of those mounds and walls is sampled on an 8 or 16 grid. Vehicles may stand on a road, since they drove there; the track crosses road 7 on purpose.
  - **Every road leads somewhere:** each end meets a start, a pad, a hardstand, a ramp's top, a dump's top, the dam or another road.
  - **A road meets every haul ramp's top** (within 130).
  - **No road runs into the pit:** each road's edges, 64 out from its centre line, stay outside the rim.
  - **No road runs along a creek's bed,** only across it: no more than 450 inside a creek's 150-wide bed for each crossing.
  - **Every road is graded,** on the built heights: none climbs more than 7° along it (the steepest is 5.7°, up a dump's ramp) or leans more than 3° across it (the most 2.7°, where road 5 meets a dump ramp's foot).
  - `OpenCut roads` prints the survey behind these: each road's length, where its ends lead, its steepest and most leaning points.
- **Standing true:** every machine, building, tank and piece of site gear stands near level, the ground rising no more than 12 under its footprint (the most 7.5). The tipping trucks are left out, since they back their tails onto a stockpile or over a dump's edge.
- **Metal spots and starts:**
  - No blocking feature is within 40 of any spot.
  - Every spot and the vent is level: a 0.0 rise over its footprint.
  - Every start is on dry, nearly level ground.

The driving distances are 8-connected, with diagonals counting √2 and no corner cutting.

Also checked:
- **Feature names:** every name placed is one the map defines (`MapTool items` lists all 56 as placeable).
- **Rotations:** written in the engine's units (65536 a turn).
- **Lobby starts:** `MapTool info` reads the four starts above from the built map.
- **Repeatability:** a second build from the same sources gives the same `run.txt` and `check.txt`.
- **The models:** on flat ground, before any were placed (`models_*.png`): their scale beside the trucks (a wagon about 45 high, a loco about 60), and that rolling stock runs along its track at its heading.

## Made better on the way

1. **The pit:**
   - At first its floor was too large for its depth, so it was deepened: the plain was raised to 380, with eight benches.
   - The ramps first ended in the lake and were turned to reach the floor.
2. **The east ramp was blocked halfway** by a parked truck. A footprint is squared to the nearest quarter turn, so a truck across a diagonal road closes it. No machine now stands on a ramp. Some stand on or beside the plain's roads where they cross the pads, as at the plant's offices, and vehicles still get round them.
3. **Fairness:**
   - The distances were first counted 4-connected, which charges the tilted pit's diagonal roads up to 41% too much. They are now true driving distances.
   - Then the north-west start was moved about 700 closer to its ramp, from (1600, 2500) to (2200, 2850), with its spots and road. That brought the floor and ramp distances within 4%.
   - An empty truck parked at the top of each ramp evened the reclaim (it had been 1,200 west against 1,500 east).
4. **The look, after the first renders:**
   - **Dumps:** they were grey saucers. Now they are red oxide waste with scalloped edges, paddock-dumped on top.
   - **Breakaways:** they were rings that read as craters. Now they have gullied outlines and dark caps.
   - **Pads:** ellipses became rounded rectangles.
   - **Tailings:** the decant pond's blurred edge is now crisp.
   - **Plants and rocks:** gum leaves went from near-black to sage, and the boulders from bright orange to dull ironstone.
   - **Ramps:** the in-pit ramps' feet fade into the floor instead of ending in a painted tongue.
   - **Dump ramps:** now painted as haul roads.
   - **The pit's wall bands** were softened.
5. **The railway and the new features (the second round):**
   - The line was laid due north-south so the wagons tile into a wall with no slits. The wagons' and locos' footprints were set to their bodies (7 and 16 long), so wagons coupled at 120 leave no gap.
   - The rail bed is dead level, so the train doesn't float or sink on the rolling plain.
   - The workshop's offices and diesel tank were moved to make room for its shed.
   - The scrap tyres first showed wheel rims, and now show plain rubber.
   - The drill rigs first stood against the pit's north wall, and were moved onto the floor.
6. **Roads over things a road wouldn't cross (at your request).** A new check found 11 places where road paint ran over something:
   - **On the pads:** site offices and rainwater tanks stood on the roads that crossed the plant and workshop pads.
   - **The ROM pad:** the north-west road ran straight over one stockpile, and another road clipped a second.
   - **The north dump:** the ramp road ran onto its paddock-dumped heaps.
   - **The tailings dam:** the road to it ended up its wall.
   - **The power line:** a pole stood on a road.

   **The fixes:**
   - Roads now end at the edges of the pads and hardstands, for the ground's colour and its detail textures alike.
   - The stockpiles moved into the gaps between the roads, and the ROM pad's truck, excavator and dozer moved with them. The tipping truck now backs its load onto its pile.
   - The heaps keep 190 clear of each dump's ramp where it comes onto the top.
   - The road to the dam stops short of its wall.
   - A rainwater tank at the workshop moved onto the pad.

7. **The pit, twice the size (at your request).** It now runs to the south edge, with character instead of an oval:
   - the old pit narrows at a waist into a broad lobe with wavering walls, cut back at both corners by the edge;
   - the lake moved from the middle of the floor to a pool against the edge;
   - the lobe has two ramps of its own, one for each team, a spot beside each, two shared floor spots, and an excavator with its trucks at its east face;
   - roads 8 and 11 were cut back to the new ramps' tops, and the third windmill, the explosives magazine and the south breakaway moved off the lobe's ground.
   - **The south-east ramp:** its first start left the east 28%, then 12%, closer to its spot than the west to its own. The lobe was moved west and the ramp's start moved south, bringing it to 9%.
   - **The north is untouched:** the train, the shed, the loadout, the conveyor, the workshop, the tanks and the bucket wheel excavator stand exactly where they did (their lines in `run.txt` are the same), and the shed is still 270 short of the rim.
8. **Road touch-ups (at your request).** A survey of every road found:
   - **Roads 1 and 7:** their junction was just inside the pit's west rim, so road paint ran onto the benches, at up to 56° and leaning 42°.
   - **Road 8:** it ran 430 into the waist's wall and ended at nothing.
   - **Unmet ramp tops:** the east ramp's top had no road (the nearest 377 away), and the south-west's was 384 from road 8.
   - **The east creek:** it ran beside road 11 for 620 and under road 6 and the south dump's ramp, where it made a 14° dip in road 6.
   - **Side approaches:** two roads met ramp tops from the side and ran over the ramps' cuttings, at up to 18° (road 1) and 14° (road 11).

   **The fixes:**
   - Road 7 now starts at the west ramp's top, and road 1 joins it just north.
   - Road 8 runs to the south-west ramp's top.
   - Road 11 makes a hairpin with the east ramp's top, by a short spur (road 12), and runs on to the south-east ramp's top.
   - The east creek now comes from the south edge and drains into the lobe.
   - Roads now meet ramp tops in line with the ramps.
   - Road 1 ends inside road 7, not past its middle, so the junction has no knot.
   - The south dump's ramp shows waste rock instead of white scarp streaks, and the pit ramps' cuttings are red oxide instead of white.
   - Five new checks keep it so.
9. **The three paths you marked (at your request):**
   - **The sharp bend:** road 6 met the south dump's ramp in a tight V. Road 6 now runs straight on into the ramp, which sweeps round the dump's west end and up onto its top.
   - **The east creek:** it was the path closest to the map's edge. It now runs down the east side, from the east edge in the north-east to the south edge, long like the west's.
   - **Road 1:** its stretch past the ROM pad's stockpiles, out to the north-west start, is gone; road 1 now starts at the pad.
10. **Two bucket wheel excavators in the lobe (at your request).**
   - They were first 1.5 times the north one, back to back in the middle of the floor, where they read as one machine.
   - At your next request, they are only just higher than the pit is deep, and each stands in a round bite its wheel has cut into its wall.
   - They are staggered so their counterweights don't meet and the floor between them is open.
   - Every ramp still reaches all six floor spots on its own.
11. **The tailings dam's water (at your request).** Its painted turquoise pond looked like water, but the map has one water level, the pit's pool at 0, and the dam stands 40 over the plain. It is now a dried-out decant: a shallow dip of cracked clay-brown mud ringed with white salt crust.
12. **A beautify pass (at your request).** The plain was nearly flat. It now has swells, small mounds (real heights) and dry washes (pale beds, the main ones cut a little) on the natural ground, kept clear of everything man-made.
   - **Roads:** they're flat across, as the plain is flattened under them fully now, not 70%.
   - **The check that roads don't lean** had its threshold reported wrong. It is now 3°, and it was tested at 4°.
   - **Standing true:** a new check keeps every machine and building level. It moved a light tower off the north dump's flank, onto its top, and the ROM pad's loader and idle excavator off their piles' slopes.
   - **Rocks:** they keep further from roads.

## The minimap and lobby picture

The map's minimap is the map seen straight down in 3D, with heights, light and features included, not the flat texture. That minimap is what shows in game, and `MapTool install` draws the lobby's picture from it.
1. `MapTool render3d <map> top.png --size 8192x8192 --camera 6144,6144,0,89.9,62423 --models`. The camera is 5.08 map-widths up, as for the king maps, so high and low ground sit only slightly out of place. The pit is 370 deep and the dumps stand 120 up. The map covers 1,958 pixels of the render, square and centred.
2. `OpenCut minimap top.png minimap.png 1024` cuts the map out of the render (the border is the renderer's background) and scales it.
3. `OpenCut setminimap <extracted map> minimap.png` puts it into the SMF.
4. `MapTool pack` packs the map again.

The minimap is `minimap_3d.png`. `MapTool info` reads it back as 1024 × 1024 DXT1. The railway, the conveyor, the sheds and the tank farms show on it.

## Pictures

- **The map:** `map_2d.png` (texture, spots and starts), `3d_overview.png`, `minimap_3d.png`
- **The pit:**
  - `3d_pit.png`
  - `3d_pit_south.png` (the waist, the lobe and its two ramps, the pool)
  - `3d_pool.png` (the pool against the south edge)
  - `3d_pit_wheels.png` (the lobe's two bucket wheel excavators in their bites)
  - `3d_pit_wheel_west.png` (the west one's wheel in its round cut)
- **The natural ground:**
  - `3d_natural_ground.png` (washes, mounds and swells in the north-east)
  - `3d_natural_low.png` (mounds near ground level, by a windmill)
  - `3d_pit_floor.png` (the old floor's machines and spots, and its two ramps' feet)
  - `3d_drills.png` (the drill rigs on their pattern)
  - `3d_bucketwheel.png` (on its terrace)
- **The railway:**
  - `3d_railway.png` (the train, the loadout, the siding)
  - `3d_loco_shed.png` (the shed, the level crossing)
  - `3d_conveyor.png` (the crusher, the conveyor and the transfer tower)
- **The sides:**
  - `3d_rom_pad.png` (stockpiles, the crusher, a truck tipping)
  - `3d_plant.png` (tanks, the water tower, the comms tower, generators, the power line)
  - `3d_tailings.png`
  - `3d_dump.png` (paddock dumping)
  - `3d_workshop.png` (the shed, the go-line, the fuel farm)
- **Movement:** `3d_passability_vehicles.png`
- **The models alone, on flat ground:**
  - `models_bucketwheel.png`
  - `models_train.png`
  - `models_loco_shed.png`
  - `models_ore_handling.png` (the loadout, the crusher, the transfer tower, conveyor sections)
  - `models_machines.png`
  - `models_tanks_and_structures.png`
  - `models_site_gear.png`
  - `models_outback.png`

## To try in BAR

- **The models in the engine, first of all:**
  - Whether BAR's shader shows the dust and the worn paint as MapTool's renderer does.
  - Whether the lamps glow at night.
  - How 3,738 features perform, with a 10,000-triangle excavator and about 37,000 triangles of train and track.
  - **Collision boxes:** the bucket wheel excavator's footprint is only its base (18 × 13), but its collision box is its whole solid box, so shots may stop at the box's corners over the boom. The lobe's two, on the floor, have their whole length (wheel to counterweight, about 1,090) in their collision boxes, which reach out over the floor. The same goes for the loadout's and the crusher's inclines, and the overland conveyor, which units drive under but shots may hit.
- **The railway:**
  - whether a wall with three gaps makes the north lane a good fight, or too slow a one;
  - whether the 616-wide level crossing is the right size;
  - how BAR turns the wagons' footprints, which the checks take as squared to quarter turns.
- **The asymmetry:**
  - **The dumps:** the east's dumps are high ground (120 up, with ramps).
  - **The tailings dam:** the west's dam is a raised flat that, by the passability picture, only bots get onto.
  - Whether either gives an edge.
- **The pit:**
  - whether the shared floor (20.4 metal/s and the vent) draws fights, now that the floor is also the only way between the sides in the south;
  - whether the slide gives bots a useful back way in;
  - whether 160-wide ramps are wide enough, and whether the new ramps' 10° matters to slow units;
  - whether closing the south (no route round the pit) makes the north lane too important.
- **Reclaim:** whether 3,370 metal of machines a side, and 4,200 in the pit, is right.
- **Players:** 4 (2v2). The map has no start boxes, so draw them in the lobby or play with the fixed starts (0 and 1 west, 2 and 3 east).
- **Archive:** 95 MB, because every one of the 147,456 texture tiles is distinct. It isn't in git (`.gitignore`). It goes on the release, as the other maps do.
