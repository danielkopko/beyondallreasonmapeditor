# Star Fort 1.0: checklist

`star_fort_1.0.sd7` ("Star Fort 1.0", short name `StarFort`, map id 20261026) is the K6 drawing (`out/koth-maps/k6_star_fort.svg`), built at its own size, 28 × 28, on the Dune planet (`out/koth-maps/planets.py`). The terrain, texture, detail maps and sky are new. The palms, detail textures, featureplacer gadget and water settings are All That Simmers v1.1.1's. How it was made is in `../converter/README.md`.

**Not tried in BAR yet.** BAR isn't installed on this machine, so everything below was checked with MapCore's copy of the engine's rules, not in the game.

**Download:** one file, [star_fort_1.0.sd7](https://github.com/danielkopko/BarMaps/releases/download/star-fort-1.0/star_fort_1.0.sd7), from the public BarMaps release [star-fort-1.0](https://github.com/danielkopko/BarMaps/releases/tag/star-fort-1.0), with its lobby picture `Star_Fort_1.0.jpg`. The same files are on [this repository's release](https://github.com/danielkopko/beyondallreasonmapeditor/releases/tag/star-fort-1.0) (signed in as danielkopko). After rebuilding the map, `../king_maps.cmd publish star_fort -Message "..."` pushes the notes and replaces both releases' files. `../king_maps.cmd install star_fort` copies the map into BAR.

**Rebuilt 2026-10-23, not yet on the releases:** the map beside this file has its features turned the way the converter meant (*Headings*, below), and a minimap and lobby picture seen from above in 3D (*The minimap and lobby picture*). The releases still have the first build, in which every feature faces south and the minimap is the flat texture. `../king_maps.cmd publish star_fort` replaces them.

## The layout

| From the drawing | On the map |
|---|---|
| Five teams, each a king and three peasants | 20 start positions in `mapinfo.lua`: team 0 is A's king, 1-3 are A's peasants, 4 is B's king, and so on clockwise (A north) |
| The play area | A disc 13978 across (radius 6989) in a 14336 square. Beyond it, mountains rise on a cliff |
| Realms | Dunes at about 200 (183 to 225), rippled |
| Bastions: raised kings' ground | Five kite-shaped cliff-tops at 300, from 582 to 3584 units out from the centre, on cliffs 100 high and 40 across (68°): nothing but spiders climbs them. Flagstone courts |
| Walls: ramparts, no doors | Earth ramparts 90 above the bastion (390), 200 wide and flat on top so you can build on them, with sheer faces. Towers, 110 above the ground beneath, sit at each bastion's corners |
| A ramp for full access | One a bastion, at its point, between the two point towers: 448 wide at 12°, from the realm (200) up to the court (300) |
| Bots-only zigzags | Two a bastion, one up each flank through the rampart: 64 wide, flights of 38° with level landings |
| The spire | A sandstone column 520 high in the middle, 1075 across: nothing climbs it |
| Ridges and passes | A sandstone ridge at 380 between each pair of realms, from the fort out to the mountains. A bots-only pass crosses each, at most 60 above the realms and no steeper than 38° |
| Metal | 70 spots, 27.2 metal/s a team (136.1 in all). Each team has nine peasant spots (1.8), two in its bastion (2.0) and two out by the mountains (2.2), and there is one at each ridge's foot between two realms (2.6). `maxMetal` 4 |
| Geothermal vents | 10: one in each bastion's court, one in each realm |

Every part is the same, turned a fifth round the centre. MapTool's symmetry only mirrors, so nothing used it: the converter made every height and colour, blending each part's noise into the next part's near the lines between them. Checked on 200,000 points against the same point a fifth of a turn round: median difference 0.00, 95% within 0.2, 99% within 1.2.

## The Dune planet

- **Ground:**
  - rippled golden dunes in the realms;
  - layered sandstone on the cliffs, ridges and mountains;
  - pale flagstone courts in the bastions;
  - block-built ramparts and towers, and stone steps on the ramps and zigzags;
  - packed-sand roads;
  - metal spots and vents ringed.
- **Plants and features (665):**
  - 135 palms and 60 ferns in an oasis at each ramp's foot;
  - 170 BAR desert rocks on the ridges and round the passes;
  - 265 smaller desert rocks as scree under the cliffs;
  - 35 half-buried solar and wind-turbine wrecks.
  
  All are kept off roads, ramps, zigzags, bases, spots and vents.
- **Sky:** a new deep blue sky with sand haze at the horizon, warm fog and a warm sun.
- **Weather:** wind 12 to 28, tidal 0. There is no water.

## Checked (`check.txt`, on the built map's own heights)

41 checks, all passed. They're run round the features too (`converter-run/run.txt`). The features block 0.6% of the map: each blocking one blocks its footprint and 8 more round it. The checks that something gets through are run round them; the checks that something doesn't are run on the heights alone.
- **Vehicles:**
  - From A's first peasant every start is reached (66.2% of the map is open to them round the features, 66.8% on the heights alone).
  - Each bastion can't be reached with its ramp shut.
  - No zigzag takes them up.
- **Hovercraft:** no zigzag takes them up; the ramp does.
- **Bots:**
  - Each zigzag on its own takes them up.
  - With the ramp and zigzags shut, the cliffs and ramparts stop them.
- **Passes:** bots cross each ridge's pass; vehicles don't.

Also checked:
- **Detail maps:** the splat, specular and normal maps were written upside down, as map `.dds` files are (PLAN.md rule 7).
- **Feature names:**
  - The palms and ferns are All That Simmers'.
  - The rocks (`rocks30_desert_*`) and wrecks (`armsolar_dead` and the like) are BAR's. MapTool warns that the map doesn't define them; BAR does.
- **The first pass profile failed:** it was a straight climb, so it ran up the ridge's cliff face. Bots couldn't cross. The pass is now the ground lowered along its line (no higher than 60 above the realms, no steeper than 38°), and all five pass the checks.
- **Headings (fixed after release, 2026-10-23):**
  - The first build wrote each feature's heading in degrees. MapTool's `item-add` takes the engine's units: 65536 to a turn, with 0 facing south. So all 665 features faced within 2° of south, and the other fifths' copies weren't turned.
  - The converter now writes `round((180 − degrees) × 65536 / 360)`, wrapped to 0-65535. Its degrees run clockwise from north, the way it turns the parts.
  - The rebuild differs from the release only in `mapconfig/featureplacer/set.lua` and in the SMF's minimap bytes (`MapTool compare`, and the SMF compared byte by byte). Its `run.txt` is the same line for line, but for the headings. Each copy faces its original turned with its fifth, to within a unit. The facings spread evenly: 166 south, 167 east, 167 north, 165 west.
  - The 41 checks come out exactly as before (`check.txt` is unchanged). They take each blocking feature's footprint as a square round it, whichever way it faces. How BAR turns a wreck's real footprint with its facing is still to try in game.
  - `info.txt` and `metal.txt` are the same as before. The 3D pictures were drawn again with the same cameras.

## The minimap and lobby picture

Since 2026-10-23, as for every map now, the minimap is the map seen straight down in 3D, with heights, light and features, not the flat texture. That covers the in-game minimap and the lobby's picture, which `MapTool install` draws from it. It's made as Starfall Marsh's is:
1. `MapTool render3d star_fort_1.0.sd7 top.png --size 8192x8192 --camera 7168,7168,0,89.9,72826 --models`. The camera is 5.08 map-widths up, four times Starfall Marsh's 1.27, with a render four times as wide, so the map still covers about 1950 pixels of it. At 1.27 widths, ground lower than the map's high edges was drawn inward of them, and the render's background showed through along the edges. Edge Keeps had a sliver of up to 5 pixels in 1024 and Corner Fortresses up to 12, round its edge lakes. Four times as far away leaves at most a hairline, and features and high ground sit a quarter as far out of place.
2. `Starfall minimap top.png minimap.png 1024` (`out/step47-starfall-marsh/converter`) cuts the map out of the render and scales it.
3. `Starfall setminimap <extracted map> minimap.png` puts it into the SMF; `MapTool pack` packs the map again.
4. `MapTool install` into a scratch maps folder gives the lobby picture (`bar-data/`).

## Pictures

- `map_2d.png`
- `3d_overview.png`
- `3d_bastion.png` (A's bastion: court, ramparts, towers, ramp, zigzags)
- `3d_oasis.png`
- `3d_passability_bots.png`
- `design/relief.png` (the heights alone)

## To try in BAR

- **Players:** a 20-player lobby (5 teams of 4). I haven't checked BAR's player limit. The map has no start boxes, so draw them in the lobby or play with fixed starts in the order above.
- **Ramparts:** whether buildings sit well on top (flat, about 160 wide, 200 at the foot), and whether the towers' tops (225 to 315 across) are useful.
- **Amphibious units and commanders** (54°) climb the zigzags and passes, as bots do.
- **Reclaim:** the rocks and wrecks hold 13,855 metal, 2,771 a team, mostly in the scree and the desert rocks.
- **Archive:** 102.5 MB, over GitHub's 100 MB a file, so the `.sd7` is on the releases and not in git.
