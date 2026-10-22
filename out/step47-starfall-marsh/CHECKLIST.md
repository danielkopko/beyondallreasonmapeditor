# Starfall Marsh 1.0: checklist

`starfall_marsh_1.0.sd7` ("Starfall Marsh 1.0", short name `StarfallMarsh`, map id 20261101) is a 16 × 16 map for 2v2. A giant warship crashed into a jungle marsh centuries ago, and the jungle has been taking it back. The terrain, texture, detail maps, sky and **every part of the ship and its wreckage** are made by the converter here (`converter/README.md`); the trees, ferns, detail textures, featureplacer gadget and water settings are All That Simmers v1.1.1's, with its unused banyan and mangrove models given feature definitions.

**Not tried in BAR yet.** BAR isn't installed on this machine, so everything below was checked with MapCore's copy of the engine's rules, and the models with MapTool's renderer, not in the game.

**Not released yet.** `starfall_marsh.cmd publish -Message "Starfall Marsh 1.0"` commits and pushes this folder and puts the map and its lobby picture on the release `starfall-marsh-1.0` here and in danielkopko/BarMaps (as for the king maps); BarMaps' README section and picture are then added by hand. `starfall_marsh.cmd install` copies the map into BAR.

## The layout

| | On the map |
|---|---|
| Teams | 2v2, north against south: start 0 north-west (1250, 1150), 1 north-east (6700, 1150), 2 south-west, 3 south-east. The south half is the north's mirror image across the middle line (heights the same to 0.00 everywhere) |
| The jungle floor | At 40, in low hummocks, nearly level in the four bases (cleared ground) |
| The furrow | Ploughed along the middle line from beyond the west edge to the mound: a floor 14 under water (wadeable: land units go to 20), banks, and berms of thrown-up dirt along both sides |
| The marsh | Two streams on each side draining into the furrow (beds 10 deep), ragged pools (most 9-12 deep, two 26 deep that only amphibious units cross), and round the streams and furrow a mosaic of mud and shallow puddles at the water's level |
| The ship | Six features on the middle line, all indestructible and with no reclaim. The engines (six bells in the stern, a pod with its own bell either side, a great dorsal fin and canted pod fins, vent banks), the aft hull (the bridge tower stepped in layers, dark windows, antennas, the ship's crest on the tower and the hull), the aft section's torn end, the fore section's torn end (the two torn ends spill decks, ribs, cables and the reactor's glow), the fore hull, pitched 4° nose-down into the mound and caked in earth towards it, and the prow blade with two broken spires on the mound's crest. Moss on everything that faces up, earth on the lower hull, vines down the sides, ferns and saplings on the decks |
| The mound | Pushed-up dirt over the buried nose: 520 above the jungle at its crest, its west face steep (raw red earth in slumped layers, creepers hanging down it: bots only), long and gentler to the east, gullied by rain. A tongue of spilled dirt rises over the fore hull so it dives into the ground. A track graded for vehicles climbs its north-east flank to the crest |
| Lanes | West of the engines across the marsh; the breach between the ship's two halves (about 320 wide between the torn ends); east, over the mound or round it. The ship walls the halves apart everywhere else |
| Metal | 22 spots, 49.1 metal/s with an extractor on each: four round each start (2.0 each, 8.0 a player), and six on the wreck (2.8 each): by the engines, at the breach and on the mound's crest, one each side. `maxMetal` 4 |
| Geothermal vents | 2: on the furrow's banks by the aft section's torn end, where the reactor spilled |
| Wreckage | 254 pieces in 15 kinds, heaviest round the breach, along the hull's flanks in the shallows and round the mound's face, thinning across the field to small scraps near the bases: hull ribs, girders, engine bells and pods, armour plates and chevron slabs, hull shards, fins, antenna spires, reactor cores still faintly alight, hatches with the ship's crest, scrap. 7,190 metal a team to reclaim |
| The jungle | 1,106 oaks, 178 kapok and 42 banyan giants, 510 palms (more by the water), 378 mangrove root clumps and 900 reed beds (with bulrushes) in the shallows and mud, 2,666 ferns (thickest over the mound), 36 mushroom clusters on the mound: 6,076 features in all with the ship and wreckage. Kept off the trails, bases, spots, vents and the wreck |

## The ship's look

One atlas (2048 square, 16 cells) for every part, painted by `converter/Atlas.cs`:
- **Plating:** gunmetal panels in staggered rows with grooves, bevels, rivets and inset hatches, streaked, grimed and rusting.
- **Armour:** faded crimson with thin brass chevron inlays, chipped to bare metal.
- **Trim:** ridged brass with green patina.
- **Details:**
  - scorched torn metal with heat tint;
  - vents;
  - dark windows;
  - engine bells with a faint cyan ember;
  - girders with lightening holes.
- **Glowing seams:** cyan, through texture 2's glow channel.
- **The crest:** the ship's own, a gold blade through a chevron with swept wings on crimson in a hexagon. No real insignia.
- **Overgrowth:** moss over the plating and over the armour, earth with roots, and leafy vines and fronds; reed blades for the marsh.

The look is angular, layered war-machine armour in the spirit asked for (the style of a Transformers ship), with no franchise names or marks anywhere.

Each wreckage piece has a mirror-image twin (`name_m`) for the south half, placed at heading −h, so the south is a true mirror image of the north, not just turned copies.

## Checked (`check.txt`, on the built map's own heights, round every blocking feature)

The features block 13.5% of the map, each blocking one counting its footprint (16-unit footprint units, turned to its facing) and 8 more round it. 13 checks, all passed:
- **Starts:**
  - From the north-west start, vehicles reach all four starts; 78.6% of the map is open to them.
  - Every start is on dry, nearly level ground.
- **Lanes:**
  - Each lane on its own lets vehicles and bots cross from north to south: across the marsh, through the breach, over or round the mound.
  - With the three lanes shut, nothing crosses: the ship's sections leave no gap.
- **Metal spots and vents:**
  - No blocking feature is within 40 of a spot.
  - Every spot and vent is level: a 0.0 rise over its footprint.
  - Bots and vehicles reach every spot and vent, including those on the mound's crest, by its track.
- **Halves:** alike to 0.00 at 200,000 points.

Also checked:
- **Model orientation and scale**, on a test map before any detail (`slice`): the textures are the right way up, a heading of 16384 turns a model a quarter (local +x north), and the models sit on the ground.
- **Feature names:** every name placed is defined by the map. MapTool reports none undefined.
- **Rotations:** written in the engine's units (65536 a turn).

## Made better on the way

1. **The first look:**
   - The crimson was too bright, and its chevrons were too regular.
   - The hull's panel grid read as tiles.
   - The prow was a flat billboard.
2. **The terrain:**
   - The furrow ran on past the mound.
   - The pools were perfect black ellipses.
   - The mound showed a target of earth bands, and was too low to read as huge.
   - The puddles came out rimmed in raw earth.
   - Its first gullies cut zigzag scars.
3. **The nose:** the fore hull ended in a box in front of the mound instead of diving into it. Now spilled dirt covers it, and the ship's minimap silhouette is no longer painted over the buried part.
4. **Placement and economy:**
   - Most wreckage first failed to find room. Its placement now allows it into the shallows, and long pieces count their mean size.
   - The reclaim was halved from 14,380 a team.
   - Features were cut from 24,000 to 6,000.
   - A wreck spot stood in a stream and was moved onto the bank.
   - Vehicles couldn't reach the mound's crest spots, so the track was cut.

## Pictures

- **The map:** `map_2d.png`, `3d_overview.png`
- **Places:**
  - `3d_crash.png` (the breach and wreckage)
  - `3d_engines.png` (over the reed marsh)
  - `3d_mound.png` (the crest from the west)
  - `3d_nose_dive.png` (the fore hull into the dirt)
  - `3d_base.png` and `3d_marsh.png`
- **Movement:** `3d_passability_vehicles.png`
- **The models alone, on flat ground:** `models_ship.png`, `models_breach.png`, `models_prow.png`, `models_wreckage.png`

## To try in BAR

- **The models in the engine:** whether BAR's shader shows them as MapTool's renderer does, how the cyan seams glow at night, and how 2048 textures and up to 8,000 triangles a section perform. The ship's collision boxes are its sections' solid boxes, so shots may stop at their corners.
- **Reclaim:** whether 7,190 metal a team in wreckage is too much early on. The metal is set in each feature's `.lua` and `Models.cs`.
- **The breach and the marsh:** whether 320 between the torn ends and the wadeable furrow make good fights.
- **Players:** 4 (2v2). The map has no start boxes, so draw them in the lobby or play with the fixed starts (0 and 1 north, 2 and 3 south).
- **Archive:** 59.7 MB. It isn't in git (`.gitignore`). It would go on the releases, as the other maps do.
