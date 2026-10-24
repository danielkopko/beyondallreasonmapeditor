# Starship Meridian: the converter

How `starship_meridian_1.0.sd7` is made. The terrain, texture, detail maps and starfield sky are made here, and so is every feature, all modelled and painted from scratch on one atlas: the craft in the hangars, the ship's machines and landmarks, the rooms' props and the hull's dressing. It is built on All That Simmers v1.1.1 for its featureplacer gadget (its detail textures aren't used). The layout is `Layout.cs`, the same numbers as `../design/layout.py`, which draws `../design/layout.svg`.

## Commands

- `Meridian gallery <template> <work>`: every model on flat deck, in rows (the craft and a service tower; the landmarks; the bridge, power and crew props; the cargo, shop and hull models), with the viewport and an engine on their own. It lists each model's triangles and footprint and whether it's its own mirror image (if not, it gets a twin).
- `Meridian build <template> <work>`: the map folder, `starship_meridian_1.0_stage.sd7` and `run.txt`. It runs the checks first and builds anyway, to look.
- `MapTool edit starship_meridian_1.0_stage.sd7 <out>.sd7 @run.txt`: the metal, the vents and the features.
- `Meridian check <final map, extracted> <run.txt>`: the checks again on the built map's own heights.
- `Meridian minimap <render.png> <out.png> <x0> <z0> <x1> <z1> [size]` and `Meridian setminimap <extracted map> <png>`: the minimap from a straight-down `render3d` with `--models`.
  - The first cuts the map out of the render at the given pixels (the map's corners at the deck's height, projected through the camera), scales it to a square (unevenly: the map is 24 × 28), and puts a starfield wherever the render shows its background, which is space.
  - The second puts it into the SMF.
  - Then `MapTool pack`. The lobby picture follows, because `MapTool install` draws it from the minimap.
- `MapTool install`, `render`, `render3d`, `info`, `metal`: the lobby picture, pictures and reports beside this folder.

The whole build, in order (the paths are examples):

```
Meridian build <template> work
MapTool edit work/starship_meridian_1.0_stage.sd7 work/features.sd7 @work/run.txt
MapTool extract work/features.sd7 work/x
Meridian check work/x work/run.txt
MapTool render3d work/features.sd7 top.png --size 8192x8192 --camera 6144,7168,0,89.9,72827 --models
Meridian minimap top.png minimap.png 3262 3123 4930 5070 1024
Meridian setminimap work/x minimap.png
MapTool pack work/x starship_meridian_1.0.sd7
MapTool install starship_meridian_1.0.sd7 bar-data/maps
```

`../converter-run/` keeps the last build's `build.log`, MapTool's `run.log` and `run.txt`.

## The files

- `Layout.cs`: the ship in game units, x east and z south.
  - The map's size (24 × 28) and the heights: the deck 240, the bridge 380, the reactor well's floor 120, bulkheads 340, the rim 380, the bridge's walls 440, space 20, and how steep a wall rises (70°).
  - The hull's port outline, mirrored for the whole hull, and the three engine housings.
  - **The cells (rooms):** each with its name, kind, group, floor height and outline. Each team's rooms are given for port and mirrored for starboard, their groups suffixed by side. The passage, the bridge ramp and its landing are one group with the bridge, so no wall stands between them. The five shared rooms are on the centreline.
  - The bridge ramp's ends, the reactor's well (its centre, radius and ramps) and the core's radius.
  - **The doors:** port's, mirrored, and the centreline's spine doors and grand stair, each widened across its bulkhead.
  - The starts, the metal spots (each marked port, starboard or shared), the vents and the five crossing lanes.
- `Common.cs`: points, geometry (smoothstep, distances to segments) and noise.
- `Terrain.cs`: the heights, per vertex, computed for the port half and copied mirrored:
  1. each vertex's cell, door, the solid hull or space;
  2. **floors:** a cell's vertices with no other group's cell within 50, so a 100-wide bulkhead stands where two groups' rooms touch, and every door's;
  3. **the floors' heights:** the cell's, the bridge ramps and the stair graded, the reactor's well and its two ramps. The doors are at the deck's height, but those in the reactor follow its ramps. These heights are mirrored before the walls are raised, so the walls by the centreline rise from both sides' floors;
  4. **the solid hull:** from the nearest floor at 70° up to its cap (the rim and the housings 380, round the bridge 440, else 340);
  5. **space:** rough ground at about 20, and the hull's sheer side down to it.
- `Paint.cs`: the ground's colour per texel, its alpha 0 in space (but the hull's own sheer side).
  - **Hull plating:** staggered 256 × 128 plates, seamed and riveted, with access hatches and vent grilles; ribbed wall faces; the rim's navigation lights, red to port and green to starboard.
  - **Decks:** 64-unit panels with seams and rivets, wear, oil stains and scorch marks; the hangars' landing circles and guide lines in the side's colour; the passages' stripes and chevrons; the CIC's lit grid and ring; the bridge's blue deck; the reactor well's rings and hazard rim; the shield's ring; the engine room's grating.
  - **Doors:** hazard chevrons (orange at the crossings and the spine); the stair's treads.
  - **Spots and vents:** round extraction hatches and grated exhausts.
  - Also the splat (all zero: no detail textures), specular and normal maps, and the starfield sky.
- `Mesh.cs`: the model builder. Flat-shaded polygons on one atlas, every face cut into tiles so a tiled atlas cell repeats without wrapping. A model's x is its front, y up and z across; it is built symmetric about its length wherever it can be.
- `Atlas.cs`: the atlas, 2048 × 4096 (`meridian_1.tga` colour, `meridian_2.tga` glow, metalness and roughness): plating, hazard stripes, windows, lights, screens, the holo, vents, grates, pipes, copper, crates, corrugation, leaves, engine bells, burnt metal, lattice, energy, deck markings.
- `Models.cs`: the 38 models, `sm_` names. Landmarks (indestructible, uncrushable, no reclaim) and reclaimable craft, machines and props, with their metal and energy.
- `Features.cs`: the features as files in the map (`features/meridian/<name>.lua`, `objects3d/<name>.s3o`, the atlas in `unittextures`).
- `Place.cs`: what MapTool adds.
  - The metal and the vents.
  - **`Ship`:** the port half's features room by room, and those on the centreline; then every port feature again at its mirror image (x → 12288 − x, heading 180° − h), as its twin (`_m`) if the model isn't its own mirror image. A room's props stand along its walls, clear of its doors and the way in front of them, its metal and its start.
  - **`run.txt`,** with headings in engine units (65536 a turn).
- `Check.cs`: the checks (see `../CHECKLIST.md`), on the engine's movement rules round every blocking feature's footprint:
  - the halves match, heights and features;
  - every start, spot and vent is reached;
  - each crossing alone lets vehicles across, and with all five shut no ground class crosses but spiders (logged);
  - no ground class reaches space, and space is too rough to build on;
  - doors are wide and clear, and the start rooms keep enough level floor free;
  - every feature stands level, and the spots, vents and starts are level and clear;
  - the fronts' driving distance is logged.
- `Create.cs`: a new SMF and SMT on the template's other files. It keeps the texture's alpha (`MapTexture.Update(..., keepAlpha: true)`), so space's transparent texels survive into the SMT.
- `Program.cs`: the commands, the build's order, the models' symmetry test (each model's surface sampled into 4-unit cells and checked against its mirror image) and `mapinfo.lua`: the name, `maxMetal` 4, `voidGround` on and `voidWater` off, the starfield sky, no fog, no wind or tide, the star's light, and the eight starts.
