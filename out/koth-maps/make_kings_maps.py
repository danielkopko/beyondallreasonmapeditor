#!/usr/bin/env python3
"""Seven king maps as SVG: three to five kings, each with three peasants.

Each team is four players. The king starts in a keep round its throne, the team's own hill: an enemy who holds the
throne topples that king. The three peasants start out in the field. Drawn with make_koth_maps.py's pieces, in the
same style. Every map is drawn for Team A and turned about the centre (a third, a quarter or a fifth of a turn), so
every team's land is the same: three- and five-king maps play on a disc, four-king maps on the whole square, which a
quarter turn maps onto itself.

Run: python make_maps.py   (writes these and the ten single-hill maps, each on its planet, then prints the checks)
"""
import math

from make_koth_maps import CX, CY, R_PLAY, Design, ab, arc, circle_pts, dist, eblob, fmt, inside, pol, poly_d, pt, rot, strip

TEAMS = [("A", "#F5C4B3", "#993C1D"), ("B", "#B5D4F4", "#185FA5"), ("C", "#D9D0F5", "#5B45A8"),
         ("D", "#BFE6D8", "#0F6E56"), ("E", "#F6C6DA", "#993556")]
KEEP_R, THRONE_R, PEASANT_R = 20, 32, 15
PART = {3: "third", 4: "quarter", 5: "fifth"}


def bearing(p):
    """Degrees clockwise from up, as pol() measures them."""
    return math.degrees(math.atan2(p[0], -p[1]))


class KingDesign(Design):
    def __init__(self, num, key, name, size, fold, ground, ground_h, notes, axis=0.0, square=False):
        super().__init__(num, key, name, size, 3, ground, ground_h, 0, notes)
        self.fold, self.axis, self.play_disc = fold, axis, not square
        self.kings, self.peasants = [], []
        self.tag, self.filename = f"K{num}", f"k{num}_{key}.svg"
        self.title = f"{name}: a map for {fold} kings and their peasants"
        self.subtitle = (f"{fold} kings, 3 peasants each · {size} × {size} map ({size * 512} game units square) · "
                         f"{fold} teams of 4, {fold * 4} players")
        self.footer = (f"Each {PART[fold]} is the same, turned {360 / fold:g}° about the centre. "
                       "Heights in game units; lighter ground stands higher.")
        self.card = f"{size}×{size}, {fold} teams of 4 ({fold * 4} players)"

    def area(self, pts, color, h, smooth=False):
        """Ground at its own height in Team A's part (turned for the others)."""
        self._rec("band", False, pts=pts, h=h, layer="bands")
        self._shape("bands", pts, f'fill="{color}" data-height="{h}"', smooth, False)

    def king(self, p):
        self.kings.append(p)
        self.use("king")

    def peasant(self, p, rel=(90, 210, 330), v=1.8):
        """A peasant's base with its three metal spots, at bearings from straight out from the centre."""
        self.peasants.append(p)
        out = bearing(p)
        for a in rel:
            o = pol(25, out + a)
            self.metal((p[0] + o[0], p[1] + o[1]), v, "peasant")
        self.use("peasant")

    def arc_wall(self, r, a0, a1, door_at=None, door_len=0, step=8):
        """A wall along a circle from a0 to a1 degrees, in straight lengths, with a door centred on door_at."""
        pieces = [(a0, a1)]
        if door_at is not None:
            half = math.degrees(door_len / 2 / r)
            pieces = [(a0, door_at - half), (door_at + half, a1)]
            self.door(pol(r, door_at - half), pol(r, door_at + half))
        for b0, b1 in pieces:
            n = max(1, round(abs(b1 - b0) / step))
            pts = [pol(r, b0 + (b1 - b0) * i / n) for i in range(n + 1)]
            for p, q in zip(pts, pts[1:]):
                self.wall(p, q)

    def _draw_things(self):
        for t in self.turns(False):
            for p in self.kings:
                c = ab(rot(p, t))
                self.L["zone"].append(f'<circle cx="{fmt(c[0])}" cy="{fmt(c[1])}" r="{THRONE_R}" fill="#F6E3A0" fill-opacity="0.5" '
                                      f'stroke="#A6790C" stroke-width="1.8" stroke-dasharray="6 3"/>')
        self._draw_resources()
        crown = [(-8, -2), (-8, -11), (-4, -7), (0, -13), (4, -7), (8, -11), (8, -2)]
        for i, t in enumerate(self.turns(False)):
            letter, fill, stroke = TEAMS[i]
            for p in self.kings:
                c = ab(rot(p, t))
                self.L["bases"].append(f'<circle cx="{fmt(c[0])}" cy="{fmt(c[1])}" r="{KEEP_R}" fill="{fill}" stroke="{stroke}" stroke-width="1.4" data-king="{letter}"/>')
                self.L["bases"].append(f'<path d="{poly_d([(c[0] + x, c[1] + y) for x, y in crown])}" fill="#E0B532" stroke="#7A5A00" stroke-width="0.7" stroke-linejoin="round"/>')
                self.L["bases"].append(f'<text x="{fmt(c[0])}" y="{fmt(c[1] + 9)}" text-anchor="middle" font-size="11" font-weight="700" fill="{stroke}">{letter}</text>')
            for j, p in enumerate(self.peasants):
                c = ab(rot(p, t))
                self.L["bases"].append(f'<circle cx="{fmt(c[0])}" cy="{fmt(c[1])}" r="{PEASANT_R}" fill="{fill}" stroke="{stroke}" stroke-width="1.2" data-peasant="{letter}{j + 1}"/>')
                self.L["bases"].append(f'<text x="{fmt(c[0])}" y="{fmt(c[1])}" text-anchor="middle" dominant-baseline="central" font-size="10" font-weight="700" fill="{stroke}">{letter}{j + 1}</text>')

    # checks ------------------------------------------------------------

    def check(self):
        problems = []
        units = self.size * 512 / 640
        turns = self.turns(False)
        spots = [(ab(rot(p, t)), v, kind, wet) for p, v, kind, wet in self.spots for t in turns]
        keeps = [(ab(rot(p, t)), i) for i, t in enumerate(turns) for p in self.kings]
        fields = [(ab(rot(p, t)), i) for i, t in enumerate(turns) for p in self.peasants]
        geos = [ab(rot(p, t)) for p, whole in self.geos for t in self.turns(whole)]
        markers = [(q, THRONE_R, i) for q, i in keeps] + [(q, PEASANT_R, i) for q, i in fields]

        def in_play(q, margin):
            if self.play_disc:
                return dist(q, (CX, CY)) <= R_PLAY - margin
            return 20 + margin <= q[0] <= 660 - margin and 20 + margin <= q[1] <= 660 - margin

        def blocked(q):
            return any(inside(q, c) for c in self.cliff_polys) or any(inside(q, w) for w in self.wall_polys)

        def wet(q):
            return (any(inside(q, w) for w in self.water_polys) and not any(inside(q, f) for f in self.ford_polys)
                    and not any(inside(q, l) for l in self.land_polys))

        for q, v, kind, is_wet in spots:
            rr = 2.6 + (v - 1.5) * 1.3
            if not in_play(q, rr + 4):
                problems.append(f"metal at {pt(q)} is off the play area")
            if blocked(q):
                problems.append(f"metal at {pt(q)} is on a cliff or in a wall")
            if wet(q) != is_wet:
                problems.append(f"metal at {pt(q)} is {'in water' if wet(q) else 'dry'} but marked {'wet' if is_wet else 'dry'}")
            for m, r, _ in markers:
                if dist(q, m) < r + rr + 1:
                    problems.append(f"metal at {pt(q)} is on a throne or a peasant's base")
            if any(dist(q, g) < 12 for g in geos):
                problems.append(f"metal at {pt(q)} is on a vent")
        for g in geos:
            if not in_play(g, 10) or blocked(g) or wet(g):
                problems.append(f"the vent at {pt(g)} is off the map, on a cliff or wall, or in water")
            if any(dist(g, m) < r + 7 for m, r, _ in markers):
                problems.append(f"the vent at {pt(g)} is on a throne or a peasant's base")
        for i in range(len(spots)):
            for j in range(i + 1, len(spots)):
                if dist(spots[i][0], spots[j][0]) < 11:
                    problems.append(f"metal at {pt(spots[i][0])} and {pt(spots[j][0])} overlap")
        for m, r, _ in markers:
            body = KEEP_R if r == THRONE_R else PEASANT_R
            if not in_play(m, r if r == PEASANT_R else KEEP_R + 4):
                problems.append(f"the base or keep at {pt(m)} runs off the map")
            if any(blocked(q) or wet(q) for q in [m] + circle_pts(m, body, 12)):
                problems.append(f"the base or keep at {pt(m)} stands on a cliff, a wall or water")
        for a in range(len(markers)):
            for b in range(a + 1, len(markers)):
                if dist(markers[a][0], markers[b][0]) < markers[a][1] + markers[b][1] + 4:
                    problems.append(f"the bases or thrones at {pt(markers[a][0])} and {pt(markers[b][0])} overlap")
        step = 360 / self.fold
        for q, v, _, _ in spots:
            t = ab(rot((q[0] - CX, q[1] - CY), step))
            if not any(dist(t, q2) < 0.5 and v2 == v for q2, v2, _, _ in spots):
                problems.append(f"metal at {pt(q)} has no twin a {PART[self.fold]} of a turn round")
        steepest = 0.0
        for L, rise in self.ramps:
            a = math.degrees(math.atan2(rise, L * units))
            steepest = max(steepest, a)
            if a > 20:
                problems.append(f"a ramp climbs {rise} in {L * units:.0f} units, {a:.0f} degrees: too steep for vehicles")
        k0 = [q for q, i in keeps if i == 0][0]
        own = [q for q, i in fields if i == 0]
        enemy = [q for q, i in keeps + fields if i != 0]
        far = max(dist(k0, q) for q in own)
        exposed = min(dist(q, e) for q in own for e in enemy)
        keep_gap = min(dist(k0, e) for e in enemy)
        team = sum(v for _, v, k, _ in self.spots if k in ("king", "peasant", "own"))
        shared = sum(v for _, v, k, _ in self.spots if k == "shared")
        centre = sum(v for _, v, k, _ in self.spots if k == "centre")
        row = (f"{self.tag:<3} {self.name:<18} {self.size}x{self.size}  {self.fold} kings  "
               f"metal: team {team:4.1f}, shared {shared:3.1f}, centre {centre:3.1f} (x{self.fold})  "
               f"king to own peasants <= {far * units:4.0f}  peasant to nearest enemy {exposed * units:4.0f}  "
               f"king to nearest enemy {keep_gap * units:4.0f}  steepest ramp {steepest:4.1f} deg")
        return row, problems


# ---------------------------------------------------------------- the seven maps

def three_crowns():
    d = KingDesign(1, "three_crowns", "Three Crowns", 20, 3, "#CFE0B2", 220, [
        "Each king holds a cliff-top keep on the rim, reached by one ramp. Its three peasants farm the slope",
        "below and look across the Commons, a low basin in the middle with the richest metal. Edge hills keep",
        "the kings apart; peasants meet their neighbours on the ring road between the realms.",
    ])
    d.heights = "rim 220 · slopes 190 to 160 · the Commons 140 · keeps 340"
    for r, col, h in [(178, "#C4D89F", 190), (120, "#B6CF8E", 160), (72, "#A9C67E", 140)]:
        d.band(r, col, h)
    d.clifftop(circle_pts(pol(252, 0), 46, 60), "#EEF2E3", 340, smooth=False)
    d.ramp(pol(186, 0), pol(224, 0), 12, 120)
    d.cliff(eblob(250, 60, 40, 24, seed=1), 300)
    d.path([pol(186, 0), pol(165, 0)])
    d.path([pol(186, 0), pol(178, -16), pol(165, -32)], both=True)
    d.path([pol(135, 0), pol(100, 0), pol(60, 0)])
    d.path([pol(165, 32), pol(160, 46), pol(158, 60)], both=True)
    d.king(pol(256, 0))
    d.metal((38, -250), 2.0, "king", both=True)
    d.peasant(pol(165, -32), rel=(110, 200, 290))
    d.peasant(pol(150, 0), rel=(90, 215, 325))
    d.peasant(pol(165, 32), rel=(70, 160, 250))
    d.metal(pol(182, 60), 2.6, "shared")
    d.metal(pol(52, 0), 3.0, "centre")
    d.metal(pol(52, 60), 3.0, "centre")
    d.geo(pol(96, 60))
    d.label((0, 4), "The Commons")
    return d


def triad_court():
    d = KingDesign(2, "triad_court", "Triad Court", 20, 3, "#C4D89F", 200, [
        "The three kings share one walled citadel in the middle, each in its own third, with a door out to its",
        "peasants and a door in to the council chamber, where the treasury's metal lies. Ridges divide the",
        "peasants' realms; the ring road round the walls is the only way between them for vehicles.",
    ])
    d.heights = "realms 200 · citadel 260 (walls 120 high) · ridges 380"
    d.band(112, "#D9E6C0", 260)
    d.cliff(strip([pol(r, 60) for r in range(184, 340, 12)], 14), 380)
    d.track(pol(254, 51), pol(254, 69), 3, 5)
    d.path(arc(136, -60, 60, 6))
    d.path([pol(110, 0), pol(160, 0), pol(207, 0)])
    d.path([pol(160, 0), pol(186, -18), pol(205, -34)], both=True)
    d.path([pol(34, 0), pol(72, 0), pol(110, 0)])
    d.arc_wall(110, -60, 60, door_at=0, door_len=24)
    d.ramp(pol(132, 0), pol(96, 0), 11, 60)
    d.wall(pol(34, 60), pol(110, 60))
    d.arc_wall(34, -60, 60, door_at=0, door_len=14, step=12)
    d.tower(pol(110, 60))
    d.tower(pol(34, 60), r=5)
    d.king(pol(72, 0))
    d.metal(pol(90, 42), 2.0, "king", both=True)
    d.metal(pol(18, 60), 3.4, "centre")
    d.peasant(pol(205, -34), rel=(120, 210, 300))
    d.peasant(pol(222, 0), rel=(90, 210, 330))
    d.peasant(pol(205, 34), rel=(60, 150, 240))
    d.metal(pol(160, 60), 2.6, "shared")
    d.metal(pol(278, 26), 2.2, both=True)
    return d


def corner_fortresses():
    d = KingDesign(3, "corner_fortresses", "Corner Fortresses", 24, 4, "#CFE0B2", 240, [
        "Four kings, each on a raised fortress yard in a corner behind earth ramparts, reached by a ramp through",
        "the gate facing the middle and by bots-only zigzags up the two walls. All twelve peasants live in the",
        "middle, round the market, each team's three nearest its king. Acid lakes part the fortresses.",
    ], axis=-45, square=True)
    d.heights = "fields 240 · the middle 210 to 180 · the market 220 · fortress yards 330, ramparts 90 higher · acid lakes 60 deep"
    d.legend_text["wall"] = "Rampart (earth; build on it)"
    for r, col, h in [(230, "#C4D89F", 210), (128, "#B6CF8E", 180)]:
        d.band(r, col, h)
    d.band(56, "#E4ECD2", 220)
    d.clifftop([(-330, -330), (-170, -330), (-170, -210), (-210, -170), (-330, -170)], "#E2EBCF", 330, smooth=False)
    d.water(eblob(292, 0, 56, 36, seed=3), smooth=True)
    gate0, gate1 = (-210, -170), (-170, -210)
    d.wall((-330, -170), gate0)
    d.wall(gate0, (-200.8, -179.2))
    d.wall((-179.2, -200.8), gate1)
    d.wall(gate1, (-170, -330))
    d.tower(gate0)
    d.tower(gate1)
    d.ramp(pol(236, -45), pol(290, -45), 11, 90)
    d.track((-265, -186), (-265, -140), 4, 6, both=True)
    d.path([(-190, -190), pol(215, -45), pol(165, -45)])
    d.path([pol(236, -45), pol(196, -60), pol(150, -73)], both=True)
    d.path(arc(116, -90, 0, 6))
    d.path([pol(135, -45), pol(116, -45)])
    d.path([pol(116, -45), pol(60, -45)])
    d.king((-255, -255))
    d.metal((-295, -205), 2.0, "king", both=True)
    d.metal((-292, -292), 2.0, "king")
    d.peasant(pol(150, -73), rel=(80, 170, 260))
    d.peasant(pol(150, -45), rel=(90, 210, 330))
    d.peasant(pol(150, -17), rel=(100, 190, 280))
    d.metal(pol(150, 0), 2.8, "shared")
    d.metal(pol(252, -27), 2.2, both=True)
    d.metal(pol(36, 0), 3.4, "centre")
    d.geo((-300, -240))
    d.geo(pol(240, -70))
    d.geo((0, 0), whole=True)
    return d


def edge_keeps():
    d = KingDesign(4, "edge_keeps", "Edge Keeps", 24, 4, "#CFE0B2", 240, [
        "Each king's keep stands on a cliff jutting from the middle of an edge, with a ramp facing the loch in the",
        "centre and bots-only zigzags up both sides. Its peasants live in the corner beside it, clockwise, under the",
        "next king's cliff: two kings look down on every corner. A bridge from each keep crosses to the isle.",
    ], square=True)
    d.heights = "fields 240 · the basin 220 · keeps 360 · the loch 60 deep · its isle 60"
    d.band(200, "#C4D89F", 220)
    d.clifftop([(-80, -335), (80, -335), (82, -270), (55, -232), (0, -216), (-55, -232), (-82, -270)], "#EEF2E3", 360)
    d.ramp(pol(186, 0), pol(228, 0), 12, 120)
    d.track((62, -268), (104, -268), 4, 6, both=True)
    d.water(72)
    d.land(circle_pts((0, 0), 38, 72), "#DDE8C8", 60, whole=True, smooth=False)
    d.bridge(pol(80, 0), pol(32, 0), 9)
    d.cliff(eblob(176, 45, 26, 16, seed=4), 300)
    d.path([pol(186, 0), pol(130, 0), pol(80, 0)])
    d.path([pol(186, 0), pol(205, 14), pol(250, 30)])
    d.path([pol(250, 30), pol(292, 38), pol(320, 45)])
    d.path([pol(320, 45), pol(292, 52), pol(250, 60)])
    d.king((0, -275))
    d.metal((48, -290), 2.0, "king", both=True)
    d.peasant(pol(250, 30), rel=(80, 180, 280))
    d.peasant(pol(320, 45), rel=(90, 180, 270))
    d.peasant(pol(250, 60), rel=(80, 180, 280))
    d.metal(pol(132, 45), 2.6)
    d.metal(pol(200, -22), 2.2)
    d.metal(pol(20, 45), 3.4, "centre")
    d.geo((-46, -306))
    d.geo(pol(296, 28))
    d.geo((0, 0), whole=True)
    d.label(pol(100, -26), "Loch")
    return d


def moat_court():
    d = KingDesign(5, "moat_court", "Moat Court", 24, 4, "#CFE0B2", 240, [
        "The four kings hold moated islands in the middle, each with two bridges out to its corner and one in",
        "to the Crown Isle, where all four meet. Their peasants hold the corners. A cliff in the middle of each",
        "edge parts two teams' peasants, with contested metal in front of it.",
    ], axis=-45, square=True)
    d.heights = "corners 240 · moat and canals 60 deep · keep islands 280 · the Crown Isle 300"
    d.water([(-204, -204), (204, -204), (204, 204), (-204, 204)], whole=True)
    d.land([(-172, -172), (-12, -172), (-12, -56), (-56, -12), (-172, -12)], "#D9E6C0", 280, smooth=False)
    d.land(circle_pts((0, 0), 28, 60), "#E4ECD2", 300, whole=True, smooth=False)
    d.bridge((-100, -166), (-100, -210), 9)
    d.bridge((-166, -100), (-210, -100), 9)
    d.bridge(pol(52, -45), pol(22, -45), 7)
    d.cliff(eblob(282, 0, 40, 22, seed=5), 300)
    d.path([(-100, -210), (-112, -226), (-128, -240)], both=True)
    d.path([(-128, -240), (-180, -250), (-233, -233)], both=True)
    d.path([(-100, -166), (-98, -128), (-92, -92)], both=True)
    d.king((-92, -92))
    d.metal((-150, -58), 2.0, "king", both=True)
    d.metal(pol(14, -45), 3.4, "centre")
    d.peasant((-233, -233), rel=(90, 180, 270))
    d.peasant((-240, -128), rel=(150, 240, 330))
    d.peasant((-128, -240), rel=(30, 120, 210))
    d.metal(pol(232, 0), 2.8, "shared")
    return d


def star_fort():
    d = KingDesign(6, "star_fort", "Star Fort", 28, 5, "#C4D89F", 200, [
        "One star fort holds all five kings, each on a raised bastion behind earth ramparts round the central",
        "spire: a ramp climbs in at the bastion's point, facing its peasants, and bots-only zigzags climb both its",
        "flanks. The peasants hold the five realms outside, parted by ridges with a bots-only pass over each.",
    ])
    d.heights = "realms 200 · bastions 300, ramparts 90 higher · ridges 380 · the spire 520"
    d.legend_text["wall"] = "Rampart (earth; build on it)"
    d.clifftop([pol(26, -36), pol(96, -36), pol(160, -6), pol(160, 6), pol(96, 36), pol(26, 36)], "#E2EBCF", 300, smooth=False)
    d.cliff(circle_pts((0, 0), 24, 48), 520, smooth=False, whole=True)
    d.cliff(strip([pol(r, 36) for r in range(222, 340, 12)], 13), 380)
    d.track(pol(276, 29), pol(276, 43), 3, 5)
    d.path(arc(184, -36, 36, 6))
    d.path([pol(181, 0), pol(205, 0), pol(247, 0)])
    d.path([pol(205, 0), pol(220, -12), pol(232, -24)], both=True)
    d.path([pol(130, 0), pol(160, 0)])
    d.wall(pol(96, -36), pol(160, -6))
    d.wall(pol(160, 6), pol(96, 36))
    d.wall(pol(26, 36), pol(96, 36))
    d.tower(pol(96, 36))
    d.tower(pol(160, -6), r=5)
    d.tower(pol(160, 6), r=5)
    d.ramp(pol(212, 0), pol(146, 0), 10, 100)
    # A zigzag up the middle of each flank, from just inside the rampart out to the realm.
    a, b = pol(96, -36), pol(160, -6)
    m = ((a[0] + b[0]) / 2, (a[1] + b[1]) / 2)
    L = math.hypot(b[0] - a[0], b[1] - a[1])
    n = (-(b[1] - a[1]) / L, (b[0] - a[0]) / L)
    keep = pol(100, 0)
    if (m[0] - keep[0]) * n[0] + (m[1] - keep[1]) * n[1] < 0:
        n = (-n[0], -n[1])
    d.track((m[0] - n[0] * 6, m[1] - n[1] * 6), (m[0] + n[0] * 26, m[1] + n[1] * 26), 4, 6, both=True)
    d.king(pol(100, 0))
    d.metal(pol(70, 22), 2.0, "king", both=True)
    d.peasant(pol(232, -24), rel=(120, 210, 300))
    d.peasant(pol(262, 0), rel=(90, 180, 270))
    d.peasant(pol(232, 24), rel=(60, 150, 240))
    d.metal(pol(204, 36), 2.6, "shared")
    d.metal(pol(298, 14), 2.2, both=True)
    d.geo(pol(122, 22))
    d.geo(pol(290, -28))
    d.label((0, 4), "Spire", italic=False, color="#2C2C2A")
    return d


def five_rivers():
    d = KingDesign(7, "five_rivers", "Five Rivers", 28, 5, "#CFE0B2", 220, [
        "Each king's hall is an oval cliff-top on the rim, long along the edge, with a graded ramp facing its",
        "peasants and bots-only switchbacks up the other three sides. Rivers run out from a lake between the",
        "realms; peasants cross them by bridge or ford, and the lake by bridge to the isle and its vent.",
    ])
    d.heights = ("rim 220, 140 nearer the lake · valleys down to the water · rivers and lake 60 deep, fords 10 deep · "
                 "bridges 16 · isle 60 · halls 340")
    for r, col, h in [(190, "#C4D89F", 180), (125, "#B6CF8E", 140)]:
        d.band(r, col, h)
    d.water(80)

    def centre(r):
        return pol(r, 36 + 3 * math.sin((r - 76) / 26))

    d.water(strip([centre(r) for r in range(76, 340, 6)], 13))
    d.land(circle_pts((0, 0), 44, 72), "#DDE8C8", 200, whole=True, smooth=False)
    d.band(28, "#E8EEDA", 230, layer="islands")

    def across(r, half):
        c = centre(r)
        a = math.radians(36 + 3 * math.sin((r - 76) / 26))
        t = (math.cos(a), math.sin(a))
        return (c[0] - t[0] * half, c[1] - t[1] * half), (c[0] + t[0] * half, c[1] + t[1] * half)

    b0, b1 = across(200, 24)
    f0, f1 = across(272, 19)
    d.bridge(b0, b1, 9)
    d.ford(f0, f1, 11, 4)
    d.bridge(pol(88, 0), pol(40, 0), 9)
    # The kings' halls: ovals 78 x 46 in place of the round 46 x 46 (70% more ground), long along the rim, so they
    # face the edge and the peasants. A graded ramp at the front; bots-only switchbacks up both ends and the back,
    # the back one down to the strip of rim behind the hall.
    hall = (0, -240)
    d.clifftop([(hall[0] + 78 * math.cos(t), hall[1] + 46 * math.sin(t)) for t in (2 * math.pi * i / 72 for i in range(72))],
               "#EEF2E3", 340, smooth=False)
    d.ramp(pol(176, 0), pol(212, 0), 12, 120)
    d.track((64, -240), (108, -240), 5, 6, both=True)
    d.track((30, -276), (38, -302), 4, 5)
    d.path([pol(176, 0), pol(152, 0)])
    d.path([pol(176, 0), pol(176, 10), pol(172, 18)], both=True)
    d.path([pol(137, 0), pol(110, 0), pol(88, 0)])
    d.path([pol(172, 18), pol(186, 24), b0], both=True)
    d.path([(108, -240), (132, -236), f0], both=True)
    d.king((0, -236))
    d.metal((44, -222), 2.0, "king", both=True)
    d.metal((48, -256), 2.0, "king", both=True)
    d.peasant(pol(172, -18), rel=(185, 250, 320))
    d.peasant(pol(152, 0), rel=(60, 180, 300))
    d.peasant(pol(172, 18), rel=(40, 110, 175))
    d.metal(centre(272), 3.0, "shared")
    d.metal(pol(24, 36), 3.4, "centre")
    # Vents: one on each hall behind the throne, one in each realm's fields by the lake road, one on the isle.
    d.geo((0, -276))
    d.geo(pol(112, 14))
    d.geo((0, 0), whole=True)
    return d


MAPS = [three_crowns, triad_court, corner_fortresses, edge_keeps, moat_court, star_fort, five_rivers]
