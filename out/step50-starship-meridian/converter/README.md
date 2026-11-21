# Starship Meridian: the converter

How `starship_meridian_1.1.sd7` is made. The terrain, texture, detail maps and starfield sky are made here, and so is every feature, all modelled and painted from scratch on one atlas: the craft in the hangars, the ship's machines and landmarks, the rooms' props and the hull's dressing. It is built on All That Simmers v1.1.1 for its featureplacer gadget (its detail textures aren't used). The layout is `Layout.cs`, the same numbers as `../design/layout.py`, which draws `../design/layout.svg`.

## Commands

- `Meridian gallery <template> <work>`: every model on flat deck, in rows (the craft and a service tower; the landmarks; the bridge, power and crew props; the cargo, shop and hull models), with the viewport and an engine on their own. It lists each model's triangles and footprint and whether it's its own mirror image (if not, it gets a twin).
- `Meridian build <template> <work>`: the map folder, `starship_meridian_1.1_stage.sd7` and `run.txt`. It runs the checks first and builds anyway, to look.
- `MapTool edit starship_meridian_1.1_stage.sd7 <out>.sd7 @run.txt`: the vents and the features (the metal is already in the SMF).
- `Meridian check <final map, extracted> <run.txt>`: the checks again on the built map's own heights.
- `Meridian minimap <render.png> <out.png> <x0> <z0> <x1> <z1> [size]` and `Meridian setminimap <extracted map> <png>`: the minimap from a straight-down `render3d` with `--models`.
  - The first cuts the map out of the render at the given pixels (the map's corners at the deck's height, projected through the camera), scales it to a square (unevenly: the map is 24 × 28), and puts a starfield wherever the render shows its background, which is space.
  - The second puts it into the SMF.
  - Then `MapTool pack`. The lobby picture follows, because `MapTool install` draws it from the minimap.
- `MapTool install`, `render`, `render3d`, `info`, `metal`: the lobby picture, pictures and reports beside this folder.

The whole build, in order (the paths are examples):

```
Meridian build <template> work
MapTool edit work/starship_meridian_1.1_stage.sd7 work/features.sd7 @work/run.txt
MapTool extract work/features.sd7 work/x
Meridian check work/x work/run.txt
MapTool render3d work/features.sd7 top.png --size 8192x8192 --camera 6144,7168,0,89.9,72827 --models
Meridian minimap top.png minimap.png 3262 3123 4930 5070 1024
Meridian setminimap work/x minimap.png
MapTool pack work/x starship_meridian_1.1.sd7
MapTool install starship_meridian_1.1.sd7 bar-data/maps
```

`../converter-run/` keeps the last build's `build.log`, MapTool's `run.log` and `run.txt`.

## The files

- `Layout.cs`: the ship in game units, x east and z south.
  - The map's size (24 × 28) and the heights: the deck 240, the bridge 380, the outer walk and the thruster steps 380 (1.1), the thruster platforms 440 (1.1), the reactor well's floor 120, bulkheads 340, the rim beside the outer walk 340, the bridge's walls 410 (a lip 30 over its deck, 1.1), space 20, and how steep a wall rises (70°).
  - The hull's port outline, mirrored for the whole hull, and the three engine housings.
  - **The cells (rooms):** each with its name, kind, group, floor height and outline. Each team's rooms are given for port and mirrored for starboard, their groups suffixed by side. The passage (1,000 wide since 1.1), the bridge ramp and its landing are one group with the bridge, so no wall stands between them. Each thruster platform, its ramp and its step belong to the room below them (the thruster bay, the engine room), so no wall stands between: the ramp rises out of the room's floor to the step, at the outer walk's height, and on up to the platform; from the step's sides paths 128 wide skirt the platform's front corners to the outer walk (`StepPath`); the edges drop sheer (1.1). The thruster bay's inboard path crosses into the machine shop's aft corner, so the shop is in the bay's group: no bulkhead between it and the path. The bridge's front opening (1.1) joins it to the outer walk. The five shared rooms are on the centreline, with the centre thruster platform and its ramp.
  - Every ramp's ends and top height (the bridge's, the thruster platforms'), which cells stand open (the thruster platforms and their ramps: no wall over them), the lip (30) the bridge's walls stand over it, the reactor's well (its centre, radius and ramps) and the core's radius.
  - **The doors:** port's, mirrored, and the centreline's spine doors and grand stair, each widened across its bulkhead.
  - The starts, the metal (1.1: a metal map, with Cloud9_V2's raw value, `maxMetal` and extractor radius), the vents and the five crossing lanes.
- `Common.cs`: points, geometry (smoothstep, distances to segments) and noise.
- `Terrain.cs`: the heights, per vertex, computed for the port half and copied mirrored:
  1. each vertex's cell, door, the solid hull or space;
  2. **floors:** a cell's vertices with no other group's cell within 50, so a 100-wide bulkhead stands where two groups' rooms touch, and every door's; and (1.1) **the outer walk:** every hull point outside the rooms within 96 of the outline (wider where it meets a step or a platform) or ahead of z 1,450 at the bow, 50 clear of the rooms it doesn't join, at 380;
  3. **the floors' heights:** the cell's, the ramps (the bridge's and the thruster platforms') and the stair graded, the reactor's well and its two ramps. The doors are at the deck's height, but those in the reactor follow its ramps. These heights are mirrored before the walls are raised, so the walls by the centreline rise from both sides' floors;
  4. **the solid hull:** from the nearest floor at 70° up to its cap (round the bridge 410, a lip; the rim beside the outer walk 340, 40 under it; else 340), and within 48 of a floor above 280 in a walled room (the bridge, its landings, the upper bridge ramps) at least a lip, 30, over it, steeper than bots climb, so no ramp runs level with a wall top (1.1); the open thruster platforms, steps and ramps and the outer walk have no wall over them;
  5. **space:** rough ground at about 20, and the hull's sheer side down to it.
- `Paint.cs`: the ground's colour per texel, its alpha 0 in space (but the hull's own sheer side). 1.1: the hull's sides are painted as a ship's side (`HullSide`), laid out along the hull's outline and up it by height: plating, the livery, the name and hull number in stroke-drawn stencil letters (reading left to right from outside on both sides), the ship's badge (`badge.png`, your map editor's icon, copied beside the program and averaged over each texel's footprint from a summed-area table), ports, airlocks, vents and the engines' scorched mounts.
  - **Hull plating:** staggered 256 × 128 plates, seamed and riveted, with access hatches and vent grilles; ribbed wall faces; the rim's navigation lights, red to port and green to starboard.
  - **Decks:** 64-unit panels with seams and rivets, wear, oil stains and scorch marks; the starts' circles in the side's colour (the hangars' landing circles, the same etched at the other starts), each with its role stencilled in white (`RoleCover`: AIR, FRONT, TECH, SUPPORT, upright from the default camera), and the hangars' guide lines; the passages' stripes and chevrons; the CIC's lit grid and ring; the bridge's blue deck; the reactor well's rings and hazard rim; the shield's ring; the engine room's grating.
  - **Doors:** hazard chevrons (orange at the crossings and the spine); the stair's treads.
  - **Vents:** grated exhausts. No metal spots are marked: the whole deck is metal.
  - Also the splat (all zero: no detail textures), specular and normal maps, and the starfield sky.
- `Mesh.cs`: the model builder. Flat-shaded polygons on one atlas, every face cut into tiles so a tiled atlas cell repeats without wrapping. A model's x is its front, y up and z across; it is built symmetric about its length wherever it can be.
- `Atlas.cs`: the atlas, 2048 × 4096 (`meridian_1.tga` colour, `meridian_2.tga` glow, metalness and roughness): plating, hazard stripes, windows, lights, screens, the holo, vents, grates, pipes, copper, crates, corrugation, leaves, engine bells, burnt metal, lattice, energy, deck markings.
- `Models.cs`: the 38 models, `sm_` names; 1.1 places three of them (the reactor's core, the drive core, the main engines). Every one can be reclaimed but the main engines, part of the hull; landmarks stand firm (uncrushable, too tough to shoot down). What each gives comes from its size (`Features.cs`): energy 50 to 250 on a log scale of its footprint's area, metal a fifth of that; the reactor's core 3,000 energy. The main engines hang below their origin, under the outer walk at the housings' aft edges, wholly under its surface and clear of the ground of space under it (1.1: only the cone, smaller and raised; `BellAxis`, `BellRadius`).
- `Features.cs`: the features as files in the map (`features/meridian/<name>.lua`, `objects3d/<name>.s3o`, the atlas in `unittextures`).
- `Metal.cs`: the metal map (1.1). Raw 79 in every 16-unit metal cell whose centre is deck floor, none on the walls, hull tops or space; and a T1 extractor's yield by the engine's rule.
- `Place.cs`: what MapTool adds.
  - The vents.
  - **`Ship`:** 1.1's features: BAR's own energy crystals in clusters (`Crystals`: names, footprints for the checks, energy and each cluster's mix; `CrystalsPort` lays them by a fixed sequence, clear of walls, doors, vents and the start circles), two edging each start's circle, two in each inboard room and two in each shared room but the reactor; the six main engines under the engine housings' aft edges, and on the centreline the reactor's core and the drive core; every port feature again at its mirror image (x → 12288 − x, heading 180° − h). Only the models placed go into the map.
  - **`run.txt`,** with headings in engine units (65536 a turn).
- `Check.cs`: the checks (see `../CHECKLIST.md`), on the engine's movement rules round every blocking feature's footprint:
  - the halves match, heights and features;
  - every start and vent is reached, and all three thruster platforms by their ramps, each 100 over the bulkheads;
  - the outer walk (1.1): vehicles reach it all round, and from it and the steps' paths, the ramps and the bridge's front opening shut, no ground class gets down into a room or onto a platform; each step's own path takes vehicles from its landing to the walk beside it;
  - the gun sites: nothing stands over the thruster platforms, steps and ramps, the bridge's walls stand no more than its lip, and what a Starfall (range 6,100) there reaches is logged;
  - each crossing alone lets vehicles across, the outer walk alone does too (a sixth way, round the hull), and with all six shut no ground class crosses but spiders (logged);
  - no ground class reaches space, and space is too rough to build on;
  - no ground class but spiders gets onto a wall or the hull's solid tops: off the floors and more than 30 over the highest floor within 24 (spiders logged);
  - doors are wide and clear, and the start rooms keep enough level floor free;
  - every feature stands level, and the vents and starts are level; the main engines stand at the outer walk's level, wholly under it, and their cones clear the ground of space;
  - the metal map: metal on every deck floor cell and nowhere else, mirrored, the same for each team's rooms, wide enough that BAR reads it as a metal map, Cloud9's yield read back from the map's own mapinfo, and all of it in reach;
  - the fronts' driving distance is logged.
- `Create.cs`: a new SMF and SMT on the template's other files. It keeps the texture's alpha (`MapTexture.Update(..., keepAlpha: true)`), so space's transparent texels survive into the SMT.
- `Program.cs`: the commands, the build's order, the models' symmetry test (each model's surface sampled into 4-unit cells and checked against its mirror image) and `mapinfo.lua`: the name, `maxMetal` 7.5 and `extractorRadius` 24, `voidGround` on and `voidWater` off, the starfield sky, no fog, no wind or tide, the star's light, and the eight starts.
