# Star Fort 1.0: checklist

`star_fort_1.0.sd7` ("Star Fort 1.0", short name `StarFort`, map id 20261026) is the K6 drawing (`out/koth-maps/k6_star_fort.svg`), built at its own size, 28 × 28, on the Dune planet (`out/koth-maps/planets.py`). The terrain, texture, detail maps and sky are new. The palms, detail textures, featureplacer gadget and water settings are All That Simmers v1.1.1's. How it was made is in `../converter/README.md`.

**Not tried in BAR yet.** BAR isn't installed on this machine, so everything below was checked with MapCore's copy of the engine's rules, not in the game.

**Download:** one file, [star_fort_1.0.sd7](https://github.com/danielkopko/BarMaps/releases/download/star-fort-1.0/star_fort_1.0.sd7), from the public BarMaps release [star-fort-1.0](https://github.com/danielkopko/BarMaps/releases/tag/star-fort-1.0), with its lobby picture `Star_Fort_1.0.jpg`. The same files are on [this repository's release](https://github.com/danielkopko/beyondallreasonmapeditor/releases/tag/star-fort-1.0) (signed in as danielkopko). After rebuilding the map, `../king_maps.cmd publish star_fort -Message "..."` pushes the notes and replaces both releases' files. `../king_maps.cmd install star_fort` copies the map into BAR.

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

41 checks, all passed:
- **Vehicles:**
  - From A's first peasant every start is reached (66.8% of the map is open to them).
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
- **Archive:** 102.5 MB, over GitHub's 100 MB a file, so the `.sd7` is on the releases and not in git.
