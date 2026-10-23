# Starfall Marsh: the converter

How `starfall_marsh_1.0.sd7` is made: the terrain, texture, detail maps and sky, and the ship and its wreckage modelled and painted from scratch, on All That Simmers v1.1.1 (its plants, detail textures, featureplacer gadget and water settings). There is no drawing; the layout is `Layout.cs`.

## Commands

- `Starfall slice <template> <work>`: a flat 8 × 8 test map with a test block (an arrow on its +x end and top, a crimson fin on +z) and the atlas on a board. It proved, before any detail:
  - that a home-made model loads;
  - that its texture is the right way up;
  - its scale;
  - what a heading means (a heading of 16384 turns a model's +x to the north).
- `Starfall gallery <template> <work>`: every model on flat ground, the ship where it lies and the wreckage in a row, with each model's triangles and footprint.
- `Starfall build <template> <work>`: the map folder, `starfall_marsh_1.0_stage.sd7` and `run.txt` (it runs the checks first and builds anyway, to look).
- `MapTool edit starfall_marsh_1.0_stage.sd7 starfall_marsh_1.0.sd7 @run.txt`: metal, vents, the ship, wreckage and jungle.
- `Starfall check <final map, extracted> <run.txt>`: the checks again on the built map's own heights.
- `Starfall minimap <render.png> <out.png> [size]` and `Starfall setminimap <extracted map> <png>`: the minimap from a straight-down `render3d` with `--models`. The first cuts the map out of the render and scales it; the second puts it into the SMF. Then `MapTool pack`. The lobby picture follows, because `MapTool install` draws it from the minimap.
- `MapTool install`, `render`, `render3d`, `info`, `metal`: the lobby picture, pictures and reports beside this folder.

## The files

- `Layout.cs`: the map in game units: the furrow, the ship's stations, the mound and its track, the lanes, starts, metal spots, vents, streams, pools, trails.
- `Common.cs`: points, noise, and the mirror. The north half is made; the south is its mirror image across the middle line. Noise is read in the north half and blended with its mirror image within 400 of the line, so there is no crease.
- `Terrain.cs`, built in this order:
  - the jungle floor at 40, nearly level in the bases;
  - the furrow (a 14-deep floor, banks, berms; ending under the mound);
  - streams and ragged pools, and a marsh of mud and puddles round them;
  - the mound (a 520 crest, a steep west face with slumped terraces, lumps, radial gullies);
  - dirt spilled over the fore hull;
  - the track up the mound's north-east flank;
  - level pads for the spots, vents and the prow.
- `Mesh.cs`: a model builder of flat-shaded polygons on one atlas. Every face is cut into tiles of 128 units, so a tiled cell repeats without its texture wrapping (an S3O can't wrap inside an atlas cell). Triangles are wound as the editor's structure models are.
  - **Weathering:** faces turned up wear moss, faces below a model's dirt line wear earth, and a model can add its own rule (the fore hull is caked in earth towards the mound).
  - **Solid box:** plants (ribbons, ferns, saplings) don't count towards it. That box is what the footprint and collision box cover.
  - **Mirroring:** `Mirrored()` gives the mirror image across z = 0.
- `Atlas.cs`: the atlas, 2048 square, 16 cells.
  - Texture 1 is colour, with alpha 0 (BAR mixes a feature's team colour, black, in by alpha).
  - Texture 2 is glow, metalness and roughness, as BAR's model shader reads them for features marked `cuspbr`.
  - Tiled cells wrap exactly once (periodic noise), so tiles meet without a seam.
- `Models.cs`:
  - **The hull:** one 12-sided cross-section, lofted.
  - **Dressing:** armour plates in layers down the sides and over the shoulders, glowing seams, a brass belt, a vented spine with chevron fins, then vines, ferns and saplings.
  - **The torn ends:** a jagged skin, an inner skin, a rim, a bulkhead, decks, ribs, cables and the reactor.
  - **The engines, aft hull, fore sections and prow,** and the wreckage pieces and reeds.
- `Features.cs`: each feature's `.lua` (footprint and box collision volume from its solid box; landmarks indestructible with no reclaim, wreckage reclaimable), its `.s3o`, and the atlas as TGA, in `features/starfall`, `objects3d` and `unittextures`. `Jungle.cs` defines the template's banyan and mangrove models, which All That Simmers ships without definitions; four of its six mangroves name textures it lacks, so only two are used.
- `Place.cs`:
  - **The ship:** on the middle line.
  - **Wreckage:** where it belongs, then thrown across the field, smaller the farther out. It keeps off the ship, the trails, the breach's way through, spots, vents, starts and its own mirror image.
  - **The jungle:** on a 34-unit grid by density, wetness and slope.
  - **`run.txt`:** every north item with its mirror image. Wreckage uses its twin model at heading −h, plants the mirrored heading, and headings are in engine units (65536 a turn).
- `Paint.cs`: the ground's colour per texel. The ship's silhouette and rust round the wreckage are painted from the models' triangles seen from above, so the crash shows on the minimap. Also the splat, specular and normal maps (written upside down, PLAN.md rule 7), grass, and the sky.
- `Check.cs`: the checks (see `../CHECKLIST.md`).
- `Create.cs`: copied from the king maps' converter. It builds a new SMF and SMT on the template's other files.
