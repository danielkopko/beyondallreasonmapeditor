# Five Rivers: the converter

How `five_rivers_1.0.sd7` was made from `out/koth-maps/k7_five_rivers.svg` (the drawing) and All That Simmers v1.1.1 (its plants, detail textures, featureplacer gadget and water settings). The editor can't make a map from scratch or at a new size, so this program does, with the editor's MapCore library, as the editor's tests make their synthetic map; MapTool then adds the metal, vents and features.

## Steps

1. `python out/koth-maps/export_geometry.py five_rivers design/geometry.json`: the drawing's geometry (water, isle, halls, ramps, switchbacks, bridges, fords, paths, metal, vents, keeps, peasants), every team's part turned into place, in the drawing's pixels.
2. `MapTool extract all_that_simmers_v1.1.1.sd7 <template>`.
3. `FiveRivers build design/geometry.json <template> <work>`:
   - **Heights** for all 1793 × 1793 vertices (`Terrain.cs`), then the **access checks** (`Access.cs`); it stops here if any fails.
   - **Texture**, 14336 × 14336, painted texel by texel (`Paint.cs`) and written straight into a new SMT; a new SMF (28 × 28) with the heights, minimap and grass (`Create.cs`).
   - **Detail maps** as DDS: splat distribution (4096, for All That Simmers' four detail textures), specular (4096), normals (4096, the slopes), and a **night sky** cube (`Derive.cs`).
   - **mapinfo.lua**: name, credits, metal, the Bloom planet's colours and weather, glowing water, the new maps and sky, twenty starts (`Program.cs`, `Build.WriteMapInfo`).
   - **run.txt** for MapTool: metal spots, geothermal vents, plants, crystals and rocks (`Place.cs`; headings in the engine's units, 65536 a turn with 0 facing south, from the converter's degrees clockwise from north); then `five_rivers_stage.sd7`.
   - All That Simmers' own layout is left out: its SMF and SMT, its detail maps, its grass and feature list, its sunrise sky, two unused textures, and its map options (a *Dry* option there would reset the heights to 150-1000).
4. `MapTool edit five_rivers_stage.sd7 five_rivers_1.0.sd7 @run.txt`.
5. `FiveRivers check design/geometry.json <final map, extracted>`: the access checks again on the built map's heights, and how alike the fifths are.
6. `MapTool render … --layer splat` and `--layer normals`: the detail maps line up with the texture (map `.dds` files are stored upside down; `Derive.cs` flips each image before writing it).
7. `five_rivers.cmd publish`: the map on the GitHub releases, here and in BarMaps (`../RELEASE.md`).

## Layout

- 28 × 28 (14336 units square); the drawing's 640 pixels are 14336 units, 22.4 a pixel. Centre (7168, 7168); Team A's king is north, B to D clockwise.
- **Five-fold, not mirrored.** MapTool's symmetry mirrors or turns by half, so nothing here uses it or Beautify. Every height and colour is made in the converter, with noise read in Team A's fifth (`Design.Fold`); the fifths meet along the rivers' winding middles, under water, and at the isle, which is kept smooth.
- **Heights:** rim 220 beyond 4256 units from the centre, 140 inside 2800; water level 0, the lake (radius 1792) and rivers (582 wide) 60 deep, in valleys that fall to the water at about 20° at most; fords 10 deep; bridges are causeways with decks at 16; the isle 8 at its shore, 60 on top; beyond the play circle (6989), mountains on a cliff.
- **Halls:** ovals 3494 × 2061 units at 340 on cliffs 120 high and 40 across (72°). A graded ramp at the front, 538 wide at 12°, from the middle peasant's base. Three switchbacks, 64 wide: two flights of 38° (bots climb 54°; vehicles 27°, hovercraft and the Thor 33° don't) with a level landing, from 40 units in from the edge down to the ground.
