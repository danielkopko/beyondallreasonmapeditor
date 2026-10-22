# Corner Fortresses 1.0: checklist

`corner_fortresses_1.0.sd7` ("Corner Fortresses 1.0", short name `CornerFortresses`, map id 20261028) is the K3 drawing (`out/koth-maps/k3_corner_fortresses.svg`), built at its own size, 24 × 24, on the Forge planet (`out/koth-maps/planets.py`). The terrain, texture, detail maps and sky are new. The detail textures, weeds (ferns), featureplacer gadget and water settings are All That Simmers v1.1.1's, and the crates and trucks come from the editor's library (Industrial Revolution). How it was made is in `../converter/README.md`.

**Not tried in BAR yet.** BAR isn't installed on this machine, so everything below was checked with MapCore's copy of the engine's rules, not in the game.

**Download:** one file, [corner_fortresses_1.0.sd7](https://github.com/danielkopko/BarMaps/releases/download/corner-fortresses-1.0/corner_fortresses_1.0.sd7), from the public BarMaps release [corner-fortresses-1.0](https://github.com/danielkopko/BarMaps/releases/tag/corner-fortresses-1.0), with its lobby picture `Corner_Fortresses_1.0.jpg`. The same files are on [this repository's release](https://github.com/danielkopko/beyondallreasonmapeditor/releases/tag/corner-fortresses-1.0) (signed in as danielkopko). After rebuilding, `../king_maps.cmd publish corner_fortresses -Message "..."` pushes the notes and replaces both releases' files. `../king_maps.cmd install corner_fortresses` copies the map into BAR.

## The layout

| From the drawing | On the map |
|---|---|
| Four teams, each a king and three peasants | 16 start positions in `mapinfo.lua`: team 0 is A's king, 1-3 are A's peasants, 4 is B's king, and so on clockwise |
| Fields, the middle and the market | Fields at 240, falling to 210 and then 180 towards the middle. The market is a low rise of 220, 2150 across. Spoil heaps rise here and there |
| Fortress yards: raised kings' ground | One in each corner, about 2900 square with the gate corner cut off, at 330 on cliffs 40 across (over 54°): nothing but spiders climbs them. Paved in big slabs, with hazard stripes at the edges |
| Walls: ramparts, no doors | Earth ramparts 90 above the yard (420) along the two inner sides, 173 wide (133 flat on top, to build on), with sheer faces. A tower stands either side of the gate |
| A ramp for full access | One a fortress, through the gate, facing the middle: 422 wide at 12°, from the fields up to the yard |
| Bots-only zigzags | Two a fortress, one through each inner rampart: 64 wide, flights of 38° with level landings |
| Acid lakes | Four, at the middle of each edge between two fortresses, about 2150 × 1380 and 60 deep, with a crust round the shore. `water.damage` 60: they hurt units in them |
| Metal | 64 spots, 32.8 metal/s a team (131.3 in all). Each team has nine peasant spots (1.8), three in its fortress (2.0), two in its fields (2.2), one between two teams' fields (2.8) and one in the market (3.4). `maxMetal` 4 |
| Geothermal vents | 9: one in each fortress yard, one in each team's fields, one in the middle of the market |

Every part is the same, turned a quarter round the centre. Checked on 200,000 points against the same point a quarter-turn round: the heights match exactly (99% within 0.0).

## The Forge planet

- **Ground:**
  - an industrial estate's lots (720 × 560), most paved in cracked concrete slabs with some broken out to the gravel;
  - waste ground between them, gravel and dirt gone to weeds;
  - rust and oil stains;
  - worn asphalt roads, patched and cracked, with concrete kerbs;
  - big-slab yards with yellow-and-black hazard stripes in the fortresses;
  - a brick market square in stretcher bond;
  - panelled concrete ramparts, steel steps;
  - a sulphur-yellow crust round the acid lakes.
- **Features (856):**
  - 104 wrecked buildings in ruined works, each a factory or a moho mine with storage tanks and metal makers round it;
  - 84 smaller wrecks (energy storage, extractors, solar collectors);
  - 272 BAR rocks as rubble;
  - 192 crates and trucks: container yards, crates stacked in the fortress yards well back from each king, trucks by the roads, crane trucks by the market;
  - 204 weeds (ferns) in the waste ground.
  
  All are kept off roads, ramps, zigzags, bases, spots and vents.
- **Reclaim:** the wrecks and rubble hold 36,340 metal, 9,085 a team. That is about 4.6 minutes of a team's income from every extractor.
- **Sky and water:** a brown smog sky with yellow haze. The acid is bright yellow-green.
- **Weather:** wind 4 to 14, tidal 10.

## Checked (`check.txt`, on the built map's own heights)

29 checks, all passed. They're run round the features too (`converter-run/run.txt`). The features block 2.5% of the map: each blocking one blocks its footprint and 8 more round it, so any way through is at least 3 squares wide. The checks that something gets through are run round them; the checks that something doesn't are run on the heights alone.
- **Vehicles:**
  - From A's first peasant every start is reached (88.6% of the map is open to them round the features, 91.1% on the heights alone).
  - Each fortress can't be reached with its ramp shut.
  - No zigzag takes them up.
- **Hovercraft:** no zigzag takes them up; the ramp does.
- **Bots:**
  - Each zigzag on its own takes them up.
  - With the ramp and zigzags shut, the cliffs and ramparts stop them.

Also checked:
- **Feature names:**
  - The crates and trucks are defined by the map, copied in from the library.
  - The ferns are All That Simmers'.
  - The wrecks (`*_dead`) and rocks (`rocks30_map_*`) are BAR's. MapTool warns that the map doesn't define them; BAR does.
- **Made better before release:**
  - The first build passed every check but looked wrong.
    - Industrial Revolution's pipe models are runs up to 2000 long and 250 tall, so they looped over the ramparts.
    - Every bit of ground was the same tiled floor.
    - The roads were black strokes.
    - The market was a red checkerboard.
    - Its mushrooms were 8 across, too small to see.
  - The pipes and mushrooms are gone. The Forge look now has paved lots on waste ground, worn asphalt, brick paving and ruined works.

## Pictures

- `map_2d.png`
- `3d_overview.png`
- `3d_fort.png` (a fortress: yard, ramparts, gate towers, ramp, zigzags, and the acid lake behind)
- `3d_estate.png` (the fields: lots, roads, ruins, trucks)
- `3d_market.png`
- `3d_passability_bots.png`
- `design/relief.png` (the heights alone)

## To try in BAR

- **Acid:** whether `water.damage` 60 is right, and whether hovercraft crossing it take the damage. Change it in `mapinfo.lua`.
- **Reclaim:** whether 9,000 metal a team in wrecks is too much early on. The factories and moho mines hold the most.
- **Players:** a 16-player lobby (4 teams of 4). The map has no start boxes, so draw them in the lobby or play with fixed starts in the order above.
- **Archive:** 89.6 MB. It's on the releases, like the other king maps, and not in git.
