# Starship Meridian 1.0: checklist

`starship_meridian_1.0.sd7` ("Starship Meridian 1.0", short name `StarshipMeridian`, map id 20261124) is a 24 × 28 map for 4v4.

It is one whole starship, a battle-carrier adrift in space, boarded from port and starboard at once. The two fleets fight for the ship's centreline: the bridge, the command room, the reactor, the shield generator and the engine room.

- **Made by the converter here** (`converter/README.md`): the terrain, texture, detail maps and starfield sky, and **every feature**: the craft, machines, landmarks and props, all modelled and painted on one atlas.
- **From All That Simmers v1.1.1:** the featureplacer gadget. Its detail textures aren't used (a ship has no ground for them).
- **The plan** is `DESIGN.md`, with its to-scale drawing (`design/layout.svg`, from `design/layout.py`, whose numbers are `converter/Layout.cs`).

The map is **symmetric**: the starboard half is the port half's mirror image across the ship's centreline (x = 6144), heights and features alike (a check).

**Not tried in BAR yet.** BAR isn't installed on this machine. Everything below was checked with MapCore's copy of the engine's rules, and the looks with MapTool's renderer, not in the game.

**Download:** one file, [starship_meridian_1.0.sd7](https://github.com/danielkopko/BarMaps/releases/download/starship-meridian-1.0/starship_meridian_1.0.sd7), from the public BarMaps release [starship-meridian-1.0](https://github.com/danielkopko/BarMaps/releases/tag/starship-meridian-1.0), with its lobby picture `Starship_Meridian_1.0.jpg`. The same files are on [this repository's release](https://github.com/danielkopko/beyondallreasonmapeditor/releases/tag/starship-meridian-1.0) (signed in as danielkopko), and BarMaps' README has its section and `LuaMenu/Images/Minimaps/Starship_Meridian_1.0.jpg`. After rebuilding, `starship_meridian.cmd publish -Message "..."` pushes the notes and replaces both releases' files (BarMaps' README section and picture are updated by hand); `starship_meridian.cmd install` copies the map into BAR.

## The layout

| | On the map |
|---|---|
| Teams | 4v4, port (west) against starboard (east). Teams 0 to 3 are port: 0 the forward hangar (1864, 5250), 1 the crew deck (3600, 5250), 2 the main hangar (1864, 9450), 3 the cargo hold (3600, 9450). Teams 4 to 7 are their mirror images. The hangars are the back seats, outboard; the crew deck and cargo hold the front seats, facing the centreline |
| The hull | Bow north at z 400, the stern at 14000, 10,488 across at its widest. A rim 160 wide runs round it at 380. Three engine housings stand out from the stern, each with two main engines hanging off its aft end over space. Everything inside the hull that isn't a room, a passage or a door is solid: bulkheads 340, the rim and the housings 380, round the bridge 440. The hull plating has staggered plates, seams and rivets, with access hatches and vent grilles here and there |
| Space | Rough ground at about 20, its texture transparent. With `voidGround` the engine doesn't draw it, so the starfield sky shows. It's above the water level, and there's no water anywhere, so nothing naval can be built. 98% of it is too steep to build on or drive over (a check), and the rim walls it off from every ground unit but spiders (a check) |
| Each team's rooms | Two columns on the main deck at 240, beside the team's own passage (600 wide, the length of the ship):<br>• **outboard:** the forward hangar (a start), the power room (the team's geothermal vent), the main hangar (a start) and the thruster bay;<br>• **inboard:** the crew deck & mess (a start), the armory, the cargo hold (a start) and the machine shop;<br>• **at the bow:** the weapons bay, across both columns.<br>Rooms join each other by doors 500 to 600 wide, and the inboard rooms join the passage |
| The bridge | Raised at the bow, its deck at 380 behind walls topped at 440. On each side, a landing joins it, and a ramp 600 wide and 880 long (about 9°) runs down in line with that side's passage. A grand stair 700 wide leads down into the CIC: bots take it, vehicles can't. Consoles in two rows before the command chair, a long viewport on its forward wall, the sensor dome and an antenna on the bow beyond |
| The CIC | The command room, its deck lit with a cyan grid, round a glowing holo-table in a ring. Consoles either side of the crossing lane |
| The main reactor | Its core glows in a sunken well 1,280 across, its floor at 120, ringed with cyan and hazard stripes. A ledge runs all round the well at deck level. From each crossing door, a ramp runs straight down into the well (about 11°). Two geothermal vents are on the well's floor, one each side of the core |
| The shield generator | The shield emitter on three pylons, a glowing sphere, in a blue ring |
| The engine room | The drive core on a framed grating, its glowing end aft, with coolant towers aft. Behind it is the central engine housing, with a turret between its engines |
| The crossings | Five lanes join the halves, one through each shared room, by crossing doors 700 wide from the passages. Spine doors 688 wide join the shared rooms bow to stern |
| Walls | Terrain at about 70°: no vehicle (27°), hover (33°) or bot (54°) climbs them, and they stop direct fire. Spiders climb them (intended: their flanking role) |
| The fronts | Crew deck to crew deck is 5,114 by the shortest drive, and to the enemy's cargo hold 7,272 |
| Metal | 46 spots, 107.6 metal/s with a T1 extractor on each (`MapTool metal` reads 108):<br>• three round each start (2.0 each);<br>• one each in the weapons bay, armory, thruster bay and machine shop, and two in the power room (2.4 each);<br>• two in each shared room (3.0 each; 3.4 in the reactor).<br>Each team has 38.4 metal/s of its own, and 30.8 is shared. `maxMetal` 4 |
| Geothermal vents | Four: one in each team's power room, two on the reactor well's floor |
| Wind and tide | None: no wind in space (your choice), no tidal |
| Reclaim | **Indestructible, no reclaim, uncrushable:** the reactor's core, the shield emitter, the drive core, the holo-table, the service towers, the repair gantries, the coolant towers, the hull's turrets, antenna, dishes and sensor dome, the viewport, the engines and the pipe runs.<br>**Reclaimable:** a dropship 600 metal, a cargo shuttle 400, a fighter 200, a loader 120, a missile cart 100, a tug, munitions or a fuel tank 80, and the props 20 to 60. Capacitor banks, transformers, fuel carts, fuel tanks and planters give energy too. Each team's rooms hold 4,460 metal and 5,600 energy, and the shared rooms 930 metal and 2,000 energy |
| Light | A nearby star's hard white light, deep shadows, low blue ambient, no fog |

## The features

191 placed, of 38 models (and the pallet's mirror-image twin, the one lopsided model):

| Where | Features |
|---|---|
| The hangars | 2 dropships, 4 fighters and 2 cargo shuttles on the landing circles; 4 service towers; tugs and fuel carts; pallets and crates |
| The crew deck | 16 bunks, 4 mess tables, 4 lockers, 4 med pods, 4 hydroponics planters |
| The weapons bay and armory | 16 weapon racks, 8 munitions stacks, 4 missile carts |
| The power room | 8 capacitor banks (two more in the reactor), transformers, pipe runs |
| The cargo hold | 8 containers (red and blue), pallets, crates, 2 loaders |
| The thruster bay and machine shop | 8 fuel tanks, transformers, pipe runs; 2 repair gantries, each with a tug beside it, 4 tool benches |
| The bridge and CIC | 12 consoles, the command chair, the viewport, the holo-table |
| The reactor, shield and engine room | The reactor's core, the shield emitter, the drive core, 2 coolant towers, capacitor banks and transformers |
| The hull | 7 point-defence turrets, 2 comm dishes, an antenna, the sensor dome, 6 main engines |

Placement keeps each room's props along its walls, clear of its doors (and the way in front of them), its metal and its start, and leaves each start room at least 95% free and level.

## The look

- **One atlas** for every model (`converter/Atlas.cs`): graphite and gunmetal plating, white panels, orange hazard trim, cyan light strips and glowing energy through the glow channel, windows, screens, vents and grates, copper, rubber, containers' corrugation, the planters' leaves. No franchise names or marks.
- **Decks:** 64-unit plating panels with seams and rivets, worn paint, light oil stains and the odd scorch mark. The hangars have landing circles and a guide line in the side's colour, with hazard edging; the passages edge stripes and chevrons; every door hazard chevrons across its threshold (orange at the crossings and the spine); the bridge a dark blue deck; the CIC a lit grid.
- **The side's colour:** red to port and green to starboard, on the hangars' circles, the passages' stripes and the rim's navigation lights.
- **The sky:** a starfield with a faint nebula, made by the converter.

## Checked

`check.txt` is the checks on the built map's own heights, round every blocking feature's footprint (16-unit cells turned to the feature's facing, and 8 more round it). 30 pass:
- **The halves:** the heights match their mirror image at every vertex (0.000 apart), and every feature has its mirror image: the same model, or its twin, at the mirrored place and heading.
- **Getting about:** vehicles reach all eight starts, every spot and every vent (the reactor's by the well's ramps); bots reach every start and spot.
- **The crossings:** each of the five alone lets vehicles across. With all five shut, vehicles, bots, hovers and amphibious units can't cross. Spiders still can, over the bulkheads; the check logs it.
- **Space:** no vehicle, bot, hover or amphibious unit reaches it, and 98.3% of it is too steep to build on or drive over.
- **Doors and rooms:** every door is at least 500 wide, and none has a feature in it. Each start room keeps 95% to 97% of its floor level and free to build on (4.1 to 4.4 M square units).
- **Standing true:** every feature but the viewport, the engines and the pipe runs stands on ground that rises no more than 4 under its footprint (all 0.0).
- **Metal:** no blocking feature within 40 of a spot; every spot, vent and start is level; each team's own metal matches.

## Made better on the way

1. **The renderer and `voidGround`** (MapCore, with tests): MapTool's 3D renderer now draws the sky where a `voidGround` map's texture is transparent, as the engine does. A texture update can now keep alpha (DXT1's transparent texels), as space needs; by default it still stores pixels opaque.
2. **The reactor's vents:** at first vehicles couldn't reach them, because the crossing doors' floor was forced to the deck's height over the well's ramps. Doors in the reactor now follow the ramp.
3. **The models' symmetry:** a voxel test of each model's surface against its mirror image (comparing vertices failed symmetric models triangulated the other way). It found real lopsidedness, all fixed: a wing's depth, the blobs' lumps, the weapon rack's loop and the shield emitter's pylons. Only the pallet keeps a twin.
4. **A crater in the bridge's forward wall:** the walls near the centreline rose from the starboard floors before those floors' heights were mirrored in, as if from height 0. The wall dropped to 20 at the centreline and swallowed the viewport. The floors' heights are mirrored first now; no dip is left anywhere on the centreline.
5. **The hull's plating** was a noisy grey checker of 64-unit squares. It's now staggered 256 × 128 plates with seams, lit lips and rivets, and here and there an access hatch or a vent grille.
6. **The decks' stains** were dark blotches everywhere. Now they're fewer and lighter.
7. **The engine room's grating** read as stairs. It's now a framed grid over the drive.

## The minimap and lobby picture

The map's minimap is the ship seen straight down in 3D, with heights, light and features, on the starfield. That minimap is what shows in game, and `MapTool install` draws the lobby's picture from it.
1. `MapTool render3d <map> top.png --size 8192x8192 --camera 6144,7168,0,89.9,72827 --models`. The camera is 5.08 map-lengths up (the 28), as for the other maps, so the decks, the bridge and the hull sit at most a pixel or two out of place.
2. `Meridian minimap top.png minimap.png 3262 3123 4930 5070 1024` cuts the map out of the render and scales it to 1024 square. The crop is the map's corners at the deck's height, projected through the camera: the map is 24 × 28, so it's scaled unevenly, and with space undrawn the background can't show where the map ends. Wherever the render shows its background (space), it puts the starfield.
3. `Meridian setminimap <extracted map> minimap.png` puts it into the SMF.
4. `MapTool pack` packs the map again.

The minimap is `minimap_3d.png`. `MapTool info` reads it back as 1024 × 1024 DXT1.

## Pictures

- **The map:** `map_2d.png` (texture, spots and starts), `3d_overview.png`, `minimap_3d.png`
- **The bow:** `3d_bow.png` (the sensor dome, the antenna, the viewport), `3d_bridge.png` (the bridge, its ramps and landings, the weapons bays)
- **The team rooms:** `3d_forward_hangar.png` (and the crew deck), `3d_power_armory.png`, `3d_main_hangar.png` (and the cargo hold), `3d_machine_shop.png` (and the thruster bay)
- **The centreline:** `3d_cic.png`, `3d_reactor.png`, `3d_shield.png`, `3d_engine_room.png`, `3d_stern.png` (the engines over space)
- **Movement:** `3d_passability_vehicles.png`
- **The models alone, on flat ground:** `models_craft.png`, `models_landmarks.png`, `models_props.png`, `models_cargo_and_hull.png`

## To try in BAR

- **`voidGround`,** new to these maps:
  - whether space shows the starfield as in the pictures;
  - whether a transport can drop units onto the undrawn ground, where they'd stand stuck;
  - where spiders go when they climb off the rim into space.
- **Interior fights:** walls stop direct fire, so the rooms may play campy and artillery-heavy. If they do, the lever is to lower the team-side bulkheads to about 60 over the deck (still a wall to movement, but tall units shoot over) and keep the centreline's at 100.
- **The fronts:** whether 5,114 between the front starts is too close for 4v4. If it is, move the front starts outboard within their rooms (up to about 800 further each).
- **No wind:** whether energy from solar, geothermal, fusion and reclaim is enough early on.
- **The models in the engine:** whether the glow shows on the energy, lights and screens; whether shots stop on the landmarks' collision boxes.
- **Players:** 8 (4v4). The map has no start boxes, so draw them in the lobby or play with the fixed starts (0 to 3 port, 4 to 7 starboard).
- **Archive:** 37 MB. It isn't in git (`.gitignore`). It goes on the release, as the other maps do.
