"""A planet for every map: how its world looks and what grows and stands on it, in terms the editor can build.

- drawing: the colours the SVG is drawn in (ground low to high, cliffs, water, sand, fords), lighter where higher.
- beautify: Beautify's six colours (0-255), for MapTool's beautify-texture or the Beautify panel.
- sky, fog, sun: mapinfo.lua's atmosphere colours (0-1), as the editor's Colours panel takes them.
- plants and features: names from BAR's own feature catalog (the editor's BarFeatures) and from the asset library
  (trees and plants from All That Simmers and Crimson Bay, rocks from Ascendancy, All That Glitters and Acidic
  Quarry, props from Industrial Revolution), so every one can be placed.
- wind and tidal: mapinfo's atmosphere.minWind / maxWind and tidalStrength, which set what wind and tidal
  generators make; a few planets set more (gravity, water.damage).
"""


def planet(planet, kind, drawing, beautify, sky, fog, sun, liquid, liquid_colour, ground, plants, features, skies,
           wind, tidal, wet=False, liquid_short="water", extra=None, note=""):
    mapinfo = {"atmosphere.skyColor": sky, "atmosphere.fogColor": fog, "atmosphere.sunColor": sun,
               "atmosphere.minWind": wind[0], "atmosphere.maxWind": wind[1], "tidalStrength": tidal}
    if wet:
        lighter = tuple(round(c + (1 - c) * 0.5, 2) for c in liquid_colour)
        mapinfo.update({"water.surfaceColor": lighter, "water.planeColor": liquid_colour})
    mapinfo.update(extra or {})
    weather = f"{skies}; wind {wind[0]} to {wind[1]}; tidal {tidal}" + (f"; {note}" if note else "") + "."
    return dict(planet=planet, kind=kind, drawing=drawing, beautify=beautify, sky=sky, fog=fog, sun=sun,
                liquid=liquid, liquid_short=liquid_short, liquid_colour=liquid_colour, ground=ground, plants=plants,
                features=features, weather=weather, mapinfo=mapinfo)


def colours(rock, dirt, sand, mud, seabed, ground):
    return dict(rock=rock, dirt=dirt, sand=sand, mud=mud, seabed=seabed, ground=ground)


def draw(ground, rock, liquid, sand, ford):
    return dict(ground=ground, rock=rock, liquid=liquid, sand=sand, ford=ford)


DRY = (0.2, 0.2, 0.22)  # the swatch for maps with no water

THEMES = {
    # ---------------------------------------------------------------- the ten single-hill maps
    "crown_mesa": planet(
        "Verdance", "a temperate grassland world",
        draw(["#93B86A", "#C8DCA6", "#EEF3E1"], "#B5AFA4", "#86B5E0", "#E5D8AE", "#CDB07A"),
        colours((118, 112, 104), (132, 112, 86), (200, 184, 140), (92, 80, 64), (60, 74, 70), (92, 112, 58)),
        (0.55, 0.70, 0.92), (0.76, 0.82, 0.88), (1.0, 0.97, 0.88), "Deep water", DRY,
        "meadow grass on the plateau, darker damp grass in the lowland, grey granite on the mesa's cliffs with "
        "scree at their feet, dirt roads.",
        "oaktree_a, oaktree_b and oaktree_c in groves on the plateau, birchtree along the lowland, ad0_fir on the "
        "edge hills, ferns in the outcrops' shade.",
        "rocks30_def boulders under the cliffs, armstone tombstones on the mesa top, a few fallen armwin_dead "
        "turbines in the lowland.",
        "clear blue sky, light haze", (6, 18), 15, liquid_short="none"),
    "three_rivers": planet(
        "Kapok", "a steaming jungle river world",
        draw(["#7FAE5E", "#B9D896", "#E3F0CF"], "#9FA89A", "#7DB3A6", "#D9CF9E", "#B9A06A"),
        colours((88, 96, 82), (112, 92, 64), (176, 160, 112), (70, 62, 44), (48, 62, 50), (64, 104, 46)),
        (0.70, 0.82, 0.76), (0.62, 0.74, 0.66), (1.0, 0.95, 0.82), "Deep river water", (0.18, 0.36, 0.30),
        "dense green jungle floor, red-brown mud banks along the rivers, moss-green rock in the clefts, a pale "
        "sand island.",
        "kapoktree canopy on the plateau, ad0_senegal palms along the banks and on the island, ferns and "
        "orangemushrooms under the canopy.",
        "rocks30_moss on the river hills and through the fords, a sunken corpship_dead and corpt_dead in the "
        "lake's shallows.",
        "humid white-green haze, fog low over the water", (2, 10), 22, wet=True),
    "caldera": planet(
        "Pyre", "a volcanic world of basalt and ash",
        draw(["#8C8178", "#BDB1A4", "#E2D9CE"], "#6E6660", "#E07A3C", "#C9B89C", "#B39A78"),
        colours((56, 50, 48), (92, 74, 62), (66, 60, 58), (46, 40, 36), (38, 44, 46), (82, 72, 62)),
        (0.36, 0.18, 0.12), (0.55, 0.32, 0.20), (1.0, 0.70, 0.45), "Lava", (0.85, 0.32, 0.08),
        "black basalt and grey ash on the flanks, glowing orange cracks painted across the caldera floor, "
        "obsidian-dark rim cliffs, red cinder roads.",
        "almost nothing: a few treetype firs on the outermost flank, nothing inside the rim and no grass.",
        "pilha_crystal_obsidian along the rim, pilha_crystal_orange round the vents, agorm_rock boulders on the "
        "flanks, editor_geocrack seams across the floor beside the vents.",
        "red-brown sky, orange ash fog", (0, 8), 0, liquid_short="lava",
        note="wind power is poor here, so the vents matter"),
    "spiral_ascent": planet(
        "Halite", "a world of salt terraces",
        draw(["#C9B3B8", "#E6D8DA", "#FAF4F2"], "#A8959B", "#9AD3D6", "#EFE4D6", "#D6C2A8"),
        colours((176, 160, 164), (196, 178, 176), (236, 228, 222), (150, 132, 134), (120, 150, 154), (224, 212, 210)),
        (0.72, 0.80, 0.92), (0.92, 0.88, 0.90), (1.0, 0.96, 0.94), "Brine", DRY,
        "white and pink salt crusts on the terraces, layered rose-grey rock in the terrace walls, cracked "
        "salt-pan polygons on the summit.",
        "none on the salt; a few ad0_senegal palms only out at the map's edge.",
        "pilha_crystal_quartz clusters along the terrace walls, pilha_crystal_red and pilha_crystal1 on the "
        "summit, rocks30_snow (white) as salt boulders.",
        "pale sky, bright glare, thin white haze", (10, 24), 0, liquid_short="none"),
    "windswept_rise": planet(
        "Gale", "a wind-scoured steppe world",
        draw(["#B7A86E", "#D9CF9C", "#F1EBCB"], "#A7A196", "#8DB5D6", "#E6DDB8", "#CBB582"),
        colours((124, 118, 106), (150, 128, 92), (206, 192, 150), (110, 96, 70), (70, 80, 76), (166, 150, 94)),
        (0.62, 0.72, 0.84), (0.82, 0.80, 0.72), (1.0, 0.95, 0.84), "Water", DRY,
        "golden steppe grass combed one way by the wind, bare brown earth on the spurs, grey weathered tors, a "
        "rutted dirt ring road.",
        "birchtree in small wind-bent copses, sparse ferns in the lee of the outcrops, no forest.",
        "rocks30_map tors for the outcrops; the wreck fields as toppled armwin_dead and armsolar_dead; "
        "legstone markers on the spurs.",
        "pale sky, dust on the horizon", (15, 30), 0, liquid_short="none",
        note="wind power at its best"),
    "hex_bastion": planet(
        "Rime", "a frozen world",
        draw(["#AFC3CF", "#D7E3EA", "#F6FAFC"], "#9AA6B0", "#9CC6E8", "#E3E8EC", "#BFC9D1"),
        colours((108, 112, 120), (150, 150, 154), (226, 230, 236), (120, 128, 136), (70, 90, 110), (232, 236, 242)),
        (0.78, 0.85, 0.95), (0.90, 0.93, 0.97), (0.95, 0.97, 1.0), "Ice-cold water", DRY,
        "deep snow on the plain, wind-packed blue ice in the hollows, frosted grey stone in the walls and rocks, "
        "trodden slush on the roads.",
        "ad0_fir and treetype firs, snow-laden, in stands round the fortress; nothing inside the walls.",
        "rocks30_snow boulders for the outcrops, pilha_crystal_blue and pilha_crystal_quartz as ice spikes, "
        "corstone tombstones in the courtyard.",
        "white-blue sky, low snow fog", (6, 16), 0, liquid_short="none"),
    "gorge_run": planet(
        "Ochre", "a red canyon desert world",
        draw(["#C08A62", "#E0B48C", "#F5DEC4"], "#A87F66", "#88B6D6", "#EED7AE", "#C9A272"),
        colours((160, 116, 82), (196, 158, 110), (224, 200, 152), (140, 110, 80), (120, 112, 92), (206, 172, 122)),
        (0.50, 0.65, 0.90), (0.92, 0.78, 0.62), (1.0, 0.92, 0.78), "Water", DRY,
        "red sandstone in bands on the highland walls, orange sand down the gorges, pale caliche on the butte, "
        "packed-sand roads.",
        "a few ad0_senegal palms where the gorges meet below the butte (a dry oasis), nothing on the highlands.",
        "rocks30_desert boulders and scree under every cliff, sun-bleached unit heaps (armaak_heap and its kind) "
        "in the gorges.",
        "deep blue sky, heat shimmer", (8, 22), 0, liquid_short="none"),
    "king_of_the_pit": planet(
        "Selene", "an airless grey moon",
        draw(["#9C9C9C", "#C7C7C5", "#EDEDEA"], "#8E8E8C", "#7C8FA8", "#D8D6D0", "#B6B2A8"),
        colours((96, 96, 98), (128, 126, 124), (176, 174, 170), (80, 80, 82), (50, 54, 60), (150, 150, 150)),
        (0.02, 0.02, 0.04), (0.08, 0.08, 0.10), (1.0, 1.0, 1.0), "Water", DRY,
        "fine grey regolith, bright ejecta rays running out from the pit, dark impact glass on the pit floor, "
        "rover tracks for roads.",
        "none.",
        "rocks30_def boulders thrown out round the rim, rocks30_map for the standing stones, pilha_crystal_quartz "
        "in the pit, the corpse wrecks of a lost landing on the plateau.",
        "black sky, no haze, hard sunlight", (0, 2), 0, liquid_short="none",
        extra={"gravity": 90}, note="solar power only; gravity 90 against BAR's usual 130 (check in play what it does to artillery)"),
    "archipelago_crown": planet(
        "Thalassa", "a tropical ocean world",
        draw(["#8CC27A", "#C6E3A4", "#EEF7DC"], "#B9B09E", "#5FC2D3", "#F2E6BE", "#E2CC92"),
        colours((150, 140, 120), (170, 148, 104), (236, 222, 176), (120, 104, 76), (40, 120, 130), (96, 150, 70)),
        (0.45, 0.72, 0.98), (0.78, 0.90, 0.96), (1.0, 0.98, 0.90), "Sea, 60 deep", (0.10, 0.55, 0.62),
        "white coral-sand beaches, lush green island interiors, turquoise shallows over the shoals, deep blue "
        "beyond.",
        "ad0_senegal palms along every beach, kapoktree and ferns inside the islands, orangemushrooms on the "
        "central island.",
        "rocks30_moss as coral heads on the shoals, corbats_dead and corroy_dead wrecks on the reefs, "
        "editor_geovent on each islet.",
        "bright sky, sea haze", (8, 20), 30, wet=True, liquid_short="sea", note="tidal power is strong"),
    "twin_rings": planet(
        "Amethyst", "a crystal world",
        draw(["#A792C2", "#CFC1E0", "#F1ECF8"], "#8F86A3", "#7FA6E0", "#E3DCEB", "#BFAED0"),
        colours((98, 84, 120), (128, 108, 140), (196, 184, 214), (84, 70, 96), (50, 50, 80), (150, 128, 176)),
        (0.42, 0.32, 0.62), (0.62, 0.52, 0.78), (0.95, 0.88, 1.0), "Water", DRY,
        "violet dust plains, lilac crystal sand inside the rings, dark purple glassy rock in the ring cliffs, pale "
        "roads of crushed crystal.",
        "orangemushrooms in glowing fungal groves, the only life; no trees.",
        "pilha_crystal_violet and pilha_crystal_teal along the ring cliffs, large pilha_crystal_team3 at the gates, "
        "pilha_crystal1 scattered across the plains.",
        "lilac sky, violet haze", (4, 14), 0, liquid_short="none"),

    # ---------------------------------------------------------------- the seven king maps
    "three_crowns": planet(
        "Harvest", "an autumn forest world",
        draw(["#B7915A", "#DDBE8A", "#F4E4C4"], "#A89B8A", "#86AFCF", "#EAD9B2", "#C9A672"),
        colours((112, 104, 96), (140, 104, 66), (196, 170, 122), (96, 70, 48), (60, 64, 58), (150, 110, 52)),
        (0.70, 0.74, 0.86), (0.88, 0.78, 0.62), (1.0, 0.88, 0.70), "Water", DRY,
        "russet leaf litter and stubble fields on the slopes, golden grass in the Commons, grey limestone on the "
        "keep mesas, muddy lanes.",
        "oaktree_a, oaktree_b, oaktree_c and birchtree woods between the realms, orangemushrooms and ferns under "
        "them, open fields round the peasants.",
        "rocks30_moss on the edge hills, rocks30_def beside the ramps, crateorange and crateyellow as market "
        "stalls in the Commons.",
        "low amber sun, morning mist", (5, 15), 0, liquid_short="none"),
    "triad_court": planet(
        "Relic", "a graveyard of old armies",
        draw(["#A39A86", "#CBC3AE", "#EEE9DC"], "#8F887C", "#86AFCF", "#DDD3BC", "#B9A782"),
        colours((104, 98, 90), (126, 114, 96), (180, 170, 150), (88, 80, 70), (56, 58, 56), (132, 124, 104)),
        (0.60, 0.62, 0.66), (0.70, 0.68, 0.64), (0.95, 0.92, 0.86), "Water", DRY,
        "grey-beige dust over old scorched craters, darker scorch marks, worn flagstones inside the citadel, dirt "
        "tracks.",
        "a few stunted treetype firs in the realms; grass only in the council chamber.",
        "wrecks everywhere to reclaim: armfus_dead, armgate_dead and corbats_dead hulks in the realms; armstone, "
        "corstone and legstone tombstones in the citadel; rocks30_map along the ridges.",
        "overcast grey sky, dust haze", (6, 16), 0, liquid_short="none"),
    "corner_fortresses": planet(
        "Forge", "a derelict industrial estate",
        draw(["#9A9284", "#C4BDAE", "#E9E5DA"], "#857E74", "#B5C95A", "#D6CDB6", "#B5A47E"),
        colours((96, 90, 84), (110, 96, 78), (160, 150, 130), (70, 64, 54), (70, 80, 40), (124, 116, 100)),
        (0.56, 0.52, 0.44), (0.62, 0.56, 0.42), (1.0, 0.88, 0.70), "Acid (hurts units)", (0.55, 0.70, 0.15),
        "lots of cracked, broken concrete between gravel waste ground gone to weeds, rust and oil stains, worn "
        "asphalt roads, paved fortress yards with hazard stripes, a brick market square in the middle.",
        "weeds (cycas ferns) in the waste ground.",
        "ruined works: armvp, corvp, armlab, corlab and moho wrecks with storage tanks and metal makers round "
        "them and rocks30_map rubble; smaller wrecks; crateblue, crateorange, cratered and crateyellow in yards "
        "and in the fortresses; minitruck1 and minitruck2 by the roads, cranetruck1 by the market.",
        "brown smog sky, yellow haze", (4, 14), 10, wet=True, liquid_short="acid",
        extra={"water.damage": 60}, note="the acid lakes hurt anything in them (water.damage 60, to tune in play)"),
    "edge_keeps": planet(
        "Heath", "a highland moor",
        draw(["#8C7F86", "#B8AC9F", "#E5DED2"], "#8E918F", "#5E7FA0", "#D9CFB8", "#B5A585"),
        colours((104, 106, 104), (98, 82, 66), (170, 158, 130), (64, 54, 46), (30, 40, 50), (112, 92, 104)),
        (0.58, 0.64, 0.72), (0.72, 0.76, 0.80), (0.96, 0.94, 0.88), "Loch, 60 deep", (0.12, 0.20, 0.28),
        "rolling hills of heather and moor grass, peat hags and green flushes in the hollows, bracken on the "
        "slopes, grey granite tors and keep cliffs, black peat round the loch, granite-sett causeways.",
        "ad0_fir pinewoods thinning to lone pines, birchtree copses by the loch and in the hollows, ferns as "
        "bracken on the slopes.",
        "rocks30_def granite tors on the crests, scree under the crags and boulders on the moor, legstone cairns "
        "on the keeps, rocks30_moss on the isle.",
        "grey sky, drifting mist", (10, 25), 12, wet=True, liquid_short="loch"),
    "moat_court": planet(
        "Bayou", "a fog-bound swamp world",
        draw(["#7F8A5E", "#AEB68A", "#DCE0C2"], "#8C8B7C", "#6F8F6A", "#CFC79F", "#A99A6C"),
        colours((90, 90, 78), (88, 76, 54), (150, 140, 100), (58, 52, 36), (40, 50, 34), (78, 92, 52)),
        (0.55, 0.60, 0.52), (0.56, 0.62, 0.54), (0.92, 0.90, 0.78), "Swamp water, 60 deep", (0.22, 0.30, 0.18),
        "olive mud and reed beds, dark peat at the water's edge, mossy stone on the keep islands, plank-grey "
        "bridges.",
        "oaktree_c and kapoktree as swamp cypress, ferns as reeds along the moat, orangemushrooms on the islands.",
        "rocks30_moss on the edge cliffs, sunken corssub_dead and corpt_dead hulks in the moat, crateblue and "
        "cratered stacked at the bridges.",
        "thick grey-green fog, little sun", (0, 8), 18, wet=True, liquid_short="swamp"),
    "star_fort": planet(
        "Dune", "a sand-sea world",
        draw(["#D1A867", "#E8CB92", "#F8EBCB"], "#B89770", "#86AFCF", "#F3E3BC", "#D6B47A"),
        colours((176, 136, 90), (204, 164, 110), (232, 206, 150), (150, 116, 76), (130, 116, 90), (220, 184, 124)),
        (0.55, 0.72, 0.95), (0.95, 0.86, 0.68), (1.0, 0.93, 0.76), "Water", DRY,
        "rippled golden dunes in the realms, wind-polished sandstone ridges, pale stone flags inside the fort.",
        "ad0_senegal palms in an oasis ring outside each bastion's door; nothing on the dunes.",
        "rocks30_desert along the ridges and passes, armsolar_dead and armadvsol_dead half-buried in the sand.",
        "deep blue sky, sand haze on the horizon", (12, 28), 0, liquid_short="none"),
    "five_rivers": planet(
        "Bloom", "a bioluminescent night world",
        draw(["#5E9C9A", "#9CCFC4", "#DDF3EC"], "#7E8C9E", "#6FA8E8", "#CDE7DF", "#9EC3B6"),
        colours((70, 80, 100), (60, 100, 110), (150, 200, 190), (40, 60, 70), (20, 40, 70), (60, 140, 130)),
        (0.12, 0.10, 0.28), (0.20, 0.30, 0.40), (0.75, 0.85, 1.0), "Glowing water, 60 deep", (0.15, 0.45, 0.75),
        "teal moss fields glowing at their edges, blue-grey rock on the hall cliffs, cyan-lit riverbanks, pale "
        "glowing roads.",
        "kapoktree and oaktree_b as giant alien trees, orangemushrooms in glowing rings by the rivers, ferns along "
        "the banks.",
        "pilha_crystal_teal, pilha_crystal_lime and pilha_crystal_blue along the rivers and on the isle, "
        "rocks30_moss on the hall mesas.",
        "dark violet night sky, blue mist over the rivers", (3, 12), 20, wet=True),
}
