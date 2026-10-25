"""Marram Line: the design's one coordinate table, and the to-scale drawing made from it (layout.svg).

Game units, x east, z south, the map 24 x 24 (12288 x 12288), 4v4. Not symmetric: the sea is in the west and the hill in
the east. From west to east: the sea; the beach; the foredune, cut by draws; the dune belt, where the beach team's bases
sit in the slacks; the beach team's trenches dug into the dunes' last ridge; the dead man's zone, a low meadow shelled
to craters and partly flooded, with a brook across it; the hill team's trenches at the hill's foot; and the slight rise
to the crest, where the hill team's bases sit among hedgerows. In the north a rocky headland juts into the sea; in the
south the brook runs out into a lagoon behind a sand spit.

Every line wavers (sums of sines from fixed seeds) and every outline is irregular. Only the trenches' bays are straight.

    python layout.py        writes layout.svg and layout.json beside this file, and prints areas, metal and distances
"""
import html, json, math, os

HERE = os.path.dirname(os.path.abspath(__file__))
W = H = 12288
MAP_UNITS = (24, 24)
SEED = 52

# ---- Heights (game units; the water is at 0)
SEA_FLOOR = [(0, 0), (240, 8), (640, 20), (1100, 40), (1500, 60)]   # depth by distance out from the waterline
BAR_AT, BAR_HALF, BAR_DEPTH = 400, 90, 5                           # the sandbars: 5 deep, ships (8) can't cross them
BERM = 10                       # the beach's top
SLACK = 22                      # dune slacks: the beach team's level ground
HEADLAND_TOP = 70               # the headland's heath
TRENCH_GROUND = 42              # each fire trench is dug where the ground is about this high
MEADOW = 16                     # the dead man's zone
BROOK_DEPTH = 6                 # land units wade it; ships (8) can't get up it
CREST = 215                     # the hill's crest
EAST_EDGE = 175                 # the back slope at the map's east edge
KNOLL = 34                      # the knoll in the hill team's salient, over the ground round it

# The fire trench's cross-section, from the trench's own rear to its front (toward the enemy): (offset, height over the
# ground there). Floor 40 wide at 32 down; a fire step 20 wide at 12 down on the enemy side; the parapet 6 over the
# ground; walls 45 to 48 degrees (bots 54 climb them; vehicles 27 and hovers 33 don't).
TRENCH_SECTION = [(-88, 0), (-64, 6), (-40, 6), (-6, -32), (34, -32), (52, -12), (72, -12), (90, 6), (114, 6), (178, 0)]
SUPPORT_BACK = 240              # the support trench, this far behind the fire trench (no fire step)
WIRE = (150, 260)               # two belts of wire, this far in front of the fire trench


def hash01(k, salt):
    h = (k * 374761393 + salt * 668265263 + SEED * 2246822519) & 0xFFFFFFFF
    h = ((h ^ (h >> 13)) * 1274126177) & 0xFFFFFFFF
    return ((h ^ (h >> 16)) & 0xFFFFFF) / 0x1000000


def wave(t, seed, amp, wavelength, octaves=4):
    """A wavering line: about +-amp, its longest wave `wavelength` long, finer ones on top."""
    s = 0.0
    for k in range(octaves):
        s += math.sin(2 * math.pi * t / (wavelength / 2.1 ** k) + 2 * math.pi * hash01(k, seed)) / 1.9 ** k
    return amp * s / 1.95


def wave2(x, z, seed, amp, wavelength):
    return amp * 0.5 * (math.sin(2 * math.pi * x / wavelength + 6.28 * hash01(1, seed)) * math.sin(2 * math.pi * z / (wavelength * 1.3) + 6.28 * hash01(2, seed))
                        + math.sin(2 * math.pi * (x + z) / (wavelength * 0.7) + 6.28 * hash01(3, seed)))


def bump(u):
    return (1 - u * u) ** 2 if abs(u) < 1 else 0.0


def smooth(t):
    t = min(1.0, max(0.0, t))
    return t * t * (3 - 2 * t)


def lerp(a, b, t):
    return a + (b - a) * t


def wobble(a, k, rough):
    """An irregular outline's radius factor at angle a (radians)."""
    return 1 + rough * (math.sin(3 * a + 6.28 * hash01(k, 91)) * 0.55 + math.sin(5 * a + 6.28 * hash01(k, 92)) * 0.3
                        + math.sin(9 * a + 6.28 * hash01(k, 93)) * 0.15)


def inside_blob(x, z, cx, cz, rx, rz, k, rough):
    """How far into an irregular blob (x, z) is: 1 at its middle, 0 on its outline, negative outside."""
    a = math.atan2((z - cz) / rz, (x - cx) / rx)
    return 1 - math.hypot((x - cx) / rx, (z - cz) / rz) / wobble(a, k, rough)


# ---- The lines, each x as a function of z.
def headland(z):
    """How much z is the headland: 1 across its middle, falling to 0 at its shoulders."""
    return min(1.0, 1.6 * bump((z - 1450) / 1750))


def coast(z):
    """The waterline. The headland juts west in the north; in the south the coast bends a little east, to the inlet's
    mouth."""
    x = 1850 + wave(z, 11, 150, 3400)
    x -= 1180 * bump((z - 1450) / 1750) + wave(z, 12, 80, 520) * headland(z)      # ragged rock on the headland
    x += 380 * smooth((z - 9000) / 1600)
    return x


def beach_width(z):
    return lerp(520 + wave(z, 13, 90, 2200), 110, headland(z))                      # shingle under the cliffs


def berm(z):
    return coast(z) + beach_width(z)


def foredune_width(z):
    return 380 + wave(z, 14, 70, 1700)


def foredune_crest(z):
    return 52 + wave(z, 15, 16, 1500)


def fire_A(z):
    """The beach team's fire trench, dug into the dune belt's last ridge. Its salient bulges east round a dune tongue."""
    return 5800 + wave(z, 21, 170, 2600) + 520 * bump((z - 8300) / 1000)


def fire_B(z):
    """The hill team's fire trench at the hill's foot. Its salient bulges west round a knoll."""
    return 7850 + wave(z, 22, 170, 2600) - 560 * bump((z - 3600) / 1000)


def crest(z):
    return 10900 + wave(z, 23, 300, 3000)


def zone_mid(z):
    return (fire_A(z) + fire_B(z)) / 2


# The headland's heath: an irregular blob, cut off in the west by its cliffs.
HEATH = (2650, 1500, 2150, 1650, 17, 0.13)


def heath(x, z):
    return inside_blob(x, z, *HEATH)


# ---- The inlet: a tidal river from the sea, through the dunes, both trench lines and the zone, to a sandy cove at the
# hill's foot behind the hill team's line. Ships of every size go all the way up it; no land unit crosses it. The cove is
# the hill team's harbour, and the sandy end (the shore south of the inlet, reached by land only from the hill) is its own
# stretch of open coast, so the hill team takes to the sea too. Its course west to east: (x, z, half its width).
INLET = [(1300, 11010, 430), (2250, 10950, 380), (2850, 10800, 320), (3400, 10990, 290), (3950, 10730, 270),
         (4550, 10930, 260), (5150, 10650, 250), (5750, 10850, 240), (6350, 10600, 240), (6950, 10790, 230),
         (7550, 10560, 220), (8150, 10700, 320), (8750, 10930, 450), (9250, 11020, 430)]
INLET_DEPTH = 26                # its channel: every ship gets up it (battleships need 15); no land unit crosses (20)
INLET_MOUTH_DEPTH = 32
COVE_DEPTH = 24                 # the cove, from x 8,300: deep enough for shipyards in its middle, sandy beaches round it
COVE_FROM = 8300
INLET_BANK = 12                 # degrees: the banks above the water, sand and mud
INLET_SHALLOWS = 0.4            # the outer 40% of its width shelves up from the channel to the bank
SAND_END = 10                   # the sandy end's flats

# ---- Gaps: where vehicles cross. The trenches stop at sandbagged ends either side, and a track runs through.
DRAWS = [3300, 5900, 8500]                                  # through the foredune, 260 wide, up at under 6 degrees
DRAW_W = 260
HEADLAND_ROAD_Z = 2950                                      # the cut road from the beach onto the headland, 9 degrees
CROSS_A = [2000, 4500, 7000, 9400]                          # through the beach team's trenches, 220 wide
CROSS_B = [1100, 3600, 6100, 8600]                          # through the hill team's
CROSS_W = 220
CROSS_GAP = 420                 # the trenches stop this far apart round each crossing: its road, and a bunker at one side

# The brook: a dry bed of mud and reeds down the rise into the cove's head (all water stands at 0, so the rise holds none).
BROOK_DRY = [(10500, 10600), (10200, 10720), (9950, 10880), (9700, 10990)]

# ---- Starts: the beach team 0-3 in the slacks (0 on the headland's heath), the hill team 4-7 among the fields.
STARTS = [(0, "beach", 3950, 1650), (1, "beach", 3750, 4300), (2, "beach", 3950, 7000), (3, "beach", 4010, 9700),
          (4, "hill", 9700, 1250), (5, "hill", 9700, 3900), (6, "hill", 9750, 6450), (7, "hill", 9710, 8900)]
PAD_R = 560                     # each start's level ground


def st(n):
    return STARTS[n][2], STARTS[n][3]


# ---- Metal (T1 metal/s by spot). Four 2.0 spots round each start (turned by its own angle, so no two starts alike).
START_SPOT_R = 230
SPOTS_AT_STARTS = [(sx + START_SPOT_R * math.cos(math.radians(a0 + 90 * k)), sz + START_SPOT_R * math.sin(math.radians(a0 + 90 * k)), 2.0, t)
                   for (n, t, sx, sz), a0 in zip(STARTS, (20, 35, 10, 50, 25, 5, 40, 15)) for k in range(4)]
SPOTS_BEACH = [(fire_A(3000) - 720, 3000, 2.4), (fire_A(5650) - 680, 5650, 2.4), (fire_A(10100) - 760, 10100, 2.4),
               (berm(4600) - 230, 4600, 2.4), (3300, 10250, 2.4),                  # on the beach, and by the inlet's mouth
               (fire_A(8300) - 440, 8300, 2.8)]                                    # the salient's, behind its support trench
SPOTS_HILL = [(fire_B(2500) + 700, 2500, 2.4), (fire_B(5600) + 680, 5600, 2.4), (fire_B(10050) + 760, 10050, 2.4),
              (11550, 3050, 2.4), (5200, 11750, 2.4),                              # the back slope, and the sandy end
              (fire_B(3600) + 440, 3600, 2.8)]
SPOTS_ZONE = [(zone_mid(z) + dx, z, 3.0) for z, dx in ((800, 0), (2300, -120), (4800, 140), (5650, -260), (6350, 260),
                                                       (7400, -100), (9400, 120), (10150, -150))]
FARM = (zone_mid(6000), 6000)                                # the ruined farm in the zone, its vent in the yard
VENTS = [("zone", FARM[0], FARM[1]), ("hill", 10950, 5850), ("beach", 4750, 5900)]   # now both teams have the water
VILLAGE = (10850, 6450)
WINDMILL = (fire_B(3300) + 480, 3300)                       # on the knoll in the hill team's salient, beside its crossing
LIGHTHOUSE = (coast(1450) + 420, 1450)
BATTERY = (coast(1750) + 620, 1750)                         # the old coastal gun casemate on the headland


# ---- Roads: (kind, control points), drawn and built as smooth curves through them. Tracks on the beach side, sunken
# lanes on the hill side, old roads across the zone.
def _p(x, z):
    return (round(x), round(z))


ROAD_POINTS = [
    ("track", [_p(berm(3300) - 60, 3300), _p(berm(3300) + 500, 3330), _p(3500, 3800), st(1)]),
    ("track", [_p(berm(5900) - 60, 5900), _p(berm(5900) + 500, 5930), _p(3750, 6450), st(2)]),
    ("track", [_p(berm(8500) - 60, 8500), _p(berm(8500) + 500, 8480), _p(3800, 9150), st(3)]),
    ("track", [_p(berm(HEADLAND_ROAD_Z) - 60, HEADLAND_ROAD_Z), _p(berm(2700) + 250, 2600), _p(3300, 2150), st(0)]),
    ("track", [st(0), _p(4700, 1850), _p(fire_A(2000) - 260, 2000), _p(fire_A(2000) + 300, 2000)]),
    ("track", [st(1), _p(4700, 4420), _p(fire_A(4500) - 260, 4500), _p(fire_A(4500) + 300, 4500)]),
    ("track", [st(2), _p(4800, 7030), _p(fire_A(7000) - 260, 7000), _p(fire_A(7000) + 300, 7000)]),
    ("track", [st(3), _p(4850, 9520), _p(fire_A(9400) - 260, 9400), _p(fire_A(9400) + 300, 9400)]),
    ("track", [st(0), _p(3700, 2900), st(1), _p(3800, 5700), st(2), _p(4000, 8400), st(3)]),
    ("lane", [_p(fire_B(1100) - 300, 1100), _p(fire_B(1100) + 260, 1100), _p(8800, 1250), st(4)]),
    ("lane", [_p(fire_B(3600) - 300, 3600), _p(fire_B(3600) + 260, 3600), _p(8550, 3950), st(5)]),
    ("lane", [_p(fire_B(6100) - 300, 6100), _p(fire_B(6100) + 260, 6100), _p(8850, 6600), st(6)]),
    ("lane", [_p(fire_B(8600) - 300, 8600), _p(fire_B(8600) + 260, 8600), _p(8800, 9200), st(7)]),
    ("lane", [st(4), _p(10300, 2800), st(5), _p(10400, 5600), VILLAGE, st(6), _p(10450, 8500), st(7), _p(10200, 11400), _p(10500, 12288)]),
    ("lane", [VILLAGE, _p(11500, 6250), _p(12288, 6350)]),
    ("lane", [st(7), _p(9450, 10150), _p(9250, 10400)]),                                         # down to the cove
    ("track", [_p(10200, 11400), _p(9500, 11700), _p(8400, 11800), _p(7000, 11700), _p(5600, 11850), _p(4200, 11750), _p(3200, 11900)]),   # the sandy end
    ("old road", [_p(fire_A(2000) + 300, 2000), _p(zone_mid(1550), 1550), _p(fire_B(1100) - 300, 1100)]),
    ("old road", [_p(fire_A(4500) + 300, 4500), _p(zone_mid(4000), 4000), _p(fire_B(3600) - 300, 3600)]),
    ("old road", [_p(fire_A(7000) + 300, 7000), _p(FARM[0], FARM[1]), _p(fire_B(6100) - 300, 6100)]),
    ("old road", [_p(fire_A(4500) + 300, 4500), _p(FARM[0] - 200, 5300), _p(FARM[0], FARM[1])]),
    ("old road", [_p(fire_A(9400) + 300, 9400), _p(zone_mid(9000), 9000), _p(fire_B(8600) - 300, 8600)]),
]


def curve(points, steps=10):
    """A Catmull-Rom curve through the points (ends held)."""
    if len(points) < 3:
        return list(points)
    pts = [points[0]] + list(points) + [points[-1]]
    out = []
    for i in range(1, len(pts) - 2):
        p0, p1, p2, p3 = pts[i - 1], pts[i], pts[i + 1], pts[i + 2]
        for s in range(steps):
            t = s / steps
            out.append(tuple(0.5 * (2 * p1[c] + (-p0[c] + p2[c]) * t + (2 * p0[c] - 5 * p1[c] + 4 * p2[c] - p3[c]) * t * t
                                    + (-p0[c] + 3 * p1[c] - 3 * p2[c] + p3[c]) * t * t * t) for c in range(len(p1))))
    out.append(points[-1])
    return out


ROADS = [(kind, curve(pts)) for kind, pts in ROAD_POINTS]
INLET_PTS = curve(INLET, 12)
INLET_S = [0.0]
for (ax, az, _), (bx, bz, _) in zip(INLET_PTS, INLET_PTS[1:]):
    INLET_S.append(INLET_S[-1] + math.hypot(bx - ax, bz - az))


def inlet_at(x, z):
    """The inlet near (x, z): the distance to its middle line, how far along it (from the sea), its half width there
    (its banks waver, each its own way), and which side: +1 south, -1 north. None when far from it."""
    if z < 9700:
        return None
    best = None
    for i, ((x0, z0, w0), (x1, z1, w1)) in enumerate(zip(INLET_PTS, INLET_PTS[1:])):
        dx, dz = x1 - x0, z1 - z0
        t = max(0.0, min(1.0, ((x - x0) * dx + (z - z0) * dz) / (dx * dx + dz * dz or 1)))
        d = math.hypot(x - x0 - t * dx, z - z0 - t * dz)
        if best is None or d < best[0]:
            across = (dx * (z - z0) - dz * (x - x0)) / (math.hypot(dx, dz) or 1)
            best = (d, INLET_S[i] + t * math.hypot(dx, dz), lerp(w0, w1, t), 1 if across > 0 else -1, across)
    d, s, w, side, across = best
    south = smooth((across / max(d, 1e-6) + 1) / 2)                   # past the cove's end the two banks' wavers blend
    return d, s, w * (1 + 0.16 * lerp(wave(s, 34, 1, 900), wave(s, 33, 1, 900), south)), side


def inlet_depth(x, s):
    d = lerp(INLET_MOUTH_DEPTH, INLET_DEPTH, smooth((s - 1000) / 1000))
    return lerp(d, COVE_DEPTH, smooth((x - COVE_FROM) / 300))


def inlet_cut(x, z):
    """The inlet's surface at (x, z) (its bed, or its bank rising at INLET_BANK), or None when far from it."""
    at = inlet_at(x, z)
    if at is None:
        return None
    d, s, w, _ = at
    if d < w:
        return -inlet_depth(x, s) * smooth((w - d) / (INLET_SHALLOWS * w))
    return (d - w) * math.tan(math.radians(INLET_BANK))


def south_of_inlet(x, z):
    at = inlet_at(x, z)
    return at is not None and at[3] > 0 and at[0] >= at[2]


def sandy_end(x, z):
    """The shore south of the inlet, from the sea to the hill's foot: the hill team's, as only the hill reaches it by land."""
    return x < fire_B(z) + 500 and south_of_inlet(x, z)


def inlet_crossing(fn):
    """Where the inlet's middle crosses a trench line: (z, its half width there)."""
    for (x0, z0, w0), (x1, z1, w1) in zip(INLET_PTS, INLET_PTS[1:]):
        if (x0 - fn(z0)) * (x1 - fn(z1)) <= 0:
            t = (x0 - fn(z0)) / ((x0 - fn(z0)) - (x1 - fn(z1)))
            return lerp(z0, z1, t), lerp(w0, w1, t)
    return None


INLET_A, INLET_B = inlet_crossing(fire_A), inlet_crossing(fire_B)
# Each line ends short of the inlet's north bank (whose slope would cut its end away), a bunker at its end; from there a
# row of concrete dragon's teeth runs across the bank and out into the water until it's too deep to wade, so nothing on
# land gets round the line's end. Nothing of the lines is south of the inlet.
END_A = INLET_A[0] - INLET_A[1] * 1.16 - 330
END_B = INLET_B[0] - INLET_B[1] * 1.16 - 330


# ---- Height model (coarse: the drawing's profile and contours; the converter's will add the detail).
def sea_depth(s):
    for (s0, d0), (s1, d1) in zip(SEA_FLOOR, SEA_FLOOR[1:]):
        if s <= s1:
            return lerp(d0, d1, (s - s0) / (s1 - s0))
    return SEA_FLOOR[-1][1]


def trench_cut(d):
    """The fire trench's section at signed distance d (positive toward the enemy), or None outside it."""
    if d < TRENCH_SECTION[0][0] or d > TRENCH_SECTION[-1][0]:
        return None
    for (d0, h0), (d1, h1) in zip(TRENCH_SECTION, TRENCH_SECTION[1:]):
        if d <= d1:
            return lerp(h0, h1, (d - d0) / (d1 - d0))
    return 0.0


def rise(x, z):
    fb, cr = fire_B(z), crest(z)
    top = CREST + wave(z, 24, 20, 2500)
    # The swell runs on over the crest and down the back slope (stopping at the crest left a step along it).
    swell = 10 * wave2(x, z, 25, 1, 1900) * smooth((x - fb) / 900)
    if x <= cr:
        h = TRENCH_GROUND + (top - TRENCH_GROUND) * smooth((x - fb) / (cr - fb))
    else:
        h = lerp(top, EAST_EDGE, (x - cr) / (W - cr))
    return h + swell + KNOLL * bump(math.hypot(x - WINDMILL[0], z - WINDMILL[1]) / 420)


def bar_on(z):
    """Where the sandbars lie off the coast: broken by rips, none off the headland or across the inlet's mouth."""
    return wave(z, 31, 1, 1400) > -0.35 and headland(z) < 0.5 and abs(z - 10950) > 650


def height(x, z):
    """The coarse ground, with the inlet cut into it."""
    h = height_base(x, z)
    cut = inlet_cut(x, z)
    return h if cut is None else min(h, cut)


def height_base(x, z):
    c = coast(z)
    if x < c:
        s = c - x
        d = sea_depth(s)
        if abs(s - BAR_AT) < BAR_HALF and bar_on(z):
            d = min(d, lerp(d, BAR_DEPTH, bump((s - BAR_AT) / BAR_HALF)))
        return -d
    b, hd = berm(z), headland(z)
    if x < b:
        return BERM * (x - c) / (b - c) * (1 - hd) + 4 * hd
    fa, fb = fire_A(z), fire_B(z)
    if sandy_end(x, z):                                                         # sand flats with low dunes, to the hill
        g = SAND_END + max(0.0, 22 * wave2(x, z, 35, 1, 600))
        return lerp(g, rise(x, z), smooth((x - (fb - 300)) / 800))
    ht = heath(x, z)
    if hd > 0.05:                                                               # the headland: cliff, then heath
        top = lerp(SLACK, HEADLAND_TOP, hd)
        if x < b + 90:
            return lerp(4, top, (x - b) / 90)
    if ht > -0.15:
        ground = lerp(SLACK, HEADLAND_TOP, smooth((ht + 0.15) / 0.35))
        ground = lerp(ground, TRENCH_GROUND, smooth((x - (fa - 500)) / 500))
    else:
        fw = foredune_width(z)
        if x < b + fw:                                                          # foredune: steep to the sea
            u = (x - b) / fw
            hc = foredune_crest(z)
            if any(abs(z - dz) < DRAW_W / 2 for dz in DRAWS):
                hc = SLACK
            return lerp(BERM, hc, smooth(u / 0.3)) if u < 0.3 else lerp(hc, SLACK, smooth((u - 0.3) / 0.7))
        ground = SLACK + max(0.0, 26 * wave2(x, z, 26, 1, 700)) * smooth((x - b - fw) / 300)
        ground = lerp(ground, TRENCH_GROUND, smooth((x - (fa - 500)) / 500))
    if x < fa + 360:
        g = ground if x < fa else lerp(TRENCH_GROUND, MEADOW, smooth((x - fa) / 360))
        cut = trench_cut(x - fa)
        return g + cut if cut is not None else g
    if x < fb - 360:
        return MEADOW + 5 * wave2(x, z, 27, 1, 900)
    if x < fb:
        g = lerp(MEADOW, TRENCH_GROUND, smooth((x - (fb - 360)) / 360))
    else:
        g = rise(x, z)
    cut = trench_cut(fb - x)
    return g + cut if cut is not None else g


# ---- Generated: dunes, craters, hedgerows, wire, wrecks.
def dist_to_path(x, z, pts):
    best = 1e18
    for (x0, z0), (x1, z1) in zip(pts, pts[1:]):
        dx, dz = x1 - x0, z1 - z0
        t = max(0.0, min(1.0, ((x - x0) * dx + (z - z0) * dz) / (dx * dx + dz * dz or 1)))
        best = min(best, math.hypot(x - x0 - t * dx, z - z0 - t * dz))
    return best


def near_road(x, z, margin):
    return any(dist_to_path(x, z, pts) < margin for _, pts in ROADS)


def near_pad(x, z, margin):
    return any(math.hypot(x - sx, z - sz) < PAD_R + margin for _, _, sx, sz in STARTS)


def near_water(x, z, margin):
    """Within margin of the inlet's water or the brook's bed."""
    at = inlet_at(x, z)
    return (at is not None and at[0] < at[2] + margin) or dist_to_path(x, z, BROOK_DRY) < margin


def dunes():
    """Parabolic dunes in the dune belt: elongated onshore (east), their steep landward slip faces bots-only (36 degrees)."""
    out, k = [], 0
    while len(out) < 70 and k < 6000:
        k += 1
        z = 200 + hash01(k, 41) * (H - 400)
        if z > 10500:
            continue
        x0, x1 = berm(z) + foredune_width(z) + 120, fire_A(z) - 520
        x = x0 + hash01(k, 42) * (x1 - x0)
        r = 110 + 210 * hash01(k, 43) ** 1.5
        if heath(x, z) > -0.12 or near_pad(x, z, r * 1.2) or near_road(x, z, r * 1.1 + 60) or near_water(x, z, r * 1.9 + 80):
            continue
        if any(math.hypot((x - px) / 1.6, z - pz) < (r + pr) * 0.95 for px, pz, pr, _ in out):
            continue
        out.append((x, z, r, 14 + 34 * hash01(k, 44)))
    k = 0
    while len(out) < 86 and k < 6000:                                           # low dunes on the sandy end
        k += 1
        x, z = 2600 + hash01(k, 45) * 5000, 11100 + hash01(k, 46) * 1100
        r = 90 + 120 * hash01(k, 47)
        if not sandy_end(x, z) or x < berm(z) + 150 or x > fire_B(z) - 400 or near_water(x, z, r * 1.9 + 80) or near_road(x, z, r + 60):
            continue
        if any(math.hypot((x - px) / 1.6, z - pz) < (r + pr) * 0.95 for px, pz, pr, _ in out):
            continue
        out.append((x, z, r, 8 + 14 * hash01(k, 48)))
    return out


def craters():
    """Shell holes across the zone, thickest near the lines. Deep ones (bomb craters) are bots-only; any whose floor is
    below the water holds water."""
    out, k = [], 0
    while len(out) < 190 and k < 20000:
        k += 1
        z = 60 + hash01(k, 51) * (H - 120)
        fa, fb = fire_A(z), fire_B(z)
        u = hash01(k, 52)
        u = min(1.0, max(0.0, 0.5 + (u - 0.5) * (1 + 0.6 * math.cos(2 * math.pi * u))))   # a little more near either line
        x = fa + 300 + u * (fb - fa - 600)
        r = 30 + 110 * hash01(k, 53) ** 2.2
        deep = hash01(k, 54) < 0.18
        if x - 1.5 * r < fa + 290 or x + 1.5 * r > fb - 290:                  # wholly in the zone, clear of the wire
            continue
        if any(math.hypot(x - sx, z - sz) < 1.5 * r + 90 for sx, sz, _ in SPOTS_ZONE) or math.hypot(x - FARM[0], z - FARM[1]) < 1.5 * r + 280:
            continue
        if near_water(x, z, r + 120) or south_of_inlet(x, z) or near_road(x, z, r + 40):
            continue
        # Shell holes may overlap; a bomb crater keeps its whole rim, so nothing gentle cuts into its steep walls.
        if any(math.hypot(x - px, z - pz) < (r + pr) * (1.25 if deep or pdeep else 0.8) for px, pz, pr, _, pdeep, _ in out):
            continue
        depth = min(0.45 * r, 34) if deep else min(0.18 * r, 20)
        ground = MEADOW + 5 * wave2(x, z, 27, 1, 900)
        out.append((x, z, r, depth, deep, depth > ground))
    return out


def wire_belts():
    """Broken belts of wire in front of each fire trench, down to the inlet's bank: (side, z0, z1, offset)."""
    out = []
    for side, fire, gaps, end in (("beach", fire_A, CROSS_A, END_A), ("hill", fire_B, CROSS_B, END_B)):
        for belt, off in enumerate(WIRE):
            z, k = 0.0, 0
            while z < end:
                k += 1
                length = 280 + 460 * hash01(k, 61 + belt + (10 if side == "hill" else 0))
                gap = 80 + 160 * hash01(k, 63 + belt + (10 if side == "hill" else 0))
                z1 = min(end, z + length)
                cuts = [g for g in gaps if z - CROSS_W < g < z1 + CROSS_W]
                if not cuts:
                    out.append((side, z, z1, off))
                else:                                                           # lanes through the wire at the crossings
                    lo = z
                    for g in sorted(cuts):
                        if g - CROSS_W / 2 - 40 > lo:
                            out.append((side, lo, g - CROSS_W / 2 - 40, off))
                        lo = g + CROSS_W / 2 + 40
                    if z1 > lo:
                        out.append((side, lo, z1, off))
                z = z1 + gap
    return out


def poly_area(points):
    return abs(sum(points[i][0] * points[(i + 1) % len(points)][1] - points[(i + 1) % len(points)][0] * points[i][1] for i in range(len(points)))) / 2


def cut_convex(points, p, n):
    """Cuts a convex polygon by the line through p with normal n: the two halves and the cut's two ends."""
    a, b, ends = [], [], []
    m = len(points)
    for i in range(m):
        u, v = points[i], points[(i + 1) % m]
        su = (u[0] - p[0]) * n[0] + (u[1] - p[1]) * n[1]
        sv = (v[0] - p[0]) * n[0] + (v[1] - p[1]) * n[1]
        (a if su >= 0 else b).append(u)
        if (su >= 0) != (sv >= 0):
            t = su / (su - sv)
            q = (u[0] + t * (v[0] - u[0]), u[1] + t * (v[1] - u[1]))
            a.append(q)
            b.append(q)
            ends.append(q)
    return a, b, ends


FIELDS = []                     # the fields the hedgerows bound (convex polygons), for the painter's crops


def hedgerows():
    """The hill's fields: hedges on low banks, the way old fields get split. The hill is first cut into blocks about
    1,600 across, then each block again and again across its long way, at a slant, until its fields are 0.15 to 0.8
    million square units. Each cut is a hedge (it wavers a little), with a gate in it every 700 or so. None runs
    across a start's ground, a lane, the village or the brook, nor in front of the hill team's support trench."""
    cuts = []
    def split(points, k, depth):
        a = poly_area(points)
        if depth > 9 or a < 300000 or (a < 800000 and hash01(k, 71) < 0.45):
            FIELDS.append(points)
            return
        cx, cz = sum(p[0] for p in points) / len(points), sum(p[1] for p in points) / len(points)
        long_x = max(p[0] for p in points) - min(p[0] for p in points) > max(p[1] for p in points) - min(p[1] for p in points)
        ang = (0 if long_x else math.pi / 2) + (hash01(k, 72) - 0.5) * 0.9      # cut across the long way, slanted
        n = (math.cos(ang), math.sin(ang))
        shift = (hash01(k, 73) - 0.5) * 0.3 * math.sqrt(a)
        a1, a2, ends = cut_convex(points, (cx + n[0] * shift, cz + n[1] * shift), n)
        if len(ends) != 2 or len(a1) < 3 or len(a2) < 3:
            FIELDS.append(points)
            return
        cuts.append(ends)
        split(a1, k * 2 + 1, depth + 1)
        split(a2, k * 2 + 2, depth + 1)
    bw, bh, x_lo = 1650, 1560, 7300
    nodes = {}
    for i in range(5):
        for j in range(10):
            jx = 0 if i in (0, 4) else (hash01(i * 31 + j, 74) - 0.5) * 500
            jz = 0 if j in (0, 9) else (hash01(i * 31 + j, 75) - 0.5) * 500
            nodes[i, j] = (min(W + 50, x_lo + i * bw) + jx, -50 + j * (H + 100) / 9 + jz)
    for i in range(4):
        for j in range(9):
            quad = [nodes[i, j], nodes[i + 1, j], nodes[i + 1, j + 1], nodes[i, j + 1]]
            if i > 0:
                cuts.append([nodes[i, j], nodes[i, j + 1]])
            if j > 0:
                cuts.append([nodes[i, j], nodes[i + 1, j]])
            split(quad, 1000 + i * 17 + j, 0)
    out = []
    for c, (p, q) in enumerate(cuts):
        length = math.hypot(q[0] - p[0], q[1] - p[1])
        n = max(2, int(length / 50))
        nx, nz = -(q[1] - p[1]) / length, (q[0] - p[0]) / length
        gates = [hash01(c * 7 + g, 76) for g in range(max(1, int(length / 700)))]
        cur = []
        for s in range(n + 1):
            t = s / n
            w = 16 * math.sin(2 * math.pi * t * length / 900 + 6.28 * hash01(c, 77)) + 7 * math.sin(2 * math.pi * t * length / 260 + 6.28 * hash01(c, 78))
            x, z = p[0] + t * (q[0] - p[0]) + w * nx, p[1] + t * (q[1] - p[1]) + w * nz
            bad = x < fire_B(z) + SUPPORT_BACK + 160 or x > W - 30 or z < 30 or z > H - 30 or near_pad(x, z, 40) \
                or near_road(x, z, 60) or math.hypot(x - VILLAGE[0], z - VILLAGE[1]) < 420 or near_water(x, z, 90) or sandy_end(x, z) \
                or any(abs(t - g) * length < 45 for g in gates)
            if bad:
                if len(cur) > 1:
                    out.append(cur)
                cur = []
            else:
                cur.append((x, z))
        if len(cur) > 1:
            out.append(cur)
    return out


def wrecks():
    """Wrecked tanks in the zone and landing craft on the beach: (kind, x, z, heading degrees)."""
    out = []
    k = 0
    while sum(1 for w in out if w[0] == "tank") < 16 and k < 4000:
        k += 1
        z = 200 + hash01(k, 81) * (H - 400)
        x = fire_A(z) + 450 + hash01(k, 82) * (fire_B(z) - fire_A(z) - 900)
        if near_water(x, z, 150) or south_of_inlet(x, z) or near_road(x, z, 70) or any(math.hypot(x - a, z - b) < 500 for _, a, b, _ in out):
            continue
        out.append(("tank", x, z, 360 * hash01(k, 83)))
    k = 0
    while sum(1 for w in out if w[0] == "craft") < 9 and k < 4000:
        k += 1
        z = 3000 + hash01(k, 84) * 6800
        x = coast(z) + lerp(-120, beach_width(z) * 0.55, hash01(k, 85))
        if any(abs(z - dz) < 200 for dz in DRAWS) or any(math.hypot(x - a, z - b) < 700 for c, a, b, _ in out if c == "craft"):
            continue
        out.append(("craft", x, z, 90 + 40 * (hash01(k, 86) - 0.5)))
    return out


DUNES, CRATERS, WIRE_BELTS, HEDGES, WRECKS = dunes(), craters(), wire_belts(), hedgerows(), wrecks()


# ---- More of what the drawing shows and the converter builds (called once the drawing's helpers exist).
def lines_of(side):
    """A trench line's fire trench function, crossings, southern end and which way the enemy is (+1 east)."""
    return (fire_A, CROSS_A, END_A, 1) if side == "beach" else (fire_B, CROSS_B, END_B, -1)


def fire_runs(side):
    """The z ranges a line's trenches are dug over, between its crossings, down to the inlet's bank."""
    fn, gaps, end, _ = lines_of(side)
    return runs(fn, [(g, CROSS_GAP) for g in gaps], 0, end)


def comm_trenches():
    """Communication trenches: zigzags back from the support trench toward the bases, every 760 or so: (side, points)."""
    out = []
    for side in ("beach", "hill"):
        fn, gaps, end, sgn = lines_of(side)
        all_gaps = gaps + [end + 200]
        for k, zc in enumerate(range(350, int(end) - 150, 760)):
            if any(abs(zc - g) < 260 for g in all_gaps):
                continue
            x0 = fn(zc) - sgn * SUPPORT_BACK
            x1 = x0 - sgn * (380 + 260 * hash01(k, 102 + sgn))
            pts = zigzag(x0, zc, x1, zc + 120 * (hash01(k, 104) - 0.5))
            # Clear of every spot and start's ground (their level pads would fill it in).
            spots = [(x, z) for x, z, *_ in SPOTS_AT_STARTS + SPOTS_BEACH + SPOTS_HILL]
            if any(dist_to_path(sx, sz, pts) < 200 for sx, sz in spots) or any(dist_to_path(sx, sz, pts) < PAD_R + 150 for _, _, sx, sz in STARTS):
                continue
            out.append((side, pts))
    return out


def mg_nests():
    """Sandbag machine-gun nests on the fire trench's parapet every 900: (side, x, z)."""
    out = []
    for side in ("beach", "hill"):
        fn, gaps, end, sgn = lines_of(side)
        all_gaps = gaps + [end + 200]
        for zc in range(600, int(end) - 100, 900):
            if all(abs(zc - g) > 200 for g in all_gaps):
                out.append((side, fn(zc) + sgn * 100, zc))
    return out


def bunkers():
    """The concrete bunkers: one on the fire line at the edge of each crossing (between its road and the trenches' end),
    one at each line's south end (the dragon's teeth run on from it), one by each draw, and the coastal gun casemate.
    (kind, x, z, the way its gun slit faces in degrees: 0 south, 90 east)."""
    out = []
    for side in ("beach", "hill"):
        fn, gaps, end, sgn = lines_of(side)
        face = 90 if sgn > 0 else 270
        for g in gaps:
            out.append(("crossing", fn(g + 140), g + 140, face))
        out.append(("end", fn(end + 80) - sgn * 150, end + 80, face))                     # behind the line, by the teeth
    for dz in DRAWS:
        out.append(("draw", berm(dz) + foredune_width(dz) + 90, dz - DRAW_W / 2 - 50, 270))
    out.append(("casemate", BATTERY[0], BATTERY[1], 270))
    return out


def hedgehogs():
    """Steel hedgehogs in two broken rows along the beach: (x, z)."""
    out = []
    for frac, salt in ((0.18, 110), (0.32, 111)):
        for z in range(140, H - 60, 72):
            x = coast(z) + frac * beach_width(z)
            if headland(z) > 0.2 or hash01(z // 72, salt) < 0.28 or any(abs(z - dz) < 160 for dz in DRAWS) or near_water(x, z, 60):
                continue
            out.append((x, z))
    return out


def pines():
    """Pines on the back dunes along the beach team's rear ridge: (x, z, size 0..1)."""
    out = []
    for k in range(150):
        z = 100 + hash01(k, 99) * (H - 1800)
        x = fire_A(z) - 420 - 300 * hash01(k, 100)
        if heath(x, z) > -0.05 or near_pad(x, z, 40) or near_road(x, z, 60) or near_water(x, z, 80):
            continue
        out.append((x, z, hash01(k, 101)))
    return out


def village_houses():
    """The ruined village's houses round the church: (x, z, turn in degrees)."""
    vx, vz = VILLAGE
    out = []
    for k in range(14):
        a, r = 6.28 * hash01(k, 95), 120 + 260 * hash01(k, 96)
        out.append((vx + r * math.cos(a), vz + r * math.sin(a), int(360 * hash01(k, 97))))
    return out


def orchard():
    return [(10250 + 60 * (k % 8) + 14 * hash01(k, 98), 7900 + 70 * (k // 8)) for k in range(40)]


# ---- Measures printed for the plan (the converter's checks measure the built map instead).
def band_area(x_lo, x_hi, step=64):
    return sum(max(0.0, x_hi(z) - x_lo(z)) * step for z in range(0, H, step))


def nearest_on(fn, x, z, step=32):
    return min(math.hypot(x - fn(zz), z - zz) for zz in range(0, H + 1, step))


# ---- The drawing.
S = 16
LEGEND = 340
PROFILE_H = 230
SECTION_H = 190
TOP = 64


def P(x, z):
    return f"{x / S:.1f},{z / S + TOP:.1f}"


def poly(points, **attrs):
    a = " ".join(f'{k.replace("_", "-")}="{v}"' for k, v in attrs.items())
    return f'<polygon points="{" ".join(P(x, z) for x, z in points)}" {a}/>'


def line(points, **attrs):
    a = " ".join(f'{k.replace("_", "-")}="{v}"' for k, v in attrs.items())
    return f'<polyline points="{" ".join(P(x, z) for x, z in points)}" fill="none" {a}/>'


def outline(x, z, rx, rz, k, n=28, rough=0.16):
    return [(x + rx * wobble(2 * math.pi * i / n, k, rough) * math.cos(2 * math.pi * i / n),
             z + rz * wobble(2 * math.pi * i / n, k, rough) * math.sin(2 * math.pi * i / n)) for i in range(n)]


def band(x_lo, x_hi, step=48, z0=0, z1=H):
    zs = list(range(int(z0), int(z1) + 1, step))
    return [(x_lo(z), z) for z in zs] + [(x_hi(z), z) for z in reversed(zs)]


def crenellated(fn, z0, z1, bay=88, off=14):
    """A fire trench's trace: fire bays alternately forward and back of the line, joined by short traverses."""
    pts, z, i = [], z0, 0
    while z < z1:
        zn = min(z1, z + bay)
        d = off if i % 2 == 0 else -off
        pts += [(fn(z) + d, z), (fn(zn) + d, zn)]
        z, i = zn, i + 1
    return pts


def zigzag(x0, z0, x1, z1, leg=120, amp=40):
    n = max(2, int(math.hypot(x1 - x0, z1 - z0) / leg))
    nx, nz = -(z1 - z0), x1 - x0
    m = math.hypot(nx, nz) or 1
    return [(lerp(x0, x1, i / n) + (amp if i % 2 else -amp) * nx / m * (0 < i < n), lerp(z0, z1, i / n) + (amp if i % 2 else -amp) * nz / m * (0 < i < n)) for i in range(n + 1)]


def runs(fn, gaps, z_lo=0, z_hi=H):
    """The z ranges of a trench line between its gaps, each gap (z, width)."""
    out, lo = [], z_lo
    for g, width in sorted(gaps):
        out.append((lo, g - width / 2))
        lo = g + width / 2
    out.append((lo, z_hi))
    return [(a, b) for a, b in out if b - a > 40]


def inlet_bank(side, scale=1.0, from_x=-1e9):
    """One bank of the inlet (side +1 south, -1 north), its wavering width times scale, from where the middle line is
    from_x past the waterline (negative: out to sea)."""
    pts = []
    for i, (x, z, w) in enumerate(INLET_PTS):
        if x < coast(z) + from_x:
            continue
        (ax, az, _), (bx, bz, _) = INLET_PTS[max(0, i - 1)], INLET_PTS[min(len(INLET_PTS) - 1, i + 1)]
        m = math.hypot(bx - ax, bz - az)
        nx, nz = -(bz - az) / m * side, (bx - ax) / m * side
        ww = w * (1 + 0.16 * wave(INLET_S[i], 33 if side > 0 else 34, 1, 900)) * scale
        pts.append((x + nx * ww, z + nz * ww))
    return pts


def inlet_water(scale, from_x):
    """The inlet's water (to scale times its width) as a polygon: the north bank, round the cove's end, the south bank."""
    (ax, az, _), (bx, bz, w) = INLET_PTS[-2], INLET_PTS[-1]
    a = math.atan2(bz - az, bx - ax)
    cap = [(bx + w * scale * math.cos(a + u), bz + w * scale * math.sin(a + u)) for u in [math.pi * (k / 12 - 0.5) for k in range(1, 12)]]
    return inlet_bank(-1, scale, from_x) + cap + inlet_bank(1, scale, from_x)[::-1]


def ribbon(spine, width_at):
    """A polygon round a polyline, its width given along it (0 to 1)."""
    left, right = [], []
    for i, (x, z) in enumerate(spine):
        a, b = spine[max(0, i - 1)], spine[min(len(spine) - 1, i + 1)]
        dx, dz = b[0] - a[0], b[1] - a[1]
        m = math.hypot(dx, dz) or 1
        w = width_at(i / (len(spine) - 1)) / 2
        left.append((x - dz / m * w, z + dx / m * w))
        right.append((x + dz / m * w, z - dx / m * w))
    return left + right[::-1]


def svg():
    o = []
    mw, mh = W / S, H / S
    w, h = int(mw + LEGEND), int(TOP + mh + 24 + PROFILE_H + 16 + SECTION_H + 10)
    o.append(f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" viewBox="0 0 {w} {h}" font-family="Segoe UI, Arial, sans-serif">')
    o.append('<defs>'
             '<pattern id="slip" width="5" height="5" patternUnits="userSpaceOnUse" patternTransform="rotate(45)"><rect width="5" height="5" fill="#d9bf86"/><line x1="0" y1="0" x2="0" y2="5" stroke="#9b7d45" stroke-width="1.6"/></pattern>'
             '<pattern id="cliff" width="5" height="5" patternUnits="userSpaceOnUse" patternTransform="rotate(-30)"><rect width="5" height="5" fill="#9a958a"/><line x1="0" y1="0" x2="0" y2="5" stroke="#55524b" stroke-width="2"/></pattern>'
             '<pattern id="marsh" width="8" height="6" patternUnits="userSpaceOnUse"><rect width="8" height="6" fill="#9fae86"/><path d="M1,5 l1,-3 M4,5 l1,-3 M6,5 l0.5,-2" stroke="#5f7244" stroke-width="0.7"/></pattern>'
             '<pattern id="heath" width="9" height="7" patternUnits="userSpaceOnUse"><rect width="9" height="7" fill="#a89d72"/><circle cx="2" cy="2" r="0.9" fill="#8a6f86"/><circle cx="6.5" cy="5" r="0.8" fill="#7d8a55"/></pattern>'
             '<linearGradient id="seaG" x1="0" x2="1" y1="0" y2="0"><stop offset="0" stop-color="#1d5d84"/><stop offset="1" stop-color="#2f7fa8"/></linearGradient>'
             '</defs>')
    o.append('<rect width="100%" height="100%" fill="#f4f3ef"/>')
    o.append(f'<text x="8" y="20" font-size="15" font-weight="700" fill="#1d2329">Marram Line · plan</text>')
    # ---- land first, then the sea over it (so the heath and marsh blobs end at the waterline)
    o.append(f'<rect x="0" y="{TOP}" width="{mw}" height="{mh}" fill="#cdbb8e"/>')                    # dune sand
    o.append(poly(outline(*HEATH[:4], HEATH[4], 64, HEATH[5]), fill="url(#heath)"))                   # the headland's heath
    o.append(poly(outline(3700, 10420, 620, 230, 7, 40, 0.14), fill="url(#marsh)"))                   # salt marsh on the inlet's north bank
    o.append(poly(band(lambda z: 0, coast), fill="url(#seaG)"))
    o.append(poly(band(lambda z: coast(z) - 640, coast), fill="#4b9cc2"))
    o.append(poly(band(lambda z: coast(z) - 240, coast), fill="#79bcd6"))
    for zs in _split([z for z in range(0, H, 24) if bar_on(z)], 30):
        if len(zs) > 2:
            o.append(poly(ribbon([(coast(z) - BAR_AT, z) for z in zs], lambda t: 140 * math.sin(math.pi * min(1, max(0.08, t)))), fill="#a9d7df", opacity="0.85"))
    # ---- the bands over the land
    o.append(poly(band(coast, berm), fill="#ead9a6"))                                                    # the beach
    o.append(poly(band(coast, lambda z: coast(z) + 0.45 * beach_width(z)), fill="#c9b98a", opacity="0.55"))   # wet sand
    o.append(poly(band(lambda z: fire_A(z) + 140, lambda z: fire_B(z) - 140), fill="#8f8a68"))           # the zone
    o.append(poly(band(lambda z: fire_B(z) - 140, lambda z: W), fill="#a7bf7f"))                         # the hill's fields
    # the headland's cliffs
    hl = [z for z in range(0, 3400, 32) if headland(z) > 0.05]
    o.append(poly([(berm(z), z) for z in hl] + [(berm(z) + 90 * headland(z), z) for z in reversed(hl)], fill="url(#cliff)"))
    # the foredune ridge, its steep seaward face, and the draws through it
    nf = [z for z in range(0, H, 32) if headland(z) < 0.3]
    for zs in _split(nf):
        o.append(poly([(berm(z), z) for z in zs] + [(berm(z) + 0.3 * foredune_width(z), z) for z in reversed(zs)], fill="url(#slip)"))
        o.append(poly([(berm(z) + 0.3 * foredune_width(z), z) for z in zs] + [(berm(z) + foredune_width(z), z) for z in reversed(zs)], fill="#d8c48f"))
    for dz in DRAWS:
        o.append(poly([(berm(dz - DRAW_W / 2) - 10, dz - DRAW_W / 2), (berm(dz - DRAW_W / 2) + foredune_width(dz) + 10, dz - DRAW_W / 2),
                       (berm(dz + DRAW_W / 2) + foredune_width(dz) + 10, dz + DRAW_W / 2), (berm(dz + DRAW_W / 2) - 10, dz + DRAW_W / 2)], fill="#e2d3a2"))
    # the sandy end: the shore south of the inlet, from the beach to the hill's foot
    south = [p for p in inlet_bank(1) if p[0] > berm(p[1]) and p[0] < fire_B(p[1]) + 200]
    east = [(fire_B(z) + 200 + wave(z, 37, 110, 800), z) for z in range(int(south[-1][1]), H + 1, 48)]
    west = [(berm(z), z) for z in range(H, int(south[0][1]), -48)]
    o.append(poly(south + east + [(fire_B(H) + 200, H), (berm(H), H)] + west, fill="#e2d1a0"))
    # the inlet: shallows round its edge (under 8), then 8 to 20, then the channel (over 20); offshore only its channel
    for scale, colour, from_x in ((1.0, "#79bcd6", -60), (0.85, "#4b9cc2", -60), (0.736, "#2a7099", -900)):
        o.append(poly(inlet_water(scale, from_x), fill=colour))
    # ---- hill contours (every 40, from the trench line's 42 to the crest's 215)
    for level in (80, 120, 160, 200):
        pts = []
        for z in range(0, H + 1, 48):
            lo, hi = fire_B(z) + 10, crest(z)
            if rise(hi, z) < level:
                pts.append(None)
                continue
            for _ in range(28):
                mid = (lo + hi) / 2
                if rise(mid, z) < level:
                    lo = mid
                else:
                    hi = mid
            pts.append(None if near_water((lo + hi) / 2, z, 30) else ((lo + hi) / 2, z))
        for run in _split_none(pts):
            o.append(line(run, stroke="#6f8a52", stroke_width="0.7", opacity="0.75"))
            if len(run) > 20:
                x, z = run[len(run) // 3]
                o.append(f'<text x="{x / S + 2:.1f}" y="{z / S + TOP:.1f}" font-size="7.5" fill="#4d6638">{level}</text>')
    o.append(line([(crest(z), z) for z in range(0, H + 1, 48)], stroke="#4d6638", stroke_width="1.4", stroke_dasharray="2 3"))
    # ---- the hedgerows, the village, orchard, windmill
    for pts in HEDGES:
        o.append(line(pts, stroke="#3f6b2f", stroke_width="1.8", stroke_linecap="round", stroke_linejoin="round"))
    vx, vz = VILLAGE
    for bx, bz, turn in village_houses():
        o.append(f'<rect x="{bx / S - 3:.1f}" y="{bz / S + TOP - 2:.1f}" width="6" height="4" transform="rotate({turn} {bx / S:.1f} {bz / S + TOP:.1f})" fill="#b7a189" stroke="#5c4a39" stroke-width="0.6"/>')
    o.append(f'<rect x="{vx / S - 4:.1f}" y="{vz / S + TOP - 7:.1f}" width="8" height="14" fill="#c9b7a1" stroke="#4a3b2c" stroke-width="0.8"/>')
    o.append(f'<text x="{vx / S + 30:.1f}" y="{vz / S + TOP + 4:.1f}" font-size="9" fill="#1d2329">ruined village,</text>')
    o.append(f'<text x="{vx / S + 30:.1f}" y="{vz / S + TOP + 15:.1f}" font-size="9" fill="#1d2329">church</text>')
    for ox, oz in orchard():                                                    # the orchard, below the village
        o.append(f'<circle cx="{ox / S:.1f}" cy="{oz / S + TOP:.1f}" r="1.6" fill="#4d7a35"/>')
    wx, wz = WINDMILL
    o.append(f'<circle cx="{wx / S:.1f}" cy="{wz / S + TOP:.1f}" r="{420 / S:.1f}" fill="#b8cb8f" opacity="0.6"/>')
    o.append(f'<g transform="translate({wx / S:.1f},{wz / S + TOP:.1f})"><circle r="3" fill="#c9b7a1" stroke="#4a3b2c" stroke-width="0.7"/>'
             '<path d="M-7,-7 L7,7 M-7,7 L7,-7" stroke="#4a3b2c" stroke-width="1"/></g>')
    o.append(f'<text x="{wx / S + 8:.1f}" y="{wz / S + TOP - 7:.1f}" font-size="8.5" fill="#1d2329">knoll, windmill</text>')
    # ---- the dunes and the starts' level ground
    for k, (x, z, r, hgt) in enumerate(DUNES):
        o.append(poly(outline(x, z, r * 1.6, r, 300 + k, 26, 0.18), fill="#dcc690", stroke="#a7884f", stroke_width="0.5"))
        o.append(poly(outline(x + r * 0.9, z, r * 0.55, r * 0.85, 300 + k, 18, 0.15), fill="url(#slip)", opacity="0.9"))
    for n, team, x, z in STARTS:
        o.append(f'<circle cx="{x / S:.1f}" cy="{z / S + TOP:.1f}" r="{PAD_R / S:.1f}" fill="{"#c8c49a" if team == "beach" else "#b9cf92"}" stroke="#7d7a55" stroke-width="0.8" stroke-dasharray="3 3"/>')
    for x, z, size in pines():                                                  # back-dune pines along the rear ridge
        o.append(f'<circle cx="{x / S:.1f}" cy="{z / S + TOP:.1f}" r="{1.6 + 1.2 * size:.1f}" fill="#4f6b3a" opacity="0.85"/>')
    # ---- the zone: craters (flooded ones blue), wrecks, the farm, the brook
    for k, (x, z, r, d, deep, wet) in enumerate(CRATERS):
        o.append(poly(outline(x, z, r, r, 600 + k, 18, 0.2), fill="#5f8fa3" if wet else ("#5e5845" if deep else "#7a7356"), stroke="#4a4535", stroke_width="1.1" if deep else "0.5"))
    for kind, x, z, hdg in WRECKS:
        L, Wd = (160, 60) if kind == "craft" else (90, 54)
        o.append(f'<rect x="{x / S - L / S / 2:.1f}" y="{z / S + TOP - Wd / S / 2:.1f}" width="{L / S:.1f}" height="{Wd / S:.1f}" transform="rotate({hdg:.0f} {x / S:.1f} {z / S + TOP:.1f})" fill="#4b4a47" stroke="#1e1e1d" stroke-width="0.6"/>')
    fx, fz = FARM
    o.append(poly(outline(fx, fz, 230, 170, 9, 12, 0.12), fill="none", stroke="#6a5a48", stroke_width="1.6", stroke_dasharray="5 2"))
    o.append(f'<text x="{fx / S:.1f}" y="{fz / S + TOP - 13:.1f}" font-size="8.5" fill="#1d2329" text-anchor="middle">ruined farm</text>')
    o.append(line(curve(BROOK_DRY, 8), stroke="#5d6b45", stroke_width="1.8", stroke_dasharray="3 2"))
    o.append(f'<text x="{BROOK_DRY[0][0] / S + 4:.1f}" y="{BROOK_DRY[0][1] / S + TOP + 3:.1f}" font-size="8.5" fill="#1d2329">brook (dry bed)</text>')
    # ---- wire belts, trenches, crossings, bunkers
    for side, z0, z1, off in WIRE_BELTS:
        fn, sgn = (fire_A, 1) if side == "beach" else (fire_B, -1)
        pts = [(fn(z) + sgn * off, z) for z in _frange(z0, z1, 32)]
        if len(pts) > 1:
            o.append(line(pts, stroke="#3a3a3a", stroke_width="0.9", stroke_dasharray="1.2 1.8"))
    for side in ("beach", "hill"):
        fn, gaps, end, sgn = lines_of(side)
        for z0, z1 in fire_runs(side):
            o.append(line(crenellated(fn, z0, z1), stroke="#4a3220", stroke_width="2"))
            o.append(line([(fn(z) - sgn * SUPPORT_BACK, z) for z in _frange(z0, z1, 40)], stroke="#5e4630", stroke_width="1.1"))
        for g in gaps:                                                          # the crossings
            o.append(f'<rect x="{(fn(g) - 130) / S:.1f}" y="{(g - CROSS_W / 2) / S + TOP:.1f}" width="{260 / S:.1f}" height="{CROSS_W / S:.1f}" fill="#3ddc97" opacity="0.75" stroke="#0c5e3b" stroke-width="0.8"/>')
    for side, pts in comm_trenches():                                           # communication trenches back to the bases
        o.append(line(pts, stroke="#5e4630", stroke_width="0.9"))
    for side, x, z in mg_nests():                                               # machine-gun nests on the parapet
        o.append(f'<circle cx="{x / S:.1f}" cy="{z / S + TOP:.1f}" r="2.2" fill="#c8b88a" stroke="#4a3220" stroke-width="0.8"/>')
    for kind, x, z, face in bunkers():                                          # bunkers: crossings, the lines' ends, the draws
        if kind != "casemate":
            s = 5 if kind == "bank" else 4
            o.append(f'<rect x="{x / S - s:.1f}" y="{z / S + TOP - 3:.1f}" width="{2 * s}" height="6" fill="#8d8f92" stroke="#2b2c2e" stroke-width="0.8"/>')
    for x, z in hedgehogs():                                                    # the beach's hedgehogs in broken rows
        o.append(f'<path d="M{x / S - 1.6:.1f},{z / S + TOP - 1.6:.1f} l3.2,3.2 M{x / S - 1.6:.1f},{z / S + TOP + 1.6:.1f} l3.2,-3.2" stroke="#3b3b3b" stroke-width="0.8"/>')
    lx_, lz_ = LIGHTHOUSE
    o.append(f'<circle cx="{lx_ / S:.1f}" cy="{lz_ / S + TOP:.1f}" r="4" fill="#fff" stroke="#c0392b" stroke-width="1.6"/>')
    o.append(f'<text x="{lx_ / S + 7:.1f}" y="{lz_ / S + TOP + 3:.1f}" font-size="8.5" fill="#1d2329">lighthouse</text>')
    bx_, bz_ = BATTERY
    o.append(f'<rect x="{bx_ / S - 6:.1f}" y="{bz_ / S + TOP - 4:.1f}" width="12" height="8" fill="#8d8f92" stroke="#2b2c2e" stroke-width="0.9"/>')
    o.append(f'<text x="{bx_ / S + 9:.1f}" y="{bz_ / S + TOP + 3:.1f}" font-size="8.5" fill="#1d2329">gun casemate</text>')
    # ---- roads and the vehicle lanes
    styles = {"track": ("#f3e6bf", "1.6", "none"), "lane": ("#e8dcb5", "1.6", "none"), "old road": ("#d6cba5", "1.3", "5 3")}
    for kind, pts in ROADS:
        c, wdt, dash = styles[kind]
        o.append(line(pts, stroke="#6b5f45", stroke_width=str(float(wdt) + 1.2), opacity="0.5", stroke_dasharray=dash))
        o.append(line(pts, stroke=c, stroke_width=wdt, stroke_dasharray=dash))
    for za in CROSS_A:
        for zb in sorted(CROSS_B, key=lambda zb: abs(zb - za))[:2]:
            if abs(zb - za) > 2200:
                continue
            o.append(f'<line x1="{(fire_A(za) + 200) / S:.1f}" y1="{za / S + TOP:.1f}" x2="{(fire_B(zb) - 200) / S:.1f}" y2="{zb / S + TOP:.1f}" stroke="#d35400" stroke-width="1.3" stroke-dasharray="6 4" opacity="0.7"/>')
    # ---- metal, vents, starts
    def spot(x, z, m):
        s = 4.3 if m >= 3 else (3.7 if m > 2.5 else 3.1)
        o.append(f'<rect x="{x / S - s:.1f}" y="{z / S + TOP - s:.1f}" width="{2 * s}" height="{2 * s}" transform="rotate(45 {x / S:.1f} {z / S + TOP:.1f})" fill="#e8ecf2" stroke="#222" stroke-width="0.7"/>')
    for x, z, m, _ in SPOTS_AT_STARTS:
        spot(x, z, m)
    for x, z, m in SPOTS_BEACH + SPOTS_HILL + SPOTS_ZONE:
        spot(x, z, m)
    for _, x, z in VENTS:
        o.append(f'<circle cx="{x / S:.1f}" cy="{z / S + TOP:.1f}" r="5" fill="#57d163" stroke="#1d5e25" stroke-width="1"/>')
    for n, team, x, z in STARTS:
        colour = "#c0392b" if team == "beach" else "#1f5fa8"
        o.append(f'<circle cx="{x / S:.1f}" cy="{z / S + TOP:.1f}" r="11" fill="{colour}" stroke="#fff" stroke-width="2"/>')
        o.append(f'<text x="{x / S:.1f}" y="{z / S + TOP + 4:.1f}" font-size="11" font-weight="700" fill="#fff" text-anchor="middle">{n}</text>')
    # ---- labels on the map
    o.append(f'<text x="{(coast(9000) - 1100) / S:.1f}" y="{9000 / S + TOP:.1f}" font-size="8.5" font-style="italic" fill="#e3f2f8">ships keep out past the bars</text>')
    o.append(f'<text x="{(coast(6900) - 1500) / S:.1f}" y="{6900 / S + TOP:.1f}" font-size="8" fill="#d6ebf3">60 deep</text>')
    o.append(f'<text x="{4300 / S:.1f}" y="{10850 / S + TOP:.1f}" font-size="9" font-weight="700" font-style="italic" fill="#e3f2f8">the inlet: {INLET_DEPTH} deep, ships of every size</text>')
    o.append(f'<text x="{6250 / S:.1f}" y="{10715 / S + TOP:.1f}" font-size="8" font-style="italic" fill="#e3f2f8">no land unit crosses</text>')
    o.append(f'<text x="{8900 / S:.1f}" y="{10960 / S + TOP:.1f}" font-size="8.5" font-weight="700" fill="#e3f2f8" text-anchor="middle">the cove</text>')
    o.append(f'<text x="{8900 / S:.1f}" y="{11640 / S + TOP:.1f}" font-size="8.5" fill="#1d2329" text-anchor="middle">sandy beaches: the hill team\'s shipyards</text>')
    o.append(f'<text x="{4600 / S:.1f}" y="{12140 / S + TOP:.1f}" font-size="9" font-weight="700" fill="#1d2329">THE SANDY END: the hill team\'s own shore</text>')
    o.append(f'<text x="{3700 / S:.1f}" y="{10270 / S + TOP:.1f}" font-size="8" fill="#1d2329">salt marsh</text>')
    o.append(f'<text x="{(coast(1450) + 520) / S:.1f}" y="{1080 / S + TOP:.1f}" font-size="9" fill="#1d2329">headland: heath on top,</text>')
    o.append(f'<text x="{(coast(1450) + 520) / S:.1f}" y="{1260 / S + TOP:.1f}" font-size="9" fill="#1d2329">cliffs 66 high</text>')
    o.append(f'<text x="{(fire_A(8300) + 330) / S:.1f}" y="{8300 / S + TOP + 3:.1f}" font-size="8.5" fill="#1d2329">salient</text>')
    o.append(f'<text x="{(fire_B(3600) - 700) / S:.1f}" y="{3600 / S + TOP + 30:.1f}" font-size="8.5" fill="#1d2329">salient</text>')
    o.append(f'<text x="{crest(600) / S + 4:.1f}" y="{600 / S + TOP:.1f}" font-size="9" font-style="italic" fill="#3d5229">crest ~{CREST}</text>')
    o.append(f'<rect x="0" y="{TOP}" width="{mw}" height="{mh}" fill="none" stroke="#1d2329" stroke-width="1"/>')
    # ---- the bands named above the map, each over its middle at z 5200
    zl = 5200
    names = [("SEA", coast(zl) / 2), ("BEACH", (coast(zl) + berm(zl)) / 2), ("DUNES", (berm(zl) + fire_A(zl)) / 2 - 300), ("TRENCHES", fire_A(zl)),
             ("DEAD MAN'S ZONE", zone_mid(zl)), ("TRENCHES", fire_B(zl)), ("THE RISE", (fire_B(zl) + crest(zl)) / 2), ("CREST", crest(zl))]
    for i, (text, x) in enumerate(names):
        yy = TOP - 22 + 11 * (i % 2)
        o.append(f'<line x1="{x / S:.1f}" y1="{yy + 3}" x2="{x / S:.1f}" y2="{TOP}" stroke="#1d2329" stroke-width="0.6"/>')
        o.append(f'<text x="{x / S:.1f}" y="{yy:.1f}" font-size="8.5" font-weight="700" letter-spacing="0.6" fill="#1d2329" text-anchor="middle">{html.escape(text)}</text>')
    # ---- legend
    lx, y = mw + 16, TOP + 4
    def leg(text, size=12, weight="400", dy=18, colour="#1d2329"):
        nonlocal y
        o.append(f'<text x="{lx}" y="{y}" font-size="{size}" font-weight="{weight}" fill="{colour}">{html.escape(text)}</text>')
        y += dy
    leg("Marram Line", 18, "700", 22)
    leg(f"{MAP_UNITS[0]} × {MAP_UNITS[1]}, 4v4, beach team vs hill team", 12, "400", 16)
    leg(f"Not symmetric · 1 px = {S} game units · north up", 11, "400", 24, "#555")
    items = [("#c0392b", "circle", "Beach team start (0-3)"), ("#1f5fa8", "circle", "Hill team start (4-7)"),
             ("#e8ecf2", "diamond", "Metal (big: zone 3.0; mid: salient 2.8)"), ("#57d163", "circle", "Geothermal vent"),
             ("#2a7099", "rect", "Over 20 deep: ships, hovers (inlet too)"), ("#4b9cc2", "rect", "8 to 20 deep: ships and land units"),
             ("#79bcd6", "rect", "Under 8 deep: land units, no ships"), ("#a9d7df", "rect", "Sandbar, 5 deep (rips between)"),
             ("url(#slip)", "rect", "Steep sand, 36° (bots only)"), ("url(#cliff)", "rect", "Cliff (spiders only)"),
             ("#e2d3a2", "rect", "Draw: vehicle way off the beach"), ("#e2d1a0", "rect", "The sandy end (the hill team's shore)"),
             ("#3ddc97", "rect", "Trench crossing for vehicles (220)"),
             ("#8d8f92", "rect", "Bunker (indestructible)"), ("#5e5845", "rect", "Bomb crater, 40° walls (bots only)"),
             ("#7a7356", "rect", "Shell hole (all units)"), ("#5f8fa3", "rect", "Flooded crater"),
             ("#4b4a47", "rect", "Wreck: tank, landing craft (reclaim)")]
    for colour, shape, text in items:
        if shape == "circle":
            o.append(f'<circle cx="{lx + 8}" cy="{y - 4}" r="6" fill="{colour}" stroke="#333" stroke-width="0.8"/>')
        elif shape == "diamond":
            o.append(f'<rect x="{lx + 4}" y="{y - 8}" width="8" height="8" transform="rotate(45 {lx + 8} {y - 4})" fill="{colour}" stroke="#222" stroke-width="0.8"/>')
        else:
            o.append(f'<rect x="{lx}" y="{y - 11}" width="16" height="12" fill="{colour}" stroke="#333" stroke-width="0.8"/>')
        o.append(f'<text x="{lx + 24}" y="{y}" font-size="11" fill="#1d2329">{html.escape(text)}</text>')
        y += 19
    for draw, text in ((lambda yy: f'<polyline points="{lx},{yy} {lx + 6},{yy} {lx + 6},{yy - 3} {lx + 12},{yy - 3} {lx + 12},{yy} {lx + 18},{yy} {lx + 18},{yy - 3} {lx + 24},{yy - 3}" fill="none" stroke="#4a3220" stroke-width="2"/>', "Fire trench (bots in and out)"),
                       (lambda yy: f'<line x1="{lx}" y1="{yy}" x2="{lx + 24}" y2="{yy}" stroke="#5e4630" stroke-width="1.1"/>', "Support, communication trench"),
                       (lambda yy: f'<line x1="{lx}" y1="{yy}" x2="{lx + 24}" y2="{yy}" stroke="#3a3a3a" stroke-width="1.2" stroke-dasharray="1.2 1.8"/>', "Wire: tanks crush it, bots can't"),
                       (lambda yy: f'<path d="M{lx + 2},{yy - 4} l5,5 M{lx + 2},{yy + 1} l5,-5 M{lx + 12},{yy - 4} l5,5 M{lx + 12},{yy + 1} l5,-5" stroke="#3b3b3b" stroke-width="1"/>', "Hedgehogs, stakes (beach)"),
                       (lambda yy: f'<line x1="{lx}" y1="{yy}" x2="{lx + 24}" y2="{yy}" stroke="#3f6b2f" stroke-width="2.2"/>', "Hedgerow on a low bank, gates"),
                       (lambda yy: f'<line x1="{lx}" y1="{yy}" x2="{lx + 24}" y2="{yy}" stroke="#6f8a52" stroke-width="0.9"/>', "Contour every 40"),
                       (lambda yy: f'<line x1="{lx}" y1="{yy}" x2="{lx + 24}" y2="{yy}" stroke="#d6cba5" stroke-width="2" stroke-dasharray="5 3"/>', "Road, track, old road"),
                       (lambda yy: f'<line x1="{lx}" y1="{yy}" x2="{lx + 24}" y2="{yy}" stroke="#d35400" stroke-width="1.6" stroke-dasharray="6 4"/>', "Vehicle lane, crossing to crossing")):
        o.append(draw(y - 4))
        o.append(f'<text x="{lx + 32}" y="{y}" font-size="11" fill="#1d2329">{html.escape(text)}</text>')
        y += 19
    y += 8
    o.append(f'<line x1="{lx}" y1="{y}" x2="{lx + 1000 / S}" y2="{y}" stroke="#1d2329" stroke-width="2"/>')
    o.append(f'<text x="{lx + 1000 / S + 8}" y="{y + 4}" font-size="11" fill="#1d2329">1000 units</text>')
    y += 26
    for t in ["Heights: sea floor to −60; beach 0 to 10;", f"foredune crest about {foredune_crest(0):.0f}; slacks {SLACK};",
              f"headland heath {HEADLAND_TOP}; both trench lines dug", f"where the ground is about {TRENCH_GROUND}; the zone",
              f"about {MEADOW}; the crest about {CREST}.", "", "The rise is about 3.5°: slight, but the hill",
              "team's rear looks down on the whole zone.", "", f"Generated: {len(DUNES)} dunes, {len(CRATERS)} craters,",
              f"{len(HEDGES)} hedgerow runs, {len(WRECKS)} wrecks (seed {SEED})."]:
        leg(t, 11, "400", 15)
    # ---- the profile along z = 6000 (heights exaggerated)
    py0 = TOP + mh + 24
    o.append(f'<text x="8" y="{py0 + 14}" font-size="13" font-weight="700" fill="#1d2329">Profile, west to east, along z = 6000 (heights × 5)</text>')
    base_y, vs = py0 + 150, 5 / S
    pts = [(x, height(x, 6000)) for x in range(0, W + 1, 8)]
    ground = " ".join(f"{x / S:.1f},{base_y - hh * vs:.1f}" for x, hh in pts)
    o.append(f'<rect x="0" y="{py0 + 22}" width="{mw}" height="{PROFILE_H - 22}" fill="#eef3f6"/>')
    o.append(f'<polygon points="0,{base_y + 60 * vs:.1f} {ground} {mw},{base_y + 60 * vs:.1f}" fill="#c9b98a" stroke="#5c4a2a" stroke-width="0.8"/>')
    sea_x = coast(6000)
    o.append(f'<rect x="0" y="{base_y:.1f}" width="{sea_x / S:.1f}" height="{60 * vs + 1:.1f}" fill="#4b9cc2" opacity="0.55"/>')
    o.append(f'<line x1="0" y1="{base_y:.1f}" x2="{mw}" y2="{base_y:.1f}" stroke="#1b4f6b" stroke-width="0.6" stroke-dasharray="3 3"/>')
    o.append(f'<text x="{mw - 4}" y="{base_y - 3:.1f}" font-size="8.5" fill="#1b4f6b" text-anchor="end">water level 0</text>')
    for text, x in (("sea", coast(6000) - 900), ("beach", coast(6000) + 40), ("foredune", berm(6000) + 20), ("dune slacks (bases)", berm(6000) + 700),
                    ("trench", fire_A(6000) - 120), ("dead man's zone", zone_mid(6000) - 380), ("trench", fire_B(6000) - 120),
                    ("the rise (bases in the fields)", fire_B(6000) + 700), ("crest", crest(6000) - 100)):
        hh = height(x, 6000)
        o.append(f'<text x="{x / S:.1f}" y="{base_y - max(hh, 0) * vs - 26:.1f}" font-size="9" fill="#1d2329">{html.escape(text)}</text>')
    for v in (0, 100, 200):
        o.append(f'<text x="3" y="{base_y - v * vs + 3:.1f}" font-size="8" fill="#555">{v}</text>')
    # ---- the fire trench's section, true scale, with T1 units for size
    sy0 = py0 + PROFILE_H + 16
    o.append(f'<text x="8" y="{sy0 + 14}" font-size="13" font-weight="700" fill="#1d2329">The fire trench, true scale (heights not exaggerated), enemy to the right</text>')
    k, gx, gy = 1.5, 20, sy0 + 112
    sec = [(-140, 0)] + TRENCH_SECTION + [(260, 0)]
    o.append(f'<polygon points="{gx:.1f},{gy + 70} ' + " ".join(f"{gx + (d + 140) * k:.1f},{gy - hh * k:.1f}" for d, hh in sec) + f' {gx + 400 * k:.1f},{gy + 70}" fill="#c9b98a" stroke="#5c4a2a" stroke-width="1"/>')
    o.append(f'<line x1="{gx}" y1="{gy}" x2="{gx + 400 * k:.1f}" y2="{gy}" stroke="#555" stroke-width="0.5" stroke-dasharray="3 3"/>')
    def unit(d, base, wdt, hgt, label, fill):
        x0 = gx + (d + 140 - wdt / 2) * k
        o.append(f'<rect x="{x0:.1f}" y="{gy - (base + hgt) * k:.1f}" width="{wdt * k:.1f}" height="{hgt * k:.1f}" fill="{fill}" opacity="0.85" stroke="#222" stroke-width="0.6"/>')
        o.append(f'<text x="{x0 + wdt * k / 2:.1f}" y="{gy - (base + hgt) * k - 4:.1f}" font-size="8.5" fill="#1d2329" text-anchor="middle">{html.escape(label)}</text>')
    unit(14, -32, 22, 30, "hidden", "#c0392b")
    unit(62, -12, 22, 30, "on the step", "#c0392b")
    unit(215, 0, 34, 18, "Stout, 18 tall", "#1f5fa8")
    unit(-115, 0, 22, 30, "Pawn, 30 tall", "#c0392b")
    for text, d, hh in (("parados", -52, 12), ("floor −32, 40 wide", 14, -44), ("fire step −12", 62, -22), ("parapet +6", 102, 14)):
        o.append(f'<text x="{gx + (d + 140) * k:.1f}" y="{gy - hh * k + 4:.1f}" font-size="8.5" fill="#1d2329" text-anchor="middle">{html.escape(text)}</text>')
    tx = gx + 400 * k + 22
    for i, t in enumerate(["Walls 45° to 48°: bots climb them;", "vehicles and hovers don't.", "Floor 40 wide: bots up to HBOT4", "(Razorback) fit, Behemoth doesn't.",
                           f"Dug where the ground is about {TRENCH_GROUND},", "so the floor (10) stays above the water.",
                           "On the step a bot's top third shows", "over the parapet; in the floor it's hidden."]):
        o.append(f'<text x="{tx:.1f}" y="{sy0 + 40 + 15 * i:.1f}" font-size="10.5" fill="#1d2329">{html.escape(t)}</text>')
    o.append("</svg>")
    return "\n".join(o)


def _along(pts, f):
    """The point a fraction f of the way along a polyline."""
    lengths = [math.hypot(b[0] - a[0], b[1] - a[1]) for a, b in zip(pts, pts[1:])]
    d = f * sum(lengths)
    for (a, b), L in zip(zip(pts, pts[1:]), lengths):
        if d <= L:
            return (lerp(a[0], b[0], d / L), lerp(a[1], b[1], d / L))
        d -= L
    return pts[-1]


def _frange(a, b, step):
    v = a
    while v < b:
        yield v
        v += step
    yield b


def _split(zs, gap=40):
    out, cur = [], []
    for z in zs:
        if cur and z - cur[-1] > gap:
            out.append(cur)
            cur = []
        cur.append(z)
    if cur:
        out.append(cur)
    return out


def _split_none(pts):
    out, cur = [], []
    for p in pts:
        if p is None:
            if len(cur) > 1:
                out.append(cur)
            cur = []
        else:
            cur.append(p)
    if len(cur) > 1:
        out.append(cur)
    return out


def table():
    """Everything the converter will read from this table."""
    r1 = lambda v: round(v, 1)
    pts1 = lambda pts: [[r1(x), r1(z)] for x, z in pts]
    return {
        "mapUnits": list(MAP_UNITS), "seed": SEED, "seaFloor": SEA_FLOOR, "bar": [BAR_AT, BAR_HALF, BAR_DEPTH],
        "heights": {"berm": BERM, "slack": SLACK, "headlandTop": HEADLAND_TOP, "trenchGround": TRENCH_GROUND, "meadow": MEADOW,
                    "brookDepth": BROOK_DEPTH, "crest": CREST, "eastEdge": EAST_EDGE, "knoll": KNOLL},
        "trenchSection": TRENCH_SECTION, "supportBack": SUPPORT_BACK, "wire": list(WIRE),
        "lines": {name: [[z, r1(fn(z))] for z in range(0, H + 1, 64)] for name, fn in
                  (("coast", coast), ("berm", berm), ("foredune", lambda z: berm(z) + foredune_width(z)), ("fireA", fire_A), ("fireB", fire_B), ("crest", crest))},
        "heath": list(HEATH),
        "draws": DRAWS, "drawWidth": DRAW_W, "headlandRoadZ": HEADLAND_ROAD_Z, "crossA": CROSS_A, "crossB": CROSS_B, "crossWidth": CROSS_W,
        "inlet": [[r1(x), r1(z), r1(w)] for x, z, w in INLET], "inletDepth": INLET_DEPTH, "inletMouthDepth": INLET_MOUTH_DEPTH,
        "coveDepth": COVE_DEPTH, "coveFrom": COVE_FROM, "inletBank": INLET_BANK, "inletShallows": INLET_SHALLOWS, "sandEnd": SAND_END,
        "linesEnd": {"A": r1(END_A), "B": r1(END_B)}, "brookDry": BROOK_DRY,
        "inletPts": [[r1(x), r1(z), r1(w)] for x, z, w in INLET_PTS], "inletS": [r1(s) for s in INLET_S],
        "fireRuns": {side: [[r1(a), r1(b)] for a, b in fire_runs(side)] for side in ("beach", "hill")},
        "commTrenches": [[side, pts1(pts)] for side, pts in comm_trenches()],
        "mgNests": [[side, r1(x), r1(z)] for side, x, z in mg_nests()],
        "bunkers": [[kind, r1(x), r1(z), face] for kind, x, z, face in bunkers()],
        "hedgehogs": pts1(hedgehogs()), "pines": [[r1(x), r1(z), round(sz, 2)] for x, z, sz in pines()],
        "villageHouses": [[r1(x), r1(z), turn] for x, z, turn in village_houses()], "orchard": pts1(orchard()),
        "starts": [[n, t, x, z] for n, t, x, z in STARTS], "padRadius": PAD_R,
        "spots": [[r1(x), r1(z), m, t] for x, z, m, t in SPOTS_AT_STARTS] + [[r1(x), r1(z), m, "beach"] for x, z, m in SPOTS_BEACH]
                 + [[r1(x), r1(z), m, "hill"] for x, z, m in SPOTS_HILL] + [[r1(x), r1(z), m, "zone"] for x, z, m in SPOTS_ZONE],
        "vents": [[t, r1(x), r1(z)] for t, x, z in VENTS], "village": VILLAGE, "farm": [r1(v) for v in FARM], "windmill": [r1(v) for v in WINDMILL],
        "lighthouse": [r1(v) for v in LIGHTHOUSE], "battery": [r1(v) for v in BATTERY],
        "roads": [[k, pts1(pts)] for k, pts in ROADS],
        "dunes": [[r1(x), r1(z), r1(r), r1(h)] for x, z, r, h in DUNES],
        "craters": [[r1(x), r1(z), r1(r), r1(d), deep, wet] for x, z, r, d, deep, wet in CRATERS],
        "wire": [[s, r1(a), r1(b), off] for s, a, b, off in WIRE_BELTS],
        "hedges": [pts1(pts) for pts in HEDGES], "fields": [pts1(f) for f in FIELDS],
        "wrecks": [[k, r1(x), r1(z), r1(hd)] for k, x, z, hd in WRECKS],
    }


if __name__ == "__main__":
    with open(os.path.join(HERE, "layout.svg"), "w", encoding="utf-8", newline="\n") as f:
        f.write(svg())
    with open(os.path.join(HERE, "layout.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(table(), f, indent=1)
    own = {t: sum(m for _, _, m, tt in SPOTS_AT_STARTS if tt == t) for t in ("beach", "hill")}
    own["beach"] += sum(m for *_, m in SPOTS_BEACH)
    own["hill"] += sum(m for *_, m in SPOTS_HILL)
    zone = sum(m for *_, m in SPOTS_ZONE)
    spots = len(SPOTS_AT_STARTS) + len(SPOTS_BEACH) + len(SPOTS_HILL) + len(SPOTS_ZONE)
    print(f"metal: beach team {own['beach']:.1f}, hill team {own['hill']:.1f} of its own; zone {zone:.1f}; {spots} spots, {own['beach'] + own['hill'] + zone:.1f} m/s in all")
    zw = [fire_B(z) - fire_A(z) for z in range(0, H + 1, 32)]
    print(f"zone (fire trench to fire trench): {min(zw):.0f} to {max(zw):.0f} wide, {sum(zw) / len(zw):.0f} on average")
    for n, t, x, z in STARTS:
        own_line = fire_A if t == "beach" else fire_B
        enemy = min(math.hypot(x - ex, z - ez) for _, et, ex, ez in STARTS if et != t)
        print(f"start {n} ({t}): to its fire trench {nearest_on(own_line, x, z):.0f}, to the zone's middle {nearest_on(zone_mid, x, z):.0f}, to the nearest enemy start {enemy:.0f}")
    print(f"inlet crosses the beach line at z {INLET_A[0]:.0f}, the hill line at z {INLET_B[0]:.0f}; the lines end at z {END_A:.0f} and {END_B:.0f}")
    centre = [(x, z, height(x, z)) for x, z, _ in INLET_PTS if x > coast(z) - 900]
    print(f"  along its middle from 900 offshore to the cove: shallowest {-max(h for *_, h in centre):.1f} deep (ships need 8 to 15, land units stop at 20)")
    widths = [2 * w for x, z, w in INLET_PTS if coast(z) < x < 8300]
    print(f"  water {min(widths):.0f} to {max(widths):.0f} wide inland, the cove about {2 * INLET_PTS[-1][2]:.0f} across")
    s7 = STARTS[7]
    cove_d = min(math.hypot(s7[2] - x, s7[3] - z) - w for x, z, w in INLET_PTS if x > COVE_FROM)
    s3 = STARTS[3]
    inlet_d = min(math.hypot(s3[2] - x, s3[3] - z) - w for x, z, w in INLET_PTS)
    print(f"  start 7 is {cove_d:.0f} from the cove's water (its pad {PAD_R}); start 3 is {inlet_d:.0f} from the inlet's")
    cells = {"sea": 0, "inlet": 0, "beach": 0, "zone": 0, "hill": 0, "sandy end": 0}
    for zz in range(32, H, 64):
        for xx in range(32, W, 64):
            hh = height(xx, zz)
            if hh < 0:
                cells["sea" if xx < coast(zz) else "inlet"] += 1
            elif sandy_end(xx, zz):
                cells["sandy end"] += 1
            elif south_of_inlet(xx, zz) or xx > fire_B(zz) + SUPPORT_BACK:
                cells["hill"] += 1
            elif xx < fire_A(zz) - SUPPORT_BACK:
                cells["beach"] += 1
            else:
                cells["zone"] += 1
    print("  ground (million square units): " + ", ".join(f"{k} {v * 64 * 64 / 1e6:.1f}" for k, v in cells.items()))
    print(f"generated: {len(DUNES)} dunes, {len(CRATERS)} craters ({sum(1 for c in CRATERS if c[4])} deep, {sum(1 for c in CRATERS if c[5])} flooded), "
          f"{len(HEDGES)} hedgerow runs, {len(WIRE_BELTS)} wire runs, {len(WRECKS)} wrecks")
    print(f"heights along z 6000: min {min(height(x, 6000) for x in range(0, W, 8)):.0f}, max {max(height(x, 6000) for x in range(0, W, 8)):.0f}")
    print(f"rise: {math.degrees(math.atan((CREST - TRENCH_GROUND) / (crest(6000) - fire_B(6000)))):.1f} degrees on average at z 6000")
