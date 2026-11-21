# Map requirements (mandatory)

Every map made here must meet BAR's mapping direction: the "General Mapping Direction" page (page 7, version 0.35) of the BAR design document, [BAR Design Doc | Focusgroup](https://www.figma.com/proto/ldyqX4m5GHuD6yWQX7mDmq/BAR-Design-Doc-%7C-Focusgroup?node-id=52-423&node-type=FRAME&scaling=scale-down-width&content-scaling=fixed&page-id=18%3A2). This is our summary in our own words; the page itself rules where they differ.

## Hard limits

- **Size:** no more than 32 map units in either dimension (32 × 32 at most). Larger maps aren't accepted: they play badly and strain the engine.
- **Unique:** a map almost identical to another in layout, playstyle or size may not be accepted. A reskin needs the Mapping Lead's and the original author's agreement, and must be clearly better.

## The guidelines

- **The map serves the game,** technically and visually: units, effects, explosions and battle scars come first.
- **Units must stand out.** Make them as easy to recognise as possible.
- **Vehicle, bot and impassable slopes must look clearly different,** so a player sees at a glance where each class can go. Height and pathing should read instantly; subtle contour lines (topolines) help where slopes are gradual or there are several ramps.
- **Keep it clean, and balance noise, saturation and contrast:**
  - **Noise:** no busy, over-detailed textures, least of all in the play areas (bases, the front, chokepoints). Busy normal maps (strong detail normals) count too.
  - **Contrast:** gentle in the play areas; stronger contrast belongs on cliffs and ground units can't cross.
  - **Saturation:** the main texture slightly desaturated, so units pop. Not too bright either (very bright or saturated maps strain the eyes over a long game).
  - **Features:** keep clutter off chokepoints.
- **Creative freedom outside the play areas:** cliffs, edges and impassable ground can carry the detail.
- **Light:** units need depth and shadow; flat or wrong light makes them bland.
- **Heights:** smooth, high-resolution slopes (16-bit heights, never blocky).
- **The look:** realistic terrain with an artistic touch, vibrant but supportive of play. Water: deep darker than shallow. Snow: greyish rather than white.

## Metal-looking ground means a metal map

Not on the page, but the same rule in practice (your call, 2026-11-12): if a map's playable ground reads as metal at a glance (a ship, metal structures), BAR players read it as extractable everywhere. So it must be a **metal map**: every buildable floor holds metal, with no discrete spots. Repainting the floor as some other kind of surface doesn't change that reading. BAR's map list tags these maps `special: Metal`. Starship Meridian follows Cloud9_V2 (by Beherith, certified and in BAR's pool, `cloud9_v2.sd7` in this folder): raw metal 79 on every cell of solid ground, `maxMetal` 7.5, `extractorRadius` 24.

## How the maps here stand (2026-11-12)

**Starship Meridian 1.1** is a metal map (above). **Meridian Landfall** is shelved: a metal ship on a spot map can't be balanced, and at 32 × 36 it breaks the size limit too.


Sizes, from `MapTool info` on each built map: Meridian Landfall 1.0 is 32 × 36 and **breaks the size limit**. Every other map is 32 × 32 or smaller: Starship Meridian 24 × 28, Marram Line 24 × 24, Red Dust Open Cut 24 × 24, Five Rivers 28 × 28, Star Fort 28 × 28, Edge Keeps and Corner Fortresses 24 × 24, Starfall Marsh 16 × 16, Simmering Ravine 14 × 20.

Texture measures, over each map's whole texture (opaque texels only) at 4 game units a pixel. These are observations to compare, not pass marks; the page gives no numbers.

| Map | Saturation | Brightness | Fine noise |
|---|---|---|---|
| *BAR maps, for scale:* Acidic Quarry, All That Glitters, All That Simmers, Ascendancy, Crimson Bay, Folsom Dam, Industrial Revolution | 0.24 to 0.86 (most 0.24 to 0.49) | 0.25 to 0.63 | 4.7 to 19.1 |
| Starship Meridian 1.1 | 0.12 | 0.44 | 6.7 |
| Meridian Landfall 1.0 | 0.06 | 0.50 | 6.9 |
| Red Dust Open Cut 1.0 | 0.58 | 0.61 | 5.2 |
| Five Rivers 1.0 | 0.46 | 0.48 | 3.7 |
| Starfall Marsh 1.0 | 0.46 | 0.26 | 2.5 |
| Star Fort 1.0 | 0.43 | 0.80 | 4.6 |
| Simmering Ravine 1.5 | 0.83 | 0.37 | 4.8 |

Saturation is HSV saturation's mean, brightness HSV value's mean, and fine noise the RMS of luminance less its 5 × 5 mean (0 to 255). They're whole-map figures from a downsampled texture: grain finer than 4 units (pebbles, rivets) is averaged away, and they ignore the light, so a map can measure mid-range and still look busy or glaring in game (Meridian Landfall's plain measures 0.53 bright but renders near-white under its star). Judge the guidelines on the renders.

What the renders show against the guidelines:
- **Starship Meridian:** slopes read clearly (dark walls, plated ramps, the bot-only stair has treads), and the metal look now matches a metal map. But yellow-and-black and orange-and-black hazard stripes cross every door, and on this ship the doors are the chokepoints: strong contrast exactly where the guidelines want it gentle. Since the middle was opened up (1.1), the crossings and spine doors are 1,400 to 1,500 long, so those stripes run in long bands through the middle of the map.
- **Meridian Landfall:** the bot-only bowls are the same pale grey as the plain and the small craters, so bot ground doesn't look different from vehicle ground. The plain renders very pale and densely pitted in the play areas. It also carries Meridian's whole ship interior, which the near-duplicate rule may catch (the Mapping Lead's call).
