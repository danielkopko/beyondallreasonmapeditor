# Edge Keeps 1.0: checklist

`edge_keeps_1.0.sd7` ("Edge Keeps 1.0", short name `EdgeKeeps`, map id 20261027) is the K4 drawing (`out/koth-maps/k4_edge_keeps.svg`), built at its own size, 24 × 24, on the Heath planet (`out/koth-maps/planets.py`). The terrain, texture, detail maps and sky are new. The detail textures, bracken (ferns), featureplacer gadget and water settings are All That Simmers v1.1.1's, and the firs and birches come from the editor's library (Crimson Bay). How it was made is in `../converter/README.md`.

**Not tried in BAR yet.** BAR isn't installed on this machine, so everything below was checked with MapCore's copy of the engine's rules, not in the game.

**Download:** one file, [edge_keeps_1.0.sd7](https://github.com/danielkopko/BarMaps/releases/download/edge-keeps-1.0/edge_keeps_1.0.sd7), from the public BarMaps release [edge-keeps-1.0](https://github.com/danielkopko/BarMaps/releases/tag/edge-keeps-1.0), with its lobby picture `Edge_Keeps_1.0.jpg`. The same files are on [this repository's release](https://github.com/danielkopko/beyondallreasonmapeditor/releases/tag/edge-keeps-1.0) (signed in as danielkopko). After rebuilding, `../king_maps.cmd publish edge_keeps -Message "..."` pushes the notes and replaces both releases' files. `../king_maps.cmd install edge_keeps` copies the map into BAR.

**Rebuilt 2026-10-23, not yet on the releases:** the map beside this file has its features turned the way the converter meant (*Headings*, below), and a minimap and lobby picture seen from above in 3D (*The minimap and lobby picture*). The releases still have the first build, in which every feature faces south and the minimap is the flat texture. `../king_maps.cmd publish edge_keeps` replaces them.

## The layout

| From the drawing | On the map |
|---|---|
| Four teams, each a king and three peasants | 16 start positions in `mapinfo.lua`: team 0 is A's king, 1-3 are A's peasants, 4 is B's king, and so on clockwise (A north) |
| Fields and the basin | A moor at 240, falling to 220 in the basin round the loch. Broad rolling hills of about ±40 on top, gentle enough for vehicles everywhere (93% of the map is open to them round the features, 95% on the heights alone). Roads and bases follow the hills |
| Keeps: raised kings' ground | Four D-shaped cliff-tops at 360 in the middle of each edge, about 3100 across and reaching 2000 in from the edge, on granite cliffs 40 across (over 54°): nothing but spiders climbs them |
| A ramp for full access | One a keep, facing the loch: 461 wide at 12°, from the moor up to the keep |
| Bots-only zigzags | Two a keep, one up each flank: 64 wide, flights of 38° with level landings |
| No walls, no doors | K4 has no ramparts: the keeps' cliffs are their walls |
| Crags | Four granite crags at 300 between the corners and the loch, about 1000 × 600: obstacles, unclimbable |
| The loch and its isle | 2765 across, 60 deep, with gently shelving banks. The isle is 1460 across and 60 high, crossed to by four causeways of granite setts (decks at 16), one from each keep's road |
| Metal | 56 spots, 28.4 metal/s a team (113.6 in all). Each team has nine peasant spots (1.8), two on its keep (2.0), two on the moor (2.2, 2.6) and one on the isle (3.4). `maxMetal` 4 |
| Geothermal vents | 9: one on each keep, one in each corner, one in the middle of the isle |

Every part is the same, turned a quarter round the centre. Checked on 200,000 points against the same point a quarter-turn round: median difference 0.00, 99% within 0.2.

## The Heath planet

- **Ground:**
  - heather on the dry rises, dark and flowering in patches;
  - tan moor grass, greener in the hollows;
  - small peat hags in the wet ground;
  - rust-brown bracken on the slopes;
  - granite with lichen on the cliffs and crags;
  - short turf on the keeps, heather round their rims, granite breaking through, flagstones round each king;
  - black peat round the loch;
  - peat-brown tracks and stone steps.
- **Plants and features (2,532):**
  - 1,624 firs, in pinewoods thick in the middle and thinning to lone trees;
  - 196 birches in copses along the loch and in the wet hollows;
  - 212 ferns as bracken on the slopes;
  - 68 BAR granite rocks in tors on the crests, 300 as scree under the crags and down the loch's banks, and 80 as boulders across the moor;
  - 36 cairns (BAR's `legstone`) on the keeps' rims;
  - 16 mossy rocks on the isle.
  
  All are kept off roads, ramps, zigzags, causeways, bases, spots and vents.
- **Sky and water:** a grey sky with drifting mist and grey-blue fog. The loch is dark and peaty.
- **Weather:** wind 10 to 25, tidal 12.

## Checked (`check.txt`, on the built map's own heights)

30 checks, all passed. They're run round the features too (`converter-run/run.txt`). The features block 1.9% of the map: each blocking one blocks its footprint and 8 more round it, so the pinewoods must leave ways at least 3 squares wide. The checks that something gets through are run round them; the checks that something doesn't are run on the heights alone.
- **Vehicles:**
  - From A's first peasant every start is reached, and the isle.
  - Each keep can't be reached with its ramp shut.
  - No zigzag takes them up.
- **Hovercraft:** no zigzag takes them up; the ramp does.
- **Bots:**
  - Each zigzag on its own takes them up.
  - With the ramp and zigzags shut, the cliffs stop them.

Also checked:
- **Feature names:**
  - The firs and birches are defined by the map, copied in from the library.
  - The ferns are All That Simmers'.
  - The rocks (`rocks30_def_*`, `rocks30_moss_*`) and cairns (`legstone`) are BAR's. MapTool warns that the map doesn't define the rocks; BAR does.
- **Made better before release:**
  - The first build was flat, with blotchy heather and pines only in three thin rows. The converter's Heath look now gives the moor broad hills, softer heather and moor grass, small peat hags, sett causeways, pinewoods, birch copses, tors on the crests, scree and boulders.
  - The hills never dip into puddles: dry ground stays above the shore.
- **Headings (fixed after release, 2026-10-23):**
  - The first build wrote each feature's heading in degrees. MapTool's `item-add` takes the engine's units: 65536 to a turn, with 0 facing south. So all 2,532 features faced within 2° of south, and the other quarters' copies weren't turned.
  - The converter now writes `round((180 − degrees) × 65536 / 360)`, wrapped to 0-65535. Its degrees run clockwise from north, the way it turns the parts.
  - The rebuild differs from the release only in `mapconfig/featureplacer/set.lua` and in the SMF's minimap bytes (`MapTool compare`, and the SMF compared byte by byte). Its `run.txt` is the same line for line, but for the headings. Each copy faces its original turned exactly a quarter with its part, so the facings are even: 633 each way.
  - The 30 checks come out exactly as before (`check.txt` is unchanged). They take each blocking feature's footprint as a square round it, whichever way it faces.
  - `info.txt` and `metal.txt` are the same as before. The 3D pictures were drawn again with the same cameras.

## The minimap and lobby picture

Since 2026-10-23, as for every map now, the minimap is the map seen straight down in 3D, with heights, light and features, not the flat texture. That covers the in-game minimap and the lobby's picture, which `MapTool install` draws from it. It's made as Starfall Marsh's is:
1. `MapTool render3d edge_keeps_1.0.sd7 top.png --size 8192x8192 --camera 6144,6144,0,89.9,62423 --models`. The camera is 5.08 map-widths up, four times Starfall Marsh's 1.27, with a render four times as wide, so the map still covers about 1950 pixels of it. At 1.27 widths, ground lower than the map's high edges was drawn inward of them, and the render's background showed through along the edges. Edge Keeps had a sliver of up to 5 pixels in 1024 and Corner Fortresses up to 12, round its edge lakes. Four times as far away leaves at most a hairline, and features and high ground sit a quarter as far out of place.
2. `Starfall minimap top.png minimap.png 1024` (`out/step47-starfall-marsh/converter`) cuts the map out of the render and scales it.
3. `Starfall setminimap <extracted map> minimap.png` puts it into the SMF; `MapTool pack` packs the map again.
4. `MapTool install` into a scratch maps folder gives the lobby picture (`bar-data/`).

## Pictures

- `map_2d.png`
- `3d_overview.png`
- `3d_keep.png` (A's keep and ramp from the loch side, with pinewoods)
- `3d_moor.png` (a corner: crag, pinewood, tors, A's keep)
- `3d_loch.png`
- `3d_passability_bots.png`
- `design/relief.png` (the heights alone)

## To try in BAR

- **Players:** a 16-player lobby (4 teams of 4). The map has no start boxes, so draw them in the lobby or play with fixed starts in the order above.
- **Reclaim:** the rocks hold 10,784 metal, 2,696 a team, and the trees give energy.
- **Woods:** whether the pinewoods block too much. The firs are blocking features; they're kept off roads, spots and bases.
- **Amphibious units and commanders** (54°, any depth) climb the zigzags and walk under the loch, as on any map.
- **Archive:** 97.7 MB. It's on the releases, like the other king maps, and not in git.
