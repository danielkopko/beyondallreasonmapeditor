# BAR Map Editor

A Windows editor for Spring/Recoil maps (`.sd7` / `.sdz`), as used by **Beyond All Reason**. It opens an existing map, lets you change it, and saves it as a new map archive. You can:

- **Heights:** raise, lower, smooth, flatten, set to a height, add noise, shift or scale the whole map, preview a new sea level.
- **Ramps:** place a rectangle on the map, stretch, widen and turn it, then apply. The editor fills in the slope, blends the edges into the ground and copies the texture from the ground the ramp joins. Add points to bend it, for example round the edge of a cliff.
- **Texture and colour:**
  - paint, tint, adjust (brightness, saturation, hue) and clone the map texture;
  - paint the splat map (which detail texture shows where);
  - edit the sky, fog, sun, lighting and water colours.
- **Metal:** move, add, delete and resize spots, and set their income. Moving a spot can carry its look (the molten-metal splat, the texture patch, the normal-map outline) with it.
- **Trees and other items:** select, move, turn, place, scatter and delete them. Paint grass and drag start positions.
- **Assets:** every map you open adds its trees, rocks, buildings and props to a library. The asset browser along the bottom of the window shows them, and everything BAR itself offers a map: all its units, buildings and features (wrecks, rocks, crystals and the rest), with their models. Any of them can be placed on any map. A preview shows each model before you place it.
- **View:** a 2D view with several layers, plus a separate 3D view that updates as you edit. The 3D view draws the placed trees, rocks and buildings as their models.
- **Beautify:** paint the texture by the terrain (rock on cliffs, dirt and scree, sand and mud along the water, shade in hollows) and roughen the cliffs, in one go, without changing where any unit or building can go.
- **Symmetry:** mirror every edit across the middle, or to the opposite corner.
- **World Machine:** send the map's heights, texture and masks to World Machine, and bring its erosion, textures and splat maps back, keeping where units and buildings go; it can watch the folder, or run a World Machine Professional build.
- **Import and export:** any layer as PNG (heights, texture, metal, grass, terrain types, splat, normals), to edit in GIMP or Photoshop.

There is also **MapTool**, a command-line tool that does every edit the editor does. It is useful for scripted changes and repeatable builds.

> **Status:** everything here is tested against `acidicquarry_5.17.sd7` and a second, synthetic map. The asset library and previews were also run on six more BAR maps (All That Glitters, All That Simmers, Ascendancy, Crimson Bay, FolsomDamR, Industrial Revolution). Nothing has been checked **in the game** yet, because BAR isn't installed on the machine where it was built. Please try your edited maps in a skirmish before sharing them (see *Checking a map in BAR* below).

---

## Requirements

- Windows 10 or 11, 64-bit.
- The **.NET 10 Desktop Runtime** (or the .NET 10 SDK). If it is missing, Windows says so when the editor starts and links to the download.
- **7-Zip** to open or save `.sd7` maps. The editor looks in `C:\Program Files\7-Zip`, `C:\Program Files (x86)\7-Zip` and on `PATH`. `.sdz` maps work without it.
- Disk space: about 1.5 times the map's unpacked size. Acidic Quarry, a 50 MB `.sd7`, unpacks to 90 MB. The editor checks before it starts.
- Memory: opening and drawing Acidic Quarry (a 6144×6144 texture) takes at least 310 MB. The normal-map and splat layers, the 3D view and the undo history (up to 512 MB) add to that.

## Quick start

1. Start `MapEditor.exe`, then **File › Open** a `.sd7` or `.sdz`. You can also drop the file on the window.
   - The map is unpacked into a private working folder. The original archive is never written to.
2. Pick a tab on the right: **Heights**, **Texture**, **Metal**, **Items** or **Colours**. The front tab's tool drives the left mouse button.
3. Edit. Every brush stroke, drag or panel change is one undo step (**Ctrl+Z** / **Ctrl+Y**). The Edit menu names the step.
   - For a symmetric map, choose **Symmetry** on the toolbar: every edit is made again at its mirror image (see *Symmetry* below).
4. **F3** opens the 3D view beside the main window. It follows your edits as you make them.
   - **Assets** on its toolbar (on by default) draws the placed features as their models, turned to their headings and lit by the map's sun. Far-off ones show as dots of their colour.
   - Features the map names but doesn't define, and the units and buildings its featureplacer places, are drawn with **BAR's own models**, shipped with the editor: the engine's trees as the fir trees BAR draws for them, BAR's rocks and wrecks, every unit and building (see *BAR's own units, buildings and features* below). Anything else with no model shows only as an *Items* marker.
   - **Passable for** (3D toolbar: Off, Hovercraft, Vehicles, Bots) tints the ground by where that kind of unit can go, using the engine's own rules:
     - green: open ground, up to the limit (hovercraft 33°, vehicles 27°, land bots 54°);
     - red: too steep;
     - purple: water deeper than 20, where vehicles and land bots stop (amphibious bots and commanders go on);
     - cyan: on water, where hovercraft go whatever the slope beneath.
   - It works per 8-unit square, as the game does (a unit's whole footprint must fit, too). It follows the 2D view's sea-level preview, and updates as you edit.
5. **File › Save As** asks for a new **name** and **version**, then writes a new archive.
   - It refuses the original name and version: BAR would treat a changed map under the same name as a mismatch in multiplayer.
   - Tick *Also copy it into BAR's maps folder* to install it too. The editor checks that folder for another map with the same name and version first.
   - Installing also puts the map's picture where BAR's lobby looks for it, so the setup screen and map list show it (see *Known limits*).

### The look

The editor is dressed as BAR's own interface, after its settings overlay and in-game controls:
- near-black panels and dark title bars;
- warm brown tabs, the chosen one dark with gold-tan text, and tan-gold section headings with a faint line;
- dark fields with a square at the end of each dropdown, toggles lit by a green square, thin sliders with a square knob;
- dark buttons; a tool button that is chosen has a gold bar under it, and the toolbar's view switches show their state as a bar under them (green on, grey off).

Message boxes and the Open and Save file windows are Windows' own and keep the system's look.

### Mouse and keys

| | |
|---|---|
| Mouse wheel | zoom about the cursor |
| Right or middle drag | pan (always, whatever the tool) |
| Left button | the current tool (with **Pan**, it pans too) |
| Arrow keys, `+` / `-`, `Home` | pan, zoom, whole map |
| `[` / `]` | smaller / larger brush |
| `Ctrl+1` … `Ctrl+7` | texture, heights, metal, grass, splat, normals, minimap layer |
| `F3` | 3D view |
| **Heights** tools | `P` pan, `R` raise, `L` lower, `S` smooth, `F` flatten, `H` set height, `N` noise, `M` ramp |
| **Ramp** tool | click to place, drag to move, drag an end handle to stretch and turn, the side handle to widen; `Q` / `E` turn 15°, `[` / `]` width, `Enter` applies; `Shift`+click adds a point beyond the nearer end, `Ctrl`+click adds a bend on the line (`Ctrl`+click a bend to remove it) |
| **Texture** tools | `P` pan, `B` paint, `T` tint, `A` adjust, `C` clone, `D` splat |
| **Metal** tools | `S` select/move, `A` add, `P` pan; `Delete` removes the selected spot |
| **Items** tools | `S` select/move, `A` place, `C` scatter, `G` grass, `P` pan; `Q` / `E` turn; `Delete` removes |
| `Ctrl+click` | pick the height (Set height) or the colour (Paint, Tint) under the cursor; add to the item selection |
| `Alt+click` | set the Clone tool's source |

**Help › Controls** shows the same list in the editor.

### Symmetry

Most maps start out symmetric, so both sides play the same. **Symmetry** on the toolbar makes every edit twice:
- **Off** (how each run starts): edits are made once.
- **Mirror:** each edit is made again at its mirror image, across the axis chosen beside it:
  - *Left | right*: the line down the middle; *Top | bottom*: the line across the middle;
  - *Diagonal ╲* and *Diagonal ╱*: corner to corner (square maps only).
- **Mirror + flip:** mirrored across both middle lines, so the copy lands at the opposite side and corner, turned half round (a half turn about the centre). The axis makes no difference to this one, so its box is greyed.

The line is drawn on the map in amber (for Mirror + flip, a mark at the centre), and a brush shows a dashed amber copy of itself where its mirror image lands.

- **Made twice:** brush strokes (heights, texture, splat, grass), ramps, and new items, metal spots and structures.
  - A copy faces the mirrored way; with Mirror + flip, the opposite way.
  - Noise, which is fixed to the map, is read mirrored, so the bumps match.
  - A ramp's copy takes the original's heights, edge, rock and dirt as worked out where the original is, so it is the same ramp.
- **Changing something already there** (moving, turning, setting, removing an item, a metal spot, a structure, a repair marker, a guardian's path or rules) does the same to its **twin**: the same thing at its mirror image, within 16 game units.
  - Without a twin, only the original changes, and the log says so.
  - With both halves chosen, each is changed once.
- **Start positions:** on a map with two, moving one moves the other to its mirror image. With more, the start nearest its mirror image (within 256) is its twin.
- **Made once:** what isn't at a place: mapinfo settings and colours, raising or scaling the whole map, imports, adding assets, new features and structure kinds.

An edit and its copy are one undo step. Something exactly on the line is its own mirror image, so it is made once. A brush reaching over the line works the overlap twice, as two dabs there would. Guardian rules that name teams are copied as they are. The texture is stored compressed (DXT1), so a painted blend can differ from its copy by a few colour levels.

The choice isn't kept between runs, so a mirror left on can't double the edits on the next map. `MapTool edit … --mirror <lr|tb|diag|anti|turn>` does the same from the command line.

### The tabs

- **Heights:**
  - Brushes take a radius (8–3000 units), strength and hardness. Full strength runs inside radius × hardness, then fades out to the radius.
  - **Flatten** pulls towards the height where the stroke starts. **Set height** pulls towards the value in the box.
  - **Noise** adds bumps of a chosen size that are fixed to the map, so going over an area again gives the same bumps.
  - The **sea-level preview** only changes the view. "Apply as height shift" turns it into a real edit.
  - **Whole map:** raise, lower or scale everything.
- **Ramp** (on the Heights tab, `M`):
  - **Placing:** click the map to place a ramp; it shows as a rectangle with an arrow from its start to its end.
    - Drag the middle to move it.
    - Drag a round end handle to stretch and turn it (the other end stays put).
    - Drag the side handle to widen it.
    - The panel has the same settings as numbers.
  - **Heights:** each end follows the ground where it meets it ("ground" ticked), or a height you type.
    - The ramp runs in a straight line between the two heights. The panel shows how much it rises or falls, and how steeply.
  - **Who can go up it:** under the slope, the panel lists BAR's unit groups with a tick or a cross. Each group has its own climbing limit: most vehicles 27°, armthor 33°, hovers 33° on land (any slope on water), bots, commanders and amphibious units 54°, spiders any slope.
    - For a group that can climb it, the panel shows the speed going up. For one that can't, it gives the length the ramp would need at the same rise.
    - **Where it meets the ground:** this is checked too, before you apply. An end that isn't on ground at its own height leaves a step, and an end against a cliff runs into it. Either can block a group even when the ramp itself is gentle enough, and the panel names which end.
      - Across each end, a unit can pick its way between single rough cells. So a crossing only counts as blocked if every cell across it is too steep.
    - "Only just" means it's within 1° of the limit, where uneven ends could tip it.
    - It notes when the low end is too deep for land units (most stop at 20 under water), and when the ramp is narrower than the widest units, since the engine checks a unit's whole footprint.
    - The limits come from BAR's `gamedata/movedefs.lua` and the engine's own slope rule (see *Notes on the map format*).
  - **Edge:** the ramp blends into the ground all round over its edge.
    - *Auto* makes the edge as wide as the biggest step between the ramp and the ground beside it, so the sides average about 45° instead of a wall.
  - **Look:**
    - **Rock to dirt** (the default), as ramps are usually painted: rock on the upper ramp, turning to sand or dirt lower down, and blending into the ground at its foot. The steep side banks turn to rock as well.
      - Rock and dirt take their colour from places on the map. Rock comes from a cliff near the top, dirt from gentler ground near the foot. Brown and sand-coloured rings mark them; drag a ring to take its colour from somewhere else, and *Auto rock, dirt* goes back to the editor's choice.
      - Only the colour is taken. The detail is the ground's own grain, so nothing is tiled or repeated.
      - With no cliff nearby, or on a level ramp, it falls back to *From the ends* and says so.
    - **From the ends** copies the ground beyond each end along the ramp, blending from one to the other.
    - **Leave texture** changes heights only.
    - Put each end on the ground the ramp should join (a plateau top, the valley floor).
  - **Rough sides** (0–100%) breaks up the side banks like broken rock. The ramp surface and its ends stay smooth for units, and the who-can-go-up check measures the rough version.
  - **Bending** (to wrap a ramp round the edge of a cliff, or zigzag up a slope):
    - `Shift`+click adds a point beyond the nearer end; that end becomes a bend. `Ctrl`+click adds a bend on the line between two points, where it changes nothing until you drag it out. `Ctrl`+click on a bend removes it. Drag any point (the ends included) to move it.
    - The ramp runs straight from point to point and rounds each bend with an arc. Each arc is as large as the runs on either side allow: a run between two bends is shared so both get the same radius, and a run to an end belongs to its bend. The line never leaves the shape its points enclose. Points close together make the ramp follow the line they're drawn along.
    - It climbs evenly along its whole length and is level across, so the inside of a bend is steeper than its middle. The panel gives the tightest bend and how steep its inside is.
    - **Refused:** a bend tighter than half the ramp's width plus 8 (the inside would fold over), a bend that turns straight back (use two bends), points closer than 8 units, and two parts of the ramp nearer each other than its width plus 16. The outline turns red and the panel says why.
    - Where two parts of the ramp blend into the same ground (between the legs of a switchback, or where the run-on past one end comes near another part), the ground slopes smoothly from one to the other. It never steps.
    - Length and Direction follow the points, so their boxes are off. `Q` / `E` turn every point about the middle, **Swap ends** reverses them, and **Straighten** goes back to a straight ramp between the ends.
  - **Applying:** **Apply ramp** or `Enter` builds it as one undo step. The ramp stays on the map, so you can adjust it, undo, and apply again.
- **Texture:**
  - **Paint** blends towards a colour.
  - **Tint** recolours but keeps each pixel's brightness, so the texture's detail survives.
  - **Adjust** changes brightness, saturation and hue.
  - **Clone:** `Alt+click` a source, then paint. Later strokes stay aligned to the same offset.
  - **Splat** paints the four detail-texture weights. They are labelled with the map's own detail textures.
  - The minimap is redrawn under every edit.
- **Metal:**
  - Click a spot to select it, drag it to move it.
  - A ring shows where it will land. It turns red where BAR would refuse or merge the spot (touching other metal, or the map's outer edge).
  - **Move the look with the spot** is on by default.
    - A short move shifts the look over smoothly.
    - A long move carries it over and fills the old place from matching ground nearby.
  - The list shows each spot's income for a T1 extractor and notes problems: too steep for an extractor, under water, touching the edge.
- **Items:**
  - Box-select, `Ctrl+click`, then drag the whole selection.
  - `Q` / `E` turn by 15°. Buildings turn by whole quarters.
  - **Place** adds a named feature. The list shows what the map defines; other names must exist in BAR. The asset browser picks one for you (see *Assets*).
  - **Selection** shows a preview of the selected item's model (drag it to turn it). Several items of one kind show that kind.
  - **Scatter** paints random placements, never closer together than the spacing.
  - **Grass:** 1 means grass, 2–254 sets the blade size.
  - Start positions are dragged in Select mode.
- **Colours:** sky, fog, sun and lighting, clouds, water.
  - Values the map doesn't set are shown in grey, at the engine's defaults.
  - Values the map's option scripts may change are marked *⚠ may be overridden*.

### Assets

Like Blender's asset browser: you see and choose assets along the bottom, and see and adjust the chosen one on the right.

- **The asset browser** runs along the bottom, under the map, in the bottom panel's *Assets* tab (the *Log* tab beside it holds the messages). Hide or show the bottom panel with View › Bottom panel (`F4`), and drag its top edge to resize it.
  - Thumbnails are grouped by category: Trees & plants, Rocks, Buildings & structures, Props & vehicles, Wrecks, Geothermal vents, Units, Other, and **Experimental** (see below).
  - *Show* picks one category, and the box next to it filters by name. ✓ marks what this map already has.
  - Click a thumbnail to see it in the **Asset** tab on the right. Double-click it to place it.
  - *Collect on open* (on by default) adds every map you open. *Collect map files…* adds maps without opening them; you can pick a whole folder's worth at once.
- **The Asset tab** (on the right) shows only what the chosen entry needs. For an asset:
  - A preview of its model. Drag to turn it; double-click to face it again.
  - What it is, where it came from, its model, and any notes.
  - **Add to this map** copies its files in, at the paths they had in their own map, as one undo step.
  - **Place** adds it if needed and switches to Items › Place with it chosen. Heading, random turn, scatter radius and spacing are set there.
  - **Category** moves it to another category, and the choice is kept. **Remove from library** takes it out; maps that already use it keep their copy. (Not for BAR's own entries, which aren't in your library.)
  - **New feature from it…** (an asset with a model) opens the new-feature tool with this model.
- **Experimental** in the browser lists the editor's own experimental tools, whatever *From* is set to:
  - **The structure types** (Door, Tunnel (collapses), Open tunnel, Oil pump, Guardian tower, Shield platform), each shown by its built model. Chosen, the Asset tab shows a preview and a **New kind** section with only that type's fields; *Create kind* makes one.
  - **The open map's structure kinds**, each with how many are placed. Chosen, the Asset tab shows how to place it (**Place it**, and *Start destroyed* for a door or tunnel); double-clicking one does the same. A new kind is chosen here as soon as it is made.
  - **New feature from a model** (below). Double-clicking a type or this tool places nothing.
  - **The open map's new features** made with that tool, each shown by its model, with how many are placed. Chosen, the Asset tab shows what it was made from, its settings and its model, and **Place it**; double-clicking one does the same. A new feature is chosen here as soon as it is made. They are found by the first line the editor writes in `features/<name>.lua`, so ones made in earlier sessions are listed too (a structure kind's parts, written the same way, aren't).
- **New feature from a model** (Experimental) makes a new feature that uses a library model, with gameplay settings of your own. Example: a rock that blocks a choke point and takes minutes to reclaim.
  - **Model:** any feature in your library that has a model. It starts as the last asset with a model you chose in the browser.
  - **Name:** lower-case letters, digits and `_`.
  - **Start from:** the model's own settings, or *Ancient monolith choke*, your example with the two keys that do nothing left out (see below).
  - **Health:** hit points. The note works out the D-gun hits: a commander's D-gun does 99,999 a hit in BAR, a shot can hit more than once, and BAR's gadgets may change damage.
  - **Indestructible:** nothing can damage it.
  - **Reclaimable, Reclaim time, Auto-reclaimed:** one builder needs *reclaim time ÷ its build power* seconds; a commander has 300. *Auto-reclaimed* means area reclaim and patrolling builders take it on their own.
  - **Metal, Energy:** what reclaiming it gives.
  - **Blocking, Flammable.**
  - **Footprint:** squares of 16 game units. Units path round the footprint, not the model, so a choke needs a footprint as wide as the gap.
  - **Create** writes `features/<name>.lua` as one undo step, lists it under Experimental, then picks it in Items › Place.
    - If the map doesn't have the model, the model and the files it needs (its textures, normal map) come from the library. The original's definition doesn't, so only the new feature is added.
    - Only keys the engine reads are written, every one of them. `nodgun` and `sightDistance` aren't offered: neither the engine nor BAR reads them for features, so they would do nothing.
  - **Build on it** (No / Yes, units can cross it / Yes, units can't cross it) makes a feature buildings can go on.
    - It needs the map's structures gadget, which Create adds (see the next item).
    - On the last choice, units can't cross it, and factories can't be built on it, since what they made couldn't move off.
- **Structures** (Experimental) are doors, tunnels, oil pumps, guardian towers and shield platforms made out of features, worked by a gadget the editor adds to the map (`LuaGaia/Gadgets/map_structures.lua`, with its data in `mapconfig/map_structures.lua`).
  - **Door:** two indestructible pillars brace a thin gate that looks like it drops into the ground to open: guide rails at its sides, stiffening bars and two hydraulic rams on one face. The gate is the part that is destroyed. It blocks, and nothing can be built in its gap.
    - **Destroyed,** a built door leaves a broken gate in its gap until it is repaired: torn stubs of the gate in its rails, plates of it fallen along the walls, the rams snapped and knocked aside. The middle of the gap is clear, so units look as if they can walk or drive through, and none of it blocks. A built tunnel leaves broken slabs (and lumps of its rock) along its walls the same way.
  - **Tunnel (collapses):** two indestructible walls with a block between them that looks like a sealed tunnel mouth.
    - With a **flat roof**, buildings (not factories) go on the roof: the gadget lifts each one onto it as it is begun. A building must stand wholly on the roof, clear of those already there. Units can't cross it.
    - With a **rock roof**, nothing is built on it: it blocks as a door does. Its rock sits on a concrete vault, as the open tunnel's does, with the arch closed by the sealed mouth's shutter.
    - When it is destroyed, everything on it is destroyed too, except aircraft flying over.
  - **Open tunnel:** units pass under its roof, the walls still block, and nothing can be built in the middle. It can't collapse, so there's nothing to repair. Its rock roof sits on a concrete vault springing from the walls, so it looks engineered; a flat roof is a bridge slab.
  - **Repairing a broken door or tunnel:**
    - Builders repair by **reclaiming** what is left of it in the gap, or any of its **repair markers** while they're red.
    - All of these feed one shared pool, and the repair costs metal and energy as it goes. Their health shows the progress.
    - When it's full and the gap is clear, the door or top comes back.
  - **Repair markers** (built kinds) are optional, and placed by you:
    - In Items › Select, click the structure, press **Place repair markers**, and click where each goes.
    - They're always on the map, lit green while the structure stands and red while it is destroyed.
    - Click one to pick it, drag it to move it, Delete removes it.
    - Without any, the structure is repaired through what is left of it.
    - A kind made from assets puts two markers beside its walls when destroyed instead.
    - A built kind made by an earlier editor (before placed markers) keeps its two markers beside its walls until you pick one of its structures and press **Rebuild**.
  - **Oil pump** (type **Oil pump**): an old pumpjack, 64 by 96 game units.
    - It starts the game derelict. Builders repair it by reclaiming the derelict, paying the repair's metal and energy as they go.
    - The team that finishes the repair owns it: it gets the pump's metal and energy every second while it stands, shown as income in BAR's top bar. The pump wears that team's colour, and its beam nods.
    - Destroyed, it's derelict again, and whoever repairs it next owns it.
    - Set what it pays (metal and energy a second), its health and its repair's work and cost when you create the kind.
    - **In game:**
      - The beam is a separate feature held in the air at the post's bearing, and the gadget tips it up and down every frame. Features can't be animated any other way.
      - The pump is made again for its owner whenever it changes hands, since the engine doesn't recolour a feature given to another team.
  - **Guardian tower** (type **Guardian tower**): an indestructible tower, 3 by 3 squares and 146 tall, with four rocket pods pointing up. It looks futuristic, coloured like an ancient weapon: verdigris bronze and stone with gold bands and glowing teal runes. It also comes in Concrete and Metal, all the same shape.
    - **What it does:** it guards a path anywhere on the map (the path needn't touch it, so a few towers can cover a map). The path splits the map into three regions, and the tower has a rule for each:
      - **Crossing the path (the band):** within the **distance** of the path.
      - **Inside the path:** inside a closed path, beyond the band. An open path has no inside.
      - **Outside the path:** everywhere else on the map beyond the band, other towers' zones included.
      - Where they meet, the band wins. A path that crosses itself (a figure 8) has each loop inside and the rest outside.
    - **A rule:** which unit types it destroys there, of which **teams**, after what **delay**.
      - **Teams:** *All*, or any of BAR's lobby teams 1 to 8 (the battle room's "Team 1", "Team 2", …: alliances, not single players). *All* also takes in Scavengers and Raptors. Unticked teams are left alone there, so a team's own space can be safe for it.
      - **Delay:** how long a unit may stay in the region before it is doomed. 0 is at once; leaving the region starts the count again.
    - **Doomed** means destroyed the **kill time** later (1 second by default) wherever the unit goes next, healed, repaired or carried off. That holds however many come: each loses its share every 6 frames, so 500 bombers die as quickly as one.
    - **Example** (the demo map): crossing the ring, the 43 ground-attacking aircraft of every team, at once; inside the ring, every aircraft of teams 2 to 8 after 30 seconds; outside, nothing. Team 1's planes can stay inside the ring; anyone else's fighters and scouts get 30 seconds there; nobody's bombers get across.
    - **Fast planes and lag:** with no delay, the band is checked along the way each unit flew since the last check, so a fast plane can't slip across a narrow band between checks. Recoil never skips simulation frames when a game lags (it catches up), so no check is missed.
    - **Rockets:** while it is wearing anything down, the tower fires one rocket every **rocket** seconds (2 by default) from the pod facing it. The rocket goes straight up and bursts high above the tower. It's for show: the damage needs no rocket.
    - **Choosing units:** each rule's **Destroys › Choose…** lists every standard BAR unit with its picture (Armada, Cortex, Legion: 681), grouped by faction and kind, with a filter. Tick to destroy, untick to let pass; each one's Scavengers variant goes with it.
      - The first preset ticks the 43 aircraft that can attack the ground: bombers, gunships, drones, torpedo planes and the armed transports (Abductor, Stronghold). Fighters, scouts, transports and constructors pass, so king-of-the-hill games can have air without the air-rush ending.
      - Other presets tick all aircraft, everything or nothing the filter shows, or nothing.
      - A unit carried by a transport is judged by the transport; units still being built are left alone.
    - **The path:** place the tower, then in Items › Select click it:
      - `Shift`+click adds a point at the end of its path (the first two make it).
      - `Ctrl`+click on a point removes it, and on a run puts one in.
      - Drag a point to move it.
      - **Distance** (16 to 2048, either side) and **Closed loop** are set on the right, and **Clear the path** removes it.
      - Each change is one undo step.
    - **Kind and tower:**
      - The look, kill time and rocket timing belong to the kind (**Apply**).
      - The path and the three rules belong to each tower. So a tower by Team 1's base and one by Team 2's can protect their own sides with the same kind.
      - A new tower starts with the rules of the kind's last placed tower. The first tower of a kind destroys the kind's **Crossing** list (set in the Asset tab) in its band, of every team, at once.
      - Each rule change (a team box, the delay, the units) is one undo step. The 2D view draws every zone's edge and path.
    - **Checks:** saving warns if a guardian destroys commanders (naming the region), if a start position is in a band that dooms at once, or if an outside rule acts on every team at once (that is the whole map).
  - **Shield platform** (type **Shield platform**): a low steel pad units drive over (6 by 6 squares to start with: a titan fits on it), with a ring of glowing emitters round a lens, four short projector posts at its corners, and stripes in its owner's colour.
    - **Owning it:** it starts the game derelict. Builders repair it by reclaiming the derelict, paying the repair's metal and energy as they go, and the team that finishes owns it. Destroyed, it's derelict again for whoever repairs it next.
    - **Shields:** a unit of the owner's or an ally's that drives off the platform gets a bubble shield.
      - Its strength is **Shield** percent of the unit's maximum health (50 to start with).
      - The unit's team pays **Price** energy for each metal the unit costs (10 to start with: a unit costing 100 metal pays 1,000 energy).
      - Driving over again tops a damaged shield up, paying only for the part that's missing. A team that can't pay is charged nothing, keeps what it had, and is told.
      - Aircraft, buildings, units still being built and units being carried get none.
    - **The bubble** surrounds its unit by its size: the larger of the unit's radius and 0.6 of its height, plus 15%, times **Bubble size**.
      - It takes hits for its unit and for allies standing within it, so a tall titan's bubble covers the grunts at its feet.
      - It doesn't stop a shot fired from inside it.
      - When nothing is left it's gone, until the unit drives off a platform again.
      - It's drawn as a glowing sphere round the unit, fainter as it weakens, for the units you can see. A flash shows where it takes a hit, and a burst where it breaks.
    - **How it works in game:** BAR's real shields are weapons built into its units, and a map can't give a unit a new weapon. So the gadget absorbs damage instead:
      - The hit lands, and the gadget puts back what the bubble takes before the engine checks whether the unit died. A hit the bubble takes whole kills nothing.
      - Shots aren't stopped in the air: splash still hits units outside bubbles, and a hit still pushes the unit.
      - Only weapons and debris are taken. Fire, acid and lava, collisions and EMP aren't.
      - A guardian tower's doom goes straight through a shield.
  - **Starting destroyed:** a door or tunnel can start the game broken, its gap open, what is left of it in the gap and its markers red, to be repaired in play. Tick **Start destroyed** under the kind's **Place it**, or untick **This one starts destroyed** for a picked one.
  - **Looks:**
    - **Concrete** and **metal** models are built by the editor for each kind, at its size, all textured from one atlas (`unittextures/map_structures_1.tga`, `_2.tga`).
    - Rock roofs take the colour of the map's own cliffs: the minimap where the ground is steepest, kept rock-like.
    - **Models from assets** uses library models: choose one each for the walls, the centre and the repair marker.
  - **Making a kind:**
    - Choose the type under Experimental in the browser, then a name (one is suggested), the look, the roof (tunnels) and the height in game units. Only the fields the type has are shown.
    - Set the gap, wall width and depth in squares of 16 game units, then the centre's health and the repair's work, metal and energy.
    - Built models start taller and thicker than a commander (52 tall, 28 across on a 48-unit footprint): 80 tall, walls and door 3 squares thick, a 4-square gap a commander walks through.
    - Press **Create kind**: one undo step, which writes the parts' models and definitions, the config and the gadget.
  - **Placing one:**
    - Choose the kind under Experimental and press **Place it** (or double-click it), then click the map. The Heading box (snapped to a quarter turn; never random for these) is the way the passage runs, and structures sit on a 16-unit grid.
    - **Moving and turning:** in Items › Select, drag a placed one to move it. Q / E, the turn buttons or Heading turn it a quarter at a time. Each is one undo step, and anything that would leave the map is refused.
    - **Quarter turns only:** the engine turns a feature's blocked footprint only by quarters, so doors and tunnels align with it.
    - Each part stands at its own ground height, so place structures on level ground: the editor says when the ground under one rises and falls by more than 8.
  - **Changing one:** in Items › Select, click a structure. **Door or tunnel** on the right shows its kind's look, roof, height, gap, wall width and depth, and the preview shows the whole of it. **Apply** changes the kind, so every structure of that kind changes, and builds its models again (one undo step). Delete removes the structure.
  - **How it works in game:**
    - Builders can work on a plain feature only by reclaiming it, so the gadget turns reclaiming a marker into repairing. There is no nano spray while repairing.
    - A tunnel top is taken off the blocking map so buildings can go on it, and the ground under it is given a terrain type with no speed (one the map doesn't use) so units can't cross it.
    - **A map whose type map uses all 256 terrain types** (All That Glitters' is a picture) has one freed:
      - It's the least used of the types that behave exactly as another does.
      - At game start, the gadget gives its squares a type that behaves the same, one mapinfo doesn't define (so map options leave it alone).
      - Nothing changes in play.
    - Buildings on a roof are held there with MoveCtrl. Lifted, a building no longer blocks the ground, so the gadget keeps buildings on a roof apart itself. Builders still reach them: build range is measured flat.
    - Built parts are drawn by BAR's PBR model shader (`customparams.cuspbr`), so metal looks like metal.
    - Every engine call the gadget makes was checked in Recoil's source, and it was run against a stand-in for the engine. It **hasn't been run in BAR**: see `out/step26-structures/CHECKLIST.md`.
- **What an asset is:** a feature definition file (`features/…lua`) with everything it needs: its models, their textures, files it names (such as normal maps), and the wrecks it leaves.
  - One file often makes a whole set (FolsomDamR has 72 pines in one file). Each feature gets its own thumbnail, but adding one brings the whole set.
- **Reading definition files:** they are run as the game runs them, so features built in loops (`"ad0_senegal_" .. i`) get their real names.
  - They run in a sandbox: plain Lua with a few safe functions, no file or system access, and stopped if they run too long or grow too big.
  - A file that calls the engine (`Spring.…`, `VFS.…`) can't run there. Its names are read from its text instead, and the library says so.
- **Opening a map again adds nothing.** Assets are compared by the content of their files.
  - Two maps with different files for the same feature are kept as variants, marked `*`. A map can hold only one of them.
- **Names a map uses but doesn't define** (BAR's own rocks and wrecks, the engine's trees and vent) are placed by name, and nothing is copied: BAR supplies them in game.
- **BAR's own units, buildings and features** are in the browser too, whether or not a map you collected uses them: BAR's 681 units and buildings (Armada, Cortex, Legion: the unit list a guardian tower's targets come from) and the 1,430 features BAR keeps (every wreck and heap, its rocks, crystals, raptor eggs, tombstones, candy canes, and the fir trees it draws for the engine's trees).
  - **From:** *Maps and BAR* (the default), *Maps* (what the maps you collected use) or *BAR* (BAR's own only). A name a map has used is one entry, with where it was seen.
  - **Pictures:** units and buildings show their build pictures, as in BAR's menus; features show BAR's models. The **filter** matches descriptions too: *Pawn* finds `armpw`.
  - **Chosen,** one shows its details (what BAR calls it, its faction, its model and its author) and BAR's model in the preview. They aren't kept in your library, so *Move* and *Remove* are off for them.
  - **Placed** (double-click, or *Place*): a feature by name, as any; a unit or building in Items › Place as a unit or building, written to the map's featureplacer (`unitlist` or `buildinglist`). The map's own featureplacer gadget (`LuaGaia/Gadgets/FP_featureplacer.lua`) makes them in game. The seven maps tried carry one of two versions of it, and both make them Gaia's, neutral, always visible and blocking (checked 2026-10-16). Units turn to any heading, buildings face one of four ways. A map without a featureplacer can't place them.
  - **A building** is a unit that doesn't move or fly (its unit file gives it no speed), so nano turrets are buildings: 302 of the 681.
  - **Items › Place** checks a name against BAR: a unit or feature BAR doesn't have (`armasp`, the removed air repair pad) is flagged as missing in game.
  - **The 3D view** draws placed units and buildings with BAR's models too.
  - **The engine's `treetype0`–`15`** are drawn as the fir trees BAR draws for them (BAR's `enginetrees_override.lua`: treetype *n* is fir *n* mod 4, smallest to largest).
  - **A name BAR doesn't define** shows a *not in BAR* tile: placed on a map, it won't appear in game (for example `armasp_dead`: BAR removed the air repair pad).
  - **The engine's `geovent`** shows a *vent* tile: it has no model in game either.
  - **Where they come from:** BAR's repository at commit 9d7bdf3 (15 October 2026, the unit list's), read by `tools/MapResearch barfeatures`, and shipped in `BarFeatures\` beside the editor (about 175 MB: 1,434 models and 52 textures). It runs BAR's `features/*.lua` as BAR's loader does, names unit wrecks as BAR does (`<unit>_dead`, `<unit>_heap`), and reads each unit's model from its unit file. Textures are cut to one mip level: 1,024 pixels for the factions' shared atlases, 512 for the rest. `BarFeatures\CREDITS.txt` credits the models' authors; the fir trees are Beherith's and 0 A.D.'s (CC BY-SA 3.0).
- **Categories** follow BAR's own rules for trees and wrecks first, then the definition's own category, then its name and description. Industrial Revolution's crates say "corpses" (copied from a wreck), so a category alone doesn't make something a wreck.

### Beautify

**View › Beautify panel** (or **Beautify** on the toolbar) opens a tab beside the tools that makes the map look natural in one go, by its terrain. It is for the finishing touches that usually take a world editor and a render: blending rock into grass, grass into dirt, dirt into sand at the water.

- **Beautify the map** roughens the cliffs, then paints the texture, as one undo step. Each half has its own button too.
- **Colours:** *From this map* reads the map's own rock (the greyest common colour on its steepest ground) and open ground (the commonest on its level ground), and works out dirt, sand, mud and seabed from them. Or pick a landscape: *Temperate*, *Desert*, *Volcanic*, *Snow and ice*. Every colour can be changed with its swatch.
- **Strength** scales everything: lower keeps more of the map's own texture. **Made-up detail** adds grain and tone where the texture is plain (a freshly painted map); texture with grain of its own gets less. **Wandering edges** is how much the borders between materials wander, as on real ground.
- **Texture** (Paint the texture):
  - **Cliff rock** from one slope to another, with layers that follow the height (*Rock layers*).
  - **Dirt and scree** on middle slopes, and heaped at the feet of cliffs.
  - **Shore sand** along the water up to *Sand height*, a **wet edge** of mud at the waterline, and a darker **seabed** deepening with the depth (only on maps with water).
  - **Open ground:** a tint for level ground, off unless set.
  - **Shade hollows** darkens cracks, gullies and cliff feet and lightens ridges a little.
  - **Soften hard edges** blends sharp edges already painted (roads, concrete plates) with dirt; off unless set.
  - Each material is mixed into the texture that is there and keeps its grain. Metal spots (their look) and structures are left alone.
- **Cliff shape** (Roughen the cliffs): broken rock, **ledges** that follow the height, and **gullies** running down, on ground steeper than *Cliffs from*; and, if set, a gentle **swell** on nearly level ground (under 8°, so slopes and ramps keep their shape). It never changes where units or buildings go:
  - every 16-unit cell's slope stays on its side of each BAR movement class's limit (vehicles 27°, heavy tanks and hovercraft 33°, bots 54°), and every square stays on its side of the water and of each depth limit, as the engine works them out;
  - ground flat enough for BAR's flattest buildings (solar collectors and fusion reactors, `maxslope` 10, 96 units across), its labs (15) and its metal extractors (30) stays flat enough: the engine lets each footprint square stand at most 40 × tan(maxslope) from the building's height;
  - where a change would cross one of these, it is halved there, and in the end left out. Values sitting right at a limit aren't touched at all, so storing the heights in 16 bits on saving can't tip them. Metal spots, structures and start positions are left alone, and heights stay within the map's own range.
- With **Symmetry** on, the noise is read alike on both halves, so a symmetric map stays symmetric.
- **Layers:** *Export layer…* and *Import layer…*, the same as the File menu's (see below).

On a finished map like Acidic Quarry, *From this map* at the default strength keeps the map's character and adds depth; on a freshly painted map, a landscape at full strength does most of the work. It takes a few seconds; Ctrl+Z takes it all back.

### World Machine

The **World Machine** group in the Beautify panel (and **File › World Machine**) trades the map with [World Machine](https://www.world-machine.com/) through an exchange folder, one per map (in *Documents\BAR Map Editor\World Machine* unless you choose another; *Open* shows it).

- **Send to World Machine** writes:
  - `heights.png`, the heightmap as 16-bit grey at its own size (and `heights.r16`, the same as RAW16). Black and white are the map's lowest and highest heights: World Machine has no heights below 0, so they become 0 to *range* metres there. One game unit is one metre.
  - `texture.png`, and masks to steer World Machine with: `mask_water.png`, `mask_slope.png`, `mask_cliffs.png`, and `mask_keep.png` (white where the editor keeps the map as it is: metal spots, structures, start positions).
  - `bar_map.json` (the sizes and the range, to map the results back) and `README.txt`, which says how to set World Machine up for this map: its extents in km, build resolution, the File Input's altitude range, and the outputs' names.
- **In World Machine**, the outputs are written into the same folder: `wm_heights.png` (a File Output, 16-bit PNG; RAW16 `.r16` and float `.r32` work too), `wm_texture.png` (a Bitmap Output), `wm_splat.png` (a Texture Weightmap device with four inputs, packed, into a Bitmap Output) and `wm_grass.png` (a mask).
- **Bring back** takes the ones ticked, as one undo step:
  - each is resampled to the map's size (heights bicubic, corners on corners);
  - heights are mapped by the range sent (*As sent*), or spread over the map's own range (*Stretch*);
  - heights are compared with the map's: an upside-down output is turned the right way up, and a height scale that doesn't match what was sent is reported, with what to set;
  - the splat map's red, green, blue and alpha become detail textures 1 to 4; grass goes where the mask is at least half white.
- **Keep where units and buildings go** (on unless unticked) holds back, by halves, any part of a height change that would alter where a BAR movement class can go or where a building fits, as cliff detail does, so a whole-map erosion comes back as far as it can without changing play. Metal spots, structures and start positions stay as they are, and their texture and splat looks with them. On Acidic Quarry, a realistic erosion test kept about a third of the change; the rest would have made cliffs climbable, moved the water's edge or tilted building ground.
- **Bring back as World Machine writes** watches the folder: each time World Machine writes an output (with "Save file every build" ticked on its File Output), it comes back by itself, once the file has stopped changing.
- **Build in World Machine** (World Machine Professional, whose automation scripts this uses): choose its program and the project once; the editor writes `build.xml` (load the project, build at the heightmap's resolution, write the outputs), starts World Machine with it, and brings the outputs back as they are written (it waits up to half an hour).
- **Edits made after sending** are replaced by World Machine's result where it is taken (it is built from what was sent): the editor says so when it brings heights back, and Ctrl+Z takes it back. Send again before working on the map in World Machine.
- **Check the set-up first:** wire World Machine's File Input straight to its File Output, build, and bring it back. The editor should say "it came back as it went"; if it reports a scale or an upside-down image, it says what to set.
- A texture built at the heightmap's resolution (769 for a 12×12 map) is soft once stretched to the map texture's size: build it larger in World Machine, or add Beautify's made-up detail after.

World Machine itself hasn't been run with this: its side follows its documentation (File Input and Output, Bitmap Output, the Texture Weightmap device, Professional's automation scripts), and the editor's side is tested with outputs made as World Machine is documented to make them.

### Import and export

**File › Export layer / Import layer** writes or reads a layer as PNG:

| Layer | PNG |
|---|---|
| heights | 16-bit grey. The height range is stored in the file and used on import. |
| texture | RGBA, full size |
| metal | 8-bit grey, raw values |
| grass | 8-bit grey |
| terrain types | 8-bit grey, one pixel a 16-unit square; each value a terrain type number (mapinfo's `terrainTypes`) |
| splat map, normal map | RGBA, in map orientation (north at the top) |

An import is one undo step, and it changes only what differs. Exporting and importing again changes nothing. The Beautify panel has the same menus under its *Layers* buttons.

### Saving and checks

Save As checks the map first:

| Level | What it covers | What happens |
|---|---|---|
| **Error** | items or start positions off the map | the save stops |
| **Warning** | unknown feature names, start positions under water, metal spots an extractor may not fit | you are asked first |
| **Note** | items under water, start positions near the edge, a wider height range | goes to the log (the bottom panel's *Log* tab; the newest line also shows in the status bar) |

A problem the map already had when you opened it is only a note, so maps with known quirks don't nag at every save.

Saving also:

- turns the normal map to follow the new slopes where the ground moved;
- redraws the minimap there.

### If the editor crashes

Every edit is recorded as it happens. When the editor next starts, it lists any map that was still open with edits since its last Save As:

- **Yes** reopens the map with those edits replayed. Undo works as before.
- **No** discards them.
- **Cancel** asks again next time.

Recovery needs the original archive, unchanged, in the same place.

## MapTool (command line)

`MapTool help` lists every command. The main ones:

```
MapTool info    <archive>                                   size, heights, metal, features, tile files
MapTool metal   <archive>                                   metal spots with T1 income
MapTool items   <archive> [--all]                           placed items (with refs), placeable names, grass
MapTool edit    <archive> <new-archive> <operation> ...     apply edits and save as a new archive ([--mirror lr|tb|diag|anti|turn]; @file: operations from a file)
MapTool export  <archive> <layer> <out.png>                 heights, texture, metal, grass, types, resource:<key>
MapTool worldmachine send <archive> <folder>                heights, texture, masks, bar_map.json, README.txt for World Machine
MapTool worldmachine bring <archive> <new-archive> <folder> [--as-is] [--stretch | --range lo,hi]   World Machine's outputs back
MapTool render  <archive> <out.png> [--layer ...]           draw the 2D view
MapTool render3d <archive> <out.png> [--camera x,z,yaw,pitch,distance] [--passability hovercraft|vehicles|bots] [--models]
MapTool install <archive> [<maps folder>]                   copy into BAR's maps folder, safely, with its lobby picture
MapTool assets collect <archive>... [--library <folder>]    add maps' assets to the library
MapTool assets list [--library <folder>]                    the library by category, with ids
MapTool assets preview <id> [<feature>] <out.png>           draw a library asset's model
MapTool preview <archive> <feature> <out.png>               draw the model of a feature a map defines
MapTool preview bar <name> <out.png>                        draw one of BAR's own (treetype0, rocks30_def_07, armfus_dead, armpw, armllt)
```

`MapTool edit … "asset-add:<id>"` copies a library asset into a map, using the ids from `assets list`. Doors and tunnels: `"structure-kind:<name>:<door|tunnel|open_tunnel>:look=concrete;height=80;roof=flat"` (or `"structure-kind:<name>:pump:mps=2;eps=25"` for an oil pump) (`look=metal`; `roof=cliff` for rock; plus `gap`, `wallwidth`, `depth`, `health`, `work`, `metal`, `energy`, `open`; or `look=assets;wall=<feature>;centre=<feature>;repair=<feature>`), `"structure-change:<name>:<settings>"`, `"structure-add:<kind>:<x>,<z>:<facing 0-3>[:broken]"`, `"structure-move:<x>,<z>:<new x>,<new z>"`, `"structure-turn:<x>,<z>:<facing 0-3>"`, `"structure-broken:<x>,<z>:<1|0>"`, `"structure-marker-add:<x>,<z>:<marker x>,<marker z>"`, `"structure-marker-move:<x>,<z>:<new x>,<new z>"`, `"structure-marker-remove:<x>,<z>"` and `"structure-remove:<x>,<z>"`. `"feature-custom:<name>:<base feature>:<settings>"` makes a new feature from another's model, for example `"feature-custom:ancient_monolith:agorm_rock2:damage=900000;reclaimtime=36000;autoreclaimable=0;metal=5000;footprintx=4;footprintz=4"` (add `--library <folder>` when the model comes from the library; `MapTool help` lists the settings).

Guardian towers from the command line:
- **A kind:** `"structure-kind:<name>:guardian:look=ancient;kill=1;rocket=2:<targets>"`. Looks are `ancient`, `concrete` or `metal`.
  - **Targets** are unit names separated by commas, `default` for the 43 aircraft that attack the ground, `aircraft` for every unit that flies (both from `BarUnits/catalog.json` beside MapTool), or `none`.
  - The kind's targets are what its first tower destroys crossing its path. `"structure-targets:<kind>:<targets>"` changes them for towers placed from then on; towers already placed keep their own rules.
- **A tower's path:** `"structure-zone:<x>,<z>:<distance>:<0|1>:<x,z;x,z;...>"`, where `1` closes the loop. `"structure-zone:<x>,<z>:none"` clears it.
- **A tower's rule for a region:** `"structure-rule:<x>,<z>:<band|inside|outside>:<delay seconds>:<teams>:<targets>"`. Teams are `all`, `none`, or lobby teams such as `2,3,4`.

For example, the demo in `out/step30-guardian`:

```powershell
.\MapTool.exe edit acidicquarry_5.17.sd7 acidicquarry_guardians.sd7 "set:version=5.17-guardians" "structure-kind:sentinel:guardian:look=ancient;kill=1;rocket=2:default" "structure-add:sentinel:3584,2048:0" "structure-zone:3584,2048:250:1:2560,2624;3584,2624;3584,3520;2560,3520" "structure-rule:3584,2048:inside:30:2,3,4,5,6,7,8:aircraft"
```

A shield platform from the command line: `"structure-kind:<name>:shield_pad:gap=6;depth=6;shield=50;epm=10;bubble=1"`, plus `health`, `work`, `metal` and `energy` for the platform and its repair. Then place it with `structure-add` as any structure.

A ramp from the command line: `"ramp:<x1>,<z1>:<x2>,<z2>:<width>:<h1>,<h2>:<edge>:<look>:<roughness>:<rock x>,<rock z>:<dirt x>,<dirt z>"`.
- **bends:** put them after the first end, each after a `;`: `"ramp:2210,2110;2600,2350;2630,2830;2360,3080:2160,2790:140:auto,auto:200:2:0.3:auto:auto"` wraps a ramp three-quarters of the way round Acidic Quarry's south mesa, from the acid floor onto its top.
- **auto:** heights, the edge, rock and dirt may be `auto`.
- **look:** `0` leaves the texture, `1` fills it from the ends, `2` is rock to dirt.
- **roughness:** from 0 to 1.

For example `"ramp:1380,900:2180,900:200:auto,auto:auto:2:0.6:auto:auto"` takes a ramp off the north-west plateau of Acidic Quarry into the acid.

Example: rename the map, move a metal spot with its look, raise a hill, move two rocks and paint a patch. Quote each operation, because PowerShell reads `;` as the end of a command:

```powershell
.\MapTool.exe edit acidicquarry_5.17.sd7 my_quarry_1.sd7 "set:name=My Quarry" "set:version=1" "metal-move:1000,872:1064,904:look" "height-raise:3072,3072:300:40" "items-move:40,0:f2;f7" "texture-paint:1150,800:60:200,180,140:0.5:0.7"
```

MapTool reports each change, then writes the new archive.

- **Many operations:** `@<file>` reads them from a text file, one a line (blank lines and lines starting `#` are skipped), so thousands of trees don't have to fit on a command line: `.\MapTool.exe edit in.sd7 out.sd7 @forest.txt --mirror tb`. Files and operations can be mixed, in order.
- **Removing a value:** `"unset:<path>"` takes a key and what it holds out of mapinfo.lua, for example `"unset:teams.15"` (a start position) or `"unset:custom.grassConfig.grassDistTGA"` (a grass texture that would override the map's own grass in BAR).

Positions are in game units, with x running east and z running south. Operations that pick a spot or an item take the nearest one within 64 units. `MapTool edit` runs the same checks as Save As and refuses to save on errors.

### Example: a map from a drawing

`out/step41-ravine/simmering_ravine_1.4.sd7` ("Simmering Ravine 1.4") is `mirrored_ravine_map_middle_walls_doors.svg` (a mirrored ravine with walls and doors on the middle path) built on All That Simmers with MapTool, in its textures and colours. `out/step41-ravine/CHECKLIST.md` has the layout, what was checked, pictures and what to try in game.

- **From the drawing** (a converter in `out/step41-ravine/converter/`, using MapCore): the heightmap (the SVG turned a quarter to fit the 14 x 20 map, Team A north, Team B south), a base texture cut from All That Simmers' own ground, and four operation files.
- **With MapTool** (`edit … @run_a.txt` to `@run_d.txt`, three of them with `--mirror tb`): All That Simmers' layout cleared (its 2,857 trees and plants, metal, starts), the heights imported, the roads graded with ramps, cliff detail, the texture painted by Beautify in All That Simmers' colours, dirt paths, foot tracks, bridges, ford and spot looks painted, the paths drawn in as one network (dirt; bitumen, concrete and cobbles in the valley, the cobbles running on down the switchbacks to the bridges' stone-flag decks; junctions blended) and the ford's weed beds, its detail maps made again, the door kind, the stone rampart either side of it (ramps), metal, vents, and its own trees with BAR's mossy rocks.
- **What it can't do by itself:** the editor doesn't read SVG, and Beautify doesn't remake a map's splat distribution, specular and normal maps; on a map rebuilt from scratch those still show the old layout, so the converter made them and MapTool imported them.

## Notes on the map format

What the editor relies on, checked against the RecoilEngine source and BAR's own code:

- **Archive:** `.sd7` (7-Zip) or `.sdz` (zip). `mapinfo.lua` must sit at the top level.
- **`mapinfo.lua`:**
  - The editor patches values in place. Comments, layout, encoding and the byte-order mark are kept, and a changed value is the only text that changes.
  - Its `smf.minheight` / `smf.maxheight` win over the SMF header's height range.
  - `smf.minimapTex`, `metalmapTex`, `typemapTex` and `grassmapTex` replace the SMF's own layers with image files. The editor then refuses to edit those layers and says why.
  - Values that `mapconfig/mapinfo/*.lua` may change (for example map options) are flagged, not applied.
- **SMF (`maps/*.smf`):**
  - Holds the heightmap, the metal map (16-unit cells), the type map, the grass map, the 1024² DXT1 minimap, the tile index and the SMF feature list.
  - Saving rebuilds the section offsets, so an unedited save is byte-identical.
  - Heights decode as `min + raw × (max − min) / 65536`. Edits within the range rewrite only the edited vertices. Going past it widens the range and requantises, moving untouched ground by less than half a step.
- **SMT (texture tiles):**
  - 32×32 DXT1 tiles with 4 mip levels, 680 bytes each. A map may spread them over several `.smt` files.
  - The editor finds tile files as the engine does: mapinfo's `smtFileName0..N` when there is one per file, otherwise the SMF's names. Each is looked for in the SMF's folder, then the archive root.
  - An edit re-encodes only the touched 4×4 blocks at every mip level. A tile shared by several places is copied before it is changed.
- **`.dds` resources** (normal map, splat map, specular, detail textures): **stored upside down**. The engine flips every map `.dds` when loading it. The editor flips them too, and exports them in map orientation.
- **Normal map:**
  - On maps where it is a bake of the terrain's slopes (as on Acidic Quarry), each pixel is rotated to follow a height edit, keeping its fine detail.
  - A detail-only normal map is left alone.
- **Splat map:** its R, G, B and A channels weight `splatDetailNormalTex1`–`4`.
- **Metal:**
  - A cell holds `raw × maxMetal`. A T1 extractor (BAR's armmex, `extractsmetal` 0.001) earns 0.001 × the sum over the cells inside `extractorRadius`.
  - BAR's spot finder skips the outermost ring of cells and merges touching cells into one spot. The editor refuses metal in that ring, and spots that would touch.
  - Metal from `mapconfig/map_metal_layout.lua` is used only when the SMF metal map is empty. Adding metal to such a map makes BAR ignore that file, and the editor warns first.
- **Slopes and units:**
  - The engine measures slope per 16-unit cell. It takes the steepest of the cell's eight triangle normals, blended towards their average, and uses 1 − its height component.
  - A cell blocks a unit class when that measure is above the class's limit, or when the cell is deeper than the class goes.
  - BAR writes each limit in degrees divided by 1.5, and the engine multiplies by 1.5 again, so the limit is 1 − cos(degrees).
  - Uphill speed is multiplied by 1 / (1 + slope × the class's slope modifier).
  - Hovers skip the slope check on any 8-unit square whose highest corner is under water.
- **Items:**
  - SMF features are placed on the ground with a 16-bit heading.
  - BAR runs the map's own featureplacer gadget (`mapconfig/featureplacer`). It reads `objectlist`, `unitlist` and `buildinglist`; buildings take a facing from 0 to 3.
  - Unknown feature names are silently skipped in game.

## Where things are kept

| What | Where |
|---|---|
| Open map's working copy | `%LOCALAPPDATA%\BarMapEditor\workspaces\<id>\`, deleted when the map is closed |
| Temporary work (MapTool's unpacked maps, map files being collected) | `%TEMP%\BarMapEditor\`, each folder deleted when its work is done. One left by a run that was stopped or crashed (its lock file beside it no longer held) is deleted the next time MapTool or the editor starts |
| Its crash-recovery record | `…\workspaces\<id>.session\` (journal, copies of imported PNGs, a lock), deleted with it |
| Settings (maps folder, copy-to-maps choice, asset browser) | `%LOCALAPPDATA%\BarMapEditor\settings.json` |
| Asset library | `%LOCALAPPDATA%\BarMapEditor\assets\`: `index.json`, each asset's files in `files\<id>\` at their map paths, thumbnails in `thumbs\<id>\` (BAR's own in `thumbs\bar-<commit>\`) |
| BAR's own units, buildings and features, with their models | `BarFeatures\` beside the editor (read-only; their build pictures in `BarUnits\`) |

## Known limits

- **Not yet tested in game.** The in-game test maps and their checklists are in `out/step26-structures`, `out/step30-guardian` and `out/step31-shield-platform`, with the bending-ramp map in `out/step29-bending-ramp` and the map made from a drawing in `out/step41-ravine`.
- **Who-can-climb uses BAR's movement classes as of 15 October 2026.** If BAR changes `movedefs.lua`, the editor's copy needs updating. It leaves out the map's terrain-type speed factors (`terrainTypes` in mapinfo, usually 1).
- **Ramps keep the old ground's fine lighting detail.** The normal map isn't filled along a ramp. Saving turns it to the new slope as for any height edit, so a ramp cut through a cliff keeps the cliff's rocky lighting detail under its new texture.
- **BAR destroys features under new buildings, and the structures gadget works around it.** BAR's *Footprint clearance* gadget (`luarules/gadgets/unit_footprint_clearance.lua`) destroys every feature under a newly placed building, except geothermal vents. Found in game on Simmering Ravine 1.0: wall pieces made with *Build on it* vanished when built on.
  - Since 0.1.1, the structures gadget moves its own features aside for the moment a building is created over them, and puts them back the same frame: tunnel roofs, every structure part, and features made with *Build on it*. BAR's game gadgets see a new building before a map's own, so the clearance finds nothing of them.
  - A map made with an earlier version still carries the old gadget. It is written again the next time a structure or a build-on feature is added or changed in the editor.
  - A building placed on a feature made with *Build on it* still stands on the ground inside it; only a tunnel's flat roof lifts buildings onto it.
- **Beautify paints the texture only.** The splat distribution (detail textures), specular map and normal map keep the old layout; after rebuilding a map's ground, make them again elsewhere and import them (`import:resource:<key>=`).
- **Mirrored cliff detail is nearly, not exactly, mirrored.** Where cliff detail is held back to keep access, the engine's slope triangles aren't symmetric, so a few rim heights differ (on Simmering Ravine: up to 18 units on 0.3% of the heights, all on cliff rims); where units go stays mirrored.
- **Painted shading doesn't move.** Many map textures have cliff shading painted in. Reshaping the ground doesn't reshape that painting; repaint it or import a texture.
- **Detail is not drawn in 3D.** The 3D view lights the ground as the engine does, but leaves out detail textures, specular highlights and water reflections.
- **Features in 3D:** they are lit like the ground, cast no shadows, stand upright even on slopes, and leave out team colour and normal maps.
- **Feature names:** the Place list shows the features the map itself defines. BAR's own trees and rocks must be typed by name, or picked in the asset browser once a map that uses them has been collected. An unknown name gives a missing feature in game.
- **BAR's own models are as of 15 October 2026** (commit 9d7bdf3): units, buildings and features BAR adds later aren't listed until `barfeatures` is run again. Their textures are smaller than BAR's (1,024 or 512 pixels), and the previews leave out team colour and BAR's shaders: units look darker than in game, and BAR's snowy rocks get their snow in its shader, so here they look grey. Two of the candy canes' textures are in a DDS format the editor can't read, and Legion's wrecks name a second texture (`led_wreck_shader.dds`) BAR doesn't have, so those are drawn without them. `armcomnew` names no model.
- **BAR's 2,000-odd entries:** the first time the browser shows them all, it draws their pictures in the background (those in view first, about 8 seconds for all of them); they're kept in the library's `thumbs\` after that. With everything shown and every picture in, the list fills in about a tenth of a second, and the pictures take about 45 MB of memory.
- **Units' headings depend on the map's featureplacer gadget:** the older version (five of the seven maps tried) turns a unit by minus its heading, the newer one (Ascendancy, Crimson Bay) by plus it. So a unit set to face east may face west in game on one of them. Buildings face the same way in both.
- **Previews:**
  - They draw S3O models only (a 3DO model shows a message instead).
  - They leave out team colour, normal maps and reflections.
  - BAR's own units, buildings and features are previewed from the models shipped in `BarFeatures\`.
- **Feature size:** BAR can't scale a single feature (the featureplacer and the SMF store only position and heading), so there is no size control. Many sets come in sizes instead (`_s`, `_m`, `_l`, `_xl`).
- **Doors and tunnels haven't been run in BAR.** Repairing is reclaiming, so builders show no nano spray.
  - Built models are simple shapes, painted procedurally. The door's gate doesn't move: destroyed, it is simply gone.
  - A building lifted onto a roof stays held there for the rest of the game.
  - A tunnel's no-cross ground works in 16-unit squares.
  - Structures can't be moved or turned once placed: remove one and place it again.
  - The map's own `0_apply_options.lua` changes terrain types for map options, but not the one the gadget uses.
- **Guardian towers haven't been run in BAR.** Every engine call was checked in Recoil's source and the gadget was run against a stand-in for the engine. Still open:
  - whether a rocket spawned without an owner flies as drawn;
  - whether BAR's weapon post-processing keeps the map's rocket weapon unchanged.
  - (The burst's textures, flare and explo, are BAR's: its gamedata/resources.lua lists them, checked 2026-10-16.)
  - **The unit list** is BAR's units on 15 October 2026 (commit 9d7bdf3). Units added since aren't in it, though names typed for MapTool still work. `tools/MapResearch barunits` reads it again.
  - **A unit in a transport** is never doomed (the transport is judged instead). For air targets that doesn't matter, since BAR's transports can't carry aircraft. But a transport not on the list can ferry listed ground units across a zone.
  - **The doom isn't kept across a reload** of the gadget (`/luarules reload`, a cheat): units already doomed then live, and delays start again.
  - **Teams are BAR's lobby teams:** the battle room labels ally team *n* as "Team *n*+1" (BYAR-Chobby's battle room, checked 2026-10-16). If a lobby leaves a team empty and the game numbers its alliances without the gap, the numbers after it shift down.
  - **Delays are counted in checks** every 6 frames (0.2 s): a unit that leaves and comes back between two checks is counted as never having left.
- **Shield platforms haven't been run in BAR.** Every engine call was checked in Recoil's source, and the gadget's game side and drawing side were both run against stand-ins for the engine.
  - **Absorbed hits still count in the stats:** the engine counts the damage dealt before the gadget puts it back, so BAR's damage statistics (and the attacker's experience) include hits a shield took.
  - **A reload drops every shield** (`/luarules reload`, a cheat).
  - **Lava and acid:** BAR's lava and acid gadget (`map_lava.lua`) deals its damage as the engine's water type (`Game.envDamageTypes.Water`, −5), which bubbles don't take (checked 2026-10-16). Damage another gadget deals without naming a weapon arrives as −1, the same as debris, and a bubble takes it.
  - **The bubbles are drawn with Lua's older OpenGL calls,** which Recoil still has (it can warn about them when its DeprecatedGLWarnLevel setting is raised).
- **Definition files that call the engine** can't be run in the sandbox. Their names are read from their text, which can miss names built by code.
- **Size and terrain types:** you can't resize a map or create one from scratch, and you can't edit the type map or terrain types.
- **Lua map options and gadgets** are not edited. Values they may override are only flagged.
- **Start positions:** teams can be moved but not added or removed. Start boxes (`map_startboxes.lua`) don't follow moved start positions.
- **Metal from images:** a map whose metal comes from `smf.metalmapTex` can't have its metal edited.
- **The minimap** is redrawn from the texture in the map's own colour style, not rendered in game. Water and lighting are approximated.
  - The style is fitted from the map as it was before the session's first texture edit. A minimap that isn't a picture of its texture (another layout's, a placeholder) is not followed on land: there the texture's own colours are used.
- **BAR's lobby** shows pictures only for the maps it ships pictures for. It never reads one from a map archive.
  - For any other map it reads `LuaMenu/Images/Minimaps/<name>_<version>.jpg` from BAR's data folder (spaces in the name become underscores).
  - Installing from the editor or with `MapTool install` writes that file: the map's minimap at the map's proportions, longer side 1024 pixels, as PNG data (the engine reads an image by its contents, whatever its extension).
  - A map copied into the maps folder by hand gets no picture. Each player's lobby reads its own data folder, so other players see the picture only if they installed the map this way too.
- **Recovery** replays onto the original archive, so if that file is moved or changed, the unsaved edits can't be recovered.

## Building from source

```
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Test -SelfTest
```

- **The build** compiles everything (`BarMapEditor.slnx`, no NuGet packages) and runs the tests in `tests/MapCore.Tests`. `-Filter Name` runs a subset.
- **`-SelfTest`** drives the editor through a whole session in a hidden window, and writes the results to `out/selftest/selftest.txt`.
- **Python cross-checks:** some tests compare against the independent Python parsers in `tools/`, so Python must be on `PATH`.
- **Test map:** the tests use `testdata/acidicquarry_5.17.sd7`, a copy of the map in the repository root. Neither is ever modified.

| Project | What it is |
|---|---|
| `src/MapCore` | File formats and every editing operation; no UI |
| `src/MapEditor` | The Windows editor, a thin shell over MapCore |
| `src/MapTool` | The command-line tool |
| `tests/MapCore.Tests` | Test runner (`MapCore.Tests.exe [filter]`) |
| `tools/` | Python reference parsers and `MapResearch` (the studies behind the normal-map and splat rules) |

`PLAN.md` records how each part was built and measured, step by step.
