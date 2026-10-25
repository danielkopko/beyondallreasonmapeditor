# Marram Line: the converter

How `marram_line_1.0.sd7` is made. The terrain, texture, detail maps and sky are made here, and so is every feature: the beach's hedgehogs, stakes and wrecked landing craft; the bunkers, the coastal gun's casemate, the dragon's teeth, the sandbag nests and the wire; the zone's wrecked tanks and shattered stumps; the farm, the village, the church and the windmill; the lighthouse; the hedgerows' bushes and oaks, pines, apple trees, marram and reeds; the cove's jetty, boat sheds and channel markers. It is built on All That Simmers v1.1.1 (its detail textures and featureplacer gadget).

The layout is the design's table, `../design/layout.py`. It writes `../design/layout.json`, which the build copies beside the converter. `Layout.cs` ports the table's lines exactly (the coast, the beach, the foredune, the two fire trenches, the crest, the headland's heath, the inlet's wavering banks) and checks the port against the table's own samples when it loads. Everything the table generated (dunes, craters, hedgerows and fields, wire, wrecks, roads, spots, the trenches' runs, bunkers, nests, hedgehogs, pines, the village) is read from the JSON, so the map is built where the drawing shows it. The village's 14 house sites are candidates: `Place.cs` keeps the 5 off the lanes and clear of the church and of each other.

## Commands

- `Marram build <template> <work> [--checks-only]`: the map folder, `marram_line_1.0_stage.sd7` and `run.txt`. The checks run first; it builds anyway, to look. With `--checks-only` it stops after them.
- `MapTool edit marram_line_1.0_stage.sd7 <out>.sd7 @run.txt`: the metal, the three vents and the 5,348 features.
- `Marram check <final map, extracted> <run.txt>`: the checks again on the built map's own heights.
- `Marram minimap <render.png> <out.png> [size]` and `Marram setminimap <extracted map> <png>`: the minimap from a straight-down `render3d` with `--models`, as for the other maps.
- `Marram gallery <template> <work>`: every model on flat sand, in rows, with each one's triangles, footprint, metal and crush resistance.
- `Marram relief <out.png> [step]`: the heights alone, coloured by who climbs them (dark red spiders only, orange bots only, yellow hovers but not vehicles) and the water by depth.
- `Marram preview <out.png> [step] [x0 z0 x1 z1]`: the painted texture, coarsely, or a window of it, in seconds.
- `Marram probe <x> <z> [half]`, `Marram leaks <vehicles|hovers>` and `Marram route <class> <from x> <from z> <to x> <to z> [beach|hill] [inlet]`: finding what the checks find. Heights and who passes round a point; where a class gets down into a trench; the way a class takes between two points, with a line's crossings (and the inlet) shut.

The whole build, in order (the paths are examples):

```
Marram build <template> work
MapTool edit work/marram_line_1.0_stage.sd7 work/features.sd7 @work/run.txt
MapTool extract work/features.sd7 work/x
Marram check work/x work/run.txt
MapTool render3d work/features.sd7 top.png --size 8192x8192 --camera 6144,6144,0,89.9,62423 --models
Marram minimap top.png minimap.png 1024
Marram setminimap work/x minimap.png
MapTool pack work/x marram_line_1.0.sd7
MapTool install marram_line_1.0.sd7 bar-data/maps
```

The build takes about 40 s, MapTool's edit about 50 s, and the whole run with the pictures about 4 minutes.

## The files

- `Layout.cs`: the table in C#. Its hash and waves are layout.py's exactly (32-bit wrapping as Python's masks). The lines; the heath; the inlet (its smoothed course from the table, its banks wavering each their own way and blended past the cove's end; `InletAt`, `SouthOfInlet`, `SandyEnd`); then everything read from `layout.json`.
- `Terrain.cs`, per heightmap vertex (8 units), west to east:
  - **The sea:** the table's depths by distance out (0, 8 at 240, 20 at 640, 40 at 1,100, 60 at 1,500), broken sandbars 5 deep at 400 out with rips between (none off the headland or across the inlet's mouth), sand ripples, a gentle swell.
  - **The beach:** 0 to 10, a runnel and a ridge low down; shingle at 4 under the headland.
  - **The headland's cliffs:** 24 wide (a little more or less), from the shingle up to the ground behind, rough rock: over 60 degrees.
  - **The land behind the beach** (`Land`): the heath (rolling, granite knolls), the foredune (a seaward face at 40 degrees up to its crest, a gentle back down to the slacks; at each draw a plain ramp from the beach to the slacks instead), or the dune belt (the slacks at 22 with low hummocks, and the table's dunes).
  - **A dune** (`DuneHeight`): a rounded dome whose crest bows downwind, furthest east in its middle, its horns trailing back; a long gentle back to windward, and its lee cut off at 40 degrees, the slip face.
  - **The trench lines:** the ground rises to 42 along each line's ridge, with the glacis down to the zone at 16. The fire trench follows the table's section (floor 40 wide at 32 down, a fire step 20 wide at 12 down toward the enemy, parapet and parados 6 high, walls 45 to 48 degrees), crenellated in bays 88 long, 14 forward and back. The support trench lies 240 behind it, the same floor with no step. The communication trenches have a floor 24 wide at 28 down and walls at 45 degrees. Every section is measured square to its line, so where a line runs at a slant its walls keep their angle. No trench floor is below 6. Each trench stops either side of a crossing, tapered over 24; at a line's south end it stops sheer (a taper there would be a ramp in).
  - **The zone:** the meadow at 16, churned. Shell holes are shallow bowls; a bomb crater has walls of about 42 degrees all round a flat floor, however wide. Craters fade out short of each line's parapet.
  - **The rise:** the table's rise to the crest and down the back slope, with a gentle swell over both (with the knoll in the hill team's salient), hedgerow banks 6 high (under 17 degrees), the brook's dry bed down to the cove.
  - **The sandy end:** flats at 10 with low dunes, rising into the hill.
  - **The inlet**, cut through it all: its channel 26 deep (32 at the mouth, 24 in the cove), shelving to its banks over the outer 40% of its width, its banks at 12 degrees.
  - **Pads, last:** the starts' ground eased (not cut flat: the slacks are flat already and the rise's 3 degrees takes any building), the spots, vents, the village, the farm's yard, every bunker's and the boat sheds' footings, the lighthouse.
  - **The headland road,** ramped up from the beach onto the heath at 5 degrees.
  - Fields for the painter: the inlet's signed distance, trench depth, dune height and slip faces, craters, distances to the hedgerows and to each kind of road, the field each vertex is in, and what kind of ground it is.
- `Paint.cs`: the ground's colour per texel; the splat (rock, gravel, ground, grass), specular (wet sand shines) and normal maps, written upside down (PLAN.md rule 7); the sky with high thin cloud.
- `Mesh.cs` and `Features.cs`: Red Dust Open Cut's, with this map's regions; faces below a model's sand line are drifted with sand (beach wrecks) or caked in earth. A feature's crush resistance is written to its file.
- `Atlas.cs`: 32 cells: concrete in board-marked formwork (and stained), the gun slit, rust, olive drab, grey, burnt metal, track plates, sandbags, grey and tarred timber, wire, stone masonry (and soot-blackened), slate, whitewash, oak bark, leaves, pine, marram, reeds, hay, apple leaves with apples, earth, white and red paint, the lighthouse's lit lantern, broken windows, granite, shingle, dark, sand.
- `Models.cs`: every model (see `../CHECKLIST.md`). No national markings anywhere.
- `Place.cs`: what MapTool adds. Nothing blocking on a start's ground, a spot, a vent, a road or a crossing; nothing on the water but channel markers, the landing craft in the surf and the dragon's teeth.
- `Check.cs`: the checks (see `../CHECKLIST.md`). A feature blocks a class only if the class can't crush it.
- `Create.cs`, `Common.cs`: Red Dust Open Cut's.
