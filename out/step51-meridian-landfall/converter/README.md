# Meridian Landfall: the converter

How `meridian_landfall_1.0.sd7` is made. **The ship is Starship Meridian's own:** `Landfall.csproj` compiles Meridian's `Layout.cs`, `Terrain.cs`, `Common.cs`, `Paint.cs`, `Check.cs`, `Place.cs` and `Create.cs` from `../../step50-starship-meridian/converter`, unchanged, and moves the ship by one offset. **So an edit to any of those files changes this map's build too.** Everything round it is made here: the asteroid's terrain, texture, detail maps and sky, the moonrock models, the landing struts and the void's invisible rubble. It is built on All That Simmers v1.1.1 for its featureplacer gadget (its detail textures aren't used).

**The layout** outside the ship is `../design/layout.py`: one table, which draws `../design/layout.svg` and writes `../design/layout.json`. The build copies that file beside the converter, and `World.cs` reads it. The small craters are generated there (seed 51), and the outlines' shapes come from the same SplitMix64 hash in both, so the build makes the craters the drawing shows. Run `python layout.py` after changing the table, then rebuild.

## Commands

- `Landfall gallery <template> <work>`: the asteroid's models on flat ground, with each one's triangles and footprint, and whether it's its own mirror image (if not, it gets a twin).
- `Landfall build <template> <work>`: the map folder, `meridian_landfall_1.0_stage.sd7` and `run.txt`. It runs the checks first and builds anyway, to look.
- `MapTool edit meridian_landfall_1.0_stage.sd7 <out>.sd7 @run.txt`: the metal, the vents and the features.
- `Landfall check <final map, extracted> <run.txt>`: the checks again on the built map's own heights.
- `Landfall minimap <render.png> <out.png> <x0> <z0> <x1> <z1> [size]` and `Landfall setminimap <extracted map> <png>`: the minimap from a straight-down `render3d` with `--models`, cut at the map's corners projected through the camera, scaled to a square, and starred where the void is. Then `MapTool pack`. The lobby picture follows, because `MapTool install` draws it from the minimap.
- `MapTool install`, `render`, `render3d`, `info`, `metal`: the lobby picture, pictures and reports beside this folder.

The whole build, in order (the paths are examples):

```
python ../design/layout.py
Landfall build <template> work
MapTool edit work/meridian_landfall_1.0_stage.sd7 work/features.sd7 @work/run.txt
MapTool extract work/features.sd7 work/x
Landfall check work/x work/run.txt
MapTool render3d work/features.sd7 top.png --size 8192x8192 --camera 8192,9216,0,89.9,93635 --models
Landfall minimap top.png minimap.png 3231 3123 4961 5070 1024
Landfall setminimap work/x minimap.png
MapTool pack work/x meridian_landfall_1.0.sd7
MapTool install meridian_landfall_1.0.sd7 bar-data/maps
```

`../converter-run/` keeps the last build's `build.log`, MapTool's `run.log` and `run.txt`.

## The files

- **Meridian's, linked:** the ship's layout, its heights (`Terrain.Build`, in its own coordinates), its painter, its features (`Place.Ship`), `Access.Fill`, and `Create`.
- `World.cs`:
  - the map's size (32 × 36);
  - the ship's offset (2048, 2048) and its raise (400, so its deck is at 640);
  - the table from `layout.json`: the outside starts, spots, vents, bay doors, aprons, bowls, pits and small craters;
  - the 16 starts, the 90 spots and the 7 vents, the ship's included;
  - the hash and the outlines (`Jag` for the pits, `Waver` for the bowls and small craters);
  - the asteroid's edge (`EdgeDepth`, `RockEdge`).
- `Asteroid.cs`: the heights, per vertex, for the port half, then mirrored:
  1. the plain, swelling gently, faded flat near the hull, and dead level on the aprons, the mares and round every spot and vent;
  2. bowls and small craters: a flat floor, a straight wall (41°; 39° for the small ones) up to a raised rim, and the rim falling away under 24°. The profile is scaled with the wavering outline;
  3. void pits: a broken rim (ridged noise, crossed by Voronoi cracks, its width wavering) and low hummocks of ejecta beyond (kept off the spots, mares and aprons). A scarp on the pit's fractal outline falls in three steps, its width wavering and its face roughened, to a floor far below;
  4. the edge: a slope worn down over ages (`EdgeAt`) from a rounded shoulder 360 to 480 inside the edge's line into the void, averaging about 23°, gullied straight down and rilled near the top, the gullies deepening as they go;
  5. the ship: Meridian's heights raised inside the hull, its sheer side down to the plain, and the bay doors cut level through the rim. Its walls within 48 of a floor above 280 (the bridge's, its landings', the ramps' upper parts) are kept 60 over it: Meridian's aren't, beside its bridge ramps. A check compares the result with Meridian's own heights.
  - `VoidV` is each vertex's height over the void's cut there: below zero is void, not drawn, and covered by rubble.
- `LandPaint.cs`: the texture.
  - Inside the hull and on its side, every texel is Meridian's painter's.
  - Outside are:
    - the regolith and its micro-craters, and the mares;
    - the craters' floors, ejecta and rays;
    - each pit in its own rock (`PitRocks`: iron, copper, ilmenite, anorthosite), its walls in layers of their own tones with seams of its ore in some, its rims bared and cracked, stones and round flecks of ore thrown out;
    - the edge's worn slope (`EdgeFace`): laminated rock bared where it steepens, dust on the shoulder, darker gully floors streaked downslope, scree toward the foot;
    - only broad tones on the steepest faces, where the cast-down texture would smear into stripes;
    - soot along the hull and the engines' blast, and tracks from the bay doors;
    - ore and frost at the spots and vents.
  - The void is transparent, its line roughened texel by texel.
  - Every noise is read at the port half's mirror image.
  - Also the specular and normal maps (written upside down, PLAN.md rule 7). The splat and the starfield sky are Meridian's.
- `Rocks.cs`: the moonrock (boulders in four sizes, and slabs and shards as faceted lumps, each face dust, basalt or fresh-broken by how it lies) on its own 1024 atlas, the ship's landing strut, and the void's rubble: an invisible feature, 128 × 128, whose collision box is a 1-unit cube 600 under the ground.
- `Mesh.cs`, `Atlas.cs`, `Models.cs`, `Features.cs`: Meridian's model builder, atlas, models and feature writer, copied. The mesh maps the moonrock's regions to the second atlas, and the writer gives each model its atlas and writes the rubble's buried collision box.
- `LandPlace.cs`: what MapTool adds.
  - The ship's features, moved.
  - The struts, three on each long side between the bay doors.
  - Cargo at the aprons' corners.
  - Moonrock round the pits, on the bowls' and small craters' rims, along the edge and over the plains, kept off the aprons, mares, spots, vents, ship and void.
  - The rubble over every 128-cell with void in it.
  - `run.txt`, with headings in engine units (65536 a turn).
- `LandCheck.cs`: the checks (see `../CHECKLIST.md`).
- `Program.cs`: the commands, the model list (Meridian's and the asteroid's, with mirror-image twins of any lopsided model), the models' symmetry test, and `mapinfo.lua`:
  - the name, `maxMetal` 4;
  - `voidGround` on, `voidWater` off;
  - the starfield sky, no fog, no wind or tide;
  - the star's light, and the sixteen starts.
