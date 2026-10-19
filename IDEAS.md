# Map feature ideas for BAR

Ideas for enriching a Beyond All Reason map: 50 map features, then five auxiliary structures for each faction.

Every premise was checked on 2026-10-16 against the source of the Recoil engine (`beyond-all-reason/RecoilEngine`, master) and of BAR (`beyond-all-reason/Beyond-All-Reason`, master). The check is named under each idea. **None has been tried in a game.**

## How a map does these things

Every idea uses one or more of six routes, each confirmed in the source.

| Route | What it is | Confirmed in |
|---|---|---|
| **mapinfo** | Settings the engine reads from the map's `mapinfo.lua`: gravity, ground hardness, `notDeformable`, tidal strength, the wind range, `voidWater` and `voidGround`, water damage, fog, sky, sun, and sound reverb and pass filter. Each terrain type also gets its own hardness, tracks, and speeds for tanks, bots, hovers and ships. | Recoil `rts/Map/MapInfo.cpp` |
| **Lava config** | A map can ship `mapconfig/lava.lua`. BAR's lava module prefers it to BAR's own config for that map, unless BAR's sets `overrideMap`. It sets the level, a rise-and-fall rhythm, damage (to features too), look and sounds. Acidic Quarry's acid already runs on BAR's lava system. | BAR `modules/lava.lua` (`MAP_CONFIG_PATH = "mapconfig/lava.lua"`), `common/configs/LavaMaps/AcidicQuarry.lua` |
| **Map gadget** (LuaGaia) | The map's own Lua (`LuaGaia/main.lua` and `draw.lua`), loaded when the map contains it. It has full synced control: it can create and transfer units and features, pay resources, reshape the ground, push and move units, spawn effects and explosions, and set wind and tides. It also receives the control events (`Allow…`, `Explosion`). Its drawing half can change the sun, atmosphere, sky, water, decals and lights, and play sounds. The doors, tunnels and oil pumps already work this way. | Recoil `rts/Lua/LuaGaia.cpp`, `LuaSyncedCtrl.cpp`, `LuaUnsyncedCtrl.cpp`, `LuaMetalMap.cpp`, `rts/System/Events.def`, `EventHandler.cpp` |
| **BAR map script** | A map can ship `luarules/configs/Atmosphereconfigs/<map name>.lua`. BAR's atmosphere gadget runs it inside BAR's own synced game Lua, as part of that gadget, so it can handle any gadget event. It can also reach BAR's own interfaces (`GG.UnitAttributes`, `GG.BuildBlocking`) and BAR's effects. Ready-made helpers spawn effects with damage, stun and sound, change sun and fog, and post messages. Acidic Quarry's green fog, fireflies and acid rain are such a script. | BAR `luarules/gadgets/map_atmosphere_cegs.lua` (included in the synced half); option `map_atmosphere` is on by default (`modoptions.lua`) |
| **Map definitions** | A map can add its own features, unit types (BAR loads the map's first) and effects (`effects/*.lua`). The engine's definitions parser can read the game, the map and the base content (`SPRING_VFS_ZIP`), and the effects loader lists `effects/` with those defaults. | Recoil `rts/Game/Game.cpp` (`LuaParser baseDefsParser("gamedata/defs.lua", SPRING_VFS_MOD_BASE, SPRING_VFS_ZIP, …)`), `rts/Lua/LuaParser.cpp` (`DirList`), `cont/base/springcontent/gamedata/explosions.lua`; BAR `gamedata/unitdefs.lua` (checked in Step 26) |
| **BAR's own units** | Units BAR already defines, which a map can create for Gaia or for a team: critters (ant, crab, duck, goldfish, gull, penguin), loot boxes, the meteor, the volcano projectile, the assist drones, and every faction unit. | BAR `units/other/`, `units/legassistdrone.lua` |

### Rules that shape the list

- **Unit stats go through BAR.** Since September 2026, BAR's `GG.UnitAttributes` manages unit stats: sensor ranges, health, speed, turning, acceleration, build speed, costs, stealth, cloak, shield power, weapon range, reload and damage. It rewrites an attribute whenever one of that unit's modifiers changes, and on transfer and experience. So a map that changed one directly would keep it only until BAR next rewrites it. So every stat effect below uses the BAR map script and `GG.UnitAttributes.SetUnitModifier` (or the weapon version). The map gadget and the map script share state through rules params (`SetGameRulesParam`, `SetUnitRulesParam`). Sources: BAR `api_unit_attributes.lua`, `include/unit_attributes.lua`, and `unit_builder_priority.lua`, which uses it for build speed.
- **Never change damage in `UnitPreDamaged`.** Each handler receives the original damage, and the last one to write wins (`ControlIterateDefFalse`). BAR's attribute system writes it too. Protective ideas below repair or shield instead.
- **Vetoes always stand.** For `Allow…` events, every handler must agree (`ControlIterateDefTrue`), so a refusal from the map gadget holds.
- **Wind:** BAR blocks wind turbines at game start when the map's wind is too low (`api_build_blocking.lua`, reason `terrain_wind`). A map with storms has to keep its base wind above that.
- **Name matching:** BAR uses the first atmosphere script whose name occurs in the map's lower-cased name. Its own `acidicquarry.lua` would match any map named "AcidicQuarry …", so an edited map should be renamed without it. BAR matches with `string.find` and no plain flag, so the file name is read as a Lua pattern. A name containing `-` won't even match itself, and `( ) . % + * ? [ ^ $` misbehave too. Use letters, digits and spaces.
- **How sturdy the BAR map script route is:** it borrows BAR's atmosphere-config include to run gameplay logic, which is not what it was made for. Its stat effects rely on an interface added in September 2026, and an error in the map's script also stops BAR's own atmosphere effects. "Confirmed possible" here means in today's BAR; an update could change any of it. The map gadget route uses only the engine, so it is the steadier of the two.
- **Handing units over:** `Spring.TransferUnit(unit, team, false)` makes it a capture and returns whether it worked. A transfer needs every handler to agree. Of BAR's gadgets that check transfers, `game_no_share_to_enemy.lua` always lets Gaia give and allows any capture. `game_prevent_excessive_share.lua` blocks only a team at its unit limit, and never a capture. `unit_prevent_share_load.lua` always agrees. I didn't read all of BAR's roughly 300 gadgets, so if a transfer is refused, the map destroys the Gaia unit and creates a fresh one for the team (`CreateUnit`), as the faction structures do.
- **LuaGaia must be on.** The engine loads it unless the game's start script turns it off (`useLuaGaia`). The Step 26 checklist is its first test in BAR.

## 50 map features

### Ground and terrain

1. **Roads, mud and salt flats.** Painted ground types change speeds: roads let tanks and bots move 25% faster, mud slows tanks to 60%, and salt flats let hovers race while bots slog. The texture shows which is which.
   *Possible:* mapinfo `terrainTypes` with `moveSpeeds` (tank, kbot, hover, ship) for each type, in the type map (the editor already writes it).
2. **Wet season.** Every 15 minutes the marshes flood for three: tanks crawl and hovers race. A message warns everyone first.
   *Possible:* map gadget `Spring.SetTerrainTypeData(type, tanks, bots, hovers, ships)` changes a type's speeds mid-game. The warning uses the BAR map script's `AtmosSendMessage`.
3. **Hard and soft ground.** Bedrock barely dents under artillery while ash fields crater deeply, so long sieges dig trenches that slow attackers.
   *Possible:* mapinfo `maphardness` and per-type `hardness`.
4. **Collapsing causeway.** A raised road across the acid rests on two pylons. Destroy both and it sinks into the acid over ten seconds. Units still on it must get off or drown.
   *Possible:* the map gadget sees the pylons die (`UnitDestroyed` or `FeatureDestroyed`) and lowers the ground (`SetHeightMapFunc`). BAR then damages units stuck below their depth limit, 5% of their health a second (`unit_water_depth_damage.lua`).
5. **Raised causeway.** The reverse: a sunken road that rises when a team repairs a pump station, opening a new route.
   *Possible:* the repair pool used by doors and pumps (`AllowFeatureBuildStep`), then `SetHeightMapFunc`.
6. **Landslides.** Marked cliffs are unstable. Enough explosions on top bring the edge down into a steep scree slope, opening a way up and burying whatever was below under rocks.
   *Possible:* the `Explosion` event, `AdjustHeightMap`, `CreateFeature` for the rocks, and `AddUnitDamage`. The event fires only for weapon types the gadget registers with `Script.SetWatchExplosion(weaponDefID, true)` (Recoil `LuaHandle.cpp`); watching heavy weapons only keeps the cost down.
7. **Forbidden ground.** Unstable or sacred ground where nothing can be built but anything can drive: a crossing nobody can wall off.
   *Possible:* `Spring.SetSquareBuildingMask(x, z, 0)` for each 16-unit square. The engine allows building only where `(square & unit) == unit`. BAR's own build blocking is per unit type, so they don't clash.
8. **Glowing fissures.** Lava cracks that glow at night and pulse brighter before an eruption.
   *Possible:* glowing ground decals from the map gadget's drawing half (`CreateGroundDecal`, `SetGroundDecalGlowParams`; map code isn't barred from them).

### Water, acid and lava

9. **Acid tides.** The acid rises and falls on a set rhythm. Low tide exposes a seabed shortcut between bases; high tide floods it and the low expansions.
   *Possible:* lava config `tideRhythm`, rows of {height, speed, dwell}.
10. **Acid that eats wrecks.** Wrecks in the acid dissolve, so the metal from a sea battle must be reclaimed quickly.
    *Possible:* lava config `damageFeatures`, either true or a fraction per second.
11. **Tidal swing.** Tidal generators pay double at high tide and little at low, in time with the acid.
    *Possible:* map gadget `Spring.SetTidal(strength)`, which applies map-wide and live.
12. **Currents.** Rivers push ships and hovers downstream: quick to go down, slow to come back.
    *Possible:* the `UnitEnteredWater` and `UnitLeftWater` events, with `AddUnitImpulse` in the current's zones.
13. **Healing springs.** Clean pools where units standing in the water slowly repair, making a natural forward base.
    *Possible:* map gadget `SetUnitHealth` (a repair, not a damage change), plus an effect from `SpawnCEG`.
14. **Bottomless chasms.** Black void gaps where anything that falls in is gone.
    *Possible:* mapinfo `voidGround` and `voidWater` draw empty space; the map gadget destroys units below a set height (`DestroyUnit`). BAR's lava system leaves void-water maps alone.
15. **Breaking the dam.** Destroying a dam drops a farmland basin below the acid line for the rest of the game.
    *Possible:* `UnitDestroyed`, then `SetHeightMapFunc` lowers the basin under the acid level; BAR's drowning applies.

### Weather and sky

16. **Day and night.** A 20-minute cycle in which the sun reddens, sets and rises. At night every unit's sight drops by a quarter, so radar and scouts matter more.
    *Possible:* the BAR map script's `MapAtmosphereConfigSetSun` and `MapAtmosphereConfigSetFog`, with sight lowered through `GG.UnitAttributes` (`losRadius`).
17. **Storm fronts.** Every few minutes a storm drives the wind to its maximum for 90 seconds, so wind turbines pay far more.
    *Possible:* map gadget `Spring.SetWind(min, max)`. BAR's windmill bonus reads the live wind (`unit_windmill_control.lua`).
18. **Lightning storms.** Strikes land at random, damaging and stunning what they hit.
    *Possible:* BAR map script `SpawnCEGInRandomMapPos(effect, offset, damage, stun, radius, sound, volume)` with BAR's `lightningstormgreen` and `distantthunder`. BAR's own Acidic Quarry script has this line, commented out.
19. **Acid monsoon.** For two minutes in ten, the light acid rain Acidic Quarry already has becomes a monsoon over the lowlands that eats light units caught in the open.
    *Possible:* BAR map script `SpawnCEGInArea` with damage. BAR's acid rain already does 15 damage over 750 units.
20. **Fog banks.** Thick fog rolls through the canyons, cutting radar there as well as sight.
    *Possible:* `MapAtmosphereConfigSetFog`, with radar cut through `GG.UnitAttributes` (`radarRadius`) for units inside.
21. **Changing skies.** An aurora on clear nights, a red dust sky during storms.
    *Possible:* the map drawing half's `SetSkyBoxTexture` and `SetAtmosphere`.
22. **Soundscape.** Machinery hum by the old factories, wind in the canyons, an echo in the quarry.
    *Possible:* `PlaySoundFile` with a position (map drawing half), and mapinfo `sound` reverb and pass filter. BAR already plays sounds over geothermal vents (`gfx_atmosphere.lua`).
23. **Wildlife.** Crabs on the shores, gulls over the acid, ants in the ruins, all caught up in the fighting.
    *Possible:* BAR's critter units (`critter_crab`, `critter_gull`, `critter_ant`, …) created for Gaia with `CreateUnit`, as BAR's own critter gadget does for the maps it knows (`game_critters.lua`).

### Resources and economy

24. **Regrowing crystal fields.** Crystals rich in metal; mined-out ones regrow after five minutes, so the field is worth holding, not just stripping.
    *Possible:* map features with metal value, the `FeatureDestroyed` event, and `CreateFeature` later.
25. **Battlefield salvage.** The wrecks of an ancient battle: experimentals and commanders lie scattered at the start, and more wreckage rains in as the game goes on.
    *Possible:* BAR's own wreck and heap features for each unit, created by the map gadget (`CreateFeature`).
26. **Supply drops.** Every six minutes a pod lands on a contested point carrying a loot box that makes energy and metal. Whoever holds the point gets it, and enemies can carry it off with transports.
    *Possible:* BAR's `lootboxbronze`, which is capturable, makes 400 energy and 20 metal a second, and can be transported by enemies. The map creates it for Gaia, then hands it over as a capture (see "Handing units over" below).
27. **Depleting veins.** Rich metal spots run dry over 20 minutes while a new vein surfaces elsewhere.
    *Possible:* `Spring.SetMetalAmount` changes the metal map, and `SetUnitMetalExtraction` slows extractors already there.
28. **Geothermal surges.** Vents erupt now and then: plants on them pay double during a surge, but anything beside them is scalded.
    *Possible:* map gadget `AddTeamResource` for the plant's owner, `SpawnCEG`, and `AddUnitDamage`.
29. **Power relay.** A neutral relay pays 50 energy a second to the team with the most units around it.
    *Possible:* `GetUnitsInCylinder` and `AddTeamResource` (which counts as income).
30. **The exchange.** Whoever holds the old exchange turns energy into metal at a better rate than converters give.
    *Possible:* `UseTeamResource` and `AddTeamResource`.
31. **Relic technology.** A powerful unit is locked for everyone until a team recovers the relic core in the centre; then only that team can build it.
    *Possible:* BAR map script `GG.BuildBlocking.AddBlockedUnit` and `RemoveBlockedUnit`, per unit type and team.
32. **Bounties.** Destroying a neutral warlord turret pays its killer's team a bounty.
    *Possible:* `UnitDestroyed` names the attacker, then `AddTeamResource`.

### Neutral structures and control points

33. **Guardians.** Neutral turrets hold the richest metal field and fire on everyone; clearing them is an early objective.
    *Possible:* BAR defence units created for Gaia, which is no one's ally (`CreateUnit(name, x, y, z, facing, gaiaTeam)`).
34. **Sleeping titan.** A derelict experimental walker at the centre. Repair it, as with the oil pump, and it wakes for the team that finished it.
    *Possible:* the repair pool (`AllowFeatureBuildStep`), then `CreateUnit` for that team.
35. **Watchtowers.** Hilltop towers with long sight and radar, claimed by standing beside one with no enemies near.
    *Possible:* BAR's radar towers, or the map's own, created for Gaia and handed over as a capture (see "Handing units over" below).
36. **Command uplink.** Holding the uplink lets the holder's alliance see the whole map for 20 seconds every five minutes.
    *Possible:* `Spring.SetGlobalLos(allyTeam, true)`, then back to false.
37. **Shield dome.** A neutral generator throws a plasma shield over a valley; whoever owns it is protected there.
    *Possible:* a shield unit (BAR's own, or a map unit type with a shield weapon) handed over as a capture (see "Handing units over" below). Shields are an engine weapon type, and `SetUnitShieldState` switches them on and off.
38. **Repair depot.** A nanite bay that repairs its owner's units nearby, a natural front-line anchor.
    *Possible:* `SetUnitHealth` on units within range.
39. **Teleport gates.** Paired gates: a unit entering one comes out of the other after a charge-up, and each gate has a cooldown.
    *Possible:* `SetUnitPosition` (or `MoveCtrl`), with `SpawnCEG` at both ends.
40. **Minefields.** Old minefields along the approaches, owned by no one.
    *Possible:* BAR's mines (`armmine1` …) created for Gaia with `CreateUnit`.
41. **Scrambler field.** A magnetic anomaly where nothing can cloak.
    *Possible:* the `AllowUnitCloak` event; the map gadget's refusal stands.

### Movement

42. **Jump pads.** Pads that throw a unit up onto the plateau. Fall short and it lands in the acid.
    *Possible:* `AddUnitImpulse`. BAR damages units that hit water fast (`unit_water_depth_damage.lua`).
43. **Lifts.** Platforms carry units between the levels of a quarry wall.
    *Possible:* `MoveCtrl.Enable` and `MoveCtrl.SetPosition`, as the tunnel roofs already use.
44. **Mag-rails.** Rails along the quarry floor: units on them move 40% faster.
    *Possible:* BAR map script, with a `speed` modifier in `GG.UnitAttributes` for units on rail squares.
45. **Ice sheet.** Slick ground where units are slow to accelerate, turn and stop.
    *Possible:* `GG.UnitAttributes` modifiers on `maxAcc`, `maxDec` and `turnRate`.
46. **Low gravity.** A moon map: shells fly further and falls are gentler.
    *Possible:* mapinfo `gravity`.

### Hazards and events

47. **Meteor showers.** Now and then meteors fall on part of the map, cratering it and destroying what they hit.
    *Possible:* BAR's `meteor` unit, whose "Asteroid" weapon does 700 damage with a 450-unit blast and crater at map-wide range. It's created for Gaia and given an attack order (`GiveOrderToUnit`), or its weapon is fired directly (`SpawnProjectile`).
48. **Volcano.** A volcano erupts, raining burning rock around it.
    *Possible:* BAR's `volcano_projectile_unit` and its volcano effects (`effects/volcano.lua`). BAR's own volcano gadget is tied to one map, so the map gadget runs its own eruptions the same way.
49. **EMP storms.** Waves sweep across the map, stunning everything in their path for a few seconds.
    *Possible:* `SetUnitHealth(unit, {paralyze = …})`, as the BAR map script's helpers do.
50. **Forest fires.** Explosions set trees alight; the fires spread to nearby trees and burn out, stripping the cover.
    *Possible:* the `Explosion` event (for weapon types registered with `Script.SetWatchExplosion`), `GetFeaturesInCylinder`, and `SetFeatureFireTime`, which burns a feature away, with `SetFeatureSmokeTime`.

## Faction structures

These sit alongside the 50 above: five auxiliary structures for each faction, as their machines would build to support themselves. All 15 share these rules:

- **Start:** each begins as a derelict that belongs to no one.
- **Ownership:** it's repaired through the repair pool, like the oil pump, and becomes the property of the team that finishes, wearing its colour.
- **Faction:** its full effect applies to units of its own faction, recognised by the name prefix BAR gives every unit (`arm`, `cor`, `leg`). A team of another faction that takes one gets a third of the effect, so it's still worth denying.
- **Construction:** they're map unit types or features built with the editor's structure models, styled for their faction. Ownership lives in the map gadget, stat effects in the BAR map script (`GG.UnitAttributes`), and the two share state through rules params.

### Armada: speed, precision, stealth, lightning

Armada's own units lead on stealth and EMP (12 and 8 descriptions mention them).

1. **Tuner (calibration spire).** Recalibrates targeting servos, so Armada units within 600 get +10% weapon range.
   *Possible:* a `maxWeaponRange` weapon modifier in `GG.UnitAttributes`.
2. **Ghostwire (phase relay).** Folds Armada units within 500 out of enemy radar while its owner pays 20 energy a second.
   *Possible:* `stealth` in `GG.UnitAttributes`, with `UseTeamResource` for the upkeep.
3. **Arc capacitor.** Banks its owner's energy, then discharges chain lightning when enemies come within 350, stunning up to five machines for four seconds.
   *Possible:* `SetUnitHealth` with `paralyze`, BAR's lightning effects, and `UseTeamResource`.
4. **Servo cradle (recharge pad).** Damaged Armada bots standing on it repair at 3% a second, then leave overclocked with +20% speed for 30 seconds.
   *Possible:* `SetUnitHealth`, and a `speed` modifier in `GG.UnitAttributes` removed after 30 seconds.
5. **Fenceline (sensor pylons).** Pylons within 800 of each other form a tripwire. Enemies crossing it, cloaked or not, are shown to the owner's alliance for ten seconds.
   *Possible:* `SetUnitLosState` and `SetUnitLosMask`, set per alliance.

### Cortex: heavy armour, brute force, heat and missiles

37 Cortex unit descriptions say "heavy" or "armoured".

1. **Bulkhead (plating forge).** Bolts on extra plating, so Cortex units within 500 get +15% maximum health.
   *Possible:* a `maxHealth` modifier in `GG.UnitAttributes`.
2. **Crucible (scrap smelter).** Drags in wrecks within 500 and smelts them into metal for its owner at 80% of their value.
   *Possible:* `GetFeatureResources`, `DestroyFeature` and `AddTeamResource`.
3. **Heatsink (coolant tower).** Floods weapon housings with coolant, so Cortex weapons within 450 reload 15% faster.
   *Possible:* a `reloadTime` weapon modifier in `GG.UnitAttributes`.
4. **Ordnance depot.** Restocks missile launchers within 400 by one missile a minute (nuclear missiles excepted).
   *Possible:* `SetUnitStockpile`.
5. **Earthworks rig.** Two buttons, *Raise berm* and *Lower berm*, reshape the ground in front of it into a wall or a ramp over 20 seconds.
   *Possible:* `InsertUnitCmdDesc` adds the buttons, the `CommandFallback` event handles them, and `SetHeightMapFunc` moves the ground.

### Legion: drones, heat, shields, a risky economy, Greek names

Legion leads on drones (15 descriptions) and heat rays (12). Its unit names come from Greek myth; these avoid names BAR already uses, such as Aegis, Prometheus and Hephaestus.

1. **Melissa (drone apiary).** Launches a Legion assist drone for its owner every 45 seconds, up to four alive at once; they help build and repair nearby.
   *Possible:* BAR's `legassistdrone`, a flying builder with 100 build power, created for the owner (`CreateUnit`).
2. **Talos (heat forge),** named after the bronze automaton of myth. Legion heat-ray weapons within 500 burn hotter, dealing +12% damage.
   *Possible:* a `damage` weapon modifier in `GG.UnitAttributes`.
3. **Pallas (shield pylon).** Throws a plasma shield over its owner's nearby units that grows stronger the more energy its owner has banked.
   *Possible:* a shield weapon on the map unit type, and `shieldMaxPower` in `GG.UnitAttributes`.
4. **Pythia (oracle spire).** Every 60 seconds it looks into the fog: an eye opens over the enemy's nearest base for 15 seconds.
   *Possible:* BAR's Legion `legvision` ("Provides temporary vision") created for the owner at that spot.
5. **Typhon core (hazard reactor).** Pays its owner 400 energy a second, but when destroyed it goes up like a fusion plant and wrecks everything near it, in keeping with Legion's "hazardous" power plants.
   *Possible:* `AddTeamResource`, then `UnitDestroyed` and `SpawnExplosion` (damage, crater, effect).
