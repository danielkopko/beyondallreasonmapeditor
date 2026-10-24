# Red Dust Open Cut: the converter

How `red_dust_open_cut_1.0.sd7` is made. The terrain, texture, detail maps and sky are made here, and so is every feature, all modelled and painted from scratch: the bucket wheel excavator, the haul trucks and excavators, the railway and its train, the ore's way to it, the tanks, the site's buildings and smaller machines, and the outback's plants and rocks. It is built on All That Simmers v1.1.1 (its detail textures and featureplacer gadget). There is no drawing; the layout is `Layout.cs`.

## Commands

- `OpenCut gallery <template> <work>`: every model on flat ground, with each model's triangles and footprint. The layout:
  - the bucket wheel excavator alone, with the loadout, the crusher, the transfer tower and conveyor sections beside it;
  - the train on its track, running into the loco shed;
  - then rows of the machines, the tanks and structures, the site gear, and the outback.
- `OpenCut build <template> <work>`: the map folder, `red_dust_open_cut_1.0_stage.sd7` and `run.txt`. It runs the checks first and builds anyway, to look. It also reports the pit's area and how much of it is under water, where the railway's ends fall, and each haul ramp's start, length and grade.
- `MapTool edit red_dust_open_cut_1.0_stage.sd7 <out>.sd7 @run.txt`: the metal, the vent, the machines, the railway and the site, and the outback.
- `OpenCut check <final map, extracted> <run.txt>`: the checks again on the built map's own heights.
- `OpenCut minimap <render.png> <out.png> [size]` and `OpenCut setminimap <extracted map> <png>`: the minimap from a straight-down `render3d` with `--models`.
  - The first cuts the map out of the render and scales it.
  - The second puts it into the SMF.
  - Then `MapTool pack`. The lobby picture follows, because `MapTool install` draws it from the minimap.
- `OpenCut relief <out.png>`: the heights alone, shaded and coloured by height.
- `OpenCut roads [<x> <z>]`: the road survey (`Roads.cs`): each road's length, where its ends lead, how steep it climbs and how far it leans (and where), whether it runs into the pit or along a creek's bed, and how far each ramp's top is from a road. With a point, also the heights and pit depth round it.
- `MapTool install`, `render`, `render3d`, `info`, `metal`: the lobby picture, pictures and reports beside this folder.

The whole build, in order (the paths are examples):

```
OpenCut build <template> work
MapTool edit work/red_dust_open_cut_1.0_stage.sd7 work/features.sd7 @work/run.txt
MapTool extract work/features.sd7 work/x
OpenCut check work/x work/run.txt
MapTool render3d work/features.sd7 top.png --size 8192x8192 --camera 6144,6144,0,89.9,62423 --models
OpenCut minimap top.png minimap.png 1024
OpenCut setminimap work/x minimap.png
MapTool pack work/x red_dust_open_cut_1.0.sd7
MapTool install red_dust_open_cut_1.0.sd7 bar-data/maps
```

## The files

- `Layout.cs`: the map in game units.
  - The pit: the old oval's centre, tilt and size at the rim; the southern lobe's centre and half-sizes, and its two cutbacks by the edge; the benches; the pool against the edge.
  - The pit's sectors, as angles round it: the old pit's two ramps, the slide and the bucket wheel excavator's terrace. The lobe's two ramps are angles round the lobe's centre.
  - The lobe's two floor spots.
  - Where the lobe's two bucket wheel excavators work (`PitWheelZ`), how far over the rim they stand, how far into their walls, and their slewing arc.
  - The starts, the ROM pad and its stockpiles, the plant pad, the tailings dam, the dumps and their ramps, and the workshop.
  - The railway: the main line's x, the siding and its turnouts, the loadout, where the train starts, the shed's length and its distance from the rim, and how far short of the level crossing the train stops.
  - The overland conveyor's three points: the crusher, the transfer tower, and the loadout's incline foot.
  - The hardstands, the windmills and the explosives magazine.
  - The breakaways, creeks, haul roads and metal spots.
- `Common.cs`: points, geometry (smoothstep, distances to segments and polylines, smoothing), and noise. The mine isn't symmetric, so nothing is mirrored.
- `Terrain.cs`, built in this order:
  - **The plain** at 380. It is level in the bases, on the pads and on the hardstands, and flat across the roads.
  - **The natural ground** (`Natural.cs`): where the ground is wild, it adds swells and mounds and cuts the main washes.
  - **The rail bed:** dead level (the plain plus 3) along the main line and siding, from the north edge to the shed's apron.
  - **The magazine's bund:** a ring of earth, with a gap on the north-east for the way in.
  - **Creeks.**
  - **Breakaways:** low mesas, their outlines eaten into by gullies.
  - **The tailings dam:** an embankment round a flat beach.
  - **The dumps:** two lifts with scalloped tip-head edges, and a graded ramp up each. Half the top lift is paddock-dumped in rows of truck-load heaps, half dozed flat.
  - **The stockpiles** on the ROM pad.
  - **The pit:**
    - **Its outline** (`Rho`) is the old tilted oval, its rim wavering, joined by a smooth minimum to the southern lobe. The lobe's wall wavers too, and two cutbacks round its corners where it meets the edge. The join makes the waist.
    - Its benches come from an exact distance-to-the-rim field (Felzenszwalb and Huttenlocher's transform), so every bench runs parallel to the rim and every face stays steep.
    - The terrace at the north end is one bench down and wide.
    - The slide is a single 40° slope.
    - The floor is at 10, with the pool against the map's south edge.
  - **The lobe's bucket wheel excavators' bites** (`PitWheels`): each stands a little into its wall at the foot, facing up it. Within its slewing arc the wall is cut back to the floor as far as its wheel reaches, with a steep (70°) curved face beyond.
  - **The four haul ramps** (`RampWays`): each follows the depth contours from just outside the rim to the floor, along rays from the old pit's centre or the lobe's. It runs at one grade to where it meets the floor, then level onto it, cutting and filling as it goes.
  - **Level pads** for the spots and the vent.
  - **The railway's geometry,** worked out once the pit is laid out (`Result`):
    - where the pit's rim crosses the main line;
    - the shed's two ends and the track's end inside it;
    - where road 7 crosses the line;
    - the track lines (`Rails`).
  - **Where a road shows** (`RoadShown`): a road runs onto a pad or hardstand and ends at its edge, and never over the ROM pad's stockpiles or the magazine's bund. The painter and the checks both read it.
  - **The paddock-dumped heaps** keep 190 clear of each dump's ramp where it comes onto the top.
  - The shapes the painter and the placer share are here: `PadRho`, `DumpRho`, `BreakawayRho`, `Heap` and `ConveyorLine`.
- `Mesh.cs`: a model builder of flat-shaded polygons on one atlas.
  - Every face is cut into tiles of 128 units, so a tiled cell repeats without its texture wrapping (an S3O can't wrap inside an atlas cell).
  - Triangles are wound as the editor's structure models are.
  - **Weathering:** faces turned up wear red dust, and faces below a model's dirt line wear red earth.
  - **Solid box:** plants and handrails don't count towards it. That box is what the footprint and collision box cover.
  - **Shapes:** boxes, slabs, beams, lofts, tubes, ribbons, and lumpy icospheres (`Blob`) for ore, crowns and rocks.
- `Atlas.cs`: the atlas, 2048 × 4096, 32 cells.
  - Texture 1 is colour, with alpha 0 (BAR mixes a feature's team colour, black, in by alpha).
  - Texture 2 is glow, metalness and roughness, as BAR's model shader reads them for features marked `cuspbr`.
  - **Paint:** mining yellow, orange, steel, white and blue, each clean and dusty, worn to bare metal at the edges.
  - **Parts:** lattice bays, tyre treads, wheel faces, windows, grilles, chrome, hazard stripes, lamps (which glow), track plates, the bucket wheel's buckets, and ore.
  - **The outback:** ghost gum bark, leaves, spinifex, saltbush, ironstone, termite mound earth.
  - **The site:** corrugated iron, concrete, earth, and sun-greyed timber (the cell that was the test cell).
  - Tiled cells wrap exactly once (periodic noise), so tiles meet without a seam.
- `Models.cs`:
  - **The haul truck**, empty, loaded with ore, or tipping. It has six wheels, a deck with the cab and stairs, handrails, and a body with its canopy.
  - **The excavator**, working or idle: tracks, house, cab, counterweight, boom, stick and toothed bucket.
  - **The bucket wheel excavator:**
    - four crawlers, a turntable and the house;
    - two lattice masts with stays;
    - the lattice main boom out to a wheel of 14 buckets;
    - the conveyor along it and the cab;
    - the counterweight boom and its block;
    - the discharge conveyor.
    - About 10,200 triangles.
  - **The lobe's bucket wheel excavators** (`BucketWheelPit`): the same model scaled so it stands just higher than the pit is deep; its footprint is its crawlers.
  - **The site gear:** a ute with its flag, a lighting plant, two site offices (dongas), a diesel tank in its bund.
  - **The outback:** two spinifex, two ghost gums, a dead tree, saltbush, two termite mounds, three boulders.
  - No makers' names, marks or model numbers anywhere.
- `ModelsRail.cs`: the railway and the ore's way to it. Rolling stock faces +x along the track, its wheels on rails at z ±8.5 whose heads are 4.5 up.
  - **The locomotive:** two three-axle bogies, the long hood with its radiators, fans and exhaust, the cab and short nose, a white stripe, hazard-striped ends, walkways with handrails. Footprint 16 × 2, so locos coupled at 262 leave no gap.
  - **The ore wagon,** empty or loaded: two bogies, a ribbed open body rusted by the ore. Footprint 7 × 2 (its body), so wagons coupled at 120 leave no gap.
  - **The track:** 128 of rail on eight concrete sleepers. It doesn't block.
  - **The buffer stop.**
  - **The loco shed:** a long steel shed of corrugated iron with portal frames, skylights, a roller-door opening at its +x end, and an inspection pit.
  - **The train loadout:** a bin on four legs straddling the line (+x along it), its chute over the wagon beneath, and the incline conveyor from the overland conveyor's end, 560 off along −z, on trestles. Footprint the legs only (7 × 7).
  - **The primary crusher:** its concrete ROM bin trucks tip into, a bump block, the steel hopper with ore in it, a rock breaker, the crusher house, and the conveyor out at a climb. Footprint its bin and house (11 × 8).
  - **A section of the overland conveyor:** 128 of gallery (side frames braced in bays, the belt, a hood, a walkway and handrail) on its trestle. Footprint the trestle only, so units drive under it.
  - **The transfer tower.**
- `ModelsSite.cs`:
  - **Tanks:** a vertical diesel tank, large in a concrete bund with a ladder and a roof handrail, or small; a corrugated water tank, large on its pad or a rainwater tank on a timber stand; a water tower.
  - **The workshop shed:** three roller doors (one up), portal columns, bollards, windows, downpipes.
  - **The smaller machines:**
    - a water cart (the haul truck's chassis, shared with the trucks);
    - a motor grader;
    - a track dozer;
    - a wheel loader;
    - a blast-hole drill rig;
    - a service truck;
    - a crew bus.
  - **Clutter and smaller structures:**
    - shipping containers in three colours;
    - a windmill and its trough;
    - a communications tower banded orange and white;
    - a timber power pole, its wires out half a span each way;
    - a generator set;
    - worn-out tyres;
    - the explosives magazine;
    - drums on a pallet;
    - polyethylene pipe.
- `Features.cs`: writes each feature's files into `features/opencut`, `objects3d` and `unittextures`:
  - its `.lua`, with the footprint and the box collision volume. A machine's footprint is its base only, because its boom overhangs.
  - its `.s3o`;
  - the atlas, as TGA.
  - **Indestructible, uncrushable, no reclaim:** the bucket wheel excavator, the train, the loadout, the loco shed, the crusher, the conveyor, the transfer tower, the workshop shed, the buffer stop and the track.
  - **Reclaimable:**
    - a truck 300 metal, an excavator 600;
    - a water cart 300, a grader 200, a dozer 400, a loader 400, a drill rig 300, a service truck 150, a bus 120;
    - the site gear and tanks 20 to 80 metal (the diesel tanks also hold energy);
    - the plants a little energy.
- `Place.cs`: what MapTool adds.
  - The metal and the vent.
  - **The machines,** where the crews left them. None stands on a ramp: a footprint, squared to the nearest quarter turn, would close a diagonal ramp. Some stand on the plain's roads where they cross the pads.
  - **`Works`:** the railway, the ore's way to it, and the site.
    - The track is laid back from its end in the shed to the north edge, with the siding and its turnouts.
    - The train: three locos at the north edge heading north, wagons loaded as far as the loadout, empty behind it to 160 short of the level crossing, an empty rake on the siding, and a spare loco in the shed against the buffer stop.
    - The crusher, then a conveyor section every 128 to the transfer tower and on to the loadout's incline.
    - A power line from the plant's generators to the crusher.
    - The tanks, buildings, smaller machines and clutter. Each side gets the same reclaimable machines, and the pit gets the drill rigs.
  - **The outback** on a 46-unit grid, by slope and by nearness to the creeks: spinifex, saltbush, gums along the creeks, termite mounds, dead trees, and boulders on steep ground and the pit's berms. It is kept off the roads, ramps, railway, conveyor, pads, hardstands, spots and bases.
  - **`run.txt`,** with headings in engine units (65536 a turn).
- `Paint.cs`: the ground's colour per texel.
  - **The outback:** red pindan, spinifex speckle, gravel lag and calcrete.
  - **Creeks:** sand.
  - **Breakaways:** ferricrete caps over white kaolin scarps.
  - **The tailings dam:** a cracked beach round its dried-out decant (cracked mud, rings of salt crust).
  - **The washes:** pale sand-and-pebble beds with darker banks. **The mounds:** paler sand on their tops.
  - **The dumps:** red oxide waste with tip lines. On top, each paddock-dumped load has its own colour, and the dozed half shows blade lines.
  - **The pads:** concrete with go-line bays, and the ROM pad's ore.
  - **The pit's walls:** banded by depth, from red oxide through white and pink kaolin and ochre and purple clays to grey-green fresh rock. Faces are darker and streaked; the floor is wet toward the lake.
  - **The pit floor's drill pattern:** holes on a 32 grid, each in its ring of pale cuttings.
  - **The railway:** a pale formation, grey ballast under each track, and concrete under the loadout and round the shed.
  - **Under the conveyor:** a service track.
  - **The hardstands:** gravel, oil-stained.
  - **Roads and ramps:** rutted, with windrows. They show only where `RoadShown` lets them, in the colour and the splat alike. A ramp's foot fades into the floor. Road 7's level crossing is painted over the ballast.
  - **Spots and the vent:** marked.
  - Also the splat, specular and normal maps (written upside down, PLAN.md rule 7) and the sky.
- `Natural.cs`: the natural ground away from man's works.
  - **`Wild`:** how untouched ground is, 1 to 0, with a wide road margin for the heights (so roads and their verges stay flat) and a narrow one for the paint (so washes run up to a road's edge).
  - **Swells.**
  - **Mounds:** on a jittered grid, their shoulders under about 17°.
  - **Washes:** the zero-crossings of a warped noise, broken into stretches; the main ones cut 5 deep, the rills paint only.
  - `Terrain.Build` keeps them per vertex (`Rise`, `Mound`, `Cut`, `Wash`) for the heights, the painter and the plants.
- `Roads.cs`: the road survey, which the checks read on the built map's heights. A road meets a ramp's top in line with the ramp: the ramp drops into a cutting as soon as it leaves its top, and a road from the side runs over the cutting's wall.
- `Check.cs`: the checks (see `../CHECKLIST.md`), including that every machine and building stands near level.
  - Distances are driving distances: 8-connected, with diagonals counting √2 and no corner cutting. The pit is tilted, so a 4-connected count would charge a diagonal road up to 41% more than its length.
  - **The ramps:** each of the four alone, then all four shut, then the slide shut too.
  - **The south edge:** with the pit and the north shut, nothing crosses round the pit's south end.
  - **The north lane:** with the pit and the south shut, the train's three gaps are shut together and then one at a time. This proves the train walls the lane off but for those gaps, and that each gap works.
  - **Roads:** a road's paint (as `RoadShown` lets it show, out to its windrows) must not lie under any model but a vehicle, the track or ground cover. Nor may it lie over a stockpile, a paddock-dumped heap, the tailings dam's wall, a breakaway or the magazine's bund.
  - **Every road** must lead somewhere at both ends, keep out of the pit, cross creeks rather than run along them, and stay under 7° along and 4° across. A road must meet every ramp's top.
- `Create.cs`: copied from the king maps' converter. It builds a new SMF and SMT on the template's other files.
