# Five Rivers 1.0: checklist

`five_rivers_1.0.sd7` ("Five Rivers 1.0", short name `FiveRivers`) is the K7 drawing (`out/koth-maps/k7_five_rivers.svg`), built at its own size, 28 × 28, on the Bloom planet (`out/koth-maps/planets.py`). The terrain, texture, detail maps and sky are new; the plants, detail textures, featureplacer gadget and water settings are All That Simmers v1.1.1's. How it was made is in `converter/README.md`.

**Not tried in BAR yet.** BAR isn't installed on this machine, so everything below was checked with MapCore's copy of the engine's rules, not in the game.

**Download:** one file, [five_rivers_1.0.sd7](https://github.com/danielkopko/BarMaps/releases/download/five-rivers-1.0/five_rivers_1.0.sd7), from the public BarMaps release [five-rivers-1.0](https://github.com/danielkopko/BarMaps/releases/tag/five-rivers-1.0), with its lobby picture `Five_Rivers_1.0.jpg`; the same files are on [this repository's release](https://github.com/danielkopko/beyondallreasonmapeditor/releases/tag/five-rivers-1.0) (signed in as danielkopko). After rebuilding the map, `five_rivers.cmd publish -Message "..."` pushes the notes and replaces both releases' files; `five_rivers.cmd install` copies the map beside it into BAR. `RELEASE.md` has how it was converted and released.

**Rebuilt 2026-10-23, not yet on the releases:** the map beside this file has its features turned the way the converter meant (*Headings*, below), and a minimap and lobby picture seen from above in 3D (*The minimap and lobby picture*). The releases still have the first build, in which every feature faces south and the minimap is the flat texture. `five_rivers.cmd publish` replaces them.

Install by hand: copy `five_rivers_1.0.sd7` into BAR's `maps` folder, and `bar-data/LuaMenu` (beside this file) into BAR's data folder (the folder that holds `maps`, usually `%LOCALAPPDATA%\Programs\Beyond-All-Reason\data`) for the lobby picture; or `MapTool install five_rivers_1.0.sd7` on a machine with BAR.

## The layout

| From the drawing | On the map |
|---|---|
| Five teams, each a king and three peasants | 20 start positions in `mapinfo.lua`: team 0 is A's king, 1-3 A's peasants, 4 B's king, and so on round clockwise (A north) |
| Hall: oval cliff-top on the rim | 3494 × 2061 units at height 340, long along the rim, on a cliff 120 high and 40 across (72°): nothing but spiders climbs it |
| Graded ramp facing the peasants | 538 wide at 12°, from the middle peasant's base to the hall's edge, drawn as stone steps |
| Bots-only zigzag switchbacks on the other three sides | One at each end and one at the back: 64 wide, two flights of 38° with a level landing. Bots (54°) climb them; vehicles (27°), hovercraft and the Thor (33°) don't. The back one comes down to a strip of rim about 580 wide behind the hall |
| Rivers and the lake | Water level 0, 60 deep, in valleys that fall to the water at about 20° at most: vehicles reach the shore anywhere |
| Stone bridges | Causeways with decks at 16: one across each river, one from each realm to the isle |
| Fords | The river bed raised to 10 deep (land units go to 20), gravel |
| The isle | Low (8 at the shore, 60 on top), so its bridges meet it level. **The drawing said 200-230; its heights line now says 60** |
| Map edge | Beyond the play circle (radius 6989), mountains on a cliff |
| Metal | 75 spots: each peasant's three (1.8), four on each hall (2.0), one in each ford (3.0), five on the isle (3.4); 24.3 metal/s a team, 153 in all. `maxMetal` 4 |
| Geothermal vents | 11: one on each hall behind the throne, one in each realm's fields by the lake road, one in the middle of the isle |

Everything is the same in all five fifths. MapTool's symmetry only mirrors, so nothing used it: the converter made every height and colour, with its noise read in Team A's fifth, and the fifths meet under the rivers and at the smooth isle. Checked: 200,000 points against the same point a fifth of a turn round, median difference 0.00, 99% within 0.1.

## The Bloom planet

- **Ground:** teal moss with glowing veins, blue-grey stone on the hall tops, layered blue-grey rock on the cliffs, sand and a cyan glow along the water, pale glowing roads, stone stairs and causeways, gravel fords; metal spots and vents dark, ringed with light.
- **Plants and features (2,515):** 765 kapok and oak trees in groves, 1,215 ferns along the banks, 180 glowing mushrooms in rings, 115 BAR crystals (teal, lime, blue) at the water's edge and round the isle, 240 BAR mossy rocks along the halls' rims and feet. All are kept off roads, stairs, bridges, fords, bases, thrones, spots and vents.
- **Sky:** a new night sky (violet, a teal glow at the horizon, stars) in place of All That Simmers' sunrise; fog (0.2, 0.3, 0.4); a blue-white sun; cool but bright ambient light, so it can be played.
- **Water:** glowing blue (base 0.1, 0.45, 0.75).
- **Weather:** wind 3 to 12, tidal 20. Gravity is All That Simmers' 100.

## Checked (`check.txt`, on the built map's own heights)

62 checks, all passed:
- **Vehicles:**
  - From A's first peasant every start is reached, and the isle.
  - Each hall, with its ramp shut, can't be reached.
  - With the bridges and fords shut, no neighbour and not the isle.
  - By the fords alone, and by the river bridges alone, the next team's land.
  - No switchback takes them up.
- **Hovercraft:** no switchback takes them up; the ramp does.
- **Bots:** each switchback on its own takes them up. With the ramp and switchbacks shut, the cliffs can't be climbed. With the bridges and fords shut, the rivers stop them.

Also checked:
- **Detail maps:** the splat, specular and normal maps line up with the texture. They're stored upside down, as map `.dds` files are (PLAN.md rule 7); the first build had them the wrong way up, which MapTool's `--layer splat` and `--layer normals` renders showed.
- **Feature names:** every one placed (54 distinct) is defined by BAR (crystals, mossy rocks) or by the map (All That Simmers' plants).
- **Headings (fixed after release, 2026-10-23):**
  - The first build wrote each feature's heading in degrees. MapTool's `item-add` takes the engine's units: 65536 to a turn, with 0 facing south. So all 2,515 features faced within 2° of south, and the other fifths' copies weren't turned. Step 47's test map showed it.
  - The converter now converts its headings (degrees clockwise from north, the way it turns the fifths) to the engine's units: `round((180 − degrees) × 65536 / 360)`, wrapped to 0-65535.
  - The rebuild differs from the release only in `mapconfig/featureplacer/set.lua` and in the SMF's minimap bytes (`MapTool compare`, and the SMF compared byte by byte). Its `run.txt` is the same line for line, but for the headings. Each copy faces its original turned with its fifth, to within a unit. The facings spread evenly: 618 south, 637 east, 629 north, 631 west.
  - `check.txt`, `info.txt` and `metal.txt` are the same as before. The pictures with features in them were drawn again.

## The minimap and lobby picture

Since 2026-10-23, as for every map now, the minimap is the map seen straight down in 3D, with heights, light and features, not the flat texture. That covers the in-game minimap and the lobby's picture, which `MapTool install` draws from it. It's made as Starfall Marsh's is:
1. `MapTool render3d five_rivers_1.0.sd7 top.png --size 8192x8192 --camera 7168,7168,0,89.9,72826 --models`. The camera is 5.08 map-widths up, four times Starfall Marsh's 1.27, with a render four times as wide, so the map still covers about 1950 pixels of it. At 1.27 widths, ground lower than the map's high edges was drawn inward of them, and the render's background showed through along the edges. Edge Keeps had a sliver of up to 5 pixels in 1024 and Corner Fortresses up to 12, round its edge lakes. Four times as far away leaves at most a hairline, and features and high ground sit a quarter as far out of place.
2. `Starfall minimap top.png minimap.png 1024` (`out/step47-starfall-marsh/converter`) cuts the map out of the render and scales it.
3. `Starfall setminimap <extracted map> minimap.png` puts it into the SMF; `MapTool pack` packs the map again.
4. `MapTool install` into a scratch maps folder gives the lobby picture (`bar-data/`).

## Pictures

`map_2d.png`, `map_2d_overlays.png` (water, metal, items, starts), `3d_overview.png`, `3d_hall_A.png`, `3d_switchback.png`, `3d_isle.png`, `3d_passability_vehicles.png`, `3d_passability_bots.png`, and `design/relief.png` (the heights alone).

## To try in BAR

- **Players:** a 20-player lobby (5 teams of 4). I haven't checked BAR's player limit. There are no start boxes in the map, so draw them in the lobby, or play with fixed starts in the order above.
- **Light:** whether the night is too dark to play. If it is, raise `lighting.groundAmbientColor` and `groundDiffuseColor` in `mapinfo.lua`.
- **Fords:** whether extractors go down on the ford spots (10 deep; MapTool warns that only builders that reach them can place one).
- **Amphibious units and commanders** (54°, any depth) climb the switchbacks and walk under the rivers, as on any map.
- **Ships:** the bridges are causeways, so they cut the rivers and lake into separate stretches.
- **Archive:** 112 MB, over GitHub's 100 MB a file. To keep it in git, shrink it as Simmering Ravine was (the normal map and sky smaller), or use Git LFS.
