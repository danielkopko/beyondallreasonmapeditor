# BAR Map Editor — Build Plan

A Windows editor for Spring/Recoil maps (`.sd7` / `.sdz`), as used by Beyond All Reason. It opens existing maps, edits them, and repackages them.

**Required features:** change terrain height · paint texture / colour · move metal spots · move trees and other items

Test map: `acidicquarry_5.17.sd7`. **Never modify this file.** All work happens on copies.

## Progress

| Step | Status | Notes |
|---|---|---|
| 1. Projects + reference parser | ✅ Done 2026-10-14 | Builds offline with no package sources; 2/2 tests pass; editor window starts and closes cleanly |
| 2. Archive layer | ✅ Done 2026-10-14 | 14/14 tests pass; `.sd7` and `.sdz` repacks match 7-Zip's listing of the original (paths, sizes, CRCs; attributes too for `.sd7`) |
| 3. SMF read/write | ✅ Done 2026-10-14 | 23/23 tests pass; unedited save is byte-identical; every header value, section position and section hash matches the Python parser; edited files with resized sections re-read cleanly in both C# and Python |
| 4. DXT codec + DDS | ✅ Done 2026-10-14 | 39/39 tests pass; all 9 map textures load and save byte-identical; decoded pixels match an independent Python decoder exactly; 6144² full re-encode in about 0.5 s (Debug, 16 threads); region edits rewrite only the touched blocks at every mip level |
| 5. SMT tiles + minimap | ✅ Done 2026-10-14 | 51/51 tests pass; `.smt` saves byte-identical; sampled tiles decode exactly as an independent Python decoder does; editing one tile changes only that tile's 680 bytes; shared tiles are copied before editing; tile files are found by the engine's own rules (checked against its source); an edit saved to disk reloads correctly; full texture re-encode at 61.4 dB in 1.3 s |
| 6. Normal map + splat research | ✅ Done 2026-10-14 | Findings below (Step 6). New rule 7: map `.dds` files are stored upside down. 54/54 tests pass, including orientation regression tests |
| 7. mapinfo.lua reader + patcher | ✅ Done 2026-10-14 | 69/69 tests pass; unedited save is byte-identical (encoding and byte-order mark kept); an edit changes only that value's text; missing keys are inserted in the table's own style; overrides from `mapconfig/mapinfo/*.lua` are detected; the temporary pattern matching is gone; `MapTool set` edits values and saves a new archive |
| 8. Items, start positions, grass | ✅ Done 2026-10-14 | 80/80 tests pass; rotations kept in the engine's range; the 56 rocks in `set.lua` read and save byte-identical; an added rock is written in the file's own line format; move, rotate and remove change only their own text; SMF features add, move and remove; feature catalog; grass painting; start positions move, add and remove (add then remove restores the file exactly) |
| 9. Metal spots | ✅ Done 2026-10-14 | 89/89 tests pass; spot resizing; spots match a Python port of BAR's spot finder; income formula confirmed from engine source (2.30 metal/s per Acidic Quarry spot); `MapTool edit ... metal-move` moves one spot, leaving the other 23 and total metal unchanged; layouts BAR would misread are refused; `edit` command and `MapEditSession` added |
| 10. Height model + normal-map rotation | ✅ Done 2026-10-14 | 95/95 tests pass; decoding matches the engine exactly (÷65536, mapinfo overrides); an in-range edit leaves every other raw value untouched; widening the range keeps untouched terrain within half a step; the normal map is rotated only near the edit; `height-*` edit operations |
| 11. PNG import/export | ✅ Done 2026-10-14 | 102/102 tests pass; the PNG reader decodes an independent Python encoder's files exactly (all filters, palette + transparency, 2/16-bit grey, colour key, interlaced); exporting then re-importing every layer leaves every file byte-identical; edited imports change only what differs; `texture-paint` and `import:` operations; `MapTool export` |
| 12. Window, viewport, undo | ✅ Done 2026-10-14 | 118/118 tests pass; undo/redo restores every store exactly (SMF, tiles, mapinfo, featureplacer, heights, texture, splat); renderer tests: 1:1 texture view equals the texture, zoomed-out view matches an independent area average, splat/normal views are in map orientation, pyramids after edits and undo equal a fresh build, the normals view refreshes after saving; a full 1600x1000 frame renders in 20-50 ms; the editor's headless render equals MapCore's pixel for pixel; `MapEditor --selftest` passes 20/20 (open, undo/redo, background layer load, Save As refusals, save, reopen, close deletes the workspace) |
| 13. Height tools | ✅ Done 2026-10-14 | 123/123 tests pass; every edit operation is checked on its own: recorded for undo, redraws the view, and undo restores every store exactly; the editor's operations work in a comma-decimal locale; re-importing a heights export changes nothing, even with unsaved edits; editor self-test 41/41, driving the real mouse handlers: a 500-unit stroke makes 21 dabs and one undo step, a stroke cut off by losing the mouse closes its step, right-drag pans while a tool is active, Ctrl+click picks a height, set height / flatten / noise / sea-level preview / whole-map shift / heights PNG round trip |
| 13b. 3D view | ✅ Done 2026-10-14 | 131/131 tests pass; colour and start-position edits don't redo the shadows, moving the sun or the previewed sea level does; a patch painted at an asymmetric point shows where it projects and not at its mirror images, from three directions; a raised mound hides the ground behind it; looking straight down matches the 2D view (mean difference 4.4 vs 27.9 upside down); projected points show their own ground; a tall pillar shades the side away from the sun; local shadow updates equal a full rebuild; 1600x1000 still frame about 130 ms, moving frames about 15 ms; editor self-test 51/51 through the 3D view's real mouse handlers (left drag only rotates; right drag keeps the grabbed ground within 3 px of the mouse; the wheel only zooms; losing the mouse ends a drag), a height edit shows in 3D and undo restores the frame exactly, Look at 2D view, closing the map closes the 3D view |
| 14. Texture and colour tools | ✅ Done 2026-10-14 | 136/136 tests pass; tint keeps brightness and takes the colour; adjust brightens, desaturates to grey and turns the hue; clone copies its source (mean difference 0.4 after re-encoding); splat paint lands where it is painted (not mirrored: the file is upside down), survives save and reload there, and undo restores the file exactly; the splat view after painting and undo equals a fresh build; every new operation passes the undoable-and-reported check; editor self-test 66/66 (paint, Ctrl+click colour pick, clone needs an Alt+click source and then stays aligned, the splat tool shows the splat layer, colour edits are one undo step and undo shows the old value, water rows marked as overridden); panel layouts checked by drawing them hidden |
| 15. Metal tools | ✅ Done 2026-10-14 | 142/142 tests pass; the look's size is measured from the splat map (48 units: the molten-metal channel and the texture patch); a 32-unit nudge moves the blob's centre 31.5 units and keeps its weights (1.0 apart); a far move carries the look whole (2.4 apart) and leaves the old place matching its surroundings (163 -> 3 above its rim); move with look undoes and redoes exactly, also on shared tiles (a test that makes them); the extractor check follows the engine (BAR armmex: height within 23.1 over a 4x4 footprint); editor self-test 79/79 (select, drag cut off by losing the mouse, red ghost and refused drop, drag-and-drop with look as one undo step, add, Delete, income and size) |
| 16. Item tools | ✅ Done 2026-10-14 | 147/147 tests pass; group moves are all-or-nothing (one item leaving the map refuses the move); items are addressed exactly (store, list, index) and removal keeps the rest in order; rotation in degrees, buildings by quarters; grass painted on a map without a grass map gains the section and its header, read cleanly by the Python parser; 1,000 items: 2.8 ms per add, 13 ms per frame; editor self-test 96/96 (box select, a group drag as one step changing only the moved items' lines in set.lua, drag cut off by losing the mouse, Ctrl+click and Delete, turning, place, a scatter stroke of 20 rocks none closer than the spacing, grass stroke, start-position drag) |
| 17. Updates and checks on save | ✅ Done 2026-10-14 | 155/155 tests pass; a 300-dab paint stroke keeps 2.4 MB for undo and undoes exactly; a problem already there when the map was opened is a note, a new one a warning; widening the range warns about this map's water-level options; the unedited map raises no check at all; an item off the map, a start off the map (errors), an unknown feature name and a start under water (warnings), a wider height range (note) are each caught; a redrawn bit of minimap under water matches the map's own minimap at 34.7 dB in its style against 26.7 dB from the bare texture; saving redraws the minimap where the ground moved and nowhere else; editor self-test 97/97 (Save As refuses an item off the map and says why) |
| 18. Save As and packaging | ✅ Done 2026-10-14 | 157/157 tests pass; mapinfo.lua is read straight from an .sd7 or .sdz (any case) without extracting; another archive claiming the same name and version is found in a maps folder (junk files skipped); copying never replaces a file unasked and leaves no partial file; editor self-test 100/100 (Save As copies into a maps folder holding the original; a second copy and a same-named map are not copied without asking) |
| 19. Robustness | ✅ Done 2026-10-14 | 165/165 tests pass; a session replayed from its journal matches the live one byte for byte (scatter, a spot's splat erased and then moved with its look, a paint stroke, undo/redo, an import whose file was then deleted, a save, edits after it), undo history included, and fails without priming; a torn last line is ignored; orphaned records are found only once no editor holds their lock; only a written archive counts as saved; a second map built from scratch (non-square .sdz, two tile files, SMF trees, no featureplacer/splat/normals, metalmapTex variant) opens, draws, edits and saves (Python decodes both tile files the same); editor self-test 110/110 (a simulated crash is recovered with undo working; a second crash after recovery; a Save As that fails while packing is still offered for recovery) |
| 20. Documentation | ✅ Done 2026-10-14 | `README.md`: requirements, quick start, keys, each tab, import/export, save checks, crash recovery, MapTool with a worked example (run as written), format notes, where files are kept, known limits, building and tests; figures measured (unpacked size, memory when opening) |
| 21. Ramp tool | ✅ Done 2026-10-15 | 181/181 tests pass (with who-can-climb: BAR's movement classes and the engine's slope rule, checked against slope-map cells of built ramps, including where a ramp meets the ground); a ramp is a straight slope between its two heights (exact well inside it), every vertex beyond its edge is untouched, edge vertices lie between the old ground and the ramp; a ramp at 30° runs along its own axis; auto heights are the ground at the ends; the auto edge grows with the height step; the texture and splat map at each end match the ground beyond it, nothing past the fill's feather changes, and without fill only the heights change; bad ramps are refused; it works on the synthetic map without a splat map; undo is exact and it replays from the journal; editor self-test 121/121 (place, stretch by an end handle, widen by the side handle, move, turn with E, a cut-off drag changes nothing, the heights follow the ground, Apply ramp is one step halfway down at its middle, undo removes it, another tool hides it) |
| 22. Passability in the 3D view | ✅ Done 2026-10-15 | 187/187 tests pass; hovercraft, vehicles and bots: every square agrees with the engine's rule (hovers on water whatever the slope; 33°, 27°, 54°; land units stop 20 deep); the sea-level preview and edits update it; the 3D tint is exact; editor self-test 126/126 |
| 23. Ramp look: rock to dirt, rough sides | ✅ Done 2026-10-15 | 192/192 tests pass; rough sides stay between the old ground and the ramp with smooth ends; rock and dirt colours from the map's own cliffs and banks (only colour taken, the ground's own detail kept); the same op gives the same bytes; undo exact; editor self-test 130/130 |
| 51. Meridian Landfall: an 8v8 map, Starship Meridian landed on an asteroid | ✅ Built 2026-10-25, not yet released | At your request ("scale the map out so the ship is the same, but it has landed on an astroid that gives some connecting moonrock terrain where the ship is the centre and there is areas outside to also fight, use the void to create pits that cannot be traverese except by air. think moon, mini to medium craters, no ramps into our out, limit to bot access … the edges of pit walls should appear as though rocks have blasted through to make the void. no smooth edge lines apart from the ship", a new map, for 8v8), planned in `out/step51-meridian-landfall/DESIGN.md` with a to-scale drawing, then built at your go-ahead as recommended ("invisible rubble on the void floors, hangar bay doors, 32 x 36, wind 0"). Engine facts checked first in Recoil's source: BAR's all-terrain bots climb 90° (only water deeper than 20 stops them), `voidWater` stops water being drawn, and a feature's `blocking` makes it collidable with a collision volume of its own. **The ship is Meridian's own:** a new converter (`converter/`, Landfall) compiles Meridian's layout, heights, ship paint, placement and checks from step 50 unchanged and moves the ship by (2048, 2048), raised 400 (deck 640). Its hangars' outboard walls open in 700-wide bay doors onto level aprons. Round it, a moonrock plain at the deck's height. Each side has 4 outside starts on dark, ragged mares (16 starts: port 0-7, starboard 8-15); 76 small craters and 3 bowls a side, walls 38-45° (bots in and out, vehicles and hovers never; amphibious tanks climb like bots), no ramps, rich metal or a vent on the bowls' floors; and 4 void pits a side: shattered slab rims, 74° jagged walls cut into the void, so the broken faces drop into the stars. The asteroid's edge is ragged and void beyond. Only the ship has smooth outlines: the craters waver, and the pits and the edge are broken into wedges. 1,354 invisible indestructible blockers (128 × 128, collision boxes buried 600 under the ground) cover the void, so no ground unit crosses it, spiders included, and nothing builds or unloads there. The outside lanes run round the bow and the stern. Features: the ship's 191, 942 moonrock (faceted shards and slabs, four sizes of boulder) on a second atlas, 6 landing struts, cargo at the aprons. Look: regolith with micro-craters, ejecta and rays, fractured rims and cracks, landing soot, the engines' blast, tracks from the bay doors. 90 spots (72.0 metal/s a team, 54.8 contested, 14.4 in the bowls), 7 vents, no wind. 48 checks pass on the built map's own heights: the ship matches Meridian's but for the bay doors and the walls by the bridge-level floors; halves; reach; each of the seven lanes alone; all seven shut stops all but spiders; no ground class into the void, spiders included; none but spiders onto the ship's walls; craters bots-only; bowl walls in the band; no circular outline; doors, rooms, mares, aprons; standing true; spots. The new walls check found a flaw in Meridian's ship: its bulkheads beside the upper bridge ramps are capped below the ramp, so vehicles can drive off the ramp onto the bow's hull top (they can't cross that way). It is fixed here: walls within 48 of a floor above 280 stand 60 over it, every other wall Meridian's. **Starship Meridian 1.0, released, still has it.** Then, at your request ("map end and pit edges and geometry needs more work, there can be alot more detail put into the shapes and textures to make it look natural, rectanguar extusions are not seen much"), the edge and pits were reworked. Their rims were flat-topped plates at stepped heights; they are now ridged, cracked rock with hummocks of ejecta. Their walls were one straight plunge; they now fall in three steps of wavering width (the edge's into buttresses and gullies). Their outlines were straight-sided wedges; they are now fractal (39 harmonics; eight octaves). The texture is layered basalt, with dust on the ledges, cracks at two scales and stones with lit sides and shadows, and only broad tones on the steepest faces, where the cast-down texture would smear into stripes. The box slabs and pyramid shards became faceted lumps. Then, at your request ("the pits need more texture and a difference, add some ore seems into the layers and the map edges need to be less sheer up and down, almost a weather worn slope like if water was running off for years"), the edge became a slope worn down over ages: from a rounded shoulder 360 to 480 inside its line, about 23° on average where it had been a cliff of up to 74°, gullied straight down and rilled near the top. The pits, the aft bowls, six spots and the small craters near the edge were moved in to stay clear of it. Each pit a side is now a different rock with its own ore: iron with rust-red seams, copper with green, ilmenite with glinting silver, anorthosite with gold. Their walls are layered in their own tones, with seams in some layers, and round ore flecks are thrown out. 48 checks still pass. Built in about 80 s plus MapTool's edit; 91 MB. Minimap and lobby picture straight down in 3D (5.08 map-lengths, an 8192 render, cropped by the camera's projection). `CHECKLIST.md` has the layout, features, checks and what was made better on the way. Not yet released: `meridian_landfall.cmd publish` at your go-ahead. Not tried in BAR |
| 50. Starship Meridian: a 4v4 map that is one whole starship, its ship modelled here | ✅ Done 2026-10-24, released | At your request ("plan out a full map spaceship design, it will be a sycetrical map, hangers, control rooms, power rooms, etc. the map is for 4v4"), planned in `out/step50-starship-meridian/DESIGN.md` with a to-scale drawing (`design/layout.svg`, from `design/layout.py`), then built at your go-ahead with no wind ("wind zero"). 24 × 28, the bow north, port against starboard: the starboard half is the port half's mirror image across the centreline, heights and features alike. Each team's half has two columns of rooms on a deck at 240 beside its own passage: the forward and main hangars (back seats, outboard) and the crew deck and cargo hold (front seats, inboard), plus a weapons bay, power room (its own geothermal vent), armory, thruster bay and machine shop. The shared centreline holds the bridge (raised to 380, a ramp up from each passage, a grand stair for bots into the CIC), the CIC round its holo-table, the main reactor (its core in a sunken well, a ramp down from each crossing door, two vents on the well's floor), the shield generator and the engine room: five crossing lanes, joined bow to stern by spine doors. Walls are terrain at about 70°, which only spiders climb; doors are at least 500 wide, crossings 700. 46 metal spots (38.4 metal/s a team, 30.8 shared) and 4 vents. Space is ground at 20 that the engine never draws (`voidGround`: the texture's alpha 0 there, checked in the engine's ground shader), so a starfield sky shows, there's no water to build ships on, and no ground unit but spiders gets there. MapCore gained what that needed, with tests: the 3D renderer draws the sky where a `voidGround` map's texture is transparent, and a texture update can keep its alpha. A new converter (`converter/`) models and paints 38 models on one atlas: dropships, fighters, cargo shuttles, tugs, fuel carts and service towers; consoles, the command chair, the holo-table, the viewport and a sensor dome; the reactor's core, capacitor banks, transformers and pipe runs; the shield emitter; the drive core, coolant towers, fuel tanks and six main engines hanging over space; bunks, mess tables, lockers, med pods and planters; weapon racks, munitions and missile carts; containers, pallets, crates and a loader; a repair gantry and benches; turrets, an antenna and dishes on the hull. No franchise marks. A voxel test of each model against its mirror image found lopsided wings, lumps, a rack and the shield's pylons, all fixed, so only the pallet needs a twin. 191 features, placed along the rooms' walls, clear of doors, metal and starts. 30 checks pass on the built map's own heights round every blocking feature: the halves match; every start, spot and vent is reached; each crossing alone lets vehicles across and with all five shut no ground class crosses but spiders; no ground class reaches space; doors are wide and clear; each start room keeps 95-97% of its floor free and level; every feature stands level. The front starts are 5,114 apart by the shortest drive. The renders found a crater in the bridge's forward wall at the centreline (the walls there rose from the starboard floors before their heights were mirrored in), fixed; the hull's noisy checker became staggered plates with hatches and vents. The minimap and lobby picture are the ship straight down in 3D, cropped by the camera's projection (the map is 24 × 28) with the starfield where space is. Built in about 25 s plus MapTool's edit; 37 MB. `CHECKLIST.md` has the layout, the features, the checks and what was made better on the way. Released at your go-ahead as `starship-meridian-1.0` here and in BarMaps ([download](https://github.com/danielkopko/BarMaps/releases/download/starship-meridian-1.0/starship_meridian_1.0.sd7)) by `starship_meridian.cmd publish`, with BarMaps' README section and lobby picture. Not tried in BAR |
| 49. Red Dust Open Cut: a 2v2 outback open-cut mine, its machines modelled here | ✅ Done 2026-10-24, released | At your request ("the next map design is a non-symetric open cut mine, a few detailed features that will need to be made are the bucket wheel excivator. the dump trucks and excavator. color like aussy outback mines. The mine is your own design layout, with a open cut mine and the 2 sides that have some flat areas, the mine should be like an abandonded site as if the people only left right before the BAR war starts", then "we can go larger 24x24 for the mine map"). `out/step49-red-dust-open-cut/`: 24 × 24, west against east, **not symmetric**. In the middle is the pit: an oval of eight benches 370 deep, its walls banded from red oxide through kaolin and clays to fresh rock. It has a green lake in the sump, two haul ramps spiralling down (one to each side, 6.5° and 6.4°), a slide on the north-east wall that only bots climb, and a terrace at the north end for a bucket wheel excavator. The west is the plant side: the ROM pad with stockpiles, the plant pad, and a tailings dam with its pond. The east is the dump side: two terraced waste dumps, paddock-dumped on top, and the workshop's go-line. Both sides have breakaways, creeks with ghost gums, spinifex and saltbush. A new converter (`converter/`, grown from Starfall Marsh's) models and paints every feature on one 2048 × 4096 atlas: the bucket wheel excavator (masts, lattice booms, a 14-bucket wheel, about 10,200 triangles); haul trucks (empty, loaded, tipping); excavators (working, idle); utes; lighting plants (still lit); site offices; fuel tanks; the outback's plants, termite mounds and boulders. All are in mining yellow and orange with red dust on everything, and no makers' marks. The machines stand where the crews left them, never on a ramp. Metal: four spots at each start (2.0), two forward of each team (2.6), one down each team's own ramp (2.8), four on the floor shared (3.4); one vent on the floor. As the map isn't mirrored, its fairness is checked: each team's driving distance to the floor's spots, to its own ramp's spot and to the enemy within 4%; its own metal (24.0 metal/s), reclaimable machines and level ground round its starts matched. Getting there took moving the north-west start about 700 toward its ramp and parking a truck at each ramp's top, after measuring distances 8-connected (a 4-connected count charged the tilted pit's diagonal roads up to 41% too much). Also checked: each ramp alone takes vehicles to the floor; with both shut, only bots get down, by the slide; with the slide shut too, nothing does. The first renders showed grey saucer dumps, ring-like breakaways, oval pads and near-black gums, all redone. Then, at your second request ("add a train locamotives with many carts behind on tracks the end at a shed … the train, tracks and carts add areas of interest and also limit some of the potential pushes as shots and movements will be blocked. Add a few large and small tanks, some water some fuel and think of 20 other features to include"), **a railway across the north lane**. A north-south line runs from the north edge to a loco shed by the pit's rim, with a passing siding and a level crossing on road 7. On it stand three locos and 28 ore wagons (loaded as far as the loadout, empty behind, an empty rake on the siding), plus a spare loco in the shed. Laid due north-south, the wagons tile into a wall (footprints are squared to quarter turns, and wagons coupled at 120 have 112-long footprints). The train, the loadout and the shed are indestructible, so the wall can't be reclaimed away; it leaves the lane three gaps: at the north edge, at the crossing and by the rim. An overland conveyor on trestles (units drive under it) runs from a new primary crusher at the ROM pad, past a transfer tower, up into the loadout. Tanks: large and small diesel tanks, large corrugated water tanks, rainwater tanks on stands, a water tower. And 21 more features: the workshop shed; the crusher, conveyor, transfer tower and loadout; water carts, graders, dozers, wheel loaders, drill rigs (on a drill pattern on the pit floor), service trucks and crew buses (one of each a side, the drills in the pit); containers, windmills with troughs, a comms tower, a power line, generator sets, tyre stacks, an explosives magazine in its bund, drums and pipe. 34 new models in `ModelsRail.cs` and `ModelsSite.cs`, checked on flat ground first (scale, and the train running along its track). Five new checks prove the wall: with the pit and the south shut, vehicles and bots cross the north lane; with the train's gaps shut, nothing does; each gap alone lets vehicles across (240, 616 and 270 clear). Both teams drive the same distance to the crossing (4,479 and 4,475), and the reclaim stays matched (3,070 metal a side). Then, at your third request ("ensure path textures are not overlapping mine features as a road would not be added over a stacked mound"), a new check: no road's paint under any building, tank, structure or clutter, nor over a stockpile, a dumped heap, the dam's wall, a breakaway or the magazine's bund. It found 11 such places: offices and tanks on the roads crossing the plant and workshop pads, a road straight over a ROM stockpile and clipping another, the north dump's ramp road onto its heaps, a road up the dam's wall, and a power pole. Now roads end at the pads' and hardstands' edges (colour and detail textures alike), the stockpiles sit between the roads (the ROM pad's truck, excavator and dozer moved with them), the heaps keep clear of the dump ramps, and the dam's road stops short of its wall. Then, at your fourth request ("expand the mine to be approx double it size towards the side of the map with little details the mine does not need to be round it can have some character … the mine should expand to border the map edge and have the water at the map edge as a pool"), the pit grew south to the map's edge: 26.8 million square units inside the rim, about 1.9 times the oval. The old oval narrows at a waist into a broad lobe with wavering walls, cut back at its corners by the edge, with a green pool against the edge in place of the middle lake. The lobe has two more haul ramps (one a team, 9.4° and 10.4°), a spot beside each, two shared floor spots and an excavator with its trucks; roads 8 and 11 stop at the new ramps' tops, and a windmill, the magazine and a breakaway moved off its ground. With no way round the pit's south end now, the sides meet only through the north lane or down in the pit (a check proves the south is closed). The south-east ramp's start was tuned until each team's way to its second ramp spot is within 9%. The north (the train, shed, loadout, conveyor, workshop and tanks) is placed exactly as before. Then, at your fifth request ("some of the roads need further touchups"), a road survey (`OpenCut roads`). It found roads 1 and 7 meeting just inside the pit's west rim (painted onto the benches), road 8 running into the waist's wall and ending at nothing, the east ramp's top with no road, the east creek running beside road 11 and under road 6, and roads meeting ramp tops from the side, over the ramps' cuttings (up to 18°). Re-routed so that every ramp's top has a road meeting it in line (road 12 is a spur making a hairpin with road 11), and the creek now drains into the lobe. The dump ramp's sides and the pit ramps' cuttings no longer show white streaks. Five new checks keep roads leading somewhere, out of the pit and creek beds, and graded (under 7° along, 4° across; now 6.2° and 3.5°). Then, at your sixth and seventh requests, three paths you marked were fixed: road 6's sharp V into the south dump's ramp (now one sweep round the dump's west end), the east creek (moved to run down the east side, long like the west's), and road 1's stretch past the ROM pad's stockpiles (removed). Two bucket wheel excavators were added to the lobe's walls. They stand only just higher than the pit is deep (0.93 of the north one, mast tops 30 over the rim), each in a round bite its wheel has cut into its wall, staggered so the floor between them is open. Then, at your eighth and ninth requests, the tailings dam's painted pond (which looked like water the map can't have: one water level, the pit's pool) became a dried-out decant of cracked mud ringed with salt, and a beautify pass gave the natural ground swells, small mounds (real heights, under 17°) and dry washes (the main ones cut 5, rills paint only), all kept clear of man's works, with the roads flat across. A new check keeps every machine and building level: it moved a light tower off a dump's flank. 33 checks pass on the built map's own heights round every blocking feature (a truck parked across the east ramp blocked it at first). The minimap and lobby picture are the map straight down in 3D, at the king maps' camera (5.08 map-widths, an 8192 render). Built in about 65 s plus MapTool's edit, and a rebuild gives the same `run.txt` and checks. `CHECKLIST.md` has the layout, the features, the checks and what was made better on the way. Released at your go-ahead as `red-dust-open-cut-1.0` here and in BarMaps ([download](https://github.com/danielkopko/BarMaps/releases/download/red-dust-open-cut-1.0/red_dust_open_cut_1.0.sd7)) by `red_dust_open_cut.cmd publish`, with BarMaps' README section and lobby picture. Not tried in BAR |
| 48. Feature headings fixed on Five Rivers and the three king maps | ✅ Done 2026-10-23, not yet on the releases | Step 47's test map showed that MapTool's `item-add` heading is in engine units (65536 a turn, 0 facing south), but Five Rivers' and the king maps' converters (`Place.Ops`) passed degrees. So on all four released maps every feature faces within 2° of south: ruins line up, trucks stand across the roads, and the turned parts' copies aren't turned. The converters' degrees also run the other way from the engine's: clockwise from north, as `Design.Turn` turns the parts and a road's bearing is worked out, where the engine's run from south towards east. So both converters now write `round((180 − degrees) × 65536 / 360)`, wrapped to 0-65535 (`Place.Heading`: MapEditSession.AddItem's rounding, after turning the frame round). The units alone would have turned each part's copies the wrong way and left diagonal roads' trucks across them. A render then showed that Industrial Revolution's truck models are long along x, not z, so Corner Fortresses' trucks are turned a quarter more than their road. All four rebuilt (Five Rivers 107 s, the king maps about 50-70 s each, plus MapTool's edit): each archive differs from its release only in `mapconfig/featureplacer/set.lua` and, after the minimap below, the SMF's minimap bytes (`MapTool compare`, and the SMFs byte by byte); each `run.txt` is the same line for line but for the headings; every copy faces its original turned with its part (within a unit); the facings spread evenly (Five Rivers 618/637/629/631 south/east/north/west, Star Fort 166/167/167/165, Edge Keeps 633 and Corner Fortresses 214 each way). The checks come out byte for byte as before: 62, 41, 30 and 29, all passed. The king maps' checks take each blocking feature's footprint as a square, the same whichever way it faces, so how BAR turns a wreck's real footprint is still to try in game. `info.txt` and `metal.txt` are unchanged; the pictures with features in them were drawn again with their first cameras, plus Corner Fortresses' `3d_trucks.png` (trucks along a north-south road that they stood across before). Then, at your new requirement for every release, each map's minimap (and the lobby picture `MapTool install` draws from it) became the map seen straight down in 3D, with heights and features. It's made as Starfall Marsh's is, but with the camera four times as far up (5.08 map-widths, an 8192 render). At Starfall's 1.27, ground lower than these maps' high edges was drawn inward of them, and the background showed along the edges: up to 5 pixels in 1024 on Edge Keeps and 12 on Corner Fortresses. Now there's at most a hairline, and the checks still come out byte for byte. Each map's `CHECKLIST.md` and `release_notes.md`, both `RELEASE.md` files (the king maps' no longer claims that a rebuild of Star Fort matches its release byte for byte) and both converter READMEs say so. Not yet on the releases: `five_rivers.cmd publish` and `king_maps.cmd publish <map>` replace the files there, at your go-ahead. Not tried in BAR |
| 47. Starfall Marsh: a 2v2 crash-site map, its ship and wreckage modelled here | ✅ Done 2026-10-23, released | At your request ("a 2v2 map that is a giant space shit crashed into the jungle, a huge pushed up creator dirt mound where the front end is buried and lost, the back end mostly intack but witch wrechage spread aross the map … 4 mex per starting area and a few near the wreackage … jungle vibes cross a marsh … build and color detailed feature component … style them like transformers space craft"). `out/step47-starfall-marsh/`: 16 × 16, north against south, mirrored across the middle line where the ship lies. A new converter (`converter/`) makes the terrain, texture, detail maps and sky and, for the first time, the map's own 3D models: a mesh builder on MapCore's S3O writer (every face tiled so atlas cells repeat without wrapping; moss on faces turned up, earth below a dirt line, plants not counted in the footprint) and one painted 2048 atlas (gunmetal plating, faded crimson armour with brass chevrons, brass trim, scorched metal, vents, windows, engine bells, girders, cyan seams through the glow channel, an original crest, moss, earth, vines, reeds; no franchise marks). Proved first on a flat test map (a test block with an arrow, the atlas on a board): textures the right way up, scale, and what a heading means. That showed MapTool's `item-add` heading is in engine units (65536 a turn), not degrees: the four released maps' converters passed degrees, so their features are all turned at most 2° (fixed in step 48). The ship: engines (six bells, two pods, fins), aft hull with its bridge tower and crest, two torn ends (decks, ribs, cables, the reactor's glow), the fore hull pitched nose-down into the mound, the prow blade on its crest: indestructible, walling the halves apart, overgrown with vines, ferns and saplings. 254 wreckage pieces in 15 kinds (each with a mirror-image twin for the south), heaviest at the crash site, small scraps near the bases, 7,190 metal a team to reclaim. Terrain: a jungle floor, the flooded furrow (wadeable) with berms, marsh streams, ragged pools and puddled mud, and a 520-high mound of pushed-up dirt (a steep west face for bots, gullies, a graded track for vehicles, dirt spilled over the buried nose). Metal: four spots at each start (2.0) and six on the wreck (2.8); two geothermal vents. Jungle: oaks, kapok, banyans, palms, mangroves, reeds, ferns from All That Simmers (its unused banyan and mangrove models defined here); 6,076 features. 14 checks on the built map's heights round every blocking feature's footprint pass (all starts and spots reached; each of the three lanes alone lets vehicles and bots cross; with all three shut the ship walls the halves off; the five hull sections, which butt together uncapped, all sit at one height on a dead-level floor; spots clear and level; halves alike to 0.00). Built in 46 s plus MapTool's edit (about a minute). Not tried in BAR. `CHECKLIST.md` has the layout, the models, the checks and what was made better on the way. Released at your go-ahead as `starfall-marsh-1.0` here and in BarMaps ([download](https://github.com/danielkopko/BarMaps/releases/download/starfall-marsh-1.0/starfall_marsh_1.0.sd7)) by `starfall_marsh.cmd publish`, with BarMaps' README section and lobby picture |
| 46. King maps: Star Fort (K6), Edge Keeps (K4) and Corner Fortresses (K3) built and released | ✅ Done 2026-10-22 | At your request ("now run the end to end process for K6 and K4 and K3, do 1 at a time, and push as a release to the BarMaps repo for each map … no doors for the moment, walls are terrain, kings areas elivated with both ramp for full access and size zig zags for bot access. add geo vents and rocks/tree features making maps look like real locations"). The three drawings reworked first (`out/koth-maps/make_kings_maps.py`): no doors, ramparts as terrain, every king's ground raised, a ramp and bots-only zigzags for each king, geothermal vents. One converter for all three, grown from Five Rivers' (`out/step46-king-maps/converter/`, `KingMaps build` and `check`): the parts are turned, not mirrored, with each part's noise blended into the next near the lines between them, so they're alike without a seam; ramparts 90 high and flat-topped, towers 110, 12° ramps, 38° zigzag flights cut through the ramparts, bots-only passes over ridges (the ground lowered along the pass, at most 60 above the higher side and 38° anywhere: a straight climb up the cliff face failed the checks first); a look for each planet (Dune, Heath, Forge) painting the ground and choosing plants, rocks and props. **Star Fort 1.0** (`out/step46-king-maps/star_fort/`): 28 × 28, five kings on the bastions of one star fort round a spire, 20 players; 41 access checks pass on the built map's own heights, the fifths match (median 0.00, 99% within 1.2); 70 metal spots (27.2 metal/s a team), 10 vents, 665 palms, ferns, rocks and wrecks; built in 54 s plus MapTool's edit. Released as `star-fort-1.0` here and in BarMaps ([download](https://github.com/danielkopko/BarMaps/releases/download/star-fort-1.0/star_fort_1.0.sd7)), with BarMaps' README section and lobby picture. **Edge Keeps 1.0** (`out/step46-king-maps/edge_keeps/`): 24 × 24, four kings on cliff keeps at the middle of each edge, a loch with an isle in the middle, 16 players. Its first build passed every check but looked flat and plain (blotchy heather, pines only in three thin rows), so the Heath look was redone before release: broad rolling hills (±40, roads and bases follow them; `Look.Swell`, never dipping into puddles), heather on the rises and moor grass in the hollows, small peat hags, granite-sett causeways, pinewoods, birch copses, tors on the crests, scree and boulders. 30 access checks pass on the built map's own heights, the quarters match (median 0.00, 99% within 0.2); 56 metal spots (28.4 metal/s a team), 9 vents, 2,532 trees, bracken, rocks and cairns. Released as `edge-keeps-1.0` here and in BarMaps ([download](https://github.com/danielkopko/BarMaps/releases/download/edge-keeps-1.0/edge_keeps_1.0.sd7)), with BarMaps' README section and lobby picture. **Corner Fortresses 1.0** (`out/step46-king-maps/corner_fortresses/`): 24 × 24, four kings in raised corner fortresses behind ramparts, all twelve peasants round the market in the middle, acid lakes between, 16 players. Its first build passed every check but looked wrong: Industrial Revolution's pipe models are runs up to 2000 long and 250 tall and looped over the ramparts, the ground was one tiled floor, the roads black strokes, the market a red checkerboard, the mushrooms 8 across. Redone before release: no pipes or mushrooms; an estate's paved lots between waste ground gone to weeds, worn asphalt, a brick market; ruined works of BAR's building wrecks (factories and moho mines with storage tanks and metal makers) with rubble, container yards, trucks, weeds. 29 access checks pass on the built map's own heights, the quarters match exactly; 64 metal spots (32.8 metal/s a team), 9 vents, 856 features, about 9,100 metal a team to reclaim (noted as a first guess, with the acid's damage). Released as `corner-fortresses-1.0` here and in BarMaps ([download](https://github.com/danielkopko/BarMaps/releases/download/corner-fortresses-1.0/corner_fortresses_1.0.sd7)), with BarMaps' README section and lobby picture. The drawings' planet notes for K3 and K4 (`planets.py`) were brought into line with what was built. Afterwards the access checks were extended to the features (`KingMaps check … run.txt`: each blocking feature's footprint and 8 more round it, in the checks that units get through) and re-run on all three: all pass, the features blocking 0.6%, 1.9% and 2.5% of the ground; and a rebuild of Star Fort with the final converter gave the released files byte for byte (until step 48's headings fix, which changes `set.lua`). `out/step46-king-maps/RELEASE.md` records the conversions and releases. `out/step46-king-maps/king_maps.cmd publish <map>` commits, pushes and releases any of the three; `install <map>` copies one into BAR. Not tried in BAR |
| 45. Five Rivers released: GitHub releases here and in BarMaps | ✅ Done 2026-10-22 | At your request ("split the map and provide a simple script to run to repackage it and push to git", then "make it so i can grab it all in 1 go without having to grab the entire repository", then "release to the BarMaps repo as well"). The 112 MB `.sd7` is over GitHub's 100 MB a file, so it was first committed in three parts (45, 45, 16.6 MB) with its SHA-256 and a join script (split and join checked byte for byte; 09d9e0e), then replaced by releases (8659a32): `five-rivers-1.0` in this repository (private) and in danielkopko/BarMaps (public: [download](https://github.com/danielkopko/BarMaps/releases/download/five-rivers-1.0/five_rivers_1.0.sd7)), each with the map and its lobby picture and `release_notes.md`; BarMaps' README has a Five Rivers section beside Simmering Ravine's and `LuaMenu/Images/Minimaps/Five_Rivers_1.0.jpg`. `out/step44-five-rivers/five_rivers.cmd publish` commits and pushes the notes and puts the map on both releases (replacing their files when they exist); `install` copies it into BAR. Pushing as danielkopko: on this PC git and the GitHub CLI default to the work account, which can't see the private repository, so the script takes danielkopko's sign-in from the CLI for that run only. `out/step44-five-rivers/RELEASE.md` records the conversion and the release, and how to release a new version |
| 44. Five Rivers: a 28 × 28 map from the K7 drawing | ✅ Done 2026-10-22 | At your request ("now build this as a working map"), after designing 17 king-of-the-hill drawings, each on a planet (`out/koth-maps`: the generator, `planets.py`, `index.html`) and reworking K7 (oval halls 70% larger, a ramp at the front, bots-only zigzag switchbacks on the other three sides, 11 geothermal vents). `out/step44-five-rivers/five_rivers_1.0.sd7`: no local map is 28 × 28 and the editor can't make one, so a converter using MapCore (`out/step44-five-rivers/converter/`) builds the SMF and SMT from scratch, as the tests' synthetic map does, on All That Simmers' plants, detail textures, featureplacer gadget and water settings (its layout, sunrise sky, two unused textures and its map options dropped: a *Dry* option there would reset the heights to 150-1000). Probed first: a flat 28 × 28 map builds in 2 s at 1.3 GB, and MapTool reads, edits and renders it in seconds. `export_geometry.py` writes the drawing's geometry (every team's part turned into place); the converter makes every height and colour with noise read in Team A's fifth, since MapTool's symmetry only mirrors (the fifths meet under the rivers' winding middles and at the smooth isle): realms 220 to 140, water 60 deep in valleys of 20° at most, fords 10 deep, causeway bridges at 16, the isle 60, halls 340 on 72° cliffs, a 12° ramp 538 wide, three 38° switchbacks 64 wide, mountains beyond the play circle; the Bloom planet's texture painted texel by texel into the SMT (14336²), splat, specular and normal maps (4096, DDS), a night sky cube, grass, mapinfo (20 starts, team order king then peasants), then one `MapTool edit` adds 75 metal spots (24.3 metal/s a team), 11 vents and 2,515 plants, crystals and rocks. Built in 44 s plus 40 s. Checked on the built map's own heights: 62 access checks pass (vehicles reach halls only by their ramps and cross rivers only at bridges and fords; bots climb each switchback alone and nothing else; hovercraft and vehicles can't use the switchbacks), and the fifths match (median 0.00, 99% within 0.1); the splat, specular and normal maps are written upside down (rule 7: the first build had them the wrong way up, caught by MapTool's splat and normals renders), and all 54 placed feature names are BAR's or the map's. The K7 drawing's heights line was changed to match (the isle 60, not 200-230). Not tried in BAR (not installed here); the 20-player limit is unchecked. `CHECKLIST.md` has the layout, checks, pictures and what to try in game |
| 43. Map pictures in BAR's lobby; the minimap after a new texture | ✅ Done 2026-10-21 | At your request ("the maps mini map on the bar setup screen does not show the map / mini map picture"). From BAR's lobby (BYAR-Chobby, configuration.lua): it shows a map's picture from its own set (1,326 official maps) or from `LuaMenu/Images/Minimaps/<name_version>.jpg` in BAR's data folder, never from the archive (Recoil's LuaVFS reads loose files first for the menu; its image loader goes by contents). Installing (editor or `MapTool install`) now writes that picture: the map's minimap at the map's proportions, longer side 1024. Two minimap bugs found on the way: MapTool, which doesn't prime its session, fitted the minimap's colour style after the first texture edit, so a whole new texture against the old minimap gave a flat wash (Simmering Ravine's minimap was flat olive); and a whole-texture import (and World Machine's bring-back) redrew the minimap from the decoded texture before refreshing it, so a primed session (the editor's) showed the old texture. Both fixed; a minimap unlike its texture is no longer followed on land (all three channels or none; BAR's own maps fit with r² 0.78 or more, seven measured, so it never trips on them). A picture that can't be written leaves the install standing, with a warning. App 0.1.2. Simmering Ravine 1.5: its minimap is the map. 329/329 tests (3 new, 2 tightened, a ramp test's minimap hashes updated: an unprimed session now matches a primed one), self-test 271/271 |
| 42. Structures survive BAR's footprint clearance | ✅ Done 2026-10-19 | At your request ("fix the tunnel roofs"), after Simmering Ravine 1.0's build-on wall pieces vanished in game. BAR's `unit_footprint_clearance.lua` (LuaRules) destroys every feature whose position lies within a new building's or factory's xsize × 5 by zsize × 5, geothermal vents apart. Recoil keeps event clients by order, lowest first (EventHandler.cpp ListInsert; LuaHandle.h: LuaRules 100, LuaGaia 300), so the structures gadget now steps its own features (tunnel tops, every part, build-on features) aside in AllowUnitCreation, the last say before the unit is made, and back in UnitCreated, after the clearance has looked, with a next-frame fallback. The tests' mock engine now runs BAR's clearance before the map's UnitCreated, as the engine does; a new test builds over a tunnel roof's own position, a build-on feature and a rock: roof and feature stay, in place, the turret is lifted, the rock is cleared (the test fails without the fix). App 0.1.1. 326/326 tests (1 new), self-test 270/270 |
| 41. A map from a drawing: Simmering Ravine | ✅ Done 2026-10-19 | At your request ("just using the tool, using the same textures and colors as all the simmers, convert this SVG into a highly details map"), with two changes asked mid-way: the close contours a cliff with "a zig zag unit path cut into the face ... tank unpassable", every hill level with the hills beside the bases ("all others should align to this areas height"), the base plateau staying lower, and the cliff edges "naturally worn" as on Ascendancy and All That Glitters (bays and spurs, chutes, fluted faces stepped in tiers, a crenellated lip, scree; the steps between ledges over 54°, scarps plain along the wall). `out/step41-ravine/simmering_ravine_1.0.sd7`, built on All That Simmers (unchanged) with four `MapTool edit` runs (three mirrored tb); a converter using MapCore (`out/step41-ravine/converter/`) did what the tool can't: the SVG to a heightmap (turned a quarter, Team A north), a base texture from All That Simmers' plain ground, and its splat, specular, normal and grass maps made again for the new ground (Beautify paints the texture only). The tool gained `@file` operation files for `MapTool edit` and an `unset:<path>` operation (mapinfo values removed, undoable). Hills all level at about 710 (the base plateau 560); cliffs 450 high beside the bases with four switchbacks each side (ledges stepping down at 38–40°: bots yes, vehicles and hovercraft no), the central slope graded with ramps to the editor's door between two scarps, 11 buildable wall pieces a side (feature-custom from the door's wall, buildon=2), all on one level footing between squared-off cliffs, 120 high with the door ("their height must be consistent and they should be about 50% higher"), stone bridges, a 10-deep ford, a 60-deep river in All That Simmers' orange; 54 metal spots (1.8 safe to 3.2 contested; maxMetal 4), 4 vents, 2,528 trees, plants and mossy rocks, 16 starts. Checked on the built map by flood fills over the engine's squares: with the door shut no vehicle or hovercraft gets down, bots only by the switchbacks, nothing with those shut too; both valleys joined by bridges and ford; mirror exact but for cliff-detail rims (≤ 18 units on 0.3%); every wall piece and door on ground at 140.0. Played in BAR: the door works; the wall pieces (build-on features) were destroyed when built on, by BAR's *Footprint clearance* gadget, so 1.1 makes the wall a rampart of the ground (MapTool ramps after the cliff detail: 120 high, top level with the door's, 96 across to build on; stops all but spiders and aircraft) and adds `map_rampart_breach.lua`: a D-Gun shot that strikes a rampart flattens 96 of it to the ground beside the wall, never raising ground, so craters stay (simulated: the breach lets every class through with the door shut). 1.2 at your request ("the outer most can stay as dirt paths, the others should be cobblestone or concrete or bitumen ... the ford water area needs details like water weeds"): the valley's roads drawn into the texture in each road's own coordinates (bitumen central road with lane polish, centre line, patches and jagged cracks; a concrete slab apron at the door; cobbled bridge roads and cleft lanes with kerbs), each with its own splat detail and shine; weed beds on the ford's bed and clumps of All That Simmers' (non-blocking) ferns along its sides. 1.3 at your request ("the cobble stone that stops at the cliff and turns to dirt, should continue unified. and a blend and transition between the different paths as they branch out"): the paths drawn as one network, so the cobbles run on from the plateau down the switchbacks and through the clefts to the bridges. Branches are filleted (a smooth union of the paths' edges, which kerbs follow); setts thin out one by one into dirt, bitumen frays into it; pavings and crossing cobbled ways meet along one seam. Also a dirt forecourt at the base that the road and dirt paths leave from, stone-flag bridge decks with parapet copings that the bridge roads meet square, a square-ended door apron, and ruts and grass on the dirt paths. 1.4 at your request ("change the texture to look like stairs, and smooth the humps, the terrain on the slopes shows stepped instead of a gradient that is only bot accessable"): each switchback leg now goes down in one smooth flight of 37° (39° where the legs are short), eased at its ends, with level landings, in place of 15 rock-cut steps (a slope over the whole path would be about 13°, which tanks climb); the flights drawn as stone stairs, found from the built heights. Every access check as before. Then made small enough to email ("gmail" size limit): 105 → 52 MB (sky 1024 a face, normal map 2K slopes-only, two detail textures halved, four unused files removed), split into three parts under 18 MB with join instructions (`copy /b`, checked byte for byte); the parts later dropped at your request, git being easier to fetch from on both PCs. 325/325 tests (1 new), self-test 270/270 |
| 40. World Machine integration | ✅ Done 2026-10-19 | At your request ("i also want integration into World Machine"). From World Machine's documentation (help.world-machine.com: File Input/Output, Bitmap Output, Texture Weightmap, the Professional edition's XML automation): an exchange folder per map. Send writes heights.png (16-bit, the map's own range: World Machine has no heights below 0) and .r16, texture.png, masks (water, slope, cliffs, keep), bar_map.json and README.txt (the set-up: extents in km at 1 unit = 1 m, resolution, altitude range, output names). Bring back (`wm-import`, journaled with a copy of the file, one undo step): heights (16-bit PNG, RAW16, float RAW; bicubic, corners on corners), texture (PNG/BMP), splat (RGBA = detail textures 1-4), grass (mask); upside-down outputs found by comparing with the map, a mismatched height scale reported with what to set; with Keep, height changes are halved where they would alter any class's access or building ground (the cliff detail's guard, now saying what it held back for), finishing for certain, and metal spots, structures and starts kept with their looks. A live link watches the folder for World Machine's outputs (once a file has settled); Build in World Machine (Professional) writes build.xml, runs it and brings the outputs back as written. In the Beautify panel's new World Machine group and File › World Machine; MapTool worldmachine send/bring. Checked with outputs made as documented (World Machine isn't installed here): a pass-through at 769 comes back within a 16-bit step, at 1025 within 0.54 on average; an upside-down one is turned; a doubled scale reported; a realistic whole-map erosion keeps 33% of the change with every class's access identical after saving and reopening; texture 2048 → 6144 with spot looks kept; splat quarters in place; journal replay identical after World Machine's file is gone. 324/324 tests (6 new), self-test 270/270 (4 new, the live link among them) |
| 39. Beautify panel: the texture painted and the cliffs roughened by the terrain | ✅ Done 2026-10-19 | At your request ("implement another panel that can be opened or closed that will allow this beautify of the map"; it must not change metal, ramps, water or access). View › Beautify panel (or the toolbar's Beautify) adds a tab beside the tools. Texture (`beautify-texture`): rock on cliffs with layers following the height, dirt and scree on middle slopes and at cliff feet, shore sand, a wet edge and a darker seabed on maps with water, an optional open-ground tint, shade in hollows, optional softening of hard painted edges, and made-up grain and tone where the texture is plain; each mixed into what is there, keeping its grain, with ragged, wandering edges; metal spots' looks and structures left alone. Colours: the map's own (k-means clusters of its local average colours on steep and level ground) or Temperate, Desert, Volcanic, Snow and ice; every one choosable. Cliff shape (`beautify-heights`): broken rock, ledges and gullies above a slope, and an optional swell on ground under 8°; then checked as the engine sees it and taken back where it would change where any BAR movement class goes (each slope cell's side of 27/33/54°, each square's side of the water and depth limits, with a margin, and anything already within the margin untouched) or where buildings fit (footprints flat enough for maxslope 10, 15 and 30 stay so: the engine's 40 × tan(maxslope), RecoilEngine UnitDef.cpp and GameHelper.cpp; BAR's solar and fusion 10, labs 15, extractors 30); heights kept within the map's own, so saving doesn't store them all again. Both one undo step, deterministic, journaled; symmetric with Symmetry on. Also: the terrain type map in File › Import/Export layer (and MapTool), and `colours=map` for MapTool. Import/export of heights, metal, grass and the rest already existed. Checked: every class's access identical after saving and reopening; fusion footprints kept; a ramp's surface kept; metal, spot looks untouched; undo exact; same bytes twice; mirrored halves alike; Soften hard edges blends a painted plate's border (347 of 620 border pixels) and leaves grainy grass alone (its edges are found between local averages, not in the grain: 6.6% of Acidic Quarry, its rims and plates). With the panel open the tools pane is 48 px wider, so seven tabs fit (Windows' 54-pixel least tab width had scrolled two off). 318/318 tests (6 new), self-test 266/266 (6 new). Pictures and two test maps in `out/step39-beautify` |
| 38. Symmetry: Mirror and Mirror + flip | ✅ Done 2026-10-17 | At your request ("Add the option to toggle this on. Mirror is all actions are duplicated on the middle axis (pick one) and if also flipped the its the adjacent position on the map"). Toolbar: Symmetry Off / Mirror / Mirror + flip, and the axis for Mirror (left | right, top | bottom, and the two diagonals on square maps). Read as: Mirror copies each edit across the chosen middle line; Mirror + flip also flips it, so the copy is at the opposite side and corner (a half turn about the centre, the same for any axis). Done in MapCore's Apply: the copy is an operation of its own, worked out from the map before the edit (an item's, spot's, structure's, marker's or start's twin at its mirror image) and applied after it in the same undo step; the journal records both and replays them without mirroring again. Brushes, ramps and new things are made at the mirror image, turned to match; noise (brush and ramp roughness) is read mirrored, and a ramp's copy takes the original's worked-out heights, edge, rock and dirt; changes to what is there go to its twin; settings, whole-map edits, imports, assets, new features and kinds are made once. The line (or a centre mark) is drawn on the map and the brush's copy dashed. Off at each start (not kept). MapTool edit --mirror. Checked: heights mirror to 0.00002, a mirrored ramp with rough sides to 0.05, texture within DXT1 rounding (45 pixels, 9 levels); items, metal, a door (gap 5: its parts mirrored); twins moved, turned, their markers, a guardian's path, a spot moved with its look, grass; starts (Acidic Quarry's four: team 0's twin is team 2; a lone one says so; two always pair); one undo; journal replay byte-identical. 312/312 tests (8 new), self-test 260/260 (8 new). Untested in BAR: a unit's heading across a diagonal on maps with the older featureplacer gadget, which turns units by −rot (left | right, top | bottom and the half turn come out right with either) |
| 37. The map's new features under Experimental | ✅ Done 2026-10-16 | At your request ("list the custom features under experimental too"). The browser's Experimental group also lists the new features the open map has that were made with New feature from a model, each by its model (drawn in memory from the map's files) with how many are placed. They are found by the first line the editor writes in `features/<name>.lua`, so ones from earlier sessions show too; a structure kind's parts made from models are written the same way and are left out by name. Chosen, the Asset tab shows what it was made from, its settings as its file has them (build-on from the structures config), its model, and Place it; double-clicking it places it too. A new feature is chosen there as it is made; undone, it goes. The group is now ordered types, kinds, the tool, its features. Self-test 252/252 (7 new checks, one placing it), 304/304 tests; also checked on step 26's map, whose platform (made in an earlier session) is listed and none of its kinds' parts |
| 36. Experimental tools under Experimental, the Asset tab showing only what is chosen | ✅ Done 2026-10-16 | At your request ("the experimental items should also be listed under experimental in the bottom bar, and the right menu should show their sections when selected"). The browser's Experimental group lists the six structure types (each by its built model, made in memory), the new-feature tool and the open map's kinds (with how many placed), whatever the source. The Asset tab shows only the chosen entry's sections: an asset its details and buttons (and In the library for the library's own); a type a New kind section with only that type's fields; a kind Place it (and Start destroyed for a door or tunnel); the tool its settings, with a Model box (a library model; the last asset with one chosen). "Use" buttons became model boxes. Double-clicking a kind places it; a type or the tool, nothing. A new kind is chosen in the browser as it is made. Self-test 245/245 (9 new checks), 303/303 tests |
| 35. Temporary folders cleaned after a stopped run | ✅ Done 2026-10-16 | MapTool runs killed part-way (a pipeline cut short, Ctrl+C, a crash) left their unpacked map in `%TEMP%\BarMapEditor` (8 found, 86 MB each), since their `finally` never ran. Each temporary folder now has a lock file beside it, held for the run's life; MapTool and the editor delete, at start, folders whose lock no process holds (and lockless ones from older builds after an hour). Checked: a killed render's folder swept by the next MapTool start; a running render's left alone while another MapTool swept; 303/303 tests, editor self-test 236/236 |
| 34. BAR's units, buildings and all features in the editor | ✅ Done 2026-10-16 | All 681 units (302 buildings by their unit files) and all 1,430 features BAR keeps, with their models (1,434 models, 175 MB), listed in the asset browser from BAR's catalog (Maps / BAR / both), units by their build pictures; previews, Items › Place as unit or building, and the 3D view; names BAR lacks flagged; 302/302 tests, editor self-test 236/236 |
| 33. BAR's own feature models | ✅ Done 2026-10-16 | 1,432 features BAR defines at commit 9d7bdf3 (its features/*.lua run as BAR runs them, units' wrecks named as BAR names them), with the models of BAR's own features and the 20 wrecks the seven maps use (14.3 MB, 6.2 MB zipped); the engine's trees as BAR's firs in the browser, previews and 3D view; 298/298 tests, editor self-test 229/229 |
| 32. BAR look for the editor | ✅ Done 2026-10-16 | Colours sampled from BAR's settings overlay; tabs, headings, buttons, toggles, dropdowns, number boxes, sliders, lists, menus, toolbar and status bar themed; fonts follow the window through DPI changes; 292/292 tests, editor self-test 228/228; before and after pictures in `out/step32-theme` |
| 31. Shield platforms (repaired, owned, bubbles) | ✅ Done 2026-10-16, **untested in BAR** | 292/292 tests pass: repair-to-own; shields for the owner's and allies' units driving off, none for an enemy's, aircraft, nanoframes or carried units, nothing for standing on it; hits taken whole (a lethal one kills nothing), the emptying hit lets the rest through; a titan's 82.8 bubble covers a grunt 50 off at its feet, not 90, not an enemy, not a shot from inside; debris taken, fire, acid, collisions and EMP not; top-ups for the part missing, nothing charged a team that can't pay; a derelict gives nothing; recycled IDs get nothing; a guardian's doom goes through; a reload; the drawing side draws only what this player sees (run in a stand-in with gl recorded); models low enough to drive over, team colour on the stripes; editor self-test 228/228 |
| 30. Guardian towers (zones, regions, teams, doom) | ✅ Done 2026-10-16, **untested in BAR**; package refresh waiting (the packaged editor was open) | 281/281 tests pass (a tower written without rules, as the noon build writes them, acts as its kind's targets in the band). Regions: inside for lobby teams 2-4 after 30 s dooms a team 2 fighter at exactly 900 frames, leaves team 1 alone, and starts again for one that left at 29 s; outside reaches across the map and into another tower's band, takes in Scavengers with All but not with 1-8; the band wins and resets the inside count; a band with a delay lets a quick crossing pass; an open path has no inside; a bow-tie path's loops are inside by the even-odd rule, the gadget and the editor agreeing; doom carries on after leaving; a recycled ID gets no count; rules copied to a new tower and kept through moves; structure-rule checked and undone. Editor self-test 223/223 (each region's teams, delay and units; a second tower copying the rules). Before that: listed units that come into a zone (an open or closed path, the edge exact to half a unit) die on the 5th tick of a second, 1 or 500 together; doomed wherever they go after, healed or repaired in full or carried off; a plane crossing a 32-wide zone between checks caught; a fighter, a transport, a carried bomber and a nanoframe untouched; the Scavengers variant struck; a recycled unit ID not doomed; one rocket every 2 s while anything is in the zone, bursting high over the tower after 45 frames; unknown names reported; a reload takes the tower back; the models' winding, footprint, pod mouths and atlas; the catalog's 681 units and pictures; save warnings (commanders, a start in a zone); editor self-test 218/218 (targets dialog made but not shown: filter, presets, faction; create, place, pick; Shift+click, Ctrl+click, drag, distance, closed, targets, Apply, Clear; twelve undos) |
| 29. Bending ramps (points, rounded bends) | ✅ Done 2026-10-16 | 260/260 tests pass: straight ramps come out byte for byte as before (hashes taken from the code before paths); Local and World undo each other on a bending ramp, by the grid and without it; a radius-75 switchback measured against its true geometry (to 0.05); every stored height on a hairpin and a switchback rises evenly along the line; between the legs the blend has no step (and the nearest point alone would, which the test sees); a ramp wrapped round a mesa, its run-on pointing back at its start, leaves no new cliff; the edge traced without loops; bad shapes refused; one undo step; a 1,255-unit bending rock-to-dirt ramp in about 1 to 2 s, the same twice; editor self-test 194/194 (Shift+click, Ctrl+click on the line and on a bend, drag, a cut-off drag, E, Swap ends, Apply, undo, Straighten; a point of the mesa ramp moved, its preview updated in about 130 ms; its dashed edge one line without loops) |
| 28. Oil pump (repaired, owned, pays, nods) | ✅ Done 2026-10-15, **untested in BAR** | 250/250 tests pass: a pump repaired by one team then another, paid each second, its beam nodding a whole cycle, destroyed, reloaded (mock engine with each call as the source has it); its models' winding, footprint, pivot and team-colour alpha; editor self-test 182/182 (made, placed, picked) |
| 27. Built models for doors and tunnels | ✅ Done 2026-10-15, **untested in BAR** | 248/248 tests pass (moving and turning, a map using every terrain type, broken doors and tunnels, placed repair markers, kinds from earlier builds, included): every built part read back as the engine reads it (wound as 259 real models are, unit normals, texture coordinates in the atlas, header height and middle), parts fill their footprints, an open tunnel's passage clear for a commander, the atlas's alphas (texture 1: 0, texture 2: 255), the cliff tint; a kind changed for every one placed, undone; buildings lifted onto a flat roof and kept apart, a rock-roofed tunnel unbuildable, a structure starting destroyed and repaired (mock engine); journal replay to the same bytes; editor self-test 178/178 (build-on, a built gate placed destroyed, picked, toggled, dragged, turned, given a repair marker that is picked, dragged and deleted, resized, undone) |
| 26. Doors and tunnels (map structures gadget) | ✅ Done 2026-10-15, **untested in BAR** | 228/228 tests pass; the gadget runs against a stand-in for the engine (only the calls checked in Recoil's source): parts where the editor's layout puts them; a broken door's markers feed one pool, stall unpaid, hold while the gap is occupied, rebuild; a tunnel collapse kills what is on it but not aircraft flying over, and gives the ground back its own type; build rules; reload; features to build on; editor self-test 159/159 (create a door kind, place it by click, pick it, Delete, undo); an in-game test map and checklist in `out/step26-structures` |
| 25. Experimental: new feature from a model | ✅ Done 2026-10-15 | 217/217 tests pass; only keys the engine reads (checked in Recoil's source), every one written out; reads back the same run or as text; one undo step; a library model brings only what it needs; clashes and bad names refused; replays from the journal; editor self-test 152/152 (the monolith preset: 10 D-gun hits, 180 s at 200 BP, 120 s for a commander; Create then Items › Place; undo) |
| 24. Asset library, previews, asset browser | ✅ Done 2026-10-15 | 213/213 tests pass; on the seven maps every feature (256 in 35 definition files) gets its real name, model and textures, and the only names left to BAR are BAR's and the engine's; collecting again adds nothing; previews checked by eye (truck, crate, pine, fir, palm, fern, tanks, rock); editor self-test 148/148 (the 3D view draws the placed assets, on by default, off takes them away; in a 1366x768 window the bottom panel sits under the map view and the tool tabs run the full height; browser thumbnails in groups, details and preview on the right, filter, double-click to place; the log in a tab beside it; the selected item's preview in Items) |

**`out/` was cleared on 2026-10-16** at your request. It keeps the latest package (`out/step21-ramp`: `BarMapEditor/` and `BarMapEditor-ramp.zip`) and the in-game test maps with their checklists (`out/step26-structures`, `step29-bending-ramp`, `step30-guardian`, `step31-shield-platform`). Since then: `out/step39-beautify` (two beautified test maps, before and after pictures, a checklist). The older outputs the steps below name are gone: the milestone builds and checklists, the example maps, and the picture folders of steps 32 to 34. `out/selftest` and `out/tests` come back with the next test run.

**Build and test:** `powershell -ExecutionPolicy Bypass -File .\build.ps1 -Test`. Add `-Filter Name` to run a subset of tests.
**Reference parser:** `python -I tools/smf_reference.py <map.smf>` prints JSON.
**Editor checks without a window:** `MapEditor --render <archive> <out.png> [options]` draws the view, labels included. `MapEditor --selftest <archive> <folder>` drives the hidden window through a whole session; `build.ps1 -Test -SelfTest` runs it after the tests (only after a successful build). `MapTool render` and `MapTool png-diff` cross-check them.

---

## Technology

| Choice | Why |
|---|---|
| **C# / .NET 10 WinForms** (`net10.0-windows`) | The .NET SDK 10.0.401 and the WindowsDesktop 10.0.12 targeting pack are already installed. Large textures need real speed: the diffuse texture is 6144², about 150 MB as RGBA. |
| **No NuGet packages** | Everything gets written in-house: DXT codec, DDS, PNG (via `DeflateStream`), SMF/SMT, Lua-table parsing. That keeps the build offline. |
| **7-Zip** (`C:\Program Files\7-Zip\7z.exe`) | Used to read and write `.sd7`. `.sdz` uses the built-in `ZipArchive`, so it works without 7-Zip. |
| **Python** (already working) | Independent cross-check of the C# parsers in tests. |

**Projects:**
- `MapCore`: all file formats and editing operations. No UI.
- `MapTool`: command line over `MapCore`.
- `MapEditor`: WinForms, a thin shell over `MapCore`.
- `MapCore.Tests`: a console test runner.

**Why there's a CLI:** I can't click through a WinForms window. So every operation must also be reachable from `MapTool`, and that's how I test it. The UI is checked by you.

## Rules for every step

1. **Lossless where untouched.** Opening and saving with no edits gives byte-identical `.smf`/`.smt`/`.dds` files. When something is edited, only the changed DXT blocks or tiles are re-encoded; everything else keeps its original compressed bytes.
2. **Never overwrite the source.** Always use *Save As* with a new map name and version. BAR treats a modified map under the same name as a mismatch in multiplayer.
3. **Patch `mapinfo.lua` in place.** Edit only the specific values that change and keep everything else, including comments. Never regenerate the file.
4. **`mapinfo.lua` wins over the SMF header.** Its `smf.minheight` / `smf.maxheight` replace the SMF header's height range (−97…353 vs −50…100 on this map).
5. **Rebuild SMF offsets on write.** Never patch in place. The tiles section embeds the SMT filename.
6. **Leave internal file names alone on Save As.** Only mapinfo `name`/`version` and the archive file name change.
7. **Map `.dds` files are stored upside down.** The engine flips every `.dds` vertically when loading it (RecoilEngine `rts/Rendering/Textures/Bitmap.cpp`, `flipDDS`; only `unitpics` are exempt). So a map's `.dds` resources (normal map, splat map, specular, detail textures) have the map's **south** edge in their first row.
   - Always call `RgbaImage.FlipVertical()` after decoding and before encoding.
   - **Regions too:** `DdsFile.Encode(..., region)` and `RebuildMips(..., region)` take file coordinates. Convert a map-orientation rectangle with `PixelRect.FlipVertical(height)` at mip 0, before any `ToNextMip`. Otherwise the wrong (mirrored) blocks are rewritten and the edit silently doesn't show. Test: `OrientationTests.RegionEditInMapOrientationLandsOnTheRightBlocks`.
   - Step 11's PNG import and export of `.dds` layers works in map orientation.
   - PNG, TGA and BMP resources are **not** flipped.
   - The `.smt` tiles and the SMF minimap are uploaded raw, so they are not flipped either.
   - `MapTool dds-info --png` exports in file orientation (upside down relative to the map).

## Facts already established about the test map

- 768×768 squares, so 6144×6144 game units and a 769² 16-bit heightmap.
- 1 extra header: **grass** (type 1) at offset 92. It's 192², currently all zero.
- Metal map 384², with 24 spots of 3×3 pixels at value 246. The type map is all 0.
- **0 trees** in the SMF. The 56 rocks come from `mapconfig/featureplacer/set.lua`.
- SMT: 36,864 tiles, all unique, DXT1 32×32 with 4 mip levels (680 bytes each).
- DDS files:

  | File | Size | Format |
  |---|---|---|
  | `normalmap.dds` | 6144² | DXT1, 13 mips |
  | `specular.dds` | 3072² | DXT1 |
  | `splat_distr.dds` | 2048² | DXT5 |
  | detail textures | — | DXT5, plus uncompressed 32-bit `rugged_rock.dds` |
  | `sky.dds` | — | cubemap, not used |

- **SMF layout** (from `tools/smf_reference.py`). The sections cover the file exactly, with no gaps and no overlaps:

  | Offset | Section | Length (bytes) |
  |---|---|---|
  | 0 | header | 80 |
  | 80 | grass extra header | 12 |
  | 92 | grass | 36,864 |
  | 36,956 | heightmap | 1,182,722 |
  | 1,219,678 | type map | 147,456 |
  | 1,367,134 | minimap | 699,048 |
  | 2,066,182 | metal map | 147,456 |
  | 2,213,638 | tiles | 147,485 |
  | 2,361,123 | features | 182 |

  - The minimap is DXT1 1024² with 9 mip levels, down to 4×4.
  - The features section has 17 type names (`TreeType0`–`15`, `GeoVent`) and 0 features.
  - The raw heightmap uses the full 0…65535 range.
- Gotcha: in Windows PowerShell 5.1, piping one native program's output into another corrupts it. Use the Bash tool or temporary files instead.
- **Archive:** 48 files and 12 folders. Two `desktop.ini` files are Hidden + System. `mapconfig/featureplacer` is stored as a **read-only folder** (`DR`), and that flag survives extraction.
  - Deleting a workspace must clear the flags on folders as well as files (`FileSystemUtil.DeleteTree` does this).
  - Later steps that write workspace files must clear a file's ReadOnly flag first.
- **Repacking** with 7-Zip 26.04 and the dictionary pinned at `-md=16m` (`LZMA2:24`, as the original) gives a 50,602,763-byte `.sd7` against the original's 50,602,762, with identical contents.
  - Without pinning, level 5 in this 7-Zip version uses a 32 MB dictionary (`LZMA2:25`).
  - The `.sdz` version is 58.4 MB.
- **`MapPackage.FindSmf`** uses a temporary pattern match on `mapfile = "..."`. Step 7's mapinfo reader replaces it.
- **`SmfFile`** (`src/MapCore/Smf`) keeps raw values plus the section order it was read in. Saving writes the sections in that order and recalculates every pointer, which is why an unedited save is byte-identical.
  - Unknown extra headers are kept as raw bytes, with a warning.
  - Gaps, overlaps and inconsistent counts become entries in `Warnings`, not failures.
  - Damaged files fail with `InvalidDataException`.
  - `MapTool info <archive|.smf>` prints a summary.
- **Texture code** (`src/MapCore/Textures`, `src/MapCore/Imaging`):
  - The DXT encoder works like stb_dxt: principal-axis endpoints, two least-squares refinement passes, and exact handling of single-colour blocks.
  - It measures error as plain RGB distance, so normal maps aren't distorted by perceptual weighting.
  - `DdsFile` keeps the original header and every untouched level byte for byte. `Encode(..., region)` and `RebuildMips(..., region)` rewrite only the blocks a region touches.
  - DXT3, ATI2, volume textures and texture arrays load as "not editable" and are kept unchanged.
- **Encoder quality** (after Step 5 added 3-colour mode for opaque DXT1 blocks):

  | Test | Result |
  |---|---|
  | Re-encode the decoded normal map | 61.9 dB (47.7 dB before 3-colour mode) |
  | Re-encode the decoded map texture tiles | 61.4 dB (37.3 dB before) |
  | Re-encode the decoded splat map | 58.4 dB, alpha included |
  | Encode the uncompressed rock texture | 28.9 dB (naive baseline: 25.1 dB) |

  A slower search that nudges each endpoint up or down one step at a time, starting from our encoder's endpoints, gained only 0.15 dB (to 29.0 dB). So the encoder sits at a local optimum, and more search isn't worth the cost.
- **DXT1 transparency:** DXT1 encoding treats alpha below 128 as transparent black. This is DXT1's 3-colour mode, and decoding gives alpha 0 wherever a block uses it.
  - Measured transparent pixels: `normalmap.dds` 0, `specular.dds` 0, `sky.dds` 0 (all 6 faces), SMF minimap 0. Step 5 must count the SMT tiles too.
  - **Rule for Phase 2:** any pixel written into a DXT1 texture, by import or by brush, gets alpha 255. Pixels that aren't touched keep their stored alpha. Otherwise painting over a transparent pixel with a brush that keeps alpha would leave it transparent black.
- **`PngWriter`** is a minimal RGBA writer. Step 11 adds reading and 16-bit support.
  - `MapTool dds-info <file.dds> [--png out.png --mip N --face N --no-alpha]` describes a texture and can export one level as PNG.
- **For Step 6:** the decoded normal map shows fine noise across the sea floor and the cliffs. That suggests it holds detail beyond the 769² heightmap; Step 6 will measure it.
- **`build.ps1 -Filter A,B`** now splits on commas. `powershell -File` passes `A,B` as a single string.
- **How the engine finds tile files.** Checked against BAR's RecoilEngine source on GitHub (`rts/Map/SMF/SMFGroundTextures.cpp`, `LoadTiles`, and `rts/Map/MapInfo.cpp`):
  - mapinfo's `smf.smtFileName0..N` are read in order until one is missing. They replace the SMF's names **only when there is exactly one per tile file**; otherwise the engine warns and uses the SMF's names.
  - Each name is tried in the SMF's folder first (`maps/` + name), then from the archive root.
  - The engine reads as many tiles from each file as the **SMF** says. The `.smt` header's own count is ignored; only its magic text, version, tile size and compression are checked.
  - Tile mip offsets are 0, 512, 640 and 672 bytes.
  - `MapPackage.ResolveSmtEntries` follows these rules.
- **mapinfo can replace SMF sections with image files:** `smf.minimapTex`, `metalmapTex`, `typemapTex` and `grassmapTex`. When set, editing the SMF's copy has no effect in game.
  - This map sets none of them.
  - `MapPackage.TextureOverrides()` reports them, and `MapTool info` prints a note.
  - **Step 9 (metal) and Phase 2 must check this** before editing those layers.
- **Acidic Quarry's tiles:** 2,359,294 of 2,359,296 blocks are stored in DXT1's 3-colour mode (the normal map: 1.9%). The encoder now tries both 3-colour and 4-colour modes for opaque DXT1 blocks and keeps the better; 99.7% of random 3-colour blocks reproduce exactly. There are 0 transparent pixels in the tiles.
- **`MapTexture`** handles edits tile by tile:
  - Only the 4×4 blocks an edit touches are rewritten, in each tile at all 4 mip levels.
  - A tile shared by several positions is copied (appended to the last `.smt`) before editing, and the SMF's tile counts are updated.
  - Painted pixels are stored opaque.
  - It does not de-duplicate identical tiles.
  - **Rule:** the `.smt` files and the `.smf` must always be saved together (`MapPackage.SaveTexture`). Editing a shared tile changes both the tile file and the SMF's tile index and counts.
  - **Rule: one `SmfFile` per editing session.** `SaveTexture` and `SaveItems` each write the whole `.smf`, and the metal and height saves will too. If two separately loaded `SmfFile` copies are edited, whichever is saved last silently discards the other's changes.
    - Load the SMF once and pass that same instance to every layer (`LoadTexture(smf)`, `LoadItems(smf)`, metal, heights).
    - Write it once, at the end.
    - The Milestone 1 `edit` command and the Phase 2 document model must follow this.
- **Minimap:** the map's own minimap shows a smooth water surface where the texture shows the rocky sea floor. It looks like an in-game render with the water drawn on top. Rebuilding it fully from the texture gives only 24.8 dB against the original.
  - So `Minimap.Rebuild` defaults to the edited region only.
  - **For Step 17:** edits under water will show the sea-floor look on the minimap. Consider tinting under-water pixels with mapinfo's water colours when rebuilding.
- **New `MapTool` commands:** `export-texture <archive> <out.png> [--width N]` and `export-minimap <archive> <out.png>`. `info <archive>` now also reports the texture, its tile files and any texture overrides.

---

## Phase 0 — Setup

**Step 1. Create the projects and fix the reference parser.** ✅
- Create the four projects and a `build.ps1`, then confirm the build works offline.
- Make a `testdata/` folder holding a working copy of the map.
- Fix the Python reference parser: the header is 80 bytes, followed by the grass extra header. Keep it next to the tests.
- Optional: `git init` so we can roll back.
- *Done when:* `build.ps1` builds all projects with no network access.

## Phase 1 — File formats (`MapCore` + `MapTool`, no UI)

**Step 2. Archive layer.** ✅
- Open `.sd7` (extract with 7z) or `.sdz` (ZipArchive) into a workspace folder. Find `mapinfo.lua` at the root, and from it the `.smf`.
- Write archives back out: non-solid 7z (`-ms=off`, matching the original) or zip. `mapinfo.lua` goes at the root and paths use `/`.
- **Watch out:** 7-Zip restores `desktop.ini` as a Hidden + System file. .NET's default folder listing skips those files, so always list with `AttributesToSkip = 0`.
- *Done when:* extracting, repacking and extracting again gives an identical file tree. Check this against 7-Zip's own listing of the original archive, not against our own folder listing.

**Step 3. SMF read/write.** ✅
- Covers the header, extra headers (grass parsed; unknown types kept as raw bytes), heightmap, type map, minimap, metal map, tile index plus SMT names, and features.
- *Done when:* saving with no edits is byte-identical, and every value matches the Python parser.

**Step 4. DXT1/DXT5 codec and DDS read/write.** ✅
- Covers DXT1, DXT5, uncompressed 32-bit, full mipmap chains, and cubemap pass-through.
- Re-encoding works per block and only on the regions that changed, and it runs in parallel.
- *Done when:*
  - an untouched round trip is identical;
  - re-encode quality (PSNR) is near the original's;
  - a full 6144² encode takes a few seconds.

**Step 5. SMT tiles ↔ diffuse texture.** ✅
- Decode tiles into a full-texture view.
- On save, re-encode only changed tiles, plus their mip levels.
- The minimap is rebuilt from the diffuse texture: 1024², DXT1, with mipmaps.
- **Finding the tile files:** `mapinfo.lua` can name them with `smf.smtFileName0`, `smtFileName1`, and so on. As far as I know, the engine uses those in place of the names stored in the SMF.
  - The mapinfo names count from the archive root (`maps/AcidicQuarry.smt`). The SMF's names count from the SMF's own folder (`AcidicQuarry.smt`).
  - So: look up through mapinfo first, and fall back to the SMF's name next to the `.smf`.
  - On this map both point to the same file. Confirm the engine's behaviour while doing this step.
- Count DXT1-transparent pixels in the decoded tiles; see the transparency rule under Step 4's notes.
- *Done when:* editing one tile changes only that tile's bytes and its entries in the tile index.

**Step 6. Find out how the normal map and splat map work. (Research.)**
- **Normal map:** decode `normalmap.dds` and compare it with normals calculated from the heightmap. Work out:
  - which colour channel holds which axis;
  - whether it holds detail beyond what the heightmap has.

  That decides the rule used after a height edit:
  - **Default:** regenerate only inside the edited region, blending into the untouched normals at the edges.
  - **Never:** regenerate the whole map.
- **Metal-spot visuals:** sample `splat_distr` and the diffuse texture at a metal spot. That shows how the "iron" look is drawn, so moving a spot can move its look too.
- *Done when:* both findings are written into this file. ✅

**Step 6 findings** (measured with `tools/MapResearch`; rerun with `MapResearch normals|splat <archive> <out-folder>`):

*How the engine uses these textures.* Read from RecoilEngine `SMFFragProg.glsl`, `MapInfo.cpp` and `SMFReadMap.cpp`:
- mapinfo `resources.detailNormalTex` is loaded as `blendNormalsTex` (shader option `SMF_BLEND_NORMALS`).
- The shader computes `normal = normalize(mix(base, STN * (tex.rgb*2-1), tex.a))`.
  - `base` is the normal the engine builds itself from the **current** heightmap (`normalsTex`). The engine rebuilds it whenever heights change.
  - `STN` axes are R → +x (east), G → +z (south), B → along the base normal.
  - DXT1's alpha is 1, so the texture fully replaces the base.
- `resources.splatDistrTex` channels weight `splatDetailNormalTex1`–`4` (R → 1, G → 2, B → 3, A → 4).
- `mapinfo.lua` names them: R = `moltenmetal.dds`, G = `rock_holes.dds`, B = `raw_iron_metal_spots.dds`, A = `rugged_rock.dds`.

*Normal map (Acidic Quarry):*
- **Orientation:** stored upside down (rule 7). Without the flip, it doesn't line up with the terrain at all (correlations around 0.05).
- **Axes:** after flipping, R follows +x (correlation 0.48), G follows +z (0.47) and B follows up (0.36). These are the same axes the shader uses.
- **Content:**
  - **A world-space bake of the terrain slopes.** Averaged per heightmap square, it is 21.9° (mean) from the heightmap normal.
  - **Height scale used for the bake:** baked tilt ÷ heightmap tilt is 0.76 (median) using mapinfo's in-game range. The SMF header's range would give 0.33, so it was baked from roughly the in-game heights.
  - **Strong detail finer than the heightmap:** mean 25.8° within each square on land, 45.3° on the sea floor. Plateau tops are smooth apart from faint outlines around the metal patches.
- **In game:** with the engine's default terrain shader, the slopes are counted twice, once by the base normal and once by the bake. I haven't checked whether BAR replaces that shader. The rule below works either way:
  - If the texture is applied in tangent space, rotating keeps the map's current look.
  - If it replaces the normal in world space, rotating gives correct new slopes.
- **Rule for height edits (Steps 13 and 17):**
  - **Where heights change:** rotate each normal-map pixel by the rotation that takes the old heightmap normal at that pixel to the new one.
  - **What that gives:** the fine detail is kept and the slope part moves with the terrain.
  - **Where heights didn't change:** the rotation is the identity, so there are no seams and no edge blending is needed.
  - **What gets rewritten:** only the touched DXT blocks and their mips.
  - **Which maps:** only maps whose normal map is a world-space bake. Detect this when loading: after flipping, R correlates with x and G with z at more than about 0.2. A pure tangent-space detail map (correlation near 0) is left alone, because the engine's base normal already follows the new heights.
  - **Default: the full rotation, unscaled.**
  - The 0.76 tilt ratio is **biased low**. It was measured on 8×8-averaged normals, and averaging detail of 26–45° shrinks their tilt. So it is a lower bound, not the bake's scale, and shouldn't be used as a scaling factor.
  - The in-game check settles whether the full rotation looks right.

*Metal-spot look (Acidic Quarry):*
- **Splat channels** (whole-map histogram):

  | Channel | Detail texture | Mean | Max | Pixels above 0 |
  |---|---|---|---|---|
  | Red | `moltenmetal.dds` | 32.7 | 255 | 29.7% |
  | Green | `rock_holes.dds` | 15.8 | 255 | 20.9% |
  | Blue | `raw_iron_metal_spots.dds` | 1.4 | 5 | — |
  | Alpha | `rugged_rock.dds` | 255 | 255 | 100% |

  - Blue is **effectively unused**, despite its name.
  - Alpha is 255 everywhere.
- **What draws a spot:**

  | Distance from spot centre (units) | Splat red | Splat green | Texture brightness (luma) |
  |---|---|---|---|
  | 0–8 | 181 | low | 156 |
  | 24–32 | 88 | 123 | 117 |
  | 48–64 | 16 | 158 | 105 |

  - The splat red channel ("molten metal" detail) forms a blob at the centre, with a matching hole in green ("rock holes").
  - The texture has a brighter, irregular light patch about 60×45 units across.
  - The normal map shows a faint outline.
- **"Move the look with the spot" (Step 15):**
  - Swap circular patches of radius about 48 units, blended over 32–48 units, between the old and new positions.
  - Do it in the texture (`.smt` tiles), the splat map and the normal map, in map orientation.
  - Swapping keeps both places plausible when both are on similar ground (plateau tops).
  - Warn when the destination's height, slope or water depth differs.
  - Measure the blob radius per map (from the splat channel's peak around the spot) rather than hard-coding 48.
- **Correction to Step 4:** the visual check there said the normal map and splat map "look right". They did decode correctly, but I missed that both are upside down relative to the map.

**Step 7. `mapinfo.lua` read and in-place patch.** ✅
- A small Lua-table tokenizer, enough to read:
  - name and version;
  - `smf.minheight`/`maxheight`;
  - `maxMetal` and `extractorRadius`;
  - start positions;
  - colours: fog, sky, sun, water and lighting;
  - texture file names.
- Edits change only the value text, so comments and layout survive.
- Show a warning when `mapconfig/mapinfo/*.lua` overrides values, as this map's `0_apply_options.lua` does.
- *Done when:* changing one value changes only that value in the file. ✅

**Step 7 notes:**
- **Code:**
  - `src/MapCore/Lua`: a lexer for Lua 5.1 tokens (strings with every escape, long strings and comments, hex and exponent numbers) and a table-constructor parser that records each value's source position.
  - `MapInfo` builds on them.
  - The table is found by following the last `return`: either a table returned directly, or a `local name = { ... }` that is returned by name.
  - Anything that isn't a plain literal or table (arithmetic, calls) is kept as text and is read-only.
- **Reading:**
  - Keys match case-insensitively, as the engine does after mapinfo's `lowerkeys`; when a key repeats, the last one wins.
  - Paths use dots: `smf.minheight`, `atmosphere.fogColor`, `teams.0.startPos.x`.
  - Typed values: name, version, author, map file, maxMetal and extractorRadius (engine defaults 0.02 and 500 when missing), smf height range, tile-file names, image overrides, resource texture names, start positions, and number lists such as colours.
- **Editing:**
  - Numbers, strings (keeping the original quote style and escaping) and number lists, each replaced in place.
  - Missing keys and tables are inserted after the table's last field, using its indentation or its inline style.
  - The file's encoding (UTF-8, or Latin-1 when not valid UTF-8) and its byte-order mark are kept.
- **Overrides:** `FindOverrides` reports `mapinfo.<path> = ...` assignments after the table, plus the assignments and returned tables in `mapconfig/mapinfo/*.lua`. The folder is only checked when mapinfo.lua merges it, as the standard template does.
  - On Acidic Quarry, `0_apply_options.lua` may change `water.damage`, `water.absorb`, `water.basecolor`, `water.mincolor`, `water.surfacecolor`, `terraintypes.*.movespeeds.*`, `smf.minheight` and `smf.maxheight`.
  - These are conditional on map options, so they are reported, not applied.
  - **Phase 2 should flag those fields in the UI.**
- **`MapPackage`** now reads the map file, tile-file names and image overrides through `MapInfo`. If mapinfo.lua can't be parsed, opening still works and falls back to the only `.smf`.
- **`MapInfo.SetFromText(path, input)`** sets a value from typed text and keeps the value's type:
  - version "5.17" stays text;
  - number fields reject text;
  - colours take `a,b,c`;
  - a missing key takes its type from the input;
  - expressions are refused.

  `DisplayText` formats values for display. Both are for the Phase 2 UI too.
- **`MapTool set <archive> <new-archive> path=value ...`** uses `SetFromText`. It warns when the new archive keeps the original's name and version (rule 2); Step 17 adds the full check.
  - Example output: `out/acidicquarry_bluefog.sd7`, named "AcidicQuarry Blue Fog 5.17-bluefog", with blue fog and water damage 0.
  - Comparing with the original shows only `mapinfo.lua` changed, and the text diff shows only those four values.
  - Because the name differs, it can sit beside the original in BAR's maps folder, which makes an optional early in-game test.

**Step 8. Items: features, start positions, grass.** ✅
- **SMF features:** engine trees `TreeType0`–`15` and `GeoVent`.
- **Featureplacer `set.lua`:** `objectlist`, `unitlist` and `buildinglist`. Each entry is `name, x, z, rot`, with rotation in −32768…32767.
- **Feature palette:** this map's `features/*.lua`, the engine trees, and any name you type.
  - BAR isn't installed here, so I can't list BAR's built-in features automatically.
  - Optional: you point the editor at a BAR install and it lists them from there.
- The grass density map is an editable layer.
- *Done when:* reading and writing round-trip cleanly, and an added rock shows up in a correctly formatted `set.lua`. ✅

**Step 8 notes:**
- **Engine facts** (RecoilEngine `FeatureHandler.cpp`, `FeatureDefHandler.cpp`, `GrassDrawer.cpp`):
  - SMF features are placed at **ground height**; the stored y is ignored.
  - Their `rotation` is cast to a 16-bit heading, 65536 per full turn.
  - A type with no definition is skipped silently.
  - SMF type names may be any feature the game or map defines, not only trees. Names containing `treetype` or `geovent` get engine defaults when nothing else defines them.
- **BAR facts** (BAR repository):
  - BAR ships no `LuaGaia`, so **the map's own** `LuaGaia/Gadgets/FP_featureplacer.lua` runs. It reads `mapconfig/featureplacer/config.lua`; on Acidic Quarry that file includes `set.lua`.
  - Features go to ground height + 5, with heading `rot`.
  - Units get the same heading.
  - Buildings pass `rot` as a **facing** (0–3).
  - `luarules/gadgets/cmd_feature_placer.lua` is BAR's in-game brush tool, not a loader.
- **Grass:**
  - The engine treats any non-zero value as grass.
  - BAR draws grass itself (`luaui/Widgets/map_grass_gl4.lua`): 0 = none, 1 = old-style on/off grass, 2–254 = blade size from `grassMinSize` to `grassMaxSize`.
  - `mapinfo` `custom.grassConfig` tunes it, and `grassDistTGA` replaces the map's grass entirely.
  - The editor paints values 1–254. Acidic Quarry has no grass.
- **Code:**
  - `LuaDocument` (`src/MapCore/Lua`) is the shared in-place Lua editor: get/set by path, `SetFromText`, `Append`, and `Remove` (which takes the whole line when the entry sits on its own line). `MapInfo` is now a typed layer on top of it.
  - `FeaturePlacerConfig` follows `return VFS.Include(...)` and reads/edits `objectlist`, `unitlist` and `buildinglist`.
  - `MapItems` puts SMF features and featureplacer entries in one list, with move, rotate, add and remove. Its index is 0-based within each list. It is meant for the Phase 2 UI.
  - `FeatureCatalog` lists the map's `features/*.lua` definitions (checking each model under `objects3d/`) plus the engine default trees and vent.
  - `GrassLayer` handles painting grass; `MapInfo.SetStartPosition` and `RemoveStartPosition` handle start positions.
  - `MapPackage.LoadItems` and `SaveItems` save the SMF and the featureplacer file together.
- **Catalog limit:** BAR's own feature definitions (trees, wrecks, and so on) aren't in the map archive, so they aren't listed; any name can still be typed. **Optional later:** point the editor at a BAR install to list them.
- **`MapTool items <archive>`** lists placed objects, placeable names and grass. Editing items, heights, metal and texture from the command line comes as one combined `edit` command at Milestone 1, so every kind of edit shares one save path.

**Step 9. Metal-spot model.** ✅
- Find spots as connected clusters of metal pixels.
- Operations: move, add, delete, and set value or size. Each one rewrites the metal map.
- Estimated income per spot: Σ(metal map value × `maxMetal`) × 0.001 for a T1 extractor, within `extractorRadius`. **This formula is from memory: confirm it against the engine's metal map and extractor code before relying on it.**
- Check mapinfo `smf.metalmapTex` first (`MapInfo.TextureOverrides`). If it is set, the game uses that image and not the SMF metal map.
- *Done when:* moving a spot by command line leaves the other spots unchanged and the total metal the same. ✅

**Step 9 notes:**
- **Engine facts** (RecoilEngine `MetalMap.cpp`, `ReadMap.cpp`, `ExtractorBuilding.cpp`, `UnitDef.cpp`, `Unit.cpp`):
  - A metal cell (16×16 units) holds `raw × maxMetal`. The scale is `maxMetal` itself, **not** `maxMetal ÷ 255`.
  - An extractor's range is mapinfo `extractorRadius`, for every extractor.
  - It takes `extractsMetal × (raw × maxMetal)` from every cell whose **centre** is closer than the radius. That is credited as `metalExtract × 0.5` every half second, so `metalExtract` metal per second.
  - Engine defaults when mapinfo is silent: `maxMetal` 0.02, `extractorRadius` 500.
  - The formula from memory was right.
- **BAR facts:**
  - `armmex`'s `extractsmetal` is 0.001. Each Acidic Quarry spot gives 9 × 246 × 1.04 × 0.001 = **2.30 metal/s**, or 55.3 for all 24.
  - BAR's spot finder (`common/upgets/api_resource_spot_finder.lua`) joins metal cells by 8-way adjacency, but **skips the outermost ring of cells** (16 units from each edge).
  - Its spot centre is the middle of the bounding box, and its "worth" is Σ `raw × maxMetal`.
  - Any spot wider than 6 × `extractorRadius` makes it treat the map as a "metal map" with no discrete spots.
  - `luarules/gadgets/map_metal_spot_placer.lua` fills metal from `mapconfig/map_metal_layout.lua`, but **only when the SMF metal map is completely empty**.
- **`MetalLayer`** (`src/MapCore/Metal`):
  - Finds spots as BAR does; a Python port of BAR's strip-based finder agrees on all 24.
  - Computes income with the engine formula.
  - Moves a spot whole on the 16-unit grid, keeping its cell values, so the total is unchanged.
  - Adds spots using the map's most common spot shape (`TypicalPattern`), removes them, and sets income by scaling cell values. Values above 255 are refused.
  - Refuses cells in the outer ring and spots whose cells would touch other metal, which BAR would merge.
  - Becomes read-only when mapinfo sets `smf.metalmapTex`.
  - Warns about `map_metal_layout.lua` and about metal in the edge ring.
- **`MapEditSession`** (`src/MapCore/Editing`): loads the SMF **once**, shares it with metal, items and mapinfo, applies text operations, and saves each file once. It is the start of the Phase 2 document model.
- **`ResizeSpot` / `metal-resize`** rebuild a spot as an n×n square with the same income. On this map a 3×3 spot tops out at 9 × 255 × 1.04 × 0.001 = 2.39 metal/s, so higher incomes need a bigger spot. Sizes the extractor can't fully reach are refused.
- **`MapTool edit <archive> <new-archive> <ops...>`** supports `set:`, `start-move:`, `metal-move/add/remove/income/resize:` and `item-move/rotate/add/remove:`. `set` is now shorthand for `edit` with `set:` operations.
- **Limitation until Step 15 (now lifted):** metal operations changed only the metal map, so a moved spot's **look** (the splat "molten metal" blob and the light texture patch) stayed at the old position. `metal-move:...:look` now moves it too.
  - **Height (Step 10) and texture painting (Steps 11/14) operations will be added to this same command**, so Milestone 1's `test_b` can be produced in one run.
- **`MapTool metal <archive>`** lists spots with T1 income.
  - Example: `out/acidicquarry_metalmoved.sd7` ("AcidicQuarry 5.17-metalmoved") has one spot moved from (1000, 872) to (1064, 904). Comparing it with the original shows only `mapinfo.lua` and the `.smf` changed.

**Step 10. Height model.** ✅
- Floating-point heights in a working buffer; quantised to 16 bits on save.
- If edits go above or below the height range, widen it and requantise every sample so untouched terrain stays where it is. Then write the new range to both `mapinfo.lua` and the SMF header.
- Operations: raise/lower, scale or offset everything, flatten an area.
- *Done when:* raising one region leaves all other heights unchanged within 1 quantisation step (about 0.007 game units).
- **Moved here from Steps 13/17:** the normal-map rotation rule from Step 6. A height edit also rotates `detailNormalTex` pixels in the edited area. Without it, Milestone 1's in-game check of a raised plateau would judge flank lighting that is known to be out of date.
- Height edits become `MapTool edit` operations.

**Step 10 notes:**
- **Engine facts** (`SMFReadMap.cpp`, `SMFMapFile::ReadHeightmap`):
  - `height = min + raw × (max − min) / 65536`, computed in float.
  - The divisor is 65536, so raw 65535 is one step **below** the maximum.
  - mapinfo `smf.minheight` and `smf.maxheight` replace the header's values **independently** (`KeyExists` for each).
- **`HeightMap`** (`src/MapCore/Heights`):
  - Holds float heights and decodes exactly as the engine does.
  - `Brush` gives full strength within radius × hardness, with a cosine falloff to the radius.
  - Operations: `Raise` (negative values lower), `Flatten`, `Smooth`, and whole-map `Offset` and `Scale`.
  - **`Write`, range unchanged:** re-quantises only the edited vertices, so every other raw value is kept exactly.
  - **`Write`, range exceeded:** widens the range to whole numbers and re-quantises everything. Measured: untouched terrain moves at most 0.004 units, against a step of 0.0081.
  - **Where the new range goes:** the SMF header, plus mapinfo `smf.minheight`/`maxheight` where those already exist.
  - `AcceptChanges` resets the baseline after a save, so a second save doesn't rotate the normals twice.
- **`DetailNormalMap`:**
  - Measures whether the normal map is a slope bake: R correlates with the heightmap's x and G with its z, sampled every 8 pixels. Acidic Quarry scores 0.48; the threshold is 0.2.
  - Rotates pixels by the old-to-new heightmap normal (Rodrigues).
  - Re-encodes only the touched blocks and their mips, with the upside-down region conversion.
  - Detail-only normal maps are left alone.
  - A visual check shows a smooth, correctly oriented slope ring with the detail kept.
- **`MapEditSession.Save()`** order: normal map, then heights into the SMF, then the SMF written once, then mapinfo and featureplacer. It returns notes.
- **`MapEditSession.Heights`** loads on first use. `ResolveResource` looks up resource names as the engine does: as given, then under `maps/`.
- **Edit operations:** `height-raise` (negative amounts lower), `height-flatten`, `height-smooth`, `height-offset`, `height-scale`.
  - Example: `out/acidicquarry_raised.sd7` (version "5.17-raised") raises the NW base by 80.
  - Only `mapinfo.lua`, the `.smf` and `normalmap.dds` change. It takes about 14 s, mostly the 7-Zip packing.

**Step 11. PNG import/export.** ✅
- Formats: 16-bit greyscale for heights; 8-bit RGB(A) for diffuse, metal, splat and grass. This lets you edit in GIMP or Photoshop.
- *Done when:* PNG files survive export and re-import unchanged, and they open in a normal image viewer. ✅

**Step 11 notes:**
- **`PngReader`** handles every colour type and bit depth, all five row filters, Adam7 interlacing, tRNS (palette alpha and colour key) and tEXt chunks, and checks CRCs.
  - Tested against `tools/png_reference.py`, an independent Python encoder.
- **`PngWriter`** writes RGBA 8-bit and greyscale 8- and 16-bit, choosing a filter for each row, with optional tEXt.
- **Layers**, via `MapEditSession.ExportLayer` / `ImportLayer`, `MapTool export <archive> <layer> <png>` and `edit ... import:<layer>=<png>`:

  | Layer | PNG format | Notes |
  |---|---|---|
  | `heights` | 16-bit grey | Raw SMF values; tEXt `BarMapEditor.HeightRange` holds "min max" and is used on import; 8-bit images are accepted with a warning |
  | `texture` | RGBA | Full resolution |
  | `metal` | 8-bit grey | Raw values |
  | `grass` | 8-bit grey | 255 is clamped to 254 |
  | `resource:<key>` | RGBA | Any editable single-face `.dds` named in mapinfo `resources`, **in map orientation** (rule 7) |

- **Imports change only what differs:**
  - `MapTexture.UpdateFrom` and `DdsFile.EncodeChanged` re-encode only the differing 4×4 blocks, then rebuild mips over the changed area.
  - `HeightMap.SetAll` marks only the vertices that differ.
  - Re-importing every unedited export leaves **every file byte-identical**.
  - Written DXT1 pixels are made opaque (Step 4 rule).
- **`texture-paint`:** brush colour blending. Only touched blocks are re-encoded, and the minimap is redrawn over the brush area.
- **Session stores:** `MapEditSession` caches the decoded texture and loaded `.dds` resources. `Save` writes heights, then the normal map, tiles and resources, then the SMF once, then mapinfo and featureplacer. The detail-normal update goes through the same resource cache, so an imported normal map and a height edit in one session combine correctly.

### 🎯 Milestone 1: unchanged repack + command-line edits — **you test in BAR**

**Status (2026-10-14):** archives built and **waiting for your in-game test**. See `out/milestone1/CHECKLIST.md`.
- `acidicquarry_m1_a.sd7`: unchanged repack; its contents are identical to the original.
- `acidicquarry_m1_b.sd7`: built with one `MapTool edit` run.
  - Changed files: `set.lua`, `mapinfo.lua`, the `.smf`, the `.smt` and `normalmap.dds`.
  - The minimap shows the painted patch, and the listings confirm every item and metal change.

Phase 2 continues meanwhile. Any in-game problem found here gets fixed in `MapCore` first.
- `MapTool` produces:
  - `acidicquarry_test_a.sd7`: a repack with no edits.
  - `…_b.sd7` contains:
    - one plateau raised;
    - one metal spot moved;
    - one rock moved;
    - **one rock added to `set.lua`**;
    - **one `agorm_rock1` added to the SMF feature list**;
    - the fog colour changed;
    - one clearly visible patch painted on a plateau top, with its minimap area redrawn.

    The map has no SMF features today, so the SMF rock is the only in-game check that the engine loads a features section the editor wrote. It uses a feature the map itself defines, so the check doesn't depend on how BAR draws the engine's default trees.
- **Your checklist:**
  1. Does the map appear in the lobby?
  2. Does a skirmish start?
  3. Are the changes visible? That means the raised plateau, the moved spot (in the metal view), the moved rock, both added rocks, and the fog.
     - The moved spot's **look** stays at the old position until Step 15. That's expected, not a bug.
     - What to check instead: a mex placed on the new position earns about 2.3 metal/s, and nothing can be extracted at the old position.
  4. Does the painted patch look right **both up close and fully zoomed out**? The game draws distant ground from the tiles' smaller mip levels, so the zoomed-out view is the only check of the rebuilt mips.
  5. Does the patch show on the minimap?
  6. Are there map errors in `infolog.txt`?

## Phase 2 — Editor UI (`MapEditor`)

**Step 12. Window, viewport and undo.**
- Open / Save As (asks for new name and version), with loading in the background and a progress bar.
- **Viewport:** 2D top-down with pan and zoom. Rendering uses a tile pyramid so the 6144² texture stays smooth.
- **Layers:** texture, hillshaded height, metal, splat, normals, grass.
- **Overlays:** water line, metal spots, features, start positions, grid.
- **Status bar:** cursor position, height, metal value.
- **Undo/redo:** snapshots of the changed region.

*Done (2026-10-14):*
- **Undo/redo (`MapEditSession.History.cs`):** `Apply` snapshots exactly what an operation can change, just before it runs (the brush's vertex box, the painted texture region plus the minimap, the metal map, the item stores, mapinfo text, a .dds resource's levels). A failed operation is rolled back. `BeginGroup`/`EndGroup` make a brush stroke one step. 100 steps. `Changed` reports the layers and world area touched; `Save` raises it too (the rotated normal map, a widened height range).
- **Renderer (`MapCore/Rendering`):** every layer is an image pyramid in map orientation, built when first shown and updated only over the changed area. Pixel *i* of every layer covers world [i·s, (i+1)·s) (texture 1 unit, hillshade 4, metal 16, grass 32, .dds by fraction of the map), so overlays line up. Overlays and label positions are drawn in MapCore; the window only copies the image (swapping red and blue for GDI) and draws the label text.
  - Layers: texture, heights (hillshade lit from the north-west, ×2 relief, height-coloured, blue under water), metal and grass (over dimmed hillshade), splat (RGB), normals, minimap.
  - Overlays: water line (height 0 crossings), metal spots (extractor radius from mapinfo: 90 on this map, with income labels), items (heading tick; SMF orange, featureplacer green), start positions, 512-unit grid.
  - The normals view shows the stored normal map, which follows height edits when saving; the view says so while heights have unsaved edits.
  - The large .dds layers decode in the background when chosen and are dropped when another layer is shown.
- **Window (`MapEditor`):** File (Open, Save As, Close), Edit (Undo/Redo, naming the step), View (Ctrl+1…7 layers, overlays, zoom). Toolbar with the same. Status bar: cursor x/z, height, metal value, what an extractor there would make, grass; zoom; the layer's note. A message list logs opens, saves and notes. Drag and drop of `.sd7`/`.sdz`. Wheel zooms about the cursor; right or middle drag pans (the left button too with the Pan tool, see Step 13); arrows, +/−, Home.
  - Open and Save As run in the background with a progress bar; meanwhile the view shows its last frame and every command is off, since the session isn't thread-safe.
  - Workspaces: `%LOCALAPPDATA%\BarMapEditor\workspaces\<guid>`, deleted on close (only folders inside that root). Leftovers after a crash are offered for recovery at the next start (Step 19).
  - Save As asks for name, version and file; refuses the original name *and* version (compared with what was loaded) and the source archive; suggests `<name>_<version>.sd7` next to the last saved file. The name/version change is one undo step. If packing fails after the workspace was saved, the window still treats the edits as unsaved.

**Step 13. Height tools.**
- **Brushes:** raise, lower, smooth, flatten, set-to-height and noise. Each has radius, strength and falloff.
- **Whole map:** raise, lower or scale everything.
- A water-level preview, and heightmap PNG import/export.

*Done (2026-10-14):*
- **Tool panel** (right of the map): Pan, Raise, Lower, Smooth, Flatten, Set height, Noise; radius (8–3000 units), strength, hardness, target height, bump size. Keys with the map focused: P R L S F H N, `[` `]` for the radius.
- **Mouse:** the left button runs the tool; right or middle drag pans. The brush outline shows the radius, with the full-strength core dashed.
- **Strokes:** dabs a quarter of the radius apart, plus 10 per second while held still (raise, lower, smooth, noise). One stroke is one undo step named after the tool. A stroke also ends when the view loses the mouse or the focus, so a step is never left open.
- **Strength:** raise, lower and noise move up to 10 units per dab at 100%; smooth, flatten and set height pull that fraction of the way per dab.
- **Flatten** pulls towards the height where the stroke starts. **Set height** pulls towards the target box; Ctrl+click picks the height under the cursor.
- **Noise:** smooth gradient noise fixed to the map (repeatable), bumps of the chosen size, up to the amplitude.
- **Sea-level preview:** view only (the heights, metal and grass views and the water line); "Apply as height shift" makes it one `height-offset` step. Whole map: raise/lower all, scale all (sea level stays).
- **File › Import layer / Export layer:** heights (16-bit), texture, metal, grass, splat, normal map, in the background, each import one undo step. A heights value that rounds to the current height counts as unchanged, so re-importing an export changes nothing.
- **MapCore:** `height-noise` operation, flatten strength, smooth hardness; typed operations (`RaiseHeights`, `FlattenHeights`, …) with invariant number formatting.

**Step 13b. 3D view** *(added 2026-10-14 at your request: height and detail need to be seen in 3D)*.
- **View only:** no editing or picking in 3D. It opens as its own window beside the main one, so you can paint in 2D and watch the 3D view update with every stroke.
- **Rendering on the CPU, in `MapCore`,** like the 2D view.
  - This keeps it testable without a window: what the tests render is exactly what you see. No GPU or driver dependence.
  - Budget: under 100 ms per frame at reduced resolution while the camera moves; full resolution once it stops.
- **Detail:**
  - The full heightmap (every vertex) for the still frame; a coarser mesh only while moving.
  - The texture is sampled from the pyramid level matching each pixel's size on screen, so distant ground doesn't shimmer.
  - Lighting uses mapinfo's sun direction and ground colours with live heightmap normals, so unsaved height edits light correctly.
  - The water plane at height 0 uses mapinfo's water colours. Fog and sky colour come from mapinfo.
  - Vertical exaggeration control, 1× by default (true to the game).
  - Optional markers: metal spots, items, start positions.
- **Camera:** orbit, zoom and pan with the mouse, and "look here" from the 2D view.
- **Checks:**
  - A painted patch at a known, asymmetric point shows up where that point projects. Acidic Quarry is nearly symmetric, so this guards against a mirrored view.
  - A raised mound hides the ground behind it.
  - Looking straight down matches the 2D render.

*Done (2026-10-14):*
- **`MapCore/Rendering/TerrainView`:** rasterises the heightmap mesh (two triangles per square) into a depth buffer that keeps, for each pixel, the ground point it shows; bands of the image run in parallel. Shading then samples the texture pyramid at that point, at the level matching the pixel's footprint.
- **Lighting as the engine's ground shader does it** (`SMFFragProg.glsl`, `GetShadeInt`): (ambient + diffuse × cos × shadow) × 210/255, with mapinfo's `lighting` colours and `sunDir`, and `groundShadowDensity` for shadows. The sea bed takes the engine's absorption tint (`water.baseColor − absorb × depth`, at least `minColor`, blended in over the first 10 units of depth).
- **Shadows:** a march towards the sun every 4 units, plus a second map for the water surface (sea level), so underwater bumps don't shade the surface. After a brush edit only the strip the edit can shade is redone (checked against a full rebuild).
- **Approximations:** the water surface is a sunlit layer of `water.surfaceColor`, thicker over deeper water; the engine's reflections and refraction aren't reproduced. Detail textures, specular and the splat detail normals are not drawn. Normals come from the live heightmap, so unsaved edits light correctly.
- **Window** (View › 3D view, F3, or the toolbar's 3D button): beside the main window, view only. Left drag rotates, right or middle drag moves, wheel zooms, arrows rotate, Home gives the overview. Toolbar: Overview, Look at 2D view, height exaggeration (1× default), shadows, water, markers (metal with incomes, start positions, items).
- Follows the 2D view's sea-level preview. Mapinfo edits re-read the colours at once; shadows are redone only when the sun or the sea level moves (a colour slider must not stall). The camera never goes below the ground under it.
- While the camera moves or edits keep arriving, frames use every 4th vertex at half resolution; 0.22 s after things settle, the full-detail frame. Frozen while the map is saved or loaded in the background; closed before the map closes.
- **Checks:** `MapTool render3d <archive> <out.png> [--camera x,z,yaw,pitch,distance] ...` renders the same view from the command line.

**Step 14. Texture and colour tools.**
- **Diffuse:** colour brush, tint/brightness/hue brush, and clone stamp. Full-texture PNG import/export.
- **Splat painting:** which detail texture shows where.
- **Colour panel** for the mapinfo colours: fog, sky, sun, water, ambient and diffuse lighting.
- The minimap is rebuilt on save.

*Done (2026-10-14):*
- **Tool tabs** right of the map: Heights, Texture, Colours. The front tab's tool drives the left button.
- **Texture tools** (keys with the map focused: P pan, B paint, T tint, A adjust, C clone, D splat):
  - **Paint** blends towards the colour; **Tint** recolours but keeps each pixel's brightness, so the texture's detail stays; Ctrl+click picks a colour from the texture.
  - **Adjust:** brightness, saturation and hue (rotation about the grey axis); each dab applies the strength's share, so going over an area builds it up.
  - **Clone:** Alt+click sets the source; the first stroke fixes the offset and later strokes stay aligned, until a new Alt+click. Each dab reads its source before writing, so overlapping source and target don't feed into each other.
  - **Splat:** paints the four detail-texture weights (R, G, B, A, labelled with the map's `splatDetailNormalTex1-4` names) towards the chosen channel and amount, or erases them. Choosing it switches the 2D view to the Splat layer.
- **Splat editing in MapCore:** the session keeps the splat file decoded at every level (file orientation) and re-encodes only the dab's blocks and the mips over them; undo stores just those bytes and pixels (`DdsFile.CaptureRegion`). The splat view refreshes over the dab's area only. The 3D view doesn't draw detail textures.
- **Minimap:** redrawn over each texture edit as it happens, so nothing is left for saving.
- **Colours tab:** sky, fog, sun, clouds, sun direction, ground and unit ambient/diffuse/specular, water surface/plane, sea-bed tint and darkest, water absorb. Numbers plus a swatch (colour picker). Values the map doesn't set show the engine's defaults in grey; editing one adds it. Rows that `mapconfig/mapinfo` code may change are marked "⚠ may be overridden" with the file and key on hover (on Acidic Quarry: the water colours, from `0_apply_options.lua`). Each change is one undo step; the 3D view follows at once.
- **Checking layouts:** the self-test draws each panel to `out/selftest/panel_*.png` without showing a window (creating the controls' handles first). This found clipped labels and buttons, now fixed.

**Step 15. Metal tools.**
- A spot list. Drag to move, add, delete, and change the value. Shows estimated income.
- Optional "move the look with the spot", using what Step 6 finds.
- Warnings for spots placed on a steep slope or under water.

*Done (2026-10-14):*
- **Metal tab:** Select/move (S), Add (A), Pan (P). Click a spot to select it (the selected one wins over a neighbour within reach); drag to move it, with a ghost ring at where it would land (red when the game would refuse it: touching other metal, the map edge). Nothing changes until release, and losing the mouse cancels. Add clicks place a spot shaped like the map's usual one. Delete removes the selected spot. Income (metal/s for a T1 extractor) and size (cells across) are edited in the panel. The list shows every spot with its income and notes; choosing one centres the view on it.
- **Move the look with the spot** (default on) is part of the same operation: `metal-move:<x>,<z>:<x2>,<z2>:look` (also in `MapTool edit`, so the Milestone 1 limitation is gone). One undo step restores metal, tiles, minimap, splat and normal map exactly.
  - The look is centred where its splat blob is (it need not sit on the metal's centre), and its size is measured per map: where the average profile of the molten-metal splat channel and of the texture patch flattens out after its edge (48 units on Acidic Quarry; 48 by default when a map has no such channel).
  - **Nudges** (closer than two look radii) warp the area: everything within the radius of the path shifts by the move, fading out over at least twice the distance moved, so nothing folds or doubles.
  - **Far moves** carry the disc to the new place, and fill the old place from nearby ground that matches its rim (texture and splat, no look of its own, similar height), or, where none does (small plateaus: everything that far out is cliff), rebuild it smoothly from the ground around its rim (a pull-push fill). The result says which, and flags a destination on different ground.
  - All of it in the texture tiles, the splat map and the normal map, each in its own orientation.
- **Undo across several regions:** one texture snapshot (and one .dds region snapshot) now covers all areas of one operation. Separate snapshots per area would each record the tile index from before, and undoing one cut off tile copies the other still pointed at.
- **Spot notes**, checked as the engine checks buildings (`GameHelper.cpp`): every footprint square within `40 × tan(maxslope)` of the build height; BAR's armmex has maxslope 30 and a 4×4 footprint, so 23.1 units. Under water is noted (armmex has no depth limit; only builders that reach it can place one). Spots touching the map edge are noted.
- **Lua metal layouts:** before the first metal edit on a map whose metal map is empty and whose metal comes from `mapconfig/map_metal_layout.lua`, the editor warns that adding metal makes BAR ignore that file. (Converting such a layout into the metal map is possible later work.)
- **Map view:** a pick-and-drag pointer mode and rings drawn over the map, shared with Step 16.

**Step 16. Item tools.**
- Place, drag, rotate, delete and multi-select features. A scatter brush for trees and rocks.
- Drag start positions.
- A grass painting brush.

*Done (2026-10-14):*
- **Items tab:** Select/move (S), Place (A), Scatter (C), Grass (G), Pan (P).
  - **Select:** picks in screen pixels: start positions first, then the selection, then the nearest item. Ctrl+click adds or removes; dragging on empty ground draws a box. Dragging a selected item moves the whole selection; nothing changes until release, and losing the mouse cancels.
  - **Turning:** Q / E or the panel turn the selection by 15°, or set its heading (0° faces south, 90° east, as the map view's tick shows). Buildings turn by whole quarters. Delete removes the selection.
  - **Place** adds the named feature where you click, with a fixed or random heading, to the featureplacer when the map has one (otherwise the SMF list). The name box lists what the map defines and uses; any other name gets a warning that it must exist in BAR.
  - **Scatter** paints random placements, each kept only if it is at least the spacing from every item, including those placed earlier in the same stroke. One stroke is one undo step.
  - **Grass** paints a value (1 grass, 2-254 blade size) or erases; choosing it shows the grass layer. Refused when mapinfo sets `grassmapTex`.
  - **Start positions** are dragged in Select mode. (Adding or removing teams isn't offered: removing one from the middle would leave a gap in `teams`.) A map with `mapconfig/map_startboxes.lua` gets a note that the boxes don't move with the positions.
- **MapCore:** `items-move`, `items-rotate`, `items-heading`, `items-remove` take exact refs (`s3`: SMF feature 3; `f12`, `u0`, `b2`: featureplacer object, unit, building; `MapTool items --all` lists them), and each is a single operation: if one item can't change, none does. `grass-paint`. The editor finds its selection again after every change by store, list, name and position.

**Step 17. Updates and checks on save.**
- Regenerate the normal map and minimap where needed.
- Checks:
  - items under water or off the map;
  - start positions out of bounds;
  - the height range;
  - the name and version have changed from the original.

*Done (2026-10-14):*
- **Checks before saving** (`MapEditSession.CheckBeforeSave`), in the editor's Save As and in `MapTool edit`:
  - **Errors** stop the save: items or start positions off the map; in the editor, the original name and version (the Save As dialog already insists; `MapTool edit` only warns, for test builds).
  - **Warnings** ask first: feature names the map doesn't define (they must exist in BAR), start positions under water, metal spots an extractor may not fit, metal-layout notes.
  - **Notes** go to the log: items under water, starts near the edge, a wider height range, and that saving rotates the normal map and redraws the minimap over reshaped ground.
- **Minimap in the map's own style:** the map's minimap is often an in-game picture (water surface over the sea bed), so a part redrawn from the bare texture stood out. A per-channel linear fit, land and under-water ground apart, is made from the map's own minimap before the session first redraws it and applied to every redraw. (Study: 24.9 dB as is, 27.6 dB with the fit over the whole map; lighting, depth or blur add nothing. Under water, where it matters most, 34.7 vs 26.7 dB.)
- **Minimap where the ground moved:** saving redraws it over the height-edited area, so a raised island stops showing water. The normal map is rotated there as before (Step 10).
- **Only new problems warn:** what the map was like before this session's first edit is recorded. A warning it already had becomes a note ("already so when the map was opened"), so sea maps, maps with BAR's own trees and the like don't nag at every save.
- **Height range and map options:** widening the range warns when `mapconfig/mapinfo` code sets `smf.minheight`/`maxheight`. Acidic Quarry's `0_apply_options.lua` assigns fixed ranges for its water-level options, and with one of those on, the game spreads the stored heights over that range instead.
- **Undo memory** (found in review): every texture dab used to keep the whole minimap (699 KB) and the whole tile index (147 KB), about 255 MB for a long stroke. Now a dab keeps only the tiles, tile-index entries and minimap blocks it touches: 2.4 MB for a 300-dab stroke, still exact. Undo also has a byte limit (512 MB; the oldest steps go first, and the latest always stays).

### 🎯 Milestone 2: full edit session in the UI — **you test the UI and test in BAR**

**Status (2026-10-14):** ready and **waiting for your tests**. See `out/milestone2/CHECKLIST.md`.
- `out/milestone2/editor/`: Release build of `MapEditor.exe` and `MapTool.exe` (its self-test passes 97/97).
- `out/milestone2/acidicquarry_m2_sample.sd7` ("AcidicQuarry M2 Test 5.17-m2"), made with one `MapTool edit` run of the new operations. It has:
  - two spots moved with their looks (a nudge, and a carry with the old place filled);
  - a raised island with noise;
  - a sun-direction change;
  - tint, adjust and clone texture patches;
  - a splat-painted patch;
  - grass;
  - moved, turned and added rocks;
  - a moved start position.
- Previews: `preview_2d_nw_plateau.png`, `preview_2d_centre.png`, `preview_3d_centre.png`.

Phase 3 continues meanwhile.

## Phase 3 — Packaging and finishing

**Step 18. Save As and packaging.**
- Write a new `.sd7` (or `.sdz`).
- Optionally copy it into a BAR `maps` folder you choose.

*Done (2026-10-14):*
- **Save As** has "Also copy it into BAR's maps folder" with the folder (Browse…). The folder and the choice are remembered in `%LOCALAPPDATA%\BarMapEditor\settings.json`. The first time, BAR's usual install places are tried (`%LOCALAPPDATA%\Programs\Beyond-All-Reason\data\maps` and others).
- **Before copying,** every `.sd7`/`.sdz` in the folder is checked for the same mapinfo name and version, which BAR would list as two maps under one name. Its `mapinfo.lua` is read straight from the archive (`ArchiveIO.ReadEntry`), so nothing is extracted. A clash asks first, and so does an existing file of the same name. The copy goes through a temporary file, so a failed copy leaves nothing half-written.
- **`MapTool install <archive> [<maps folder>] [--overwrite] [--allow-same-name]`** does the same from the command line, finding the folder itself when not given.

**Step 19. Robustness.**
- Autosave the workspace, recover after a crash, and handle large files and memory sensibly.
- Test against at least one other map with trees and/or several SMT files, if you can provide one.

*Done (2026-10-14):*
- **Journal instead of autosave.** Every edit is one text operation, and operations are deterministic, so the session writes each one as it happens (apply, undo, redo, start and end of a stroke, save) to `<workspace>.session\session.journal`, flushed at once. Rewriting the 30 MB texture every few minutes would cost more and save less. Imported PNGs are copied into `files\` first, so recovery doesn't need them to still exist.
- **Saved means packed.** A save first writes the workspace, then packs the archive, and packing can fail (disk full, file locked) or be cut off. So the journal gets a `packed` entry only once the archive is written, and only edits after the last `packed` count as unsaved. A Save As that never finished is offered for recovery, and the recovered map still shows unsaved changes.
- **Same start for live and replay.** Some values are worked out on first use from the map as it is at that moment: the metal spots' look radius, the minimap colour fit, the heights (which read mapinfo's height range), and the save-check baseline. The session works them all out when the journal is attached, and the replay does the same before its first entry. Otherwise, whatever the editor happened to show first could change the result (a test checks this: without it, the replay differs).
- **Recovery.** Beside the journal sit `manifest.json` (the source archive, its size and time) and a `lock` file held open while the map is open. At start-up the editor looks for records whose lock it can take (their editor stopped without closing the map). For any with edits since the last save it offers **Yes** (recover), **No** (discard) or **Cancel** (decide next time). Recovery re-extracts the archive into the same workspace and replays the journal, with progress, then carries on recording. Undo works as before, and a stroke the crash cut short ends there. Records with nothing unsaved are cleared away quietly. If the source archive has gone or changed, the edits can't be replayed and it says so (No keeps them in case the file comes back).
- **Large files and memory.** Opening checks free disk space first (the unpacked size plus half again plus 64 MB) and says how much is needed. Undo is capped at 512 MB of snapshots (from Step 12's region snapshots). Running out of memory shows a plain message: the map is as before that step, and everything is in the journal.
- **Second map.** None was supplied, so the tests build one (`tests/MapCore.Tests/SyntheticMap.cs`): 2048×1024 game units (not square), `.sdz`, the texture split over two tile files named through mapinfo's `smtFileName0/1`, SMF trees and geovents but no featureplacer, no splat or normal map, ground under water. A variant sets `smf.metalmapTex`, which makes metal read-only with the reason shown. A real second map is still welcome.

**Step 20. Documentation.**
- A README with usage, the format notes, and known limits.

*Done (2026-10-14):* `README.md` in the repository root, also shipped with the release build. The MapTool example in it was run as written. The figures in it were measured: Acidic Quarry unpacks to 90 MB, and opening and drawing it takes at least 310 MB (measured without the splat decode that recording adds).

**Step 21. Ramp tool** *(added 2026-10-15 at your request: add a ramp and have the editor fill in the slope, the colour and the edges)*.
- **On the map:** a rectangle you can move, lengthen, widen and turn (Heights tab, Ramp tool), then apply with a button. One undo step.
- **Slope:** heights run in a straight line from the low end to the high end. Each end's height is read from the ground where the ramp meets it, and can be overridden.
- **Edges:** the ramp blends into the ground all round over an edge width. *Auto* makes the edge as wide as the largest height difference between the ramp and the ground beside it, so a cut or an embankment averages about 45° rather than a vertical wall.
- **Colour:** the texture (and splat map, when the map has one) along the ramp is filled from the ground just beyond its two ends, blended from the low end to the high end and feathered over the edge.
- **MapCore first:** a `ramp:` operation (undoable, journaled, in `MapTool edit`), so it is testable without a window.
- **Normal map:** not filled. The save step turns it to the new slope as for any height edit, so the ramp keeps the old ground's fine lighting detail.

*Done (2026-10-15):*
- **`ramp:<x1>,<z1>:<x2>,<z2>:<width>:<h1>,<h2>[:<edge>[:<fill>]]`** (`MapEditSession.Ramp.cs`, `Heights/Ramp.cs`), also in `MapTool edit`:
  - Heights and the edge may be `auto`. The editor always writes the actual numbers, so the Undo menu and the recovery journal show what was built.
  - **Heights:** set through the same loop as the brushes (`HeightMap.Apply` over a vertex box and a weight), so saving stores them and turns the normal map as for any height edit. Weight 1 on the ramp, then a cosine falloff over the edge.
  - **Auto edge:** as wide as the largest step between the ramp's surface and the ground along its sides and ends (at least 32, at most 512 units). That averages about 45° (57° at the steepest). A first try at 1.5× made embankments too wide.
- **Fill:**
  - **Source:** each texture pixel along the ramp takes the ground beyond the start, mirrored back over a strip (half the ramp's length, 16–128 units), blended with the same from beyond the end by position along the ramp. It is seamless at both ends.
  - **Feather:** the fill fades out only 24 units past the sides. Over the whole edge, it smeared plateau texture onto the cliffs beside the ramp.
  - **Splat map:** done the same way, in file orientation (rule 7).
  - **Reads before writes:** every source pixel is read before any is written.
- **Texture edits re-encode only the 4x4 blocks where a pixel changed** (`MapTexture.UpdateBlocks`), not every block in the bounding box: re-encoding isn't lossless, and a turned ramp's box is mostly untouched. The brushes benefit too.
- **Editor:**
  - **Tool:** Heights tab, Ramp (`M`). Its settings replace the brush settings while it is chosen, so Apply ramp is in view.
  - **On the map:** a rectangle with an arrow from start to end, the edge dashed, and round handles at both ends (labelled with their heights) and on one side. Handles are picked in screen pixels.
  - **Mouse and keys:** a click elsewhere moves the ramp there; a cut-off drag puts it back. `Q` / `E` turn it 15°, `[` / `]` change the width, `Enter` applies.
  - **Readout:** the panel shows the rise or fall, the angle, the grade and the edge. Heights marked "ground" follow edits and undo. Help › Controls lists the keys.
- **Checked by eye:** `MapTool render3d` of a ramp from the north-west plateau into the acid shows a straight slope with plateau texture fading into the acid floor's.
- **Build to try it:** `out/step21-ramp/BarMapEditor/` (Release; 174/174 tests and self-test 121/121 on Release; the packaged copy builds a ramp with MapTool and draws it), zipped as `out/step21-ramp/BarMapEditor-ramp.zip`. `out/milestone3` is unchanged.
- **Who can go up it** *(added 2026-10-15 at your request)*:
  - **Source:** read from source, not guessed. BAR's `gamedata/movedefs.lua` gives each class's `maxslope`, `slopeMod`, `maxwaterdepth` and footprint. Its `setMaxSlope` divides `maxslope` by 1.5 and sets `slopeMod` 4 for every class named BOT.
  - **Engine (RecoilEngine):**
    - `MoveDefHandler.cpp`, `DegreesToMaxSlope`: 1 − cos(clamp(deg, 0, 60) × 1.5).
    - `ReadMap.cpp`: triangle normals per square; the slope map per 2×2 squares as `mix(min, avg, min/avg)`, stored as 1 − that.
    - `GroundMoveMath.cpp`: blocked when slope > maxSlope or −height > depth; speed 1 / (1 + slope × dirSlopeMod × slopeMod).
    - `HoverMoveMath.cpp`: no slope limit on water.
    - `TestMoveSquareRange`: the terrain test covers the whole footprint.
  - **Code:** `MapCore/Movement`.
    - `BarMoveClasses` holds 24 land and hover classes in six groups: vehicles 27°, armthor 33°, amphibious and T4 vehicles with armamph 54°, bots and commanders 54°, spiders any slope, hovercraft 33°.
    - `TerrainSlope.Cell` is the engine's slope measure.
    - `RampPassability` gives the verdicts and the panel text: a tick or a cross per group, uphill speed, the length needed when too steep, "only just" within 1°, a water-depth note and a width note.
    - The `ramp:` result lists who can and can't climb it, so `MapTool edit` reports it too.
  - **Where it meets the ground** (found in review): the readout first covered only the ramp's surface. But beyond each end the ramp blends back into the ground, and that can be much steeper: an end set below the ground beyond it leaves a step, and an end on a cliff face runs into the cliff.
    - **Measuring:** `RampLipDegrees` measures it before building, from the heights the ramp would leave. Every 8 units from the edge beyond an end to 16 units onto the ramp, it takes the gentlest cell across the width (a unit picks its way between single rough cells), and reports the steepest of those crossings.
    - **Verdict:** a group gets a tick only if the surface and both ends pass. Otherwise the panel says which end blocks it and suggests a fix.
    - **Why gentlest-across:** measuring only the centre line flagged nearly every end on Acidic Quarry's acid floor. By the engine's own measure, 98% of the floor's cells are over 27° and half are over 54°; plateau tops are about 0.2°.
  - **Wording fixes:** "needs N long at this rise", and no "too steep for none".
  - **Checks:** 181/181 tests.
    - Two level areas: a ramp with each end on ground at its height gets no warning. One whose end is 130 below the ground beyond it is flagged at that end, vehicles blocked. The built heights measure exactly what the preview predicted.
    - The class limits equal the engine's conversion of BAR's numbers.
    - Every slope-map cell on a built ramp (straight and turned 30°) measures 1 − cos(angle) to 1e-4, and a level one measures 0.
    - Verdicts at 20°, 26.5° (only just), 30°, 40° and 60°.
    - Hovers cross a ramp under water at any slope, but not partly on land.
    - The depth and width notes.
  - **Self-test 122/122:** at 27.4° the panel says vehicles can't and bots can.

**Step 22. Passability in the 3D view** *(added 2026-10-15 at your request: hovercraft first, then vehicles and bots)*.

*Done (2026-10-15):*
- **`MapCore/Movement/PassabilityMap`:** what each 8-unit square is for one movement class, by the engine's rule (`MoveMath.cpp GetPosSpeedMod` with `GroundMoveMath`/`HoverMoveMath`).
  - **Height and slope:** a square's height is its highest corner (`ReadMap.cpp` maxHeightMap). Its slope is the 16-unit slope-map cell it lies in.
  - **Hovers:** on water (highest corner below sea level) they go anywhere, whatever the slope. Otherwise a square is too steep over the class's limit and, for land units, too deep past their water depth.
  - **Sea-level preview:** heights are taken relative to the previewed sea level, so the preview shows what the game would make of the shifted map.
  - **Updates:** after edits, only the changed area (plus one cell) is redone.
- **3D view:** a **Passable for** box on the toolbar (Off, Hovercraft, Vehicles, Bots). Each ground pixel is blended 45% towards its square's colour: green open, red too steep, purple too deep, cyan a hover on water. The status bar shows that choice's legend.
  - **Hovercraft:** HOVER3, 33° (HOVER2 and HHOVER4 go to the same places; only their speed differs).
  - **Vehicles:** TANK3, 27°, 20 deep (every class in the Vehicles group goes to the same places).
  - **Bots:** land bots (BOT3), 54°, 20 deep. Amphibious bots and commanders share the slope limit but go deeper, and the legend says so.
  - On Acidic Quarry: vehicles 50,126 open squares; bots 145,921 open (they climb much of the cliffs) and 144,671 too deep.
  - On Acidic Quarry: 358,702 squares of water (the acid), 53,658 of open land (the plateau tops), 177,464 too steep (cliffs and rough ground above the waterline).
- **MapTool:** `render3d ... --passability hovercraft|vehicles|bots` draws the same (`--hover-paths` is short for hovercraft).
- **Checks:** 186/186 tests.
  - Every square on Acidic Quarry agrees with the engine's rule, worked out separately, including over 1,000 sampled squares under water steeper than 33° that are still open to hovers.
  - A previewed sea level covers more ground. Bots meet water too deep and never ride on it.
  - After an edit (a ramp out of the acid), the updated map equals a fresh one.
  - Every 3D pixel is tinted exactly towards its square's colour, and the tint follows an edit.
  - Vehicles and bots each agree with the rule. Every square open to vehicles is open to bots, too.
  - The 3D tint is exact for all three.
  - Editor self-test 126/126: each choice in the real 3D window's box tints the view its own way with its legend, and Off restores the view exactly.

**Step 23. Ramp look: rock to dirt, rough sides** *(added 2026-10-15 at your request: "ramps are usually the rock to bottom sand/dirt blend to base grass")*.

*Done (2026-10-15):*
- **Rough sides:** `Ramp.Shaped`, one function for building and for the who-can-climb prediction. It bends the side blend with rocky noise rather than adding noise to heights, so every height still lies between the old ground and the ramp.
  - The noise is strongest half-way down a side, and none on the ramp surface or past the edge.
  - It fades in along the sides, so the ends units drive onto stay smooth.
- **Rock to dirt** (look 2):
  - **Along the ramp from its foot:** the ground beyond the foot, then dirt, then rock over the upper half, then the ground beyond the top over the last tenth. The boundaries wander with noise and settle at the ends. Steep side banks turn to rock.
  - **Top and foot:** decided by height, not by which end is the start. A level ramp falls back to from-the-ends, and says so.
- **Colour, not copies:** the first version copied 64-unit rock and dirt patches, mirrored to fill. On Acidic Quarry that made an obvious kaleidoscope, and dark streaks in the dirt patch repeated as stripes.
  - Now rock and dirt give only their mean colour, and each pixel keeps its own detail: its brightness against a local average over about 25 units, from a summed-area table.
  - Nothing repeats, and the ramp keeps the grain of the ground it was cut from. The splat map takes the materials' mean weights.
- **Sources** (`RampMaterialSources`): ground above water and clear of the ramp, with steepness measured over 64 units (the engine's 16-unit measure calls Acidic Quarry's rough acid floor steep).
  - **Rock:** at least 35°, nearest the typical cliff colour there (not a sunlit or shadowed extreme), favouring ones near the top.
  - **Dirt:** 6–22°, nearest 14°, favouring ones near the foot.
  - **Fallbacks:** with no dirt, dirt is mixed from the rock and the ground at the foot, lightened. With no rock, the ramp falls back to from-the-ends, and says so.
- **Op:** `ramp:...:<edge>:<look>:<roughness>:<rock x>,<rock z>:<dirt x>,<dirt z>`. Look 1 is still from-the-ends, so older ops and journals replay unchanged. The editor writes the sources it used as numbers.
- **Editor:**
  - A Look box (Rock to dirt, From the ends, Leave texture) and Rough sides, in %.
  - Brown and sand-coloured rings on the map mark the rock and dirt sources; dragging one picks that source; *Auto rock, dirt* resets them.
- **Checks:** 192/192 tests.
  - With roughness 1, the band invariants hold, the sides are rougher, and the ends are exactly the smooth ramp's.
  - The sources meet their rules. The upper ramp's mean colour is the rock's (39 vs 39), and the foot's is the ground beyond it (65 vs 67). Nothing past the edge changes.
  - The same op twice gives the same bytes.
  - Level and no-cliff fallbacks; picked sources are used as given.
  - Undo is exact, and the rough prediction matches what gets built.
  - Editor self-test 130/130: the rings, dragging the rock ring, Apply with the picked rock, undo.
- **Checked by eye:** a ramp off the north-west plateau into the acid (texture, height shading, 3D from two sides).

**Step 24. Asset library, previews, asset browser** *(added 2026-10-15 at your requests: an assets tab that collects from every map opened, compares a map opened again, sorts by category and can add any asset to any map; then a preview of the chosen asset and of a selected placed item; then, like Blender, choosing at the bottom with thumbnails in groups, and details and adjustments on the right)*.

*Done (2026-10-15):*
- **Library** (`MapCore/Assets`):
  - An asset is a definition file with everything it needs: its models, their textures (S3O texture names, or a model's `.lua` sidecar), files its definitions name, and the definitions of the wrecks it leaves.
  - Its identity is a hash of those files. Collecting the same map again only marks what is already there as seen; a different file for a feature of the same name is a variant.
  - Names a map uses without defining them are listed by name. The engine's trees and vent are engine features, whichever list names them.
  - Kept in `index.json`, written atomically; a damaged index is set aside. Files are under `files/<id>/`, thumbnails under `thumbs/<id>/`.
  - Entries carry the version of the reading that made them, and are read again when seen with a newer one.
- **Definition files are run** (`MapCore/Lua/LuaInterpreter`, `MapCore/Items/FeatureDefs`):
  - **Why:** the six new maps build their features in code. All That Simmers loops `'ad0_senegal_' .. i .. size`, and Crimson Bay sets `name = "Fir Tree"` on 55 firs keyed by their real names. The text scan took "ad0_senegal_" and "Fir Tree" for names, and listed the real ones as BAR's.
  - **What runs:** a tree-walking interpreter for Lua 5.1. It has numbers printed as `%.14g`, tables iterated in insertion order (so runs repeat exactly), and `lowerkeys`.
    - The library functions are only those a census of the maps' files called, plus a few safe basics. There is no `io`, `os`, `load` or `require`.
  - **Limits:** steps, call depth, nesting, table entries and string length are capped, and each run gets a thread with a 16 MB stack. Hostile files are stopped, never crash the editor.
  - **Names:** as the engine's loader takes them, a feature is each table the file returns under a string key, lower-cased.
  - **Fallback:** a file that can't run falls back to the old text scan, with a note.
  - **One loader** serves the Items name list, the asset-add clash check and the library.
- **Categories:** Props & vehicles added.
  - Wrecks go by BAR's name rule. Industrial Revolution's crates and trucks carry a copied `arm_corpses` category.
  - A definition's own category counts before its description (a fern "Ferns and Stones" is a plant).
  - Words match at word starts ("ore" isn't in "forest").
- **Previews** (`S3oModel.Load`, `TgaReader`, `ModelPreview`, `ModelRenderer`):
  - **Models:** S3O read whole (pieces offset down the hierarchy; triangles, strips ended by -1, quads). Every offset and count is checked, and a piece can't be visited twice.
  - **Textures:** TGA (true-colour, grey, colour-mapped, RLE, either corner), DDS at a mip level no wider than 1024, and PNG.
  - **Drawing:** a z-buffered, perspective-correct software renderer.
    - Faces are drawn and lit from both sides, so winding doesn't matter. The light comes from over the viewer's left shoulder, and texture 2's red adds self-illumination.
  - **Transparency:** texture 2's alpha cuts out leaves. Measured on the maps: texture 1's alpha is 0 everywhere (team colour, not transparency); texture 2's is 38–70% clear on trees and 0% on solid models. An alpha that is all one way cuts nothing.
  - **Orientation:** v = 0 is a texture's top row, checked by eye on a truck, crates and trees, and pinned by a test.
- **Editor:**
  - **Bottom panel** under the map view only; the tool tabs keep the whole right side.
    - Two split containers make the layout: map | tool tabs, and map over the bottom panel. Docked siblings would follow their order in the parent, which Windows re-sorts when windows are made out of order. That was caught by checking real positions in a 1366x768 window, not just parentage.
    - Its tabs are *Assets* (the browser) and *Log*. The log moved there from its own full-width strip, giving the map 100 px more at 768 px tall.
    - The Log tab counts lines not yet seen, and the newest line also shows in the status bar (click it to open the log). `F4` hides or shows the panel, and its height is remembered.
  - **Asset browser:**
    - Thumbnails (80 px) are grouped by category, with a category picker and a name filter. They are drawn in the background, kept in the library, and only drawn once.
    - Click shows the asset in the **Asset** tab; double-click places it.
  - **Asset tab:** preview (drag to turn), details, Add to this map, Place (to Items › Place with its heading, scatter radius and spacing), Category, Remove.
  - **Items › Selection:** a preview of the selected item's model, from the map's own definition, or else the library's.
  - The map's definitions are run again only when its `features/`, `objects3d/` or `unittextures/` change.
  - **3D view** (*added at your request: render all the assets in the 3D window*): *Assets* on its toolbar (on by default) draws the placed features as their models (`TerrainOptions.Models`, `SceneModels`).
    - Each model is turned to its heading and stood on the ground, and drawn into the terrain's depth buffer, so nearer ground hides it.
    - Texture 2's alpha cuts out leaves. Models are lit by mapinfo's sun and colours, and shaded where the terrain shadows their foot.
    - Models under about two pixels across (six while the camera moves) are single dots of their mean colour.
    - Each kind is read once, with shared textures. Projection runs in parallel per feature, and drawing in parallel per band of rows.
    - The engine's trees get a cone stand-in. BAR's own features have no model.
    - A whole-map view of All That Simmers' 2,826 features takes about 240 ms. `MapTool render3d --models` does the same.
- **MapTool:** `assets collect|list|preview`, `preview <archive> <feature> <out.png>`, and `edit … asset-add:<id>`.
- **Checks:** 213/213 tests.
  - The 3D view draws Acidic Quarry's rocks only over ground near them (741 of 742 changed pixels), and reads each kind once. The synthetic map's engine trees are all drawn as the stand-in, with nothing read.
  - The interpreter: Lua 5.1 numbers and modulo, closures, varargs, pcall, multiple assignment, `__index`, the loader patterns of the real maps, and the fallback.
  - Hostile files are stopped: endless loops and recursion, huge tables and strings, source nested 5,000 deep. `pcall` can't catch a limit.
  - S3O hierarchy, strips and quads; loops and truncation refused.
  - TGA both ways up, RLE and grey; a four-colour square drawn the right way round; cut-out.
  - A file of generated palms collected as one asset of three.
  - Editor self-test 148/148.
- **The seven maps** (`MapTool assets collect` into a scratch library): 35 definition files make 256 features, and every model and texture is found.
  - The names left to BAR are only `rocks30_*`, `*_dead`, `geovent` and `treetypeN`.
  - Collecting all seven took 20 s.

**Step 25. Experimental: new feature from a model** *(added 2026-10-15 at your request: Lua gameplay settings applied to assets; you chose a new named feature that reuses the model, over changing the original)*.

*Done (2026-10-15):*
- **Checked against the sources first** (Recoil master and BAR master, 2026-10-15):
  - **What the engine reads:** `FeatureDefHandler.cpp` reads description, blocking, noselect, flammable, indestructible, reclaimable (defaults to not indestructible), autoreclaimable (defaults to reclaimable), metal, energy, `health` then `damage`, reclaimTime (default (metal + energy) × 6), object, upright, footprintX/Z, mass, crushResistance and the collision keys.
  - **Not read, so not offered:** `nodgun` and `sightDistance`. BAR's `featuredefs_post.lua` doesn't read them either.
  - **BAR's post-processing** only lower-cases keys and adds customparams. It drops a feature whose model isn't under `objects3d/`, so the model must be in the map.
  - **Reclaiming** (`Builder.cpp`, `UnitDef.cpp`): each frame a builder takes off `reclaimSpeed / 30` of `reclaimTime`, and `reclaimSpeed` defaults to `workerTime`. One builder therefore needs reclaimTime ÷ build power seconds; BAR's armcom has 300.
  - **Damage** (`Feature.cpp`): features take default damage. BAR's armcom D-gun does 99,999 by default, not the 30,000 the example's comment assumed, so 900,000 health is about 10 hits, not 30. A D-gun shot can hit more than once, and gadgets may change damage, so this is shown as an estimate.
- **The definition** (`CustomFeature`, `CustomFeatureSettings`):
  - **From the base:** the model as its definition names it, the look and collision keys (height, upright, hitdensity, collisionvolume*), and of its customparams only how it is drawn and who made it. Not lod_master or featuredead, which name the original.
  - **Gameplay:** every key written out, defaults included, because the engine's defaults chain.
  - **Format:** health as `damage`, the maps' spelling. Wrapped in lowerkeys. UTF-8 without a byte-order mark. Strings escaped (`\ddd` for control characters). The same input gives the same bytes.
- **Op** `feature-custom:<name>:<base>:<settings>`, with settings as key=value pairs and percent-encoded values:
  - The base is a feature the map defines, or one from the library. From the library, its model, textures, sidecar and normal map are copied in, with the asset-add clash rule; its definition isn't.
  - One undo step (`PackageFilesSnapshot`), and it replays from the journal.
  - **Refused:** bad names, names the map or a file already has, the engine's or BAR's names, unknown or out-of-range settings, and a model the map can't get.
- **Editor:** the Asset tab's *Experimental: new feature from this model* group.
  - Fields: Name, Start from (the model's own settings, or *Ancient monolith choke*), Description, Health with a D-gun note, Indestructible, Reclaimable, Reclaim time with a time note, Auto-reclaimed, Metal, Energy, Blocking, Flammable, and Footprint in game units.
  - **Create** writes the file, then picks the feature in Items › Place.
- **MapTool:** `edit … feature-custom:…` (`--library` for a model from the library). Industrial Revolution with a monolith from Acidic Quarry's rock2: made, placed, saved, listed by `items`, and drawn by `render3d --models`.
- **Checks:** 217/217 tests.
  - From a map's own model: the file runs, and reads the same as text. Every key is written; nodgun and featuredead are absent. A tricky description survives exactly. Items › Place finds it with its model. Undo is exact, and the same op gives the same bytes.
  - From the library onto a map without it: only the model and its two textures come in.
  - Refusals, the clash case, and journal replay are tested.
  - The operations list includes it.
  - Editor self-test 152/152.

**Step 26. Doors and tunnels** *(added 2026-10-15 at your request: a door and a tunnel made of several features, worked by a gadget: indestructible walls, a centre, repair markers; a tunnel top buildable but not crossable that kills what is on it when destroyed; an open tunnel; and features that can be built on)*.

*Done (2026-10-15):*
- **Checked in Recoil's source first** (master, 2026-10-15):
  - **Repairing plain features:** `AddBuildPower` refuses positive build power on a feature that isn't a unit's corpse. So repair is done by reclaiming: `AllowFeatureBuildStep` gets a negative part per frame, and returning false stops the engine's step while the builder carries on.
  - **LuaGaia:** synced, with full control (`UseTeamResource` on any team; its table form returns false if the team can't pay). BAR ships no LuaGaia, and maps' `LuaGaia/main.lua` runs their `LuaGaia/Gadgets`.
  - **Building on a feature:** a feature off the blocking map doesn't block building (`TestBuildSquare`). A building's terrain check uses height, slope and water only, never the terrain type.
  - **Crossing:** a terrain type with all speeds 0 gives `GetPosSpeedMod` 0, so it can't be crossed. `SetMapSquareTerrainType` (16-unit squares) re-paths units, and `SetTerrainTypeData` sets the speeds.
  - **The other calls used:** `SetFeatureBlocking` (blocking, solid, projectile and ray flags), `CreateFeature` (heading to facing), `DestroyUnit`, `AllowUnitCreation` (position and facing when building), and the `UnitDefs`/`FeatureDefs` fields (`isImmobile`, `isFactory`, `canFly`, `xsize`/`zsize` in 8-unit squares).
  - **Footprints:** whole squares from the position, and turned only in quarter turns.
  - The user's sketch used callins that don't exist (`FeaturePreResurrect`, `SetFeatureResurrectProgress`), so the gadget was written fresh.
- **The gadget** (`StructureFiles.GadgetText`) creates and tracks every part itself from `mapconfig/map_structures.lua`; nothing is linked by distance.
  - **Door centre destroyed:** two repair markers appear.
  - **Repairing:**
    - Reclaiming either marker repairs the structure, into one pool, paid in metal and energy by the builder's team.
    - It stalls when the team can't pay, and the markers' health shows progress.
    - When full, the centre comes back once the gap is clear.
  - **Tunnel top:** taken off the blocking map, but still hit by shots. The ground under it gets the type with no speed. When destroyed, ground units and landed aircraft on it are destroyed, and the ground's own types are put back.
  - **Build rules (`AllowUnitCreation`):** nothing in a door's gap, an open gap or an open tunnel's middle. On a standing tunnel top or a build-on feature, only buildings that aren't factories.
  - **Reload:** takes back the parts already there.
- **Editor and MapCore:**
  - **Ops,** each one undo step and replayable from the journal: `structure-kind`, which writes the parts as custom features (models from the map or the library, with the clash rule), the config, the gadget, and LuaGaia's loader for maps without one; `structure-add`, which snaps to a 16-unit grid and refuses anything sticking out; and `structure-remove`.
  - **Terrain type:** the highest one neither the SMF type map nor mapinfo uses.
  - **New features:** a *Build on it* setting.
  - **Views:** the 2D view draws structures: walls, a coloured centre, and dashed squares for the markers. The 3D view draws their models.
  - **Asset tab:** a group to make and place kinds. Items › Select picks a structure, and Delete removes it.
- **Checks:** 228/228 tests.
  - The parts tile with no gap at all four facings.
  - Ops, undo and replay.
  - Six gadget scenarios against the mock engine, whose calls behave as the source says; anything else is nil and fails.
  - Editor self-test 159/159.
- **Test map:** `out/step26-structures/acidicquarry_5.17-structures.sd7` has a door, a tunnel, an open tunnel and a platform on team 0's plateau, with `CHECKLIST.md` for BAR.
  - Each passage has open, flat ground at both ends: checked with the vehicle and bot passability views.
- **Saving:** a file swap Windows refuses because something has the file open for a moment (a virus scanner, the search indexer) is now retried for up to 0.75 s.
  - Two tests failed this way once.
  - New tests hold a file open, briefly and for good.

**Step 27. Built models for doors and tunnels** *(added 2026-10-15 at your request: actual models, concrete or metal, taller and thicker than a commander, their size changed where they're selected; a tunnel as a door with a flat roof to build on; a jagged rock roof on a concrete vault; a door as a thin gate that lowers, with rams, braced by its pillars; and structures that start destroyed)*.

*Done (2026-10-15):*
- **Checked first** (Recoil and BAR master, 2026-10-15):
  - **Commanders:** armcom and corcom are a cylinder 28 across and 52 tall on a 3-square footprint. Defaults: 80 tall, 3 squares thick, a 4-square gap.
  - **S3O:** the engine works out radius and height if 0, but never the middle, which a collision volume centres on, so the editor writes it. Uncompressed 32-bit TGA loads.
  - **Winding:** measured on 259 models in seven maps: (b−a)×(c−a) points along the stored normal for 99.8% of triangles.
  - **BAR's model shader** draws every feature. Texture 1's alpha mixes in team colour, which is black for map features, so it must be 0. Texture 2's red, green and blue are glow, metalness and roughness, for features with `customparams.cuspbr`.
  - **MoveCtrl:** works on buildings and holds them in place. Lifted above its radius, a building stops blocking its footprint. Build range is 2D. UnitCreated fires on the nanoframe, and UnitDestroyed carries its arguments.
- **Models** (`StructureModels`): walls with plinth, cap and ribs; the door's gate in rails with rams; a tunnel block with a sealed mouth and a flat, striped pad, or rock; an open tunnel's rock roof on a concrete vault, or a bridge slab; repair markers. Each part reaches 16 below ground, and the texture faces start at the ground. One 512 atlas for all looks.
- **Rock:** jagged and faceted, and coloured like the map's steepest ground, at half its saturation.
- **Kinds:**
  - **Settings:** look, height and roof. The full settings are kept in the config, so a kind shows what it was given; older kinds get theirs back from their parts.
  - **Ops:** `structure-change` rebuilds a kind for every one placed. `structure-add …:broken` and `structure-broken` make a structure start destroyed.
  - **Undo** covers binary files and deletions.
  - **Uneven ground** under a structure is reported.
- **Gadget:** lifts buildings begun on a flat roof onto it (MoveCtrl) and keeps them apart, takes only buildings wholly on the roof, treats rock-roofed tunnels like doors, and starts a structure broken when the config says so.
- **Editor:**
  - **Asset tab:** look, roof and height, a suggested name, and Start destroyed.
  - **Items › Select:** a picked structure shows its kind's size and look, with Apply for every one of that kind, and its own Starts destroyed box. The preview shows the whole structure.
  - **Views:** the 2D view outlines a destroyed one's centre; the 3D view draws walls and markers.
- **Saving:** a file swap Windows refuses for a moment (a scanner or the indexer holding the file) is retried for up to 0.75 s.
- **Repair markers you place, and repairing through the wreckage** (asked for 2026-10-15):
  - **Markers:** a built kind's markers are placed by you, per structure (`structure-marker-add`, `-move`, `-remove`; editor: Place repair markers, then pick, drag and Delete in Select). They're always on the map: green (`<kind>_marker`, not reclaimable) while the structure stands, red (`<kind>_repair`) while it is destroyed. The gadget swaps them.
  - **The wreckage:** what is left of a built door or tunnel (`<kind>_broken`, a tunnel's new too: slabs and rock along its walls, the middle clear) is reclaimable. With every red marker it feeds the one repair pool, so a structure with no markers is repaired through it.
  - **Asset kinds:** kinds made from assets keep their two markers beside the walls.
  - **Kinds from earlier builds:** the gadget leaves out marker and wreck names that aren't defined, so a built kind made by an earlier editor keeps its two markers beside its walls until rebuilt. The editor offers Rebuild when a built kind's parts are missing.
- **A broken door** (asked for 2026-10-15):
  - **The model** (`<kind>_broken`): torn stubs of the gate in its rails, plates fallen along the walls, the rams snapped and knocked aside. The middle 60% of the gap is clear, so units look as if they can drive through. It's low, doesn't block, and is indestructible.
  - **The gadget:** puts it in the gap when the gate is destroyed or the door starts destroyed, and takes it away when the gate is rebuilt. A reload takes back the one already there.
  - **Not the engine's `featuredead`:** a door that starts destroyed needs it too.
  - **The mock engine** now sets a feature's blocking from its definition, as the engine does.
- **The rock-roofed tunnel** has the open tunnel's concrete vault too (asked for 2026-10-15): the same rock-on-vault, the arch closed at each end by the sealed mouth's shutter (mapped so its striped ring follows the arch), set back so the vault's underside shows.
- **Moving and turning** (asked for 2026-10-15): drag a placed door or tunnel in Items › Select; Q / E, the turn buttons or Heading turn it.
  - Ops: `structure-move`, and `structure-turn` (quarter turns).
  - Turns stay at quarters, at your choice: the engine turns a feature's blocked footprint only by quarters.
  - Placing uses the Heading box, never a random heading.
- **Every terrain type used** (All That Glitters: its type map is a 0–255 picture; mapinfo defines only 0 and 255):
  - Undefined types take the engine's defaults (checked in Recoil's MapInfo.cpp), so types 0–254 behave alike.
  - The least-used undefined type is freed: the gadget gives its squares the most-used undefined type at game start, before anything else.
  - A structure's recorded ground type follows the remap.
- **Test map:** `out/step26-structures` rebuilt with a concrete gate, a metal bunker, a rock-and-vault arch, a second gate that starts destroyed and the platform, with `CHECKLIST.md`.

**Step 28. Oil pump** *(added 2026-10-15 at your request: an olden-day oil pump that gives whoever repairs it metal and energy a second, animated in game)*.

*Done (2026-10-15), **untested in BAR**:*
- **Checked first** (Recoil and BAR master, 2026-10-15):
  - **AddTeamResource** works from LuaGaia (full control), takes "m…"/"e…", and counts as income. Once a second, it shows as +X/s in BAR's top bar, which reads GetTeamResources' income.
  - **Ownership:** CreateFeature takes a team, and a feature made for a team wears its colour by texture 1's alpha (cus_gl4). TransferFeature doesn't recolour, so the pump is made again for each new owner.
  - **Features have no animation:** piece matrices look broken on master. A separate beam feature, held by SetFeatureMoveCtrl in the air and turned by SetFeatureDirection with only a front (right and up then come from the world's up), pitches about the axis across the pump whichever way it faces. The engine draws features between frames smoothly.
  - **SendMessageToTeam** works from synced code.
  - **Unit defs:** maps can add them in BAR, and a unit could have animation scripts. Features were kept: no new unit in BAR's lists, and they fit the structures system.
- **Models** (`StructureModels.Pump.cs`), from their own atlas (`map_pump_1.tga`, `_2.tga`):
  - **The pump:** a pumpjack on a concrete pad and skid, with its A-frame post and brass bearing, crank and counterweights, gearbox, engine with flywheel and exhaust, well, and a hooped wooden tank.
  - **The beam:** its own model with its horse head, bridle and pitman arms, its pivot at its origin.
  - **The derelict:** rusted and greyed, a leg gone, the beam fallen, the tank collapsed into staves, oil spilt.
  - **Team colour:** the paint cell's texture-1 alpha is 200, so the owner's colour shows on the frame and beam.
- **The `pump` kind type:** `structure-kind:<name>:pump:mps=2;eps=25;health;work;metal;energy`.
  - **Its parts:** `<kind>_centre` (the pump), `<kind>_beam` (non-blocking, noselect, indestructible) and `<kind>_broken` (the derelict, reclaimable with the repair's work).
  - **Its footprint** is 4 by 6 squares.
- **Gadget:**
  - Pumps start derelict, and reclaiming the derelict repairs them into the shared pool. The team that finishes owns the pump: pump and beam are made for it and it's told.
  - The beam nods every frame, and the owner is paid every 30 frames.
  - Destroyed, a pump drops its beam, becomes derelict again and tells its owner.
  - A reload takes back a standing pump (its owner from its feature's team) or a derelict one.
- **Editor:** "Oil pump" in the Asset tab's types, with metal and energy a second. A picked pump explains itself, and the 2D and 3D views show it derelict, as it starts.
- **Checks:**
  - The mock engine has CreateFeature's team, SetFeatureMoveCtrl, SetFeatureDirection, AddTeamResource, SendMessageToTeam and GetFeatureTeam, each as the source has it.
  - A test takes a pump through repair by team 2, its beam nodding a whole cycle, four seconds of pay, destruction, repair by team 3, and a reload.
  - Model tests cover its winding, footprint, pivot and team-colour alpha.
  - The editor self-test makes, places and picks one: 182/182.
- **Test map:** an oil pump east of team 0's start, with `CHECKLIST.md` steps.

**Step 29. Bending ramps** *(added 2026-10-15 at your request: points added to the path a ramp is made along, so it can wrap the edges of a cliff; the current ramp with more points and curves applied)*.

*Done (2026-10-16):*
- **The line** (`RampPath`): straight runs from point to point, each bend rounded by a circular arc.
  - Each arc is as large as the runs beside it allow. A run between two bends is shared so that both get the same radius; a run to an end belongs to its bend.
  - A point the line runs straight on through takes nothing, so adding one on the line changes nothing.
  - The line is smooth, its tightest radius is exact, and it never leaves the shape its points enclose.
  - **Tried first and dropped:** a Catmull-Rom spline through the points. Where a long straight run meets a short bend (the run along a cliff and round its corner), it over-tightens, pinching a radius-75 bend to 52. A C2 spline does the same, and so do denser points.
- **Where a point is:**
  - The line is sampled every 4 units, each sample with the line's normal there, turning evenly between samples. So (u, v) and world points map to each other exactly and continuously (no steps between samples on the inside of a bend).
  - Of the places whose normal passes through a point, the one nearest the ramp's band counts. A run-on past one end can't take points from a nearer part of the ramp; it did at first, and the ground stepped like a comb.
  - Beyond the ramp's sides, u is averaged over places nearly as near (within a window that opens from 0 at the side to 32 units). So where two parts blend into the same ground, the ground slopes from one to the other instead of stepping. On the ramp itself u is exact.
  - A grid of 16-unit cells keeps the segments that can matter in each, for each reach and width. Far cells answer "far" at once. A path keeps its three most recent grids, since the editor keeps one path while you drag its width.
- **The operation:** bends go after the first end, each after a `;` (`ramp:x1,z1;bx,bz;...:x2,z2:...`). Straight ramps write and build exactly as before.
  - **Refused:** a bend tighter than half the width plus 8, a bend that turns straight back, points closer than 8, more than 64 points, and two parts of the ramp nearer than the width plus 16.
  - The result gives the number of bends, the tightest one, and how steep its inside is (the ramp is level across, so the inside of a bend is steeper).
- **Editor:**
  - `Shift`+click adds a point beyond the nearer end. `Ctrl`+click adds a bend on the line where the ramp runs straight, so nothing moves until it's dragged. `Ctrl`+click on a bend removes it.
  - Points drag by how far the mouse moves. A click off a bending ramp does nothing (it would lose its shape).
  - Losing the mouse mid-drag puts every point back.
  - `Q` / `E` turn every point about the middle, **Swap ends** reverses them, and **Straighten** goes back to straight. Length and Direction follow the points.
  - The outline follows the curve (red when the shape is refused). The points are joined by a dashed line, with the bends as handles, and the arrow runs along the curve.
  - The dashed edge is traced where the blend ends (marching squares). Grown from the curve, it looped inside any bend tighter than the edge's reach, which is usual when wrapping a cliff with an auto edge, and made a sound ramp look folded.
- **Demo:** `out/step29-bending-ramp/acidicquarry_bending_ramp.sd7` ("AcidicQuarry Bending Ramp"), a ramp wrapped three-quarters of the way round the south mesa from the acid floor onto its top. It has 2D and 3D renders. Its foot is in the acid, like all of this map's low ground.

**Step 30. Guardian towers** *(added 2026-10-16 at your request: a tower with rocket launchers pointing up, futuristic but coloured as an ancient weapon, about 3 by 3 squares, that destroys bombers crossing a path it guards, its damage a share a tick so no number of them overwhelms it, a rocket now and then for show; the path adjustable and anywhere on the map; then a list of every standard unit with its picture to tick for destroy; "if you enter you die", even with lag)*.

*Done (2026-10-16), **untested in BAR**:*
- **Checked first** (Recoil and BAR master, 2026-10-16):
  - **Health set directly:** GetUnitHealth and SetUnitHealth, and DestroyUnit for the end, so no armour, shield or damage modifier changes anything. UnitPreDamaged isn't used (last writer wins between gadgets).
  - **Rockets without a target:** SpawnProjectile takes pos, end, speed and ttl, and the team (Gaia's by default). The projectile is deleted after 45 frames with DeleteProjectile (no explosion) and a burst CEG is spawned where it was, so no dummy unit is needed.
  - **The map's own weapon:** BAR loads a map's `weapons/*.lua` (dropping any whose model is missing) and `effects/*.lua`. The weapon and model are named `barmapeditor_guardian_rocket` so they can't shadow BAR's.
  - **Speeds:** UnitDefs' `speed` is in elmos a second (the move types divide it by GAME_SPEED).
  - **Lag:** Recoil runs every simulation frame and catches up rather than skipping when a game lags, so GameFrame sees every frame.
- **The units** (`tools/MapResearch barunits`): BAR's units at commit 9d7bdf3 were read from its repository: 681 (Armada 223, Cortex 224, Legion 234), each with its name, its weapons' target categories and its build picture (DDS, the 64-pixel mip), shipped as `BarUnits/catalog.json` and `icons/`.
  - **Destroyed by default (43 units):** aircraft that can attack the ground. That is, a unit that flies and has a weapon not limited to VTOL targets: bombers, gunships, drones, torpedo planes, and the armed transports (Abductor, Stronghold). Fighters, scouts, transports and constructors pass.
- **Models** (`StructureModels.Guardian.cs`), from their own atlas (`map_guardian_1.tga`, `_2.tga`):
  - **The tower:** a stepped stone plinth; three tapering octagonal drums banded in gold with glowing rune strips; four struts; a crown; four rocket pods of four tubes each, pointing up and leaning out; a sensor mast with a glowing eye. 48 by 48, 146 tall.
  - **Looks:** Ancient (verdigris bronze, stone, gold, teal glow), Concrete and Metal (amber glow), all the same shape.
  - **The rocket:** a slim body, a gold nose and fins.
- **The `guardian` kind type:** `structure-kind:<name>:guardian:look=...;kill=S;rocket=S:<targets>`, then `structure-targets:<kind>:<names|none>` and `structure-zone:<x>,<z>:<distance>:<0|1>:<x,z;...>|none`.
  - **Its part** is `<kind>_centre`: indestructible, not reclaimable, blocking 3 by 3.
  - **Zones:** 2 to 256 points, open or closed, distance 16 to 2048 either side, measured flat. (At first a closed path guarded only the ring along it; its inside and the outside are now regions with rules of their own, below.)
- **Gadget:**
  - Every 6 frames, listed units (and their `_scav` variants) near each run of a path are checked along the line from where they were at the last check, so one can't slip across a narrow zone between checks.
  - A unit that comes within the distance is doomed. It loses its share each tick wherever it goes, from what the last tick left (so healing and repair don't count), and is destroyed when less than a quarter of a share is left. Every unit takes the kill time, however many come.
  - Nanoframes are skipped, and a unit carried by a transport is judged by the transport. A destroyed unit's doom is dropped, so its ID can't pass it on.
  - While anything is in its zone, a tower fires one rocket every rocket seconds from the pod facing it. The rocket rises 45 frames and bursts high over the tower.
  - Each strike shows a small hit flash, at most 16 a tick however many are struck (500 bombers would otherwise be 2,500 effects a second).
  - Moving or turning a tower leaves its path where it is.
  - A reload takes the tower back.
- **Editor:**
  - **Asset tab:** "Guardian tower" in the types, with its look, kill time, rocket interval, and **Destroys › Choose…**: every unit's picture in groups by faction and kind, a filter, faction and show choices, and presets (aircraft that attack the ground, all aircraft, everything or nothing shown, nothing).
  - **Items › Select:** a picked tower shows its kind (look, times, targets: Apply and Choose…) and its own zone (distance, closed, Clear the path).
  - **Drawing the path:** `Shift`+click adds a point at its end (the first two make it), `Ctrl`+click on a point removes it and on a run puts one in, and points are dragged. Each change is one undo step.
  - **Views:** the 2D view and renders draw every zone's edge and path. A picked tower's zone is drawn brighter, with its points and a line to the tower.
- **Checks on save:** a kind that destroys commanders, and a start position inside a zone, are warnings. A tower guarding nothing is a note.
- **MapTool:** the operations above, with `default` for the catalog's default targets (BarUnits beside MapTool).

*Regions (added 2026-10-16 at your request: "3 states to manage, the path and width is 1, the inner closed space another and the outer space a 3rd", each with its own list, the teams it impacts (All, 1 to 8) and a delay, so enemy planes can enter but are destroyed after a while, with no impact in allied space). Your answers: teams are the lobby's teams (alliances), a delay resets on leaving, and the rules are per tower:*
- **Checked first:** Spring.GetUnitAllyTeam and Spring.GetAllUnits (every unit to LuaGaia's full read), in Recoil's LuaSyncedRead.cpp. BYAR-Chobby's battle room names ally team *n* "Team *n*+1".
- **The three regions:** the band (within the distance), inside a closed path beyond the band (by the even-odd rule, so a figure 8 has two insides), and outside (the rest of the map beyond the band, other towers' zones included). The band wins where they meet.
- **Each tower's rules** (`GuardianRules`, written into the instance in `mapconfig/map_structures.lua`). For each region:
  - targets (unit names, each with its `_scav`);
  - teams (null for every one, Scavengers and Raptors included; else lobby teams 1 to 8, ally team + 1);
  - delay (seconds in the region without leaving before the unit is doomed; 0 at once).
- **Defaults:** a new tower copies the kind's last placed tower. The first gets the kind's targets in the band, of every team, at once. A tower from before rules reads that way too, in the editor and the gadget.
- **`structure-rule:<x>,<z>:<band|inside|outside>:<delay>:<all|none|2,3>:<targets>`**, one undo step. It refuses unknown regions, delays outside 0 to 3600, teams outside 1 to 8, bad names, and no change.
- **Gadget:**
  - Per tower, per tick: the band (swept when its delay is 0, else counted), the inside (units in the path's box, inside and not in the band), and the outside (every unit, anything off the zone's box at once).
  - Counts are kept by unit ID in each tower, for this tick and the last, so leaving resets them. They're never iterated, and UnitDestroyed clears them.
  - A tower fires its rockets while it is wearing down anything it doomed, at one of those units.
- **Editor:** Items › Select shows three groups for a picked tower (Crossing the path, Inside the path, Outside the path). Each has Teams (All, 1 to 8), Delay and Destroys › Choose…, and every change is one undo step. Inside says when the path isn't closed. The Asset tab's list is now **Crossing (first tower)**.
- **Checks on save:** commanders in any region (naming it); a start position in a band that dooms at once; an outside rule for every team at once (the whole map).
- **MapTool:** `structure-rule`, and `aircraft` (every unit that flies) beside `default`.
- **The Edit menu** cuts an operation's name at 120 characters (a rule can list 86 units).
- **Demo:** `out/step30-guardian/acidicquarry_guardians.sd7` (version 5.17-guardians):
  - **sentinel:** an ancient tower on the north-east mesa, guarding a closed square round the middle (250). Crossing it, the 43 ground-attackers of every team at once; inside it, every aircraft of teams 2 to 8 after 30 s; outside, nothing.
  - **bastion:** a concrete tower on the south-west plateau, guarding a lane (180) with the default band.
  - 2D and 3D renders.

**Step 31. Shield platforms** *(added 2026-10-16 at your request: a platform that gives units leaving it a personal bubble shield, its energy cost proportional to the unit's metal cost, an extra 50% of their health; kept until lost, then gone; the bubble round the unit by its size, covering others within it, so a tall titan shields grunts at its feet. Your answers: the platform belongs to whoever repairs it, as an oil pump does, and driving over it again tops a shield up)*.

*Done (2026-10-16), **untested in BAR**:*
- **Checked first** (Recoil and BAR master, 2026-10-16):
  - **A map can't add real shields:** they are weapons in BAR's unit definitions.
  - **Hits can be absorbed instead:**
    - `DoDamage` calls `ApplyDamage`, which only takes the health (no kill, no clamp), then the UnitDamaged callin, then kills the unit if its health is 0 or less. So health put back in UnitDamaged saves it.
    - UnitPreDamaged isn't used (the last gadget to write wins).
  - **Damage codes:** SolidObject.h's DamageType gives weapons as 0 and up and debris as −1. Fire, acid and lava, collisions and the rest are below that.
  - **UnitDefs** have `radius` and `height` (from the model) and `metalCost`.
  - **Calls used:** GetUnitsInSphere, AreTeamsAllied, GetUnitPosition's middle.
  - **The drawing side:**
    - The engine's base `gadgets.lua` runs LuaGaia's gadgets unsynced too, from `LuaGaia/draw.lua`, with RecvFromSynced and DrawWorld.
    - LuaGaia reads everything, so IsUnitVisible would show units out of sight. IsUnitInLos with the player's ally team is asked instead.
    - Lua's CreateList, BeginEnd and Vertex are still in Recoil.
  - **Effect textures:** BAR's resources include flare, explo and circularthingy, so the guardian burst's textures are confirmed too.
  - **Lava and acid:** BAR's `map_lava.lua` deals them with AddUnitDamage and `Game.envDamageTypes.Water`, which is −5 (LuaConstGame.cpp), so bubbles don't take them. AddUnitDamage without a weapon passes −1, as debris does, so damage a gadget deals that way is taken.
- **The `shield_pad` kind type:** `structure-kind:<name>:shield_pad:gap;depth;shield;epm;bubble;health;work;metal;energy`.
  - **Defaults:** 6 by 6 squares, 6,000 health; a repair of 12,000 work for 300 metal and 1,500 energy; shields of 50% of health, for 10 energy a metal, bubbles ×1.
  - **Parts:** `<kind>_centre` (doesn't block; destructible; not reclaimable) and `<kind>_broken` (the derelict; doesn't block; reclaimed with the repair's work).
  - **Its own atlas:** `map_shieldpad_1/2.tga`, its stripes in the owner's colour (alpha 200), its emitters glowing.
  - **Its effects:** `effects/barmapeditor_shield.lua`.
- **Gadget, game side:**
  - The platform takes the oil pump's repair-to-own path. Made again for its owner, it is taken off the blocking map (units drive over it) and is still hit by shots. Its repair isn't held for units standing on it.
  - **Shields:**
    - Every 6 frames, units of the owner's and allies' on a standing platform are noted. Those noted last time and not now have driven off, and get a shield.
    - A shield is the kind's percentage of the unit's maximum health; the team pays epm × metal cost × the part missing.
    - A team that can't pay is charged nothing and told, at most every 30 s.
  - **The bubble:**
    - It is centred on the unit's middle. Its radius is max(radius, 0.6·height) × 1.15 × bubble, so a 120-tall titan's is 82.8 and covers a grunt 50 off at its feet but not one 90 off.
    - In UnitDamaged, the bubble that takes a hit is the unit's own, else the fullest of its allies' that holds the unit's middle. Never one the attacker is inside.
    - It takes min(damage, what is left) and puts that health back. Empty, it bursts.
  - **Effects:** at most 16 hit flashes a frame.
  - **The drawing side** is told when a shield goes up, when it goes down, and of its strength at most once a check (anything held back is sent at the next check).
  - **Recycled IDs:** UnitDestroyed clears the unit's shield and its platform note, so a new unit with the ID gets neither.
- **Gadget, drawing side:** a translucent sphere with lines of latitude and longitude, its brightness following the strength, at each shielded unit's middle (its view position). It is drawn for units in this player's line of sight, or all of them for a spectator with full view.
- **Loaders:** the editor now adds whichever of `LuaGaia/main.lua` and `draw.lua` a map lacks (before, only both or neither). A map's own are never replaced.
- **Editor:**
  - **Asset tab:** "Shield platform" in the types, with Gap and Depth, the platform's health and repair, Shield (% of health), Price (energy a metal) and Bubble size.
  - **Items › Select:** a picked platform explains itself, and the views show it derelict, as it starts.
  - **Fix:** the Rebuild check looked for a concrete or metal guardian tower's walls, which it doesn't have. Towers and platforms now check only their own parts.
- **Checks:**
  - **The mock engine:**
    - `Damage` follows DoDamage's order exactly.
    - It has GetUnitTeam, AreTeamsAllied, GetUnitsInSphere, GetUnitPosition's middle, SendToUnsynced, and units' radius, height and metal cost.
    - A new stand-in runs the drawing side with gl recorded.
  - **Ten tests:**
    - the files and settings;
    - repair-to-own;
    - shields for the owner and an ally, none for an enemy, an aircraft, a nanoframe, a transport or the unit it carries; standing on the platform gives nothing;
    - hits taken whole, then the one that empties the bubble;
    - the titan's bubble covering a grunt at 50, not at 90, nor an enemy, nor a shot from inside;
    - debris taken; fire, acid, collisions and EMP not;
    - top-ups, and a team that can't pay;
    - a derelict platform and recycled IDs;
    - a guardian's doom going through a shield;
    - a reload;
    - drawing only what this player can see.
  - **Models:** wound as the engine reads them; low enough to drive over, with only the posts standing up; team colour on the stripes alone.
  - **Editor self-test** 228/228.

**Step 32. BAR look for the editor** *(added 2026-10-16 at your request: style the editor as BAR's in-game controls, with the settings overlay's look for the panels. Your questions on the panel layout and the Poppins font went unanswered, so the tool panel stays docked and the font is Segoe UI; Poppins would need a download you approve)*.

*Done (2026-10-16):*
- **Colours** sampled from the settings overlay screenshot (`images/2.png`): the panel, the tab gradient and its text, the chosen tab, the headings, labels and sub-labels, the dropdowns, the toggles' green square, the slider. The in-game controls give the buttons and the state bars. All in `src/MapEditor/BarTheme.cs`.
- **Controls:**
  - **Tabs** (`BarTabControl`): brown gradient tabs rounded at the top, the chosen one dark with gold-tan text. The tool tabs share the strip's width.
  - **Sections** (`BarGroupBox`, used by every panel's groups and the guardian regions): a tan-gold heading with a faint line, no box.
  - **Buttons:** dark and slightly rounded; a chosen tool has a gold bar under it.
  - **Tick boxes and choices:** a dark square (round for a choice) lit by a green square. Sized from the font so they fit the space Windows lays out for them. Lists with tick boxes (the units dialog) use the same.
  - **Dropdowns, number boxes, sliders:** drawn over Windows' own after they paint: a dark field and edge, a square at a dropdown's end, dark spin buttons, a thin track with a square knob.
  - **Lists:** Windows' dark look for scroll bars and column headings; group headings drawn in the headings' colour (Windows' dark ones are a hard-to-read blue).
  - **Menus, toolbar, status bar** (a ToolStrip renderer): dark; the toolbar's view switches show a green or grey bar.
  - **Title bars:** dark, through Windows' DWM (Windows 10 2004 and later).
- **Fonts and DPI:** the font is set on each window only. Other fonts (semibold tabs, headings, titles) are made from a control's own font and follow it. A font set directly on a control whose window doesn't exist yet isn't rescaled when Windows changes the DPI (a hidden window here is made at 96 DPI while the system is at 144). `--window-pictures` writes `fonts.txt` to check that every control's font agrees.
- **The tool tabs' width follows the screen's DPI:** 336 at 96 DPI, 504 on a 150% screen. The split container keeps a fixed panel's width in pixels, so before this it was two thirds as wide on a 150% screen.
- **Kept as Windows draws them:** message boxes and the file windows.
- **Checks:**
  - Pictures before and after: `out/step32-theme/before` and `after` (`MapEditor --window-pictures`, one per tool tab).
  - The self-test's panel and dialog pictures, plus a new one of the Save As dialog.
  - 292/292 tests, editor self-test 228/228.

**Step 33. BAR's own feature models** *(added 2026-10-16 at your request: show the in-game features' models instead of the "BAR" and "engine" placeholder tiles)*.

*Done (2026-10-16):*
- **Checked first** (BAR at 9d7bdf3, the unit catalog's commit):
  - **The loader:** `gamedata/featuredefs.lua` runs every `features/*.lua`. Then `featuredefs_post.lua` names each unit's `featuredefs` entry `<unit>_<key>`, and drops any feature whose model is missing.
  - **The engine's trees:** `features/enginetrees_override.lua` makes treetype0–15 four fir models (*i* mod 4), credited to Beherith and 0 A.D.
  - **Vents:** BAR's `geovent.lua` defines only `editor_geovent` and `editor_geocrack`. The engine's own `geovent` has no model.
  - **Map rocks:** BAR has no `rocks30_map_color.dds`, so `rocks30_map_*` use the `def` models unless a map ships that texture.
  - **What the seven maps use:** 16 tree types, the vent, 55 `rocks30_*` and 22 wrecks. BAR no longer defines two of the wrecks: `armasp` is gone, and `armnanotc` has no wreck now.
- **`tools/MapResearch barfeatures <out> <commit> none|all|<names-file>`:**
  - Reads BAR's file list at the commit.
  - Runs `features/*.lua` in MapCore's sandbox with what BAR's loader gives them (`table.copy`, `VFS.FileExists` answered from the file list), and reads every unit file's wrecks.
  - Fetches the chosen models, then the textures each model's header names. A DDS keeps one mip level of at most 512 pixels, fetched by ranged requests. TGA and PNG textures become DDS (DXT1 when opaque).
  - Writes `catalog.json` (each feature's model, description, category, metal, energy, file, author and wreck's unit) and `CREDITS.txt`.
- **Shipped:** `src/MapEditor/BarFeatures` holds 1,432 features and 262 models: every feature in BAR's own files, plus the 20 wrecks in `tools/MapResearch/BarWrecks.txt`. That is 14.3 MB, 6.2 MB zipped. All wrecks would add about 78 MB.
- **MapCore:**
  - `BarFeatureCatalog` (Shipped, Find, ModelOf, LoadModel).
  - The 3D view draws names a map doesn't define with BAR's models (the cone stand-in only without them). 1,000 firs cost no more than 1,000 cones (about 300 ms for a 1280×800 still, either way).
  - The Lua sandbox gains `string.format` (C's directives, as Lua 5.1), and `FeatureDefs.Read` takes extra globals.
- **Editor:**
  - **Thumbnails** of BAR's models, kept per commit in `thumbs\bar-<commit>\`.
  - **Tiles:** *not in BAR* for names BAR doesn't define, *vent* for the engine's vent.
  - **Previews and the Assets panel** show BAR's model and its author.
  - **Still refused:** adding a BAR feature to a map or making a feature from it, since BAR supplies it.
- **MapTool:** `preview bar <feature>`; `render3d --models` uses BAR's models (`--no-bar-models` to leave them out).
- **Checks:**
  - 298/298 tests: the catalog's commit and credits, the firs by *i* mod 4 with their cut-out textures, all 91 names the maps use loading with both textures, `rocks30_map` drawn as `def`, the dropped names absent, `string.format`, and the loader hook.
  - Editor self-test 229/229: a tree's thumbnail and preview are BAR's fir, and the vent's tile is a plain one.
  - Previews checked by eye: firs, rocks, a crystal, an egg, a tombstone whose "R.I.P." reads the right way round, and two wrecks.

**Step 34. BAR's units, buildings and all its features in the editor** *(added 2026-10-16 at your request: "similar for bar units, and structures and features. each should be in the editor". Read as BAR's units, its buildings ("structures") and every feature it defines; the editor's own structure kinds, doors to shield platforms, stay in Assets › Experimental)*.

*Done (2026-10-16):*
- **Checked first:**
  - **Size:** the catalog's 681 units have 641 distinct models (84 MB), and all wrecks and heaps come to 78 MB more. Shipped raw: you had asked for every one after seeing the wreck figure.
  - **Placing:** maps place units and buildings through their own featureplacer gadget (Acidic Quarry's `LuaGaia/Gadgets/FP_featureplacer.lua`). It makes them Gaia's, neutral, always visible and blocking.
    - A unit's heading is a full turn in 65,536 steps; a building faces one of four ways.
    - BAR's own code only writes these lists (`luaui/Widgets/cmd_feature_placer.lua`).
  - **Buildings:** a building is told from its unit file: no speed, and not flying. The catalog's folder-based group got Legion wrong, and the engine's own test calls nano turrets mobile, because BAR gives them a movement class. 302 of the 681 are buildings.
- **`barfeatures`** gains a unit catalog argument. It reads each unit file's `objectname` and speed, and ships those models along with every wreck (`all`). Textures shared by 8 or more models (the factions' atlases) are kept at 1,024 pixels, the rest at 512. Result: 1,434 models and 52 textures, 175 MB.
  - **Its own problems:** `armcomnew` names no model. Legion's wrecks name `led_wreck_shader.dds`, which BAR doesn't have. Two candy-cane textures are in an unsupported DDS format.
- **MapCore:**
  - `BarUnit`, with `FindUnit`, `UnitModelOf` and `LoadUnitModel`.
  - `AssetEntries()` makes BAR's browser entries, with the library's own ids, marked `BarOnly` and never saved.
  - The 3D view draws placed units and buildings with BAR's models.
  - Candy canes are now props (BAR gives them energy only, which read as a plant).
- **Editor:**
  - **The browser's *From* choice:** *Maps and BAR* (the default), *Maps*, *BAR*.
  - **Tiles:** build pictures for units and buildings, BAR's models for features. Pictures are drawn in the order shown, and the list keeps an index of its items for them.
  - **The filter** waits for a pause in typing and matches descriptions. The dropdowns are sized to their text, so they fit at 150%.
  - **Unit details:** what BAR calls it, its faction, the featureplacer list it goes to, that it is Gaia's in game, and its model and author. Move and Remove are off for BAR's own.
  - **Placing:** a building is placed in Items › Place as a building.
  - **Items › Place** says when BAR has no such unit or feature.
- **MapTool:** `preview bar` draws units and buildings too.
- **Checks:**
  - **302/302 tests:**
    - counts, and the building rule on 13 known units (nano turrets and mexes buildings; commanders, bots, aircraft and subs units);
    - all 680 unit models loading with their textures (about 0.5 s);
    - every wreck BAR keeps with its model, and a sample of wrecks and heaps loading;
    - BAR's entries' ids, kinds and categories;
    - a placed Sentry and Pawn drawn in 3D only with the catalog.
  - **Editor self-test 236/236:**
    - with Maps and BAR, 2,118 entries filled in about 0.3 s;
    - the Pawn by its build picture, its wreck and heap by their models, its details and preview;
    - the Sentry placed from the browser as a building;
    - `armasp` flagged.
  - Unit previews checked by eye: Pawn, Grunt, Legion Commander, Sentry, nano turret, Fusion, Juggernaut, Stormbringer, Legion mex, Vehicle Plant.
- **Measured with everything shown (a fresh library):**
  - all 2,118 pictures drawn in about 8.5 s;
  - a refill with them all in takes 50 to 100 ms, once items are given their image by place (Windows Forms looks a key up by going through every key in turn);
  - memory: drawing them left the collector holding about 1 GB for 275 MB in use, so a big run of pictures now ends with a collection that gives it back. Browsing everything adds about 45 MB (395 to 440 MB).
- **The featureplacer gadgets of the seven maps:** five carry Acidic Quarry's, two (Ascendancy, Crimson Bay) a newer one. Both make placed units and buildings Gaia's, neutral, always visible and blocking. They turn units opposite ways (minus or plus the heading), so a unit's heading in game depends on the map's gadget; buildings face the same in both.
- **`--window-pictures`** stops the thumbnails still being drawn before deleting its library, which a run stopped part-way could leave behind.

### 🎯 Milestone 3: release build

**Status (2026-10-15):** built and **waiting for your tests**. See `out/milestone3/CHECKLIST.md`.
- `out/milestone3/BarMapEditor/`: Release build of `MapEditor.exe` and `MapTool.exe`, with `README.md`. Same folder zipped as `out/milestone3/BarMapEditor-1.0.zip`.
- Release build checked: 165/165 tests, editor self-test 110/110. The packaged copy itself starts, reads Acidic Quarry and draws it.
- Framework-dependent (needs the .NET 10 Desktop Runtime): a self-contained build needs runtime packs that aren't installed here, and the build stays offline.
- The checklist covers what's new since Milestone 2: copying into BAR's maps folder, crash recovery (kill the editor on purpose), a Save As that fails, the README, and other maps. Milestones 1 and 2's in-game checks still apply.

---

## Out of scope (possible later work)
- Resizing a map or creating one from scratch.
- Automatic re-texturing of cliffs after height edits. You'd repaint them yourself or import a texture.
- Editing the type map or terrain types.
- Lua gadgets and map options.

## Known risks
- **Normal map.** If it holds detail beyond the heightmap, editing height will soften lighting in the edited area. Step 6 decides how to deal with it.
- **Painted shading.** The texture has cliff shading painted into it. Moving terrain won't move that painting.
- **Feature names.** An unknown feature name gives a missing feature in game. Placed items need names that exist in BAR.
- **No in-game test here.** BAR isn't installed on this machine. Every milestone needs your in-game check.
