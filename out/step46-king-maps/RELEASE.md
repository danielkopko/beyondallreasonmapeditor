# The king maps: conversion and release

What was done to turn the K6, K4 and K3 drawings into BAR maps and get them out, one at a time, and how to do it again.

## Where they are

| Map | BarMaps release (public) | This repository's release (private) | Notes here |
|---|---|---|---|
| Star Fort 1.0 (K6) | [star-fort-1.0](https://github.com/danielkopko/BarMaps/releases/tag/star-fort-1.0): [`star_fort_1.0.sd7`](https://github.com/danielkopko/BarMaps/releases/download/star-fort-1.0/star_fort_1.0.sd7) | [star-fort-1.0](https://github.com/danielkopko/beyondallreasonmapeditor/releases/tag/star-fort-1.0) | `star_fort/CHECKLIST.md` |
| Edge Keeps 1.0 (K4) | [edge-keeps-1.0](https://github.com/danielkopko/BarMaps/releases/tag/edge-keeps-1.0): [`edge_keeps_1.0.sd7`](https://github.com/danielkopko/BarMaps/releases/download/edge-keeps-1.0/edge_keeps_1.0.sd7) | [edge-keeps-1.0](https://github.com/danielkopko/beyondallreasonmapeditor/releases/tag/edge-keeps-1.0) | `edge_keeps/CHECKLIST.md` |
| Corner Fortresses 1.0 (K3) | [corner-fortresses-1.0](https://github.com/danielkopko/BarMaps/releases/tag/corner-fortresses-1.0): [`corner_fortresses_1.0.sd7`](https://github.com/danielkopko/BarMaps/releases/download/corner-fortresses-1.0/corner_fortresses_1.0.sd7) | [corner-fortresses-1.0](https://github.com/danielkopko/beyondallreasonmapeditor/releases/tag/corner-fortresses-1.0) | `corner_fortresses/CHECKLIST.md` |

Each release has the map and its lobby picture (`Star_Fort_1.0.jpg` and so on) and the map's `release_notes.md`. BarMaps' `README.md` has a section for each, beside Simmering Ravine's and Five Rivers', and its `LuaMenu/Images/Minimaps` has the three pictures. The archives (90 to 103 MB) aren't in git: `.gitignore` leaves out `out/step46-king-maps/*/*.sd7`.

## What was asked

- **"No doors for the moment":** none of the three has a door.
- **"Walls are terrain":** the ramparts are heights, 90 above the ground beneath, flat on top (you can build on them), with sheer faces. Towers are 110.
- **"Kings areas elevated":** every king's ground is a cliff-top (Star Fort's bastions at 300 over realms at 200, Edge Keeps' keeps at 360 over 240, Corner Fortresses' yards at 330 over 240) on cliffs over 54°.
- **"Both ramp for full access and side zig zags for bot access":** each king has one 12° ramp that everything climbs, and two bots-only zigzags (flights of 38° with landings) cut through the ramparts. The access checks on each built map show it: the ramp is the only way up for vehicles, and the zigzags take bots and nothing else. They read the map's own heights, and the features as well (`KingMaps check … run.txt`): the trees, wrecks, crates and rocks block no ramp, zigzag, pass or start.
- **"Geo vents":** 10, 9 and 9.
- **"Rocks/tree features making maps look like real locations, with interest":** each planet's look paints the ground and places features to suit:
  - Star Fort: dunes, a sandstone fort, palm oases, desert rocks and wrecks.
  - Edge Keeps: a highland moor with pinewoods, birch copses, tors and cairns.
  - Corner Fortresses: a derelict industrial estate of ruined works, container yards and trucks.

## The conversion

The steps are in `converter/README.md`. In short, for each map:

1. `export_geometry.py <key>` writes the drawing's geometry to `<key>/design/geometry.json`.
2. `KingMaps build` makes:
   - the heights, then the access checks (it stops if one fails);
   - the planet's texture, straight into a new SMT;
   - the detail maps and sky;
   - `mapinfo.lua`;
   - MapTool's `run.txt`.
3. `MapTool edit … @run.txt` adds the metal, vents and features.
4. `KingMaps check <geometry> <map> <run.txt>` runs the access checks again. It reads the built map's own heights, and goes round each blocking feature's footprint in the checks that units get through. It also compares the parts.
5. `MapTool install` into a scratch maps folder makes the lobby picture.
6. The pictures (`MapTool render` and `render3d`), `info.txt` and `metal.txt` are written beside the map.

All three passed their checks first time (41, 30 and 29). Passing them wasn't enough, though: the renders were looked at too, and two of the three were redone before release.
- **Edge Keeps:**
  - The first build was flat, with blotchy heather and pines only in three thin rows.
  - The Heath look gained broad rolling hills that roads and bases follow (`Look.Swell`), natural heather and moor grass, small peat hags, sett causeways, pinewoods, birch copses, tors, scree and boulders.
- **Corner Fortresses:**
  - Industrial Revolution's pipe models (runs up to 2000 long, 250 tall) looped over the ramparts, the ground was one tiled floor, and the market was a checkerboard.
  - The Forge look now has an estate's paved lots on waste ground, worn asphalt and brick paving. BAR's building wrecks make ruined works, and the pipes and mushrooms are gone.
- **Star Fort:** the first pass over each ridge climbed the cliff face, so bots couldn't cross. The pass is now the ground lowered along its line, at most 60 above the realms and 38° anywhere.

The later changes (the swell, the new Heath and Forge looks, a feature's room by kind) left Star Fort's build as it was released: a rebuild after them gave the same SMF, SMT, detail maps, `mapinfo.lua` and `run.txt`, byte for byte. That stopped being true with the headings fix (below). A rebuild now differs from the release in `run.txt`'s headings, and so in the archive's `mapconfig/featureplacer/set.lua`, and in nothing else.

After all three were out, the checks were extended to the features. Re-run on all three maps, every check passed: the features block 0.6%, 1.9% and 2.5% of the ground, and none cuts off a ramp, zigzag, pass or start.

## Fixed after the release: the features' headings (2026-10-23)

`Place.Ops` wrote each feature's heading in degrees, but MapTool's `item-add` takes the engine's units: 65536 to a turn, with 0 facing south. Step 47's test map showed it. So in all three releases (and Five Rivers) every feature faces within 2° of south: the ruins line up, trucks stand across the roads, and the parts' copies aren't turned.

- **Units and frame:** the converter's degrees run clockwise from north, the way `Design.Turn` turns the parts and a road's bearing is worked out. The engine's run the other way from south (16384 is east). So `Place.Heading` writes `round((180 − degrees) × 65536 / 360)`, wrapped to 0-65535: MapEditSession.AddItem's rounding, after turning the frame round. The units alone would have left each part's copies turned the wrong way and diagonal roads' trucks across them.
- **Trucks:** the minitrucks' models are long along x, not z, so each is turned a quarter more than its road.
- **Rebuilt, all three:** each archive differs from its release only in `mapconfig/featureplacer/set.lua` (`MapTool compare`), and each `run.txt` is the same line for line, but for the headings. Every copy faces its original turned with its part (to within a unit for Star Fort's fifths, exactly for the quarters). The facings spread evenly.
- **Unchanged:** `check.txt` (41, 30 and 29 checks, all passed), `info.txt`, `metal.txt` and the lobby pictures. The checks take each blocking feature's footprint as a square round it, the same whichever way it faces. How BAR turns a wreck's real footprint with its facing is still to try in game.
- **Pictures:** the 3D pictures with features in them were drawn again with their first cameras. Corner Fortresses has a new one, `3d_trucks.png`.

The maps beside the notes are the rebuilt ones. The releases still have the first builds until `king_maps.cmd publish <map>` replaces their files.

## The release

`king_maps.cmd publish <map>` (`king_maps.ps1`):

1. Commits and pushes this folder, `out/koth-maps`, `PLAN.md` and `.gitignore` to origin.
2. Puts `<map>/<map>_1.0.sd7` and its lobby picture on the release `<map-with-dashes>-1.0`, in this repository and in danielkopko/BarMaps, with `<map>/release_notes.md`. It makes the release if it isn't there, or replaces its files and refreshes the notes if it is.

As with Five Rivers, it signs in as danielkopko through the GitHub CLI (`gh auth token --user danielkopko`), for that run only, because git and `gh` on this PC default to the work account. Each map's BarMaps README section and lobby picture were added by hand after its release, in a sparse clone of BarMaps (its README and `LuaMenu` only), and pushed the same way.

`king_maps.cmd install <map>` copies a map and its lobby picture into BAR (`-BarData <folder>` if BAR isn't in its usual place).

## To release a new version

1. Raise the version in `converter/Program.cs` (the file name in `Maps.For`, and `version` in `WriteMapInfo`, both `1.0` now), rebuild (`converter/README.md`) and run the checks. Look at the renders.
2. Update the map's `release_notes.md` and `CHECKLIST.md`, and its section in BarMaps' README.
3. `king_maps.cmd publish <map> -Version <version> -Message "<Map> <version>: ..."`.
