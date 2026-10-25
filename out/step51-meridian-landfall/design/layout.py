"""Meridian Landfall: the design's coordinate table, and the to-scale drawing made from it (layout.svg).

The same ship as Starship Meridian (step 50), landed on an asteroid. The ship is Meridian's own layout table, imported
and moved by one offset (OX, OZ) so its centreline is the map's, its heights raised by HY; nothing of it is retyped.
Round it is moonrock: a plain at about the ship's deck, cratered, with void pits blasted through the asteroid (ground
the engine never draws, so the starfield shows) and a ragged void edge.

Game units, x east, z south, the map 32 x 36 (16384 x 18432). The port half (x < CX) is given; the starboard half is
its mirror image across the centreline. Craters on the centreline are centred on it. Every random choice comes from
`hash01`, a SplitMix64 hash of integers, so the converter (C#) makes the same craters.

    python layout.py        writes layout.svg beside this file, and prints the areas, the metal and the craters
"""
import html, importlib.util, math, os

HERE = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location("meridian", os.path.join(HERE, "..", "..", "step50-starship-meridian", "design", "layout.py"))
ship = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(ship)

MAP_UNITS = (32, 36)
W, H = MAP_UNITS[0] * 512, MAP_UNITS[1] * 512
CX = W / 2
OX, OZ = CX - ship.CX, 2048            # the ship's offset: its centreline on the map's, its bow 2048 further south
HY = 400                               # the ship's heights raised by this: its deck at 640
PLAIN = ship.DECK + HY                 # the moonrock plain round the ship, at the deck's height (the bay doors open level)
SEED = 51


def mx(x):
    return W - x


def at(p):
    """A point of Meridian's ship in this map."""
    return (p[0] + OX, p[1] + OZ)


# ---- Random numbers: SplitMix64 over integers (the converter does the same).
M64 = (1 << 64) - 1


def splitmix(v):
    v = (v + 0x9E3779B97F4A7C15) & M64
    v = ((v ^ (v >> 30)) * 0xBF58476D1CE4E5B9) & M64
    v = ((v ^ (v >> 27)) * 0x94D049BB133111EB) & M64
    return v ^ (v >> 31)


def hash01(*ints):
    h = SEED
    for i in ints:
        h = splitmix(h ^ (i & M64))
    return (h >> 11) / float(1 << 53)


# ---- The ship, moved.
HULL = [at(p) for p in ship.HULL]
HULL_PORT_EDGE_X = 900 + OX            # the hull's long port side
BOW_TIP_Z, STERN_Z = 400 + OZ, 14000 + OZ

# The hangars' bay doors: each hangar's outboard wall opens (700 wide) onto a level apron of moonrock.
BAY_DOORS_PORT = []                    # (x0, z0, x1, z1): through the rim, from outside to the hangar's floor
APRONS_PORT = []                       # (x0, z0, x1, z1): kept level at the deck's height, clear of craters and rocks
for name, kind, deck, shape in ship.TEAM_ROOMS:
    if "Hangar" in name:
        x0, z0, x1, z1 = shape
        zc = (z0 + z1) / 2 + OZ
        BAY_DOORS_PORT.append((HULL_PORT_EDGE_X - 20, zc - 350, x0 + OX + 20, zc + 350))
        APRONS_PORT.append((HULL_PORT_EDGE_X - 900, zc - 650, HULL_PORT_EDGE_X, zc + 650))

# ---- Starts: 16. Port's team is 0-7 (0-3 in the ship as on Meridian, 4-7 outside on the asteroid); starboard's 8-15
# are their mirror images. Every outside start sits on a mare: a flat dark plain, kept level, clear of craters.
STARTS_SHIP = [(n, x + OX, z + OZ) for n, x, z, _ in ship.STARTS]
STARTS_OUT = [("Fore flank", 1800, 3600), ("Forward bay", 1500, 7700), ("Aft bay", 1500, 12000), ("Aft flank", 1650, 16150)]
MARE_R = 900                          # each outside start's mare: level within this, no craters within 1.3 times it

# ---- Metal (x, z, metal/s). The ship's 46 spots move with it; the asteroid adds:
SPOTS_SHIP_PORT = [(x + OX, z + OZ, m) for x, z, m in ship.SPOTS_PORT]
SPOTS_SHIP_SHARED = [(x + OX, z + OZ, m) for x, z, m in ship.SPOTS_SHARED_PORT]
SPOTS_OUT_PORT = []
for _, sx, sz in STARTS_OUT:           # three at each outside start
    SPOTS_OUT_PORT += [(sx - 380, sz - 300, 2.0), (sx + 380, sz - 300, 2.0), (sx, sz + 420, 2.0)]
SPOTS_OUT_PORT += [(2400, 5450, 2.4), (1150, 8950, 2.4), (2350, 9800, 2.4), (2550, 13500, 2.4)]          # the flank's own
SPOTS_OUT_CONTESTED = [(5200, 2500, 3.0), (6900, 1550, 3.0), (5500, 16950, 3.0), (7700, 17000, 3.0)]     # fore and aft fields
VENTS_OUT = [(8192, 1500)]                         # on the centreline at the bow (its own mirror image)

# ---- Craters. Bowls: walls only bots climb (about 40 degrees: over the vehicles' 27, under the bots' 54), no ramps,
# a raised rim. Pits: walls over 60 degrees down to the void, their edges shattered. (x, z, radius, depth below the
# plain, rim height, what's on the floor). Port side; those at x = CX are on the centreline.
BOWLS = [
    (3150, 2150, 560, 240, 100, "metal 3.6"),      # the fore flank's big bowl
    (1650, 14250, 540, 230, 90, "metal 3.6"),      # the aft flank's
    (3350, 16800, 380, 150, 60, "vent"),           # the aft field's, a vent on its floor
]
PITS = [
    (1350, 5800, 450, 340),                        # between the fore flank and the forward bay start
    (5000, 1550, 380, 320),                        # the fore field
    (1300, 10000, 420, 320),                       # between the bay starts
    (6600, 16900, 330, 320),                       # the aft field, clear of the stern lane
]
# The ragged void edge: the asteroid's rock comes to within EDGE_MIN of the map's border, in bays up to EDGE_MAX deep
# (shallower along the flanks, which are narrow, than at the bow and the stern); a 240-wide band inside it is shattered.
EDGE_MIN, EDGE_MAX = (160, 120, 160), (600, 400, 600)          # north, the sides, south


def jag(cx, cz, r, k, a):
    """A pit's radius at angle a: a fractal outline (harmonics 2 to 40, each weaker by about 1/n, so the line is rough at
    every scale), and a few smooth notches where a chunk of rock blew out or a buttress stayed."""
    w = 1.0
    for n in range(2, 41):
        w += 0.13 * n ** -0.9 * (0.4 + 0.9 * hash01(k, 200 + n)) * math.sin(n * a + 6.283 * hash01(k, 100 + n))
    for j in range(5):
        c, width, size = 6.283 * hash01(k, 300 + j), 0.04 + 0.08 * hash01(k, 310 + j), (hash01(k, 320 + j) - 0.5) * 0.22
        d = math.atan2(math.sin(a - c), math.cos(a - c)) / width
        w += size * math.exp(-d * d)
    return r * w


def waver(r, k, a):
    """A bowl's or small crater's radius at angle a: no circle, but no spikes either (its wall must stay in the bots' band)."""
    return r * (1 + 0.05 * math.sin(3 * a + 6.3 * hash01(k, 1)) + 0.035 * math.sin(5 * a + 6.3 * hash01(k, 2)) + 0.02 * math.sin(8 * a + 6.3 * hash01(k, 3)))


def outline(cx, cz, r, k, n=160, spiky=True):
    """The outline round a port crater, or (cx past the centreline) its starboard mirror image: angle a read as pi - a."""
    starboard = cx > CX
    def f(a):
        a = math.pi - a if starboard else a
        return jag(cx, cz, r, k, a) if spiky else waver(r, k, a)
    return [(cx + f(a) * math.cos(a), cz + f(a) * math.sin(a)) for a in (2 * math.pi * i / n for i in range(n))]


def edge_depth(b, u):
    """How far in from the map's border b (0 north, 1 the sides, 2 south) the rock starts, at u along it (the north and
    south borders read at |x - CX|, so the edge is its own mirror image): bays, eight octaves of waves from 700 down to 6
    long (each weaker), and here and there a smooth bite where a chunk broke away or a spur stayed."""
    bay = max(0.0, math.sin(u / 1150 + 6.3 * hash01(b, 1)) * math.sin(u / 470 + 6.3 * hash01(b, 2))) ** 1.3
    d = EDGE_MIN[b] + (EDGE_MAX[b] - EDGE_MIN[b]) * bay
    for j in range(8):
        length = 700 / 2 ** j * (0.8 + 0.4 * hash01(b, 320 + j))
        d += 60 * (length / 700) ** 0.8 * math.sin(u / length + 6.283 * hash01(b, 300 + j))
    k0 = int(math.floor(u / 400))
    for k in (k0 - 1, k0, k0 + 1):
        if hash01(b, k, 14) > 0.45:
            continue
        c, w, h = (k + hash01(b, k, 11)) * 400, 30 + 90 * hash01(b, k, 12), (hash01(b, k, 13) - 0.35) * 200
        d += h * math.exp(-((u - c) / w) ** 2)
    return max(60.0, d)


def rock_edge(x, z):
    """Signed distance into the asteroid's rock from its edge (negative: void, toward the map's border)."""
    u = abs(x - CX)
    return min(z - edge_depth(0, u), H - z - edge_depth(2, u), x - edge_depth(1, z), W - x - edge_depth(1, z))


# Small craters: a jittered grid over the port half, kept off the ship, the aprons, the mares, the spots, the bigger
# craters and the pits, and away from the centreline's fore and aft crossings. (x, z, radius, depth, rim)
def small_craters():
    out, g = [], 400
    for j in range(int(H // g) + 1):
        for i in range(int(CX // g) + 1):
            if hash01(i, j, 7) > 0.75:
                continue
            x, z = (i + 0.15 + 0.7 * hash01(i, j, 8)) * g, (j + 0.15 + 0.7 * hash01(i, j, 9)) * g
            r = 70 + 230 * hash01(i, j, 10) ** 2.2
            if blocked(x, z, r, out):
                continue
            out.append((x, z, r, 0.28 * r, 0.12 * r))
    return out


def in_poly(p, poly):
    x, z, inside = p[0], p[1], False
    for i in range(len(poly)):
        (x0, z0), (x1, z1) = poly[i - 1], poly[i]
        if (z1 > z) != (z0 > z) and x < (x0 - x1) * (z - z1) / (z0 - z1) + x1:
            inside = not inside
    return inside


def to_poly(p, poly):
    best = 1e18
    for i in range(len(poly)):
        (ax, az), (bx, bz) = poly[i - 1], poly[i]
        dx, dz = bx - ax, bz - az
        t = max(0, min(1, ((p[0] - ax) * dx + (p[1] - az) * dz) / (dx * dx + dz * dz)))
        best = min(best, math.hypot(p[0] - ax - t * dx, p[1] - az - t * dz))
    return best


def blocked(x, z, r, placed=()):
    if x + r > CX - 300 or rock_edge(x, z) < r * 1.5 + 420:          # clear of the edge's worn slope (its shoulder 200 to 340 in)
        return True
    if in_poly((x, z), HULL) or to_poly((x, z), HULL) < r + 350:
        return True
    for x0, z0, x1, z1 in APRONS_PORT:
        if x0 - r - 150 < x < x1 + r + 150 and z0 - r - 150 < z < z1 + r + 150:
            return True
    for _, sx, sz in STARTS_OUT:
        if math.hypot(x - sx, z - sz) < MARE_R * 1.3 + r:
            return True
    for sx, sz, _ in SPOTS_OUT_PORT + SPOTS_OUT_CONTESTED:
        if math.hypot(x - sx, z - sz) < r + 220:
            return True
    for cx, cz, cr, *_ in BOWLS + PITS:
        if math.hypot(x - cx, z - cz) < (cr + r) * 1.35:
            return True
    for px, pz, pr, *_ in placed:
        if math.hypot(x - px, z - pz) < (pr + r) * 1.15:
            return True
    return False


SMALL = small_craters()


def area(points):
    return abs(sum(points[i][0] * points[(i + 1) % len(points)][1] - points[(i + 1) % len(points)][0] * points[i][1] for i in range(len(points)))) / 2


# ---- The drawing.
S = 20
LEGEND = 300


def P(x, z):
    return f"{x / S:.1f},{z / S:.1f}"


def svg():
    o = []
    w, h = int(W / S) + LEGEND, int(H / S) + 20
    o.append(f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" viewBox="0 0 {w} {h}" font-family="Segoe UI, Arial, sans-serif">')
    o.append('<defs><pattern id="hatch" width="6" height="6" patternUnits="userSpaceOnUse" patternTransform="rotate(45)"><rect width="6" height="6" fill="#e6c25a"/>'
             '<line x1="0" y1="0" x2="0" y2="6" stroke="#8a6a12" stroke-width="2"/></pattern>'
             '<marker id="arrow" viewBox="0 0 10 10" refX="5" refY="5" markerWidth="5" markerHeight="5" orient="auto-start-reverse"><path d="M0,0 L10,5 L0,10 z" fill="#d35400"/></marker>'
             '<radialGradient id="bowl"><stop offset="0" stop-color="#6d6e72"/><stop offset="0.7" stop-color="#8a8b8f"/><stop offset="1" stop-color="#b9babd"/></radialGradient></defs>')
    o.append('<rect width="100%" height="100%" fill="#f4f3ef"/>')
    # space under everything (shows through the void), then the asteroid's rock with its ragged edge
    o.append(f'<rect x="0" y="0" width="{W / S}" height="{H / S}" fill="#05070f"/>')
    for k in range(500):
        o.append(f'<circle cx="{hash01(k, 900) * W / S:.1f}" cy="{hash01(k, 901) * H / S:.1f}" r="{0.5 + 0.6 * hash01(k, 902):.1f}" fill="#cfd8ff" opacity="{0.35 + 0.6 * hash01(k, 903):.2f}"/>')
    edge, step = [], 40                             # round the map's border clockwise, the rock's edge set in from it
    edge += [(x, edge_depth(0, abs(x - CX))) for x in range(0, int(W) + 1, step)]
    edge += [(W - edge_depth(1, z), z) for z in range(0, int(H) + 1, step)]
    edge += [(x, H - edge_depth(2, abs(x - CX))) for x in range(int(W), -1, -step)]
    edge += [(edge_depth(1, z), z) for z in range(int(H), -1, -step)]
    o.append(f'<polygon points="{" ".join(P(x, z) for x, z in edge)}" fill="#a7a9ad"/>')
    # mares round the outside starts
    for _, sx, sz in STARTS_OUT:
        for X in (sx, mx(sx)):
            o.append(f'<circle cx="{X / S:.1f}" cy="{sz / S:.1f}" r="{MARE_R / S:.1f}" fill="#8b8d92"/>')
    # small craters, bowls, pits
    def crater(x, z, r, k, rim, fill):
        o.append(f'<polygon points="{" ".join(P(px, pz) for px, pz in outline(x, z, r * 1.25, k, 60, False))}" fill="#c3c4c7" opacity="0.8"/>')
        o.append(f'<polygon points="{" ".join(P(px, pz) for px, pz in outline(x, z, r, k, 60, False))}" fill="{fill}" stroke="#55575c" stroke-width="{rim}"/>')
    for k, (x, z, r, d, rim) in enumerate(SMALL):
        crater(x, z, r, 3000 + k, 0.6, "url(#bowl)")
        crater(mx(x), z, r, 3000 + k, 0.6, "url(#bowl)")
    for k, (x, z, r, d, rim, what) in enumerate(BOWLS):
        for X in {x, mx(x)}:
            crater(X, z, r, 2000 + k, 1.4, "url(#bowl)")
            o.append(f'<text x="{X / S:.1f}" y="{(z - r * 0.55) / S:.1f}" font-size="9" fill="#1d2329" text-anchor="middle">bowl, bots only</text>')
    for k, (x, z, r, d) in enumerate(PITS):
        for X in {x, mx(x)}:
            o.append(f'<polygon points="{" ".join(P(px, pz) for px, pz in outline(X, z, r * 1.22, 1000 + k))}" fill="#6a6b70" stroke="#3d3e42" stroke-width="1"/>')
            o.append(f'<polygon points="{" ".join(P(px, pz) for px, pz in outline(X, z, r, 1000 + k))}" fill="#05070f" stroke="#e0e0e0" stroke-width="0.6"/>')
            o.append(f'<text x="{X / S:.1f}" y="{(z + 4) / S:.1f}" font-size="9" fill="#cfd8ff" text-anchor="middle">void pit</text>')
    # the ship: hull, rooms, doors (Meridian's drawing's colours), the bay doors and aprons
    o.append(f'<polygon points="{" ".join(P(x, z) for x, z in HULL)}" fill="#4a4f57" stroke="#9aa3ad" stroke-width="2.5"/>')
    fills = {"start": "#c9ced6", "team": "#b3b9c3", "passage": "#a3aab5", "shared": "#9fc1dd"}
    for name, kind, deck, shape in ship.TEAM_ROOMS:
        for side in ("port", "starboard"):
            pts = [at(p) for p in ship.poly(shape)]
            pts = pts if side == "port" else [(mx(x), z) for x, z in pts]
            o.append(f'<polygon points="{" ".join(P(x, z) for x, z in pts)}" fill="{fills[kind]}" stroke="{"#c0392b" if side == "port" else "#1e8449"}" stroke-width="1.2"/>')
    for name, kind, deck, shape in ship.SHARED_ROOMS:
        pts = [at(p) for p in ship.poly(shape)]
        o.append(f'<polygon points="{" ".join(P(x, z) for x, z in pts)}" fill="{fills[kind]}" stroke="#2e6aa0" stroke-width="1.2"/>')
        xs, zs = [p[0] for p in pts], [p[1] for p in pts]
        o.append(f'<text x="{sum(xs) / len(xs) / S:.1f}" y="{(min(zs) + 330) / S:.1f}" font-size="9.5" font-weight="600" fill="#1d2329" text-anchor="middle">{html.escape(name)}</text>')
    wx, wz = at(ship.REACTOR_WELL_AT)
    o.append(f'<circle cx="{wx / S}" cy="{wz / S}" r="{ship.REACTOR_WELL_R / S}" fill="#7f9fbd" stroke="#2e6aa0" stroke-width="1" stroke-dasharray="4 3"/>')
    for side in ("port", "starboard"):
        for x0, z0, x1, z1, a, b in ship.RAMPS + ship.WELL_RAMPS:
            pts = [at(p) for p in ship.rect((x0, z0, x1, z1))]
            pts = pts if side == "port" else [(mx(x), z) for x, z in pts]
            o.append(f'<polygon points="{" ".join(P(x, z) for x, z in pts)}" fill="url(#hatch)" stroke="#6b4f00" stroke-width="0.8"/>')
    def door(x0, z0, x1, z1, colour):
        if x0 == x1: x0, x1 = x0 - 50, x1 + 50
        if z0 == z1: z0, z1 = z0 - 50, z1 + 50
        o.append(f'<rect x="{x0 / S:.1f}" y="{z0 / S:.1f}" width="{(x1 - x0) / S:.1f}" height="{(z1 - z0) / S:.1f}" fill="{colour}" stroke="#5a4500" stroke-width="0.5"/>')
    for x0, z0, x1, z1, k in ship.DOORS_PORT:
        c = "#ff8a3d" if k == "crossing" else "#ffd84d"
        door(x0 + OX, z0 + OZ, x1 + OX, z1 + OZ, c)
        door(mx(x1 + OX), z0 + OZ, mx(x0 + OX), z1 + OZ, c)
    for x0, z0, x1, z1, k in ship.DOORS_CENTRE:
        door(x0 + OX, z0 + OZ, x1 + OX, z1 + OZ, "#c084fc" if k == "stairs" else "#ff8a3d")
    for (x0, z0, x1, z1), (ax0, az0, ax1, az1) in zip(BAY_DOORS_PORT, APRONS_PORT):
        for X0, X1, AX0, AX1 in ((x0, x1, ax0, ax1), (mx(x1), mx(x0), mx(ax1), mx(ax0))):
            o.append(f'<rect x="{AX0 / S:.1f}" y="{az0 / S:.1f}" width="{(AX1 - AX0) / S:.1f}" height="{(az1 - az0) / S:.1f}" fill="none" stroke="#ffd84d" stroke-width="1.2" stroke-dasharray="4 3"/>')
            o.append(f'<rect x="{X0 / S:.1f}" y="{z0 / S:.1f}" width="{(X1 - X0) / S:.1f}" height="{(z1 - z0) / S:.1f}" fill="#3ddc97" stroke="#0c5e3b" stroke-width="0.8"/>')
    # lanes: the ship's five crossings, and the asteroid's two round the bow and the stern
    for _, z in ship.LANES:
        o.append(f'<line x1="{(4700 + OX) / S}" y1="{(z + OZ) / S}" x2="{mx(4700 + OX) / S}" y2="{(z + OZ) / S}" stroke="#d35400" stroke-width="1.6" stroke-dasharray="6 4" marker-start="url(#arrow)" marker-end="url(#arrow)" opacity="0.8"/>')
    for pts in ([(2400, 4300), (5400, 3300), (8192, 1900), (mx(5400), 3300), (mx(2400), 4300)],
                [(2400, 15600), (4400, 16900), (8192, 16900), (mx(4400), 16900), (mx(2400), 15600)]):
        o.append(f'<polyline points="{" ".join(P(x, z) for x, z in pts)}" fill="none" stroke="#d35400" stroke-width="2.2" stroke-dasharray="9 5" marker-start="url(#arrow)" marker-end="url(#arrow)"/>')
    # metal, vents, starts
    def spot(x, z, m):
        s = 4.2 if m >= 3 else 3.3
        o.append(f'<rect x="{x / S - s:.1f}" y="{z / S - s:.1f}" width="{2 * s}" height="{2 * s}" transform="rotate(45 {x / S:.1f} {z / S:.1f})" fill="#e8ecf2" stroke="#222" stroke-width="0.7"/>')
    for x, z, m in SPOTS_SHIP_PORT + SPOTS_SHIP_SHARED + SPOTS_OUT_PORT + SPOTS_OUT_CONTESTED:
        spot(x, z, m); spot(mx(x), z, m)
    for k, (x, z, r, d, rim, what) in enumerate(BOWLS):
        for X in {x, mx(x)}:
            if what.startswith("metal"):
                spot(X, z, 3.6)
            else:
                o.append(f'<circle cx="{X / S:.1f}" cy="{z / S:.1f}" r="5" fill="#57d163" stroke="#1d5e25" stroke-width="1"/>')
    for x, z in ship.VENTS_PORT + ship.VENTS_SHARED_PORT:
        for X in (x + OX, mx(x + OX)):
            o.append(f'<circle cx="{X / S:.1f}" cy="{(z + OZ) / S:.1f}" r="4.5" fill="#57d163" stroke="#1d5e25" stroke-width="1"/>')
    for x, z in VENTS_OUT:
        o.append(f'<circle cx="{x / S:.1f}" cy="{z / S:.1f}" r="5" fill="#57d163" stroke="#1d5e25" stroke-width="1"/>')
    for i, (name, x, z) in enumerate(STARTS_SHIP + STARTS_OUT):
        for team, X, colour in ((i, x, "#c0392b"), (i + 8, mx(x), "#1e8449")):
            o.append(f'<circle cx="{X / S:.1f}" cy="{z / S:.1f}" r="12" fill="{colour}" stroke="#fff" stroke-width="2"/>')
            o.append(f'<text x="{X / S:.1f}" y="{z / S + 4:.1f}" font-size="10.5" font-weight="700" fill="#fff" text-anchor="middle">{team}</text>')
    o.append(f'<line x1="{CX / S}" y1="0" x2="{CX / S}" y2="{H / S}" stroke="#ffffff" stroke-width="1" stroke-dasharray="3 5" opacity="0.5"/>')
    # legend
    lx, y = W / S + 16, 26
    def leg(text, size=12, weight="400", dy=18, colour="#1d2329"):
        nonlocal y
        o.append(f'<text x="{lx}" y="{y}" font-size="{size}" font-weight="{weight}" fill="{colour}">{html.escape(text)}</text>')
        y += dy
    leg("Meridian Landfall", 17, "700", 22)
    leg(f"{MAP_UNITS[0]} × {MAP_UNITS[1]}, 8v8, port vs starboard", 12, "400", 16)
    leg(f"1 px = {S} game units; bow north", 11, "400", 26, "#555")
    items = [("#c0392b", "circle", "Port start (0-7)"), ("#1e8449", "circle", "Starboard start (8-15)"),
             ("#e8ecf2", "diamond", "Metal (big: contested 3.0-3.6)"), ("#57d163", "circle", "Geothermal vent"),
             ("#3ddc97", "rect", "Hangar bay door (700)"), ("none", "dash", "Level apron outside it"),
             ("url(#bowl)", "rect", "Crater: bowl, only bots in/out"), ("#05070f", "rect", "Void: pit or edge (air only)"),
             ("#8b8d92", "rect", "Mare: flat ground at a start"), ("#a7a9ad", "rect", "Moonrock plain (deck height)"),
             ("#9fc1dd", "rect", "Ship: shared room"), ("#c9ced6", "rect", "Ship: start room")]
    for colour, shape, text in items:
        if shape == "circle":
            o.append(f'<circle cx="{lx + 8}" cy="{y - 4}" r="6" fill="{colour}" stroke="#333" stroke-width="0.8"/>')
        elif shape == "diamond":
            o.append(f'<rect x="{lx + 4}" y="{y - 8}" width="8" height="8" transform="rotate(45 {lx + 8} {y - 4})" fill="{colour}" stroke="#222" stroke-width="0.8"/>')
        elif shape == "dash":
            o.append(f'<rect x="{lx}" y="{y - 11}" width="16" height="12" fill="none" stroke="#c9a000" stroke-width="1.4" stroke-dasharray="3 2"/>')
        else:
            o.append(f'<rect x="{lx}" y="{y - 11}" width="16" height="12" fill="{colour}" stroke="#333" stroke-width="0.8"/>')
        o.append(f'<text x="{lx + 24}" y="{y}" font-size="11" fill="#1d2329">{html.escape(text)}</text>')
        y += 19
    y += 6
    o.append(f'<line x1="{lx}" y1="{y}" x2="{lx + 32}" y2="{y}" stroke="#d35400" stroke-width="2.2" stroke-dasharray="9 5"/>')
    o.append(f'<text x="{lx + 40}" y="{y + 4}" font-size="11" fill="#1d2329">Outside lane (bow, stern)</text>')
    y += 20
    o.append(f'<line x1="{lx}" y1="{y}" x2="{lx + 32}" y2="{y}" stroke="#d35400" stroke-width="1.6" stroke-dasharray="6 4"/>')
    o.append(f'<text x="{lx + 40}" y="{y + 4}" font-size="11" fill="#1d2329">Ship crossing (5)</text>')
    y += 30
    o.append(f'<line x1="{lx}" y1="{y}" x2="{lx + 1000 / S}" y2="{y}" stroke="#1d2329" stroke-width="2"/>')
    o.append(f'<text x="{lx + 1000 / S + 8}" y="{y + 4}" font-size="11" fill="#1d2329">1000 units</text>')
    y += 30
    for line in [f"The ship: Meridian's, unchanged but for", "the hangars' bay doors; deck at " + str(PLAIN) + ".",
                 "Plain round it at the deck's height;", "bowls 200-260 deep, pits to the void.",
                 "Craters are drawn at their real size;", "the small ones are generated (seed " + str(SEED) + ")."]:
        leg(line, 11, "400", 15)
    o.append("</svg>")
    return "\n".join(o)


def table():
    """Everything the converter reads from this table (the ship comes from Meridian's Layout.cs, moved)."""
    return {
        "mapUnits": list(MAP_UNITS), "offset": [OX, OZ], "raise": HY, "plain": PLAIN, "seed": SEED,
        "edge": [EDGE_MIN, EDGE_MAX], "mareRadius": MARE_R,
        "startsOutside": [[n, x, z] for n, x, z in STARTS_OUT],
        "spotsOutside": [[x, z, m] for x, z, m in SPOTS_OUT_PORT],
        "spotsContested": [[x, z, m] for x, z, m in SPOTS_OUT_CONTESTED],
        "ventsOutside": [[x, z] for x, z in VENTS_OUT],
        "bayDoors": [list(d) for d in BAY_DOORS_PORT], "aprons": [list(a) for a in APRONS_PORT],
        "bowls": [[x, z, r, d, rim, what] for x, z, r, d, rim, what in BOWLS],
        "pits": [[x, z, r, d] for x, z, r, d in PITS],
        "small": [[round(x, 1), round(z, 1), round(r, 1), round(d, 1), round(rim, 1)] for x, z, r, d, rim in SMALL],
    }


if __name__ == "__main__":
    import json
    with open(os.path.join(HERE, "layout.svg"), "w", encoding="utf-8", newline="\n") as f:
        f.write(svg())
    with open(os.path.join(HERE, "layout.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(table(), f, indent=1)
    own_ship = sum(m for _, _, m in SPOTS_SHIP_PORT)
    own_out = sum(m for _, _, m in SPOTS_OUT_PORT)
    shared_ship = 2 * sum(m for _, _, m in SPOTS_SHIP_SHARED)
    contested = 2 * sum(m for _, _, m in SPOTS_OUT_CONTESTED) + 2 * sum(3.6 for b in BOWLS if b[5].startswith("metal"))
    spots = 2 * (len(SPOTS_SHIP_PORT) + len(SPOTS_SHIP_SHARED) + len(SPOTS_OUT_PORT) + len(SPOTS_OUT_CONTESTED) + sum(1 for b in BOWLS if b[5].startswith("metal")))
    print(f"map {W} x {H}; ship offset ({OX:.0f}, {OZ}); hull from x {HULL_PORT_EDGE_X:.0f} to {mx(HULL_PORT_EDGE_X):.0f}, z {BOW_TIP_Z} to {STERN_Z}")
    print(f"flank: {HULL_PORT_EDGE_X - EDGE_MIN[1]:.0f} to {HULL_PORT_EDGE_X - EDGE_MAX[1]:.0f} wide; bow lane at the tip {BOW_TIP_Z - EDGE_MIN[0]:.0f} to {BOW_TIP_Z - EDGE_MAX[0]:.0f}; "
          f"stern lane past the housings {H - STERN_Z - EDGE_MIN[2]:.0f} to {H - STERN_Z - EDGE_MAX[2]:.0f} (300 less under the engines' bells); the edge's bays and wedges cut in up to 650 more")
    print(f"metal: each team {own_ship:.1f} in the ship + {own_out:.1f} outside = {own_ship + own_out:.1f} m/s of its own; shared {shared_ship:.1f} in the ship, "
          f"{contested:.1f} on the asteroid; {spots} spots, {2 * (own_ship + own_out) + shared_ship + contested:.1f} m/s in all")
    print(f"craters: {len(BOWLS)} bowls and {len(PITS)} pits a side, {len(SMALL)} small a side")
    print(f"per player: {W * H / 16 / 1e6:.1f} M square units of map ({W * H / 1e6:.0f} M in all, the hull {area(HULL) / 1e6:.0f} M)")
