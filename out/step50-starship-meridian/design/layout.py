"""Starship Meridian: the design's one coordinate table, and the to-scale drawing made from it (layout.svg).

Game units, x east, z south, the map 24 x 28 (12288 x 14336). The port half (x < 6144) is given; the starboard half is
its mirror image across the centreline x = 6144. These numbers become the converter's Layout.cs when the map is built.
Everything inside the hull that isn't a room, a passage or a ramp is solid at the rim's height: machinery spaces and the
hull's own structure (the bow forward of the bridge and the weapons bays, the stern between the engine housings, and the
engine housings' walls). The 100-wide walls between rooms are bulkheads, 100 over the deck. 1.1: the passages are 1,000
wide, the crossings (but the CIC's) and the spine doors wider, and each engine housing carries a thruster platform at
440, open, with a ramp rising out of the room below it to a step at the outer walk's height and on up (from the step's
sides narrow paths skirt round the platform's corners to the walk), for long-range guns at the stern as the bridge is at
the bow; the bridge's walls are a lip, 30 over its deck, with a front opening onto
the bow; and an outer walk runs round the hull's edge at 380, reached from the steps and the bridge.

    python layout.py        writes layout.svg beside this file, and prints the room areas and the metal
"""
import html, math, os, random

W, H, CX = 12288, 14336, 6144
DECK, BRIDGE_DECK, REACTOR_WELL, WALL_TOP, RIM_TOP, SPACE = 240, 380, 120, 340, 380, 20
STERN_DECK = 440                    # the thruster platforms (1.1)
WALK = 380                          # the outer walk and the thruster steps (1.1)
WALK_BAND, BOW_FRONT_Z = 96, 1450    # the walk: this far in from the hull's edge, and all the bow ahead of this


def mx(x):
    return W - x


# ---- The hull: its port outline from the bow's tip, clockwise round to the stern on the centreline. Outside it is space.
HULL_PORT = [(6144, 400), (4300, 1100), (2200, 2400), (900, 3800), (900, 13200), (1600, 13200), (1600, 14000),
             (3200, 14000), (3200, 13200), (5244, 13200), (5244, 14000), (6144, 14000)]
HULL = HULL_PORT + [(mx(x), z) for x, z in reversed(HULL_PORT[1:-1])]

# ---- Rooms. (name, kind, deck, polygon or rect x0 z0 x1 z1). kind: team (each side its own), shared (on the centreline).
# Team rooms are given for port; starboard's are their mirror images.
TEAM_ROOMS = [
    ("Weapons Bay", "start", DECK, [(2300, 2700), (4400, 2700), (4400, 3780), (1080, 3780)]),
    ("Forward Hangar", "start", DECK, (1028, 3900, 2700, 6600)),
    ("Crew Deck & Mess", "team", DECK, (2800, 3900, 4000, 6600)),
    ("Power Room", "start", DECK, (1028, 6700, 2700, 8000)),
    ("Armory", "team", DECK, (2800, 6700, 4000, 8000)),
    ("Main Hangar", "start", DECK, (1028, 8100, 2700, 10800)),
    ("Cargo Hold", "team", DECK, (2800, 8100, 4000, 10800)),
    ("Thruster Bay", "start", DECK, (1028, 10900, 2700, 13072)),
    ("Machine Shop", "team", DECK, (2800, 10900, 4000, 13072)),
    # 1.1: the step part way up the ramp, and from its sides paths 128 wide round the platform's corners to the outer walk
    # (the right one to the bay's wall, and on across into the machine shop's aft corner).
    ("Thruster Step", "step", WALK, [(1772, 12700), (2700, 12700), (2700, 12944), (3232, 12944), (3232, 13200), (3104, 13200),
                                     (3104, 13072), (1696, 13072), (1696, 13200), (1568, 13200), (1568, 12944), (1772, 12944)]),
    ("Port Passage", "passage", DECK, [(4400, 3280), (5000, 3280), (5000, 13072), (4000, 13072), (4000, 3900), (4400, 3900)]),
    ("Thruster Platform", "platform", STERN_DECK, (1696, 13072, 3104, 13904)),
]
SHARED_ROOMS = [
    ("Bridge", "shared", BRIDGE_DECK, [(5200, 1500), (7088, 1500), (7288, 1800), (7288, 3780), (5000, 3780), (5000, 1800)]),
    ("CIC", "shared", DECK, (5000, 3900, 7288, 6600)),
    ("Main Reactor", "shared", DECK, (5000, 6700, 7288, 9400)),
    ("Shield Generator", "shared", DECK, (5000, 9500, 7288, 11200)),
    ("Engine Room", "shared", DECK, (5000, 11300, 7288, 13072)),
    ("Engine Step", "step", WALK, [(5716, 12700), (6572, 12700), (6572, 12944), (7076, 12944), (7076, 13200), (6948, 13200),
                                   (6948, 13072), (5340, 13072), (5340, 13200), (5212, 13200), (5212, 12944), (5716, 12944)]),
    ("Thruster Platform", "platform", STERN_DECK, (5340, 13072, 6948, 13904)),
    ("Front Opening", "shared", BRIDGE_DECK, (5844, 1300, 6444, 1500)),
]
# The reactor's sunken well (centre, radius), the floor at REACTOR_WELL, a ledge of 500 or more all round it at deck
# level. Its two ramps face the crossing doors: from each door down into the well (600 wide, 120 down over 600, about
# 11 degrees), so the reactor lane runs down past the core and up the other side; the ledges are the high way round.
REACTOR_WELL_AT, REACTOR_WELL_R = (6144, 8050), 640
WELL_RAMPS = [(5000, 7750, 5600, 8350, DECK, REACTOR_WELL)]     # port (and its mirror): x0 z0 x1 z1, high end at x0

# ---- Ramps (port; mirrored): in line with the passage, from a landing on the bridge's deck (z 1900-2400, joined to the
# bridge along its 500-long west side) down 880 at about 9 degrees to the passage's head. (x0, z0, x1, z1, from, to)
RAMP_LANDING = (4400, 1900, 5000, 2400)
RAMPS = [(4400, 2400, 5000, 3280, BRIDGE_DECK, DECK),
         (1900, 11700, 2500, 12700, WALK, DECK), (1900, 12800, 2500, 13072, STERN_DECK, WALK)]   # 1.1: the thruster bay's, by its step
CENTRE_RAMPS = [(5844, 12000, 6444, 12700, WALK, DECK), (5844, 12800, 6444, 13072, STERN_DECK, WALK)]   # 1.1: the engine room's

# ---- Doors: gaps in the bulkheads. (x0, z0, x1, z1, kind). Port side (and its mirror); centreline doors are given once.
DOORS_PORT = [
    # between the outboard and inboard columns
    (2700, 5000, 2800, 5500, "room"), (2700, 7100, 2800, 7600, "room"), (2700, 9200, 2800, 9700, "room"), (2700, 11200, 2800, 11700, "room"),
    # inboard rooms to the passage
    (4000, 4800, 4000, 5400, "passage"), (4000, 7100, 4000, 7600, "passage"), (4000, 9100, 4000, 9700, "passage"), (4000, 11700, 4000, 12200, "passage"),
    # fore and aft within each column
    (1500, 6600, 2100, 6700, "room"), (1500, 8000, 2100, 8100, "room"), (1500, 10800, 2100, 10900, "room"),
    (3100, 6600, 3700, 6700, "room"), (3100, 8000, 3700, 8100, "room"), (3100, 10800, 3700, 10900, "room"),
    # the weapons bay to the forward hangar, the crew deck, and the foot of the bridge ramp
    (1500, 3780, 2100, 3900, "room"), (3100, 3780, 3700, 3900, "room"), (4400, 3280, 4400, 3780, "passage"),
    # the passage to the shared rooms (the centreline crossings)
    (5000, 4900, 5000, 5600, "crossing"), (5000, 7300, 5000, 8800, "crossing"), (5000, 9650, 5000, 11050, "crossing"), (5000, 11500, 5000, 12650, "crossing"),
]
DOORS_CENTRE = [
    (5444, 6600, 6844, 6700, "spine"), (5444, 9400, 6844, 9500, "spine"), (5444, 11200, 6844, 11300, "spine"),
    (5794, 3780, 6494, 3900, "stairs"),       # the bridge's grand stair down into the CIC: bots only
]

# ---- Starts (1.1, 8v8): (room, x, z, role). Port teams 0-7; starboard 8-15 are their mirror images. All in the outboard
# rooms along the hull's wall: air in the weapons bay, two on each hangar's landing circles, tech by the power room's vent
# (its floor circle centred on the vent), two in the thruster bay (the aft one outboard of the ramp). None inboard.
STARTS = [("Weapons Bay", 2400, 3240, "air"), ("Forward Hangar", 1864, 4575, "front"), ("Forward Hangar", 1864, 5925, "front"),
          ("Power Room", 1864, 7580, "tech"), ("Main Hangar", 1864, 8775, "front"), ("Main Hangar", 1864, 10125, "front"),
          ("Thruster Bay", 1864, 11280, "front"), ("Thruster Bay", 1430, 12420, "support")]

# ---- Metal (1.1): a metal map. Every deck floor holds metal (raw 79, maxMetal 7.5, extractor radius 24: Cloud9_V2's),
# so there are no spots to draw. The vents (x, z) on the port side; and on the centreline (each given with its mirror).
VENTS_PORT = [(1864, 7350)]                                    # the power room's own
VENTS_SHARED_PORT = [(5844, 8470)]                             # on the reactor well's floor, one each side of the core

LANES = [("Bridge", 2150), ("CIC", 5250), ("Reactor", 8050), ("Shield", 10350), ("Engine", 12150)]


def rect(r):
    x0, z0, x1, z1 = r
    return [(x0, z0), (x1, z0), (x1, z1), (x0, z1)]


def poly(shape):
    return rect(shape) if isinstance(shape, tuple) else shape


def mirror(points):
    return [(mx(x), z) for x, z in points]


def area(points):
    return abs(sum(points[i][0] * points[(i + 1) % len(points)][1] - points[(i + 1) % len(points)][0] * points[i][1] for i in range(len(points)))) / 2


# ---- The drawing.
S = 16                              # game units a pixel
LEGEND = 300


def P(x, z):
    return f"{x / S:.1f},{z / S:.1f}"


def svg():
    o = []
    w, h = W // S + LEGEND, H // S + 30
    o.append(f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" viewBox="0 0 {w} {h}" font-family="Segoe UI, Arial, sans-serif">')
    o.append('<rect width="100%" height="100%" fill="#f4f3ef"/>')
    # space and its stars
    o.append(f'<rect x="0" y="0" width="{W / S}" height="{H / S}" fill="#05070f"/>')
    rnd = random.Random(50)
    for _ in range(420):
        x, y, r = rnd.uniform(0, W / S), rnd.uniform(0, H / S), rnd.choice([0.5, 0.6, 0.8, 1.1])
        o.append(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="{r}" fill="#cfd8ff" opacity="{rnd.uniform(0.35, 0.95):.2f}"/>')
    # the hull: its rim, then the deck
    pts = " ".join(P(x, z) for x, z in HULL)
    o.append(f'<polygon points="{pts}" fill="#4a4f57" stroke="#9aa3ad" stroke-width="3"/>')
    # rooms
    fills = {"start": "#c9ced6", "team": "#b3b9c3", "passage": "#a3aab5", "shared": "#9fc1dd", "platform": "#e8b98a", "step": "#d9c39a"}
    # the outer walk (1.1): a band inside the hull's edge, and the bow ahead of the bridge (rooms are drawn over it)
    o.append(f'<clipPath id="hullclip"><polygon points="{pts}"/></clipPath>')
    o.append(f'<g clip-path="url(#hullclip)"><polygon points="{pts}" fill="none" stroke="#7d8794" stroke-width="{2 * WALK_BAND / S:.1f}"/>'
             f'<rect x="0" y="0" width="{W / S}" height="{BOW_FRONT_Z / S}" fill="#7d8794"/></g>')
    def room(name, kind, deck, shape, side):
        pts = poly(shape) if side == "port" else mirror(poly(shape))
        stroke = {"port": "#c0392b", "starboard": "#1e8449", "centre": "#2e6aa0"}[side]
        o.append(f'<polygon points="{" ".join(P(x, z) for x, z in pts)}" fill="{fills[kind]}" stroke="{stroke}" stroke-width="1.6"/>')
        xs, zs = [p[0] for p in pts], [p[1] for p in pts]
        cx, cz = sum(xs) / len(xs), sum(zs) / len(zs)
        if kind == "passage":
            o.append(f'<text x="{cx / S:.1f}" y="{cz / S:.1f}" font-size="10" fill="#333" text-anchor="middle" transform="rotate(-90 {cx / S:.1f} {cz / S:.1f})">{"Port" if side == "port" else "Starboard"} Passage</text>')
            return
        ty = (min(zs) / S + 15) if isinstance(shape, tuple) else (cz / S - 4)          # rooms: the name along the top
        title, _, seat = name.partition(" · ")
        o.append(f'<text x="{cx / S:.1f}" y="{ty:.1f}" font-size="10.5" font-weight="600" fill="#1d2329" text-anchor="middle">{html.escape(title)}</text>')
        if seat:
            o.append(f'<text x="{cx / S:.1f}" y="{ty + 12:.1f}" font-size="9" fill="#7a2e1f" text-anchor="middle">{seat} seat</text>')
        if deck != DECK:
            o.append(f'<text x="{cx / S:.1f}" y="{ty + 12:.1f}" font-size="9" fill="#333" text-anchor="middle">deck {deck}</text>')
    for name, kind, deck, shape in TEAM_ROOMS:
        room(name, kind, deck, shape, "port")
        room(name, kind, deck, shape, "starboard")
    for name, kind, deck, shape in SHARED_ROOMS:
        room(name, kind, deck, shape, "centre")
    # the engine housings at the stern (1.1: each a thruster platform; their engine bells hang out over space)
    for x0, x1 in ((1600, 3200), (5244, 7044), (mx(3200), mx(1600))):
        o.append(f'<text x="{(x0 + x1) / 2 / S:.1f}" y="{13960 / S:.1f}" font-size="8" fill="#d7dce2" text-anchor="middle">engines</text>')
    # the reactor well and its core
    (wx, wz), wr = REACTOR_WELL_AT, REACTOR_WELL_R
    o.append(f'<circle cx="{wx / S}" cy="{wz / S}" r="{wr / S}" fill="#7f9fbd" stroke="#2e6aa0" stroke-width="1.2" stroke-dasharray="4 3"/>')
    o.append(f'<circle cx="{wx / S}" cy="{wz / S}" r="{180 / S}" fill="#5ff2ff" stroke="#1b6f80" stroke-width="1.5"/>')
    o.append(f'<text x="{wx / S}" y="{(wz + wr + 190) / S:.1f}" font-size="9" fill="#10324a" text-anchor="middle">well, deck {REACTOR_WELL}</text>')
    # the well's ramps
    for side in ("port", "starboard"):
        for x0, z0, x1, z1, a, b in WELL_RAMPS:
            pts = rect((x0, z0, x1, z1))
            pts = pts if side == "port" else mirror(pts)
            o.append(f'<polygon points="{" ".join(P(x, z) for x, z in pts)}" fill="url(#hatch)" stroke="#6b4f00" stroke-width="1" opacity="0.85"/>')
    # ramps
    for side in ("port", "starboard"):
        lx0, lz0, lx1, lz1 = RAMP_LANDING
        lpts = rect(RAMP_LANDING) if side == "port" else mirror(rect(RAMP_LANDING))
        o.append(f'<polygon points="{" ".join(P(x, z) for x, z in lpts)}" fill="#9fc1dd" stroke="#2e6aa0" stroke-width="1"/>')
        for x0, z0, x1, z1, a, b in RAMPS:
            pts = rect((x0, z0, x1, z1))
            pts = pts if side == "port" else mirror(pts)
            o.append(f'<polygon points="{" ".join(P(x, z) for x, z in pts)}" fill="url(#hatch)" stroke="#6b4f00" stroke-width="1"/>')
    for x0, z0, x1, z1, a, b in CENTRE_RAMPS:
        o.append(f'<polygon points="{" ".join(P(x, z) for x, z in rect((x0, z0, x1, z1)))}" fill="url(#hatch)" stroke="#6b4f00" stroke-width="1"/>')
    o.insert(1, '<defs><pattern id="hatch" width="6" height="6" patternUnits="userSpaceOnUse" patternTransform="rotate(45)"><rect width="6" height="6" fill="#e6c25a"/><line x1="0" y1="0" x2="0" y2="6" stroke="#8a6a12" stroke-width="2"/></pattern>'
             '<marker id="arrow" viewBox="0 0 10 10" refX="5" refY="5" markerWidth="5" markerHeight="5" orient="auto-start-reverse"><path d="M0,0 L10,5 L0,10 z" fill="#d35400"/></marker></defs>')
    # doors
    colours = {"room": "#ffd84d", "passage": "#ffd84d", "crossing": "#ff8a3d", "spine": "#ff8a3d", "stairs": "#c084fc"}
    def door(x0, z0, x1, z1, kind):
        if x0 == x1: x0, x1 = x0 - 50, x1 + 50
        if z0 == z1: z0, z1 = z0 - 50, z1 + 50
        o.append(f'<rect x="{x0 / S:.1f}" y="{z0 / S:.1f}" width="{(x1 - x0) / S:.1f}" height="{(z1 - z0) / S:.1f}" fill="{colours[kind]}" stroke="#5a4500" stroke-width="0.6"/>')
    for d in DOORS_PORT:
        door(*d)
        x0, z0, x1, z1, k = d
        door(mx(x1), z0, mx(x0), z1, k)
    for d in DOORS_CENTRE:
        door(*d)
    # crossing lanes
    for name, z in LANES:
        o.append(f'<line x1="{4700 / S}" y1="{z / S}" x2="{mx(4700) / S}" y2="{z / S}" stroke="#d35400" stroke-width="2" stroke-dasharray="6 4" marker-start="url(#arrow)" marker-end="url(#arrow)" opacity="0.85"/>')
    # vents (the metal is the whole deck)
    for x, z in VENTS_PORT + VENTS_SHARED_PORT:
        for X in (x, mx(x)):
            o.append(f'<circle cx="{X / S:.1f}" cy="{z / S:.1f}" r="5" fill="#57d163" stroke="#1d5e25" stroke-width="1"/>')
    # starts
    for i, (name, x, z, seat) in enumerate(STARTS):
        for team, X, colour in ((i, x, "#c0392b"), (i + 8, mx(x), "#1e8449")):
            o.append(f'<circle cx="{X / S:.1f}" cy="{z / S:.1f}" r="13" fill="{colour}" stroke="#fff" stroke-width="2"/>')
            o.append(f'<text x="{X / S:.1f}" y="{z / S + 4:.1f}" font-size="11" font-weight="700" fill="#fff" text-anchor="middle">{team}</text>')
            o.append(f'<text x="{X / S:.1f}" y="{z / S - 17:.1f}" font-size="9" font-weight="700" fill="{colour}" text-anchor="middle">{seat.upper()}</text>')
    # centreline
    o.append(f'<line x1="{CX / S}" y1="0" x2="{CX / S}" y2="{H / S}" stroke="#ffffff" stroke-width="1" stroke-dasharray="3 5" opacity="0.6"/>')
    # legend
    lx = W / S + 16
    y = 26
    def leg(text, size=12, weight="400", dy=18, colour="#1d2329"):
        nonlocal y
        o.append(f'<text x="{lx}" y="{y}" font-size="{size}" font-weight="{weight}" fill="{colour}">{html.escape(text)}</text>')
        y += dy
    leg("Starship Meridian", 17, "700", 22)
    leg("24 × 28, 8v8, port vs starboard", 12, "400", 16)
    leg("1 px = 16 game units; bow north", 11, "400", 16, "#555")
    leg("A metal map: every deck floor is metal", 11, "700", 26, "#555")
    items = [("#c0392b", "circle", "Port team start (0-7)"), ("#1e8449", "circle", "Starboard team start (8-15)"),
             ("#57d163", "circle", "Geothermal vent"),
             ("#ffd84d", "rect", "Door (room / passage)"), ("#ff8a3d", "rect", "Crossing / spine door"),
             ("#c084fc", "rect", "Bridge stair (bots only)"), ("url(#hatch)", "rect", "Ramp, 600 wide (bridge; thruster platforms)"), ("#e8b98a", "rect", "Thruster platform, deck 440, open"), ("#d9c39a", "rect", "Thruster step and its paths to the walk, deck 380"), ("#7d8794", "rect", "Outer walk, deck 380"),
             ("#9fc1dd", "rect", "Shared room (centreline)"), ("#c9ced6", "rect", "Team start room"),
             ("#b3b9c3", "rect", "Team room"), ("#05070f", "rect", "Space (not drawn in game)")]
    for colour, shape, text in items:
        if shape == "circle":
            o.append(f'<circle cx="{lx + 8}" cy="{y - 4}" r="6" fill="{colour}" stroke="#333" stroke-width="0.8"/>')
        elif shape == "diamond":
            o.append(f'<rect x="{lx + 4}" y="{y - 8}" width="8" height="8" transform="rotate(45 {lx + 8} {y - 4})" fill="{colour}" stroke="#222" stroke-width="0.8"/>')
        else:
            o.append(f'<rect x="{lx}" y="{y - 11}" width="16" height="12" fill="{colour}" stroke="#333" stroke-width="0.8"/>')
        o.append(f'<text x="{lx + 24}" y="{y}" font-size="11" fill="#1d2329">{html.escape(text)}</text>')
        y += 19
    y += 6
    o.append(f'<line x1="{lx}" y1="{y}" x2="{lx + 32}" y2="{y}" stroke="#d35400" stroke-width="2" stroke-dasharray="6 4"/>')
    o.append(f'<text x="{lx + 40}" y="{y + 4}" font-size="11" fill="#1d2329">Crossing lane (5)</text>')
    y += 30
    o.append(f'<line x1="{lx}" y1="{y}" x2="{lx + 1000 / S}" y2="{y}" stroke="#1d2329" stroke-width="2"/>')
    o.append(f'<text x="{lx + 1000 / S + 8}" y="{y + 4}" font-size="11" fill="#1d2329">1000 units</text>')
    y += 30
    for line in [f"Decks: main {DECK}, bridge {BRIDGE_DECK},", f"reactor well {REACTOR_WELL}; bulkheads to {WALL_TOP},",
                 f"hull rim to {RIM_TOP}; space ground {SPACE}", "(rough, never drawn: voidGround).",
                 "Unlabelled hull is solid (machinery).",
                 "", "Each start's role is stencilled on its circle:", "AIR, FRONT (five), TECH and SUPPORT."]:
        leg(line, 11, "400", 15)
    o.append("</svg>")
    return "\n".join(o)


if __name__ == "__main__":
    here = os.path.dirname(os.path.abspath(__file__))
    with open(os.path.join(here, "layout.svg"), "w", encoding="utf-8", newline="\n") as f:
        f.write(svg())
    for name, kind, deck, shape in TEAM_ROOMS + SHARED_ROOMS:
        print(f"{name:20s} {kind:8s} deck {deck:3d}  {area(poly(shape)) / 1e6:5.2f} M sq units")
    print("metal: a metal map, every deck floor (raw 79, maxMetal 7.5, extractor radius 24, as Cloud9_V2); "
          "vents: 1 each side, 2 in the reactor")
    print(f"hull area {area(HULL) / 1e6:.1f} M of the map's {W * H / 1e6:.1f} M")
