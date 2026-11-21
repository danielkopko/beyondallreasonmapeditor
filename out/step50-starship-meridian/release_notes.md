**Starship Meridian 1.1** for [Beyond All Reason](https://www.beyondallreason.info/): a 24 × 28 map for 8v8.

**New in 1.1:**

- **8v8.** Sixteen starts, eight a side, all on the back line along the hull's wall: air in the weapons bay, two front starts in each hangar, tech by the power room's geothermal vent, and a front and a support start in the thruster bay. Each is marked on the floor with a circle in the side's colour, its role stencilled across it: AIR, FRONT, TECH or SUPPORT. The inboard rooms are no one's start: ground to expand into.

- **It's a metal map.** The whole ship is metal, so now the whole deck is extractable, with no spots: build extractors anywhere on the decks. The metal is set as on Cloud9: raw 79, `maxMetal` 7.5, extractor radius 24. This changes the economy completely from 1.0.
- **The middle is open.** Each team's passage is 1,000 wide (it was 600), and the doorways into the reactor, the shield generator and the engine room are 1,150 to 1,500 wide, as are the doors between the shared rooms. The CIC's doors stay 700, below the bridge ramps, and the back line keeps its walls.
- **Thruster platforms at the stern.** Each of the three engine housings carries a platform at 440, above every inner wall and with no wall over it. A ramp rises to it out of the room below, with a step part way up: one for each team in its thruster bay, and a shared one in the engine room. The engines hang below them. With the bridge at the bow, they're high ground for long-range guns: every part of the ship is within a Starfall's range of them or the bridge.
- **An outer walk.** A narrow path runs round the whole hull's edge, level with the bridge, with the bow's upper front ahead of it. Climb onto it from the thruster steps, by a narrow path from each step's sides round the platform's front corners, or from the bridge through its new front opening. From it you look down on every room, the enemy's back line too, but there's no way down into them. Its only ways off are the steps, the bridge, and the enemy's thruster ramp: a long sixth way into the enemy's half.
- **The bridge's walls are lower:** a lip 30 over its deck, so guns there fire over them.
- **The hull's sides are a ship's side,** not a plain wall: plated and weathered, in a livery with a stripe in each side's colour, the ship's name along both sides and across the stern, its hull number at the bow, the ship's badge as nose art beside the name, lit ports and airlocks.
- **Energy crystals.** BAR's own crystals, in clusters as BAR's crystal maps lay them (5 to 14 crystals of one colour). Two clusters edge every start's circle, one on each wall side (2,400 energy a start; 1,200 at the tech start, whose geothermal vent makes up the rest); each inboard room holds two (3,600) and each shared room two larger teal ones (4,800); none in the reactor. 32,400 energy in each team's rooms, 19,200 in the middle.
- **Bare decks.** The props, craft and machines are gone; only the reactor's core (3,000 energy), the drive core (250 energy) and the main engines remain. Energy crystals take their place (below).
- **The walls beside the bridge ramps are raised.** On 1.0 a vehicle or bot could drive off a ramp onto the wall tops beside it, and from there over the ship's bulkheads. Now no ground unit but spiders gets onto a wall, and a check proves it.

A battle-carrier adrift in space, boarded from both sides at once. Port fights starboard for the ship's centreline: the bridge, the command room, the reactor, the shield generator and the engine room.

- **The ship fills the map,** its bow north and its engines at the stern. Around it is space: the engine draws a starfield there instead of ground. The hull's edge walls it off from every ground unit but spiders, and 98% of it is too steep to build on.
- **The two halves are mirror images** across the centreline, rooms, metal and features alike. Port trim is red and starboard green, a ship's navigation colours.
- **Each team's half** is two columns of rooms on one deck, walled by bulkheads, beside its own passage, 1,000 wide, that runs the length of the ship:
  - **outboard, the starts:** the weapons bay (air), the forward hangar (two), the power room (tech, with the team's own geothermal vent), the main hangar (two) and the thruster bay (two, with the ramp up to the team's thruster platform);
  - **inboard, no starts:** the crew deck, the armory, the cargo hold and the machine shop, facing the centreline.
- **The centreline** is shared, and each room on it is a way across:
  - **the bridge,** raised high at the bow behind a low lip, with a ramp up from each side's passage, a front opening onto the upper front, and a grand stair down into the command room that only bots take;
  - **the command room (CIC)**, its deck a lit grid;
  - **the main reactor,** its core glowing in a sunken well, with a ramp down from each side's door and two geothermal vents on the well's floor;
  - **the shield generator;**
  - **the engine room,** the drive core on its grating, and the ramp up to the shared centre thruster platform.
  - Spine doors 1,400 wide join the shared rooms bow to stern, so a team holding one can push along the centreline.
- **Walls are terrain at about 70°.** No vehicle, bot or hover climbs them, and they stop direct fire. All-terrain spiders climb them, so they can flank over the bulkheads. Every door is at least 500 wide.
- **Metal: a metal map.** Every deck floor is metal, 113 M square units of it, the outer walk included; the walls, the hull's solid tops and space have none.
  - Each team's rooms hold 39.2 M square units, and the shared rooms on the centreline 26.4 M.
  - An extractor anywhere on deck takes the same as on Cloud9: the same metal per cell, `maxMetal` and extractor radius.
  - There are 4 geothermal vents: one in each power room, two on the reactor well's floor.
- **No wind in space** and no tide, so energy comes from solar, geothermal, fusion and the reactor's core.
- The ship, the reactor's core, the drive core and the main engines were modelled and painted for this map. The minimap and lobby picture are the ship seen straight down in 3D, on the starfield.
- The gadgets are from All That Simmers by Nikuksis.

**Install:** put `starship_meridian_1.1.sd7` in BAR's `maps` folder (usually `%LOCALAPPDATA%\Programs\Beyond-All-Reason\data\maps`). To see its picture on the lobby's setup screen, put `Starship_Meridian_1.1.jpg` in `data\LuaMenu\Images\Minimaps` and restart the lobby.

**Not yet tried in a game.** It is the first of these maps whose space isn't drawn (`voidGround`): a transport could drop units onto the invisible ground there, where they would stand stuck. The map has no start boxes.
