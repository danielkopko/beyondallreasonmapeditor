# Five Rivers 1.0: conversion and release

What was done to turn the K7 drawing into a BAR map and get it out, and how to do it again.

## Where it is

| Where | What | Who can get it |
|---|---|---|
| [BarMaps release five-rivers-1.0](https://github.com/danielkopko/BarMaps/releases/tag/five-rivers-1.0) | [`five_rivers_1.0.sd7`](https://github.com/danielkopko/BarMaps/releases/download/five-rivers-1.0/five_rivers_1.0.sd7) and `Five_Rivers_1.0.jpg` (the lobby picture) | Anyone: BarMaps is public |
| BarMaps' `README.md` and `LuaMenu/Images/Minimaps/Five_Rivers_1.0.jpg` | The map's description and install notes, beside Simmering Ravine's, and its lobby picture | Anyone |
| [This repository's release five-rivers-1.0](https://github.com/danielkopko/beyondallreasonmapeditor/releases/tag/five-rivers-1.0) | The same two files | Signed in as danielkopko (the repository is private) |
| This folder | Everything else: the converter, the design geometry, the checks, pictures, `CHECKLIST.md` | This repository |

The `.sd7` itself (112 MB) isn't in git: GitHub refuses files over 100 MB, and release files may be up to 2 GB. An earlier commit (09d9e0e) had it split into three parts; they were taken out the next commit (8659a32) in favour of the releases, so a download is one file and needs no clone.

## The conversion

The full steps are in `converter/README.md`; in short:

1. **The drawing:** `out/koth-maps/k7_five_rivers.svg` from `make_maps.py` (K7 reworked: oval halls 70% larger, a ramp at the front, three bots-only switchbacks, 11 vents). `export_geometry.py five_rivers design/geometry.json` writes its geometry, every team's part turned into place.
2. **The template:** All That Simmers v1.1.1, extracted, for its plants, detail textures, featureplacer gadget and water settings. No local map is 28 × 28 and the editor can't make one, so a converter makes the SMF and SMT from scratch with MapCore.
3. **The probe:** a flat 28 × 28 map built first (2 s, 1.3 GB), to see MapCore and MapTool cope with the size before writing the rest.
4. **`FiveRivers build`:** every height (realms, water, isle, fords, bridges, halls, ramps, switchbacks, mountains), the access checks (it stops if one fails), the Bloom planet's 14336² texture straight into the SMT, the minimap and grass, the splat, specular and normal maps (written upside down, as map DDS files are), a night sky, mapinfo.lua (name, credits, metal, colours, weather, 20 starts), and `ops/run.txt`. All five fifths alike: noise is read in Team A's fifth, since MapTool's symmetry only mirrors.
5. **`MapTool edit … @run.txt`:** 75 metal spots, 11 geothermal vents, 2,515 plants, crystals and rocks.
6. **`FiveRivers check`:** 62 access checks on the built map's own heights, all passed; the fifths match (median 0.00, 99% within 0.1).
7. **Fixed on the way:** the detail maps were first written the right way up (they must be stored upside down); MapTool's `--layer splat` and `--layer normals` renders caught it. The drawing's heights line was changed to match the build (the isle 60, not 200-230).
8. **Pictures and reports:** `map_2d*.png`, `3d_*.png`, `info.txt`, `metal.txt`, `check.txt`; the lobby picture from `MapTool install` into a scratch maps folder (`bar-data/`).

**Fixed after the release (2026-10-23): the features' headings.** `Place.Ops` wrote each heading in degrees, but MapTool's `item-add` takes the engine's units (65536 to a turn, 0 facing south). So in the released map every feature faces within 2° of south, and the other fifths' copies aren't turned. The converter now writes `round((180 − degrees) × 65536 / 360)`, wrapped to 0-65535. Its degrees run clockwise from north, as `Design.Turn` turns the fifths; the engine's run the other way from south. At the same time, at your new requirement for every release, the minimap and lobby picture became the map seen straight down in 3D, with heights and features (`CHECKLIST.md`, *The minimap and lobby picture*). The rebuild differs from the release only in `mapconfig/featureplacer/set.lua` and the SMF's minimap bytes, and passes the same 62 checks (`CHECKLIST.md`, *Headings*). It goes out with `five_rivers.cmd publish`, which replaces the release's files.

## The release

`five_rivers.cmd publish` (`five_rivers.ps1`):

1. Commits and pushes this folder, `out/koth-maps`, `PLAN.md` and `.gitignore` to origin.
2. Puts `five_rivers_1.0.sd7` and `Five_Rivers_1.0.jpg` on the release `five-rivers-1.0` in this repository and in danielkopko/BarMaps, with `release_notes.md`: made if it isn't there; its files replaced and notes refreshed if it is.

It signs in as the repositories' owner, danielkopko, through the GitHub CLI: on this PC git and `gh` are otherwise set to the work account (Daniel-Kopko_mainpac), which can't see the private repository. The CLI needs danielkopko signed in (`gh auth login`, choosing that account; it can stay the inactive one). The token goes from `gh` to git and `gh` for that run only, and nothing is saved.

BarMaps' README section and lobby picture were added once, by hand (commit in BarMaps); a new version needs its section and picture there updated the same way.

`five_rivers.cmd install` copies the map beside the script, and its lobby picture, into BAR (`-BarData <folder>` if BAR isn't in its usual place).

## To release a new version

1. Rebuild (`converter/README.md`), naming the map `five_rivers_<version>.sd7` (mapinfo's `version`), and run the checks.
2. Update `release_notes.md`, `CHECKLIST.md` and BarMaps' README section.
3. `five_rivers.cmd publish -Version <version> -Message "Five Rivers <version>: ..."`.
