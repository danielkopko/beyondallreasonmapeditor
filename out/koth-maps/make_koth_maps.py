#!/usr/bin/env python3
"""Ten three-way king-of-the-hill map designs as SVG.

Drawn in the style of mirrored_ravine_map_middle_walls_doors.svg, the drawing Simmering Ravine was built from: the
same colours, contour bands, cliff hills, sloping paths, foot tracks, bridges, fords, walls and doors. A three-way
map adds the hill's capture zone, ramps, metal spots, geothermal vents and start positions.

Every map is drawn for Team A's third (A at the top) and turned 120 and 240 degrees about the hill, so the three
sides are exactly alike. A square BAR map can't be turned by 120 degrees onto itself, so the play area is a disc
and the corners beyond it are impassable map edge.

Coordinates: the map area is x, y 20-660 (640 px square), the hill at (340, 340). pol(r, deg) is a point r px from
the hill, deg degrees clockwise from Team A's direction (up): Team B is at 120, Team C at 240 (-120), and the gaps
between teams at 60, 180 and 300 (-60). A feature at 60 is shared by A and B.

Run: python make_koth_maps.py   (writes the ten SVGs and index.html beside this file, then prints the checks)
"""
import math
import os
import re
import sys
import textwrap
from xml.sax.saxutils import escape

HERE = os.path.dirname(os.path.abspath(__file__))
CX, CY = 340.0, 340.0
R_PLAY = 312.0
BASE_R, BASE_SIZE = 238.0, 46.0
TEAMS = [("A", "#F5C4B3", "#993C1D"), ("B", "#B5D4F4", "#185FA5"), ("C", "#D9D0F5", "#5B45A8")]

LAYERS = ["bands", "contours", "water", "islands", "islandcontours", "fords", "bridges", "cliffs", "cliffticks",
          "ramps", "paths", "tracks", "walls", "doors", "zone", "wrecks", "metal", "geo", "bases"]


# ---------------------------------------------------------------- geometry

def pol(r, deg):
    a = math.radians(deg)
    return (r * math.sin(a), -r * math.cos(a))


def rot(p, deg):
    a = math.radians(deg)
    c, s = math.cos(a), math.sin(a)
    return (p[0] * c - p[1] * s, p[0] * s + p[1] * c)


def ab(p):
    return (CX + p[0], CY + p[1])


def mirror(pts):
    """Team A's own mirror image (left for right); turned, it gives the other teams' far sides."""
    return [(-x, y) for x, y in pts]


def bp(dx, dy):
    """A point relative to Team A's base centre."""
    return (dx, -BASE_R + dy)


def dist(p, q):
    return math.hypot(p[0] - q[0], p[1] - q[1])


def fmt(v):
    s = f"{v:.1f}"
    if s.endswith(".0"):
        s = s[:-2]
    return "0" if s == "-0" else s


def pt(p):
    return f"{fmt(p[0])} {fmt(p[1])}"


def bezier_segments(pts, closed):
    """Catmull-Rom through the points, as cubic Beziers (p1, c1, c2, p2)."""
    n = len(pts)
    if closed:
        get, count = (lambda i: pts[i % n]), n
    else:
        get, count = (lambda i: pts[max(0, min(n - 1, i))]), n - 1
    segs = []
    for i in range(count):
        p0, p1, p2, p3 = get(i - 1), get(i), get(i + 1), get(i + 2)
        c1 = (p1[0] + (p2[0] - p0[0]) / 6, p1[1] + (p2[1] - p0[1]) / 6)
        c2 = (p2[0] - (p3[0] - p1[0]) / 6, p2[1] - (p3[1] - p1[1]) / 6)
        segs.append((p1, c1, c2, p2))
    return segs


def smooth_d(pts, closed):
    d = "M" + pt(pts[0]) + "".join(f"C{pt(c1)} {pt(c2)} {pt(p2)}" for _, c1, c2, p2 in bezier_segments(pts, closed))
    return d + ("Z" if closed else "")


def smooth_samples(pts, closed, k=12):
    out = []
    for p1, c1, c2, p2 in bezier_segments(pts, closed):
        for j in range(k):
            t = j / k
            u = 1 - t
            out.append((u ** 3 * p1[0] + 3 * u * u * t * c1[0] + 3 * u * t * t * c2[0] + t ** 3 * p2[0],
                        u ** 3 * p1[1] + 3 * u * u * t * c1[1] + 3 * u * t * t * c2[1] + t ** 3 * p2[1]))
    if not closed:
        out.append(pts[-1])
    return out


def poly_d(pts, closed=True):
    return "M" + "L".join(pt(p) for p in pts) + ("Z" if closed else "")


def circle_pts(c, r, n=72):
    return [(c[0] + r * math.cos(2 * math.pi * i / n), c[1] + r * math.sin(2 * math.pi * i / n)) for i in range(n)]


def inside(p, poly):
    x, y = p
    hit = False
    j = len(poly) - 1
    for i in range(len(poly)):
        xi, yi = poly[i]
        xj, yj = poly[j]
        if (yi > y) != (yj > y) and x < (xj - xi) * (y - yi) / (yj - yi) + xi:
            hit = not hit
        j = i
    return hit


def length(pts):
    return sum(dist(pts[i], pts[i + 1]) for i in range(len(pts) - 1))


def unit(a, b):
    L = dist(a, b)
    return ((b[0] - a[0]) / L, (b[1] - a[1]) / L), L


def rect_pts(a, b, hw):
    (ux, uy), _ = unit(a, b)
    nx, ny = -uy * hw, ux * hw
    return [(a[0] + nx, a[1] + ny), (b[0] + nx, b[1] + ny), (b[0] - nx, b[1] - ny), (a[0] - nx, a[1] - ny)]


def stadium_pts(a, b, hw, n=10):
    (ux, uy), _ = unit(a, b)
    base = math.atan2(uy, ux)
    pts = []
    for i in range(n + 1):
        t = base + math.pi / 2 - math.pi * i / n
        pts.append((b[0] + hw * math.cos(t), b[1] + hw * math.sin(t)))
    for i in range(n + 1):
        t = base - math.pi / 2 - math.pi * i / n
        pts.append((a[0] + hw * math.cos(t), a[1] + hw * math.sin(t)))
    return pts


def eblob(r, deg, a, b, seed=0, n=10):
    """A rough oval hill at pol(r, deg): a px along the tangent, b px along the radius."""
    cx, cy = pol(r, deg)
    ang = math.radians(deg)
    tx, ty = math.cos(ang), math.sin(ang)
    rx, ry = math.sin(ang), -math.cos(ang)
    pts = []
    for i in range(n):
        t = 2 * math.pi * i / n
        w = 1 + 0.12 * math.sin(3 * t + seed) + 0.07 * math.cos(5 * t + 2 * seed)
        u, v = a * math.cos(t) * w, b * math.sin(t) * w
        pts.append((cx + u * tx + v * rx, cy + u * ty + v * ry))
    return pts


def island(c, r, seed=0, n=16, wob=0.06):
    return [(c[0] + r * (1 + wob * math.sin(3 * t + seed) + 0.6 * wob * math.cos(5 * t + 2 * seed)) * math.cos(t),
             c[1] + r * (1 + wob * math.sin(3 * t + seed) + 0.6 * wob * math.cos(5 * t + 2 * seed)) * math.sin(t))
            for t in (2 * math.pi * i / n for i in range(n))]


def arc(r, a0, a1, step=3):
    n = max(2, int(abs(a1 - a0) / step) + 1)
    return [pol(r, a0 + (a1 - a0) * i / (n - 1)) for i in range(n)]


def arc_band(r_in, r_out, a0, a1, wobble=0.0):
    """A ring segment from a0 to a1 degrees; wobble roughens its edges (the same in every third)."""
    n = max(3, int(abs(a1 - a0) / 2) + 1)
    angs = [a0 + (a1 - a0) * i / (n - 1) for i in range(n)]
    outer = [pol(r_out + wobble * math.sin(math.radians(a) * 9), a) for a in angs]
    inner = [pol(r_in + wobble * math.sin(math.radians(a) * 7 + 1), a) for a in reversed(angs)]
    return outer + inner


def polar(fn, step=3):
    """A closed outline r = fn(deg); C3-symmetric when fn repeats every 120 degrees."""
    return [pol(fn(a), a) for a in range(0, 360, step)]


def zigzag(a, b, n, amp):
    (ux, uy), L = unit(a, b)
    nx, ny = -uy, ux
    pts = [a]
    for i in range(1, n):
        t = i / n
        s = amp if i % 2 else -amp
        pts.append((a[0] + (b[0] - a[0]) * t + nx * s, a[1] + (b[1] - a[1]) * t + ny * s))
    pts.append(b)
    return pts


def strip(center, hw):
    """A band hw either side of a polyline (a river)."""
    left, right = [], []
    for i, p in enumerate(center):
        q0 = center[max(0, i - 1)]
        q1 = center[min(len(center) - 1, i + 1)]
        (ux, uy), _ = unit(q0, q1)
        left.append((p[0] - uy * hw, p[1] + ux * hw))
        right.append((p[0] + uy * hw, p[1] - ux * hw))
    return left + right[::-1]


# ---------------------------------------------------------------- colours

def hex_rgb(h):
    return tuple(int(h[i:i + 2], 16) for i in (1, 3, 5))


def rgb_hex(c):
    """A colour as #rrggbb: from 0-255 values, or 0-1 values as mapinfo.lua writes them."""
    if all(isinstance(v, float) and v <= 1 for v in c):
        c = tuple(v * 255 for v in c)
    return "#" + "".join(f"{max(0, min(255, round(v))):02x}" for v in c)


def gradient(stops, t):
    t = max(0.0, min(1.0, t)) * (len(stops) - 1)
    i = min(int(t), len(stops) - 2)
    f = t - i
    return tuple(a + (b - a) * f for a, b in zip(stops[i], stops[i + 1]))


def wrap(text, width):
    return textwrap.wrap(text, width)


# The drawing's ground colours, low to high, and its ash; a planet lays its own ramp over them by lightness, so
# lighter ground still stands higher.
GROUND_COLOURS = ["#9DBD70", "#A9C67E", "#B6CF8E", "#C4D89F", "#CFE0B2", "#D9E6C0", "#DDE8C8", "#E2EBCF", "#E4ECD2",
                  "#E6EDD8", "#E8EEDA", "#EEF2E3", "#D6CBB0", "#E3D9C2", "#EEE6D2"]


def recolour(text, theme):
    """The drawing in the planet's colours: ground by height, cliffs, water, sand and fords (legend swatches too)."""
    dr = theme["drawing"]
    stops = [hex_rgb(c) for c in dr["ground"]]

    def lum(c):
        return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2]

    lo, hi = lum(hex_rgb("#9DBD70")), lum(hex_rgb("#EEF2E3"))
    m = {h: rgb_hex(gradient(stops, (lum(hex_rgb(h)) - lo) / (hi - lo))) for h in GROUND_COLOURS}
    m.update({"#B4B2A9": dr["rock"].lower(), "#85B7EB": dr["liquid"].lower(), "#E6D6AA": dr["sand"].lower(),
              "#D3B27A": dr["ford"].lower(), "#C9B48A": dr["ford"].lower()})
    return re.sub("#[0-9A-F]{6}", lambda mo: m.get(mo.group(0), mo.group(0)), text)


# ---------------------------------------------------------------- the design

class Design:
    def __init__(self, num, key, name, size, starts, ground, ground_h, hill_r, notes, hill_label="THE HILL"):
        self.num, self.key, self.name, self.size, self.starts = num, key, name, size, starts
        self.ground, self.ground_h, self.hill_r, self.notes, self.hill_label = ground, ground_h, hill_r, notes, hill_label
        self.L = {k: [] for k in LAYERS}
        self.labels = []
        self.used = []
        self.legend_text = {}
        self.cliff_polys, self.water_polys, self.wall_polys, self.ford_polys, self.land_polys = [], [], [], [], []
        self.spots = []  # metal: (A-frame point, value, kind, wet)
        self.geos = []
        self.routes = []  # A-frame point lists
        self.ramps = []  # (length in px, rise in game units)
        self.water_map = False
        self.heights = ""
        self.fold = 3  # the map is drawn for Team A and turned this many times round
        self.axis = 0.0  # Team A's mirror line, degrees from up (both=True mirrors across it)
        self.play_disc = True  # False: the whole square is played (a quarter turn maps it onto itself)
        self.tag = f"{num:02d}"
        self.filename = f"{num:02d}_{key}.svg"
        self.subtitle = (f"Three-way king of the hill · {size} × {size} map ({size * 512} game units square) · "
                         f"1v1v1 to {starts}v{starts}v{starts}")
        self.footer = "Each third is the same, turned 120° about the hill. Heights in game units; lighter ground stands higher."
        self.card = f"{size}×{size}, up to {starts}v{starts}v{starts}"
        self.title = f"{name}: a three-way king of the hill map"
        self.theme = None  # a planet from planets.py: the drawing's colours and the world's textures, plants and features
        self.parts = []  # (kind, whole, fields) in Team A's part, for export_geometry.py (building a map from the drawing)

    def _rec(self, kind, whole=False, **fields):
        self.parts.append((kind, whole, fields))

    def use(self, *kinds):
        for k in kinds:
            if k not in self.used:
                self.used.append(k)

    def turns(self, whole):
        return [0] if whole else [360 * k / self.fold for k in range(self.fold)]

    def _mirror(self, pts):
        if not self.axis:
            return mirror(pts)
        return [rot(q, self.axis) for q in mirror([rot(p, -self.axis) for p in pts])]

    def _shape(self, layer, pts, attrs, smooth, whole, record=None, closed=True):
        for t in self.turns(whole):
            q = [ab(rot(p, t)) for p in pts]
            d = smooth_d(q, closed) if smooth else poly_d(q, closed)
            self.L[layer].append(f'<path d="{d}" {attrs}/>')
            if record is not None:
                record.append(smooth_samples(q, closed) if smooth else q)

    # terrain -----------------------------------------------------------

    def band(self, shape, color, h, smooth=False, contour=True, layer="bands"):
        """A height band: a circle radius (about the hill) or a whole C3-symmetric outline."""
        if isinstance(shape, (int, float)):
            self._rec("band", True, pts=circle_pts((0, 0), shape, 120), h=h, layer=layer)
        else:
            self._rec("band", True, pts=smooth_samples(shape, True) if smooth else shape, h=h, layer=layer)
        if isinstance(shape, (int, float)):
            self.L[layer].append(f'<circle cx="{fmt(CX)}" cy="{fmt(CY)}" r="{fmt(shape)}" fill="{color}" data-height="{h}"/>')
            if contour:
                self.L["contours" if layer == "bands" else "islandcontours"].append(
                    f'<circle cx="{fmt(CX)}" cy="{fmt(CY)}" r="{fmt(shape)}" fill="none" stroke="#5F5E5A" stroke-width="0.5" stroke-dasharray="3 4"/>')
        else:
            self._shape(layer, shape, f'fill="{color}" data-height="{h}"', smooth, True)
            if contour:
                self._shape("contours" if layer == "bands" else "islandcontours", shape,
                            'fill="none" stroke="#5F5E5A" stroke-width="0.5" stroke-dasharray="3 4"', smooth, True)
        if contour:
            self.use("contour")

    def cliff(self, pts, h, smooth=True, whole=False):
        """A hill with cliff edges (impassable), as in the ravine drawing."""
        self._rec("cliff", whole, pts=smooth_samples(pts, True) if smooth else pts, h=h)
        self._shape("cliffs", pts, f'fill="#B4B2A9" stroke="#444441" stroke-width="1" data-height="{h}"', smooth, whole,
                    self.cliff_polys)
        self._shape("cliffticks", pts, 'fill="none" stroke="#444441" stroke-width="5" stroke-dasharray="1 3"', smooth, whole)
        self.use("cliff")

    def clifftop(self, shape, color, h, smooth=True, whole=False):
        """High ground you can stand on, with cliff edges, reached by ramps (drawn over the edge)."""
        if isinstance(shape, (int, float)):
            shape, smooth, whole = circle_pts((0, 0), shape, 90), False, True
        self._rec("clifftop", whole, pts=smooth_samples(shape, True) if smooth else shape, h=h)
        self._shape("bands", shape, f'fill="{color}" stroke="#444441" stroke-width="0.75" data-height="{h}"', smooth, whole)
        self._shape("cliffticks", shape, 'fill="none" stroke="#444441" stroke-width="5" stroke-dasharray="1 3"', smooth, whole)
        self.use("clifftop")

    def water(self, shape, smooth=False, whole=False):
        if isinstance(shape, (int, float)):
            shape, smooth, whole = circle_pts((0, 0), shape, 90), False, True
        self._rec("water", whole, pts=smooth_samples(shape, True) if smooth else shape)
        self._shape("water", shape, 'fill="#85B7EB" data-height="-60"', smooth, whole, self.water_polys)
        self.use("water")

    def land(self, pts, color, h, whole=False, smooth=True, contour=False):
        """Ground standing out of water (islands), drawn above the water."""
        self._rec("land", whole, pts=smooth_samples(pts, True) if smooth else pts, h=h)
        self._shape("islands", pts, f'fill="{color}" data-height="{h}"', smooth, whole, self.land_polys)
        if contour:
            self._shape("islandcontours", pts, 'fill="none" stroke="#5F5E5A" stroke-width="0.5" stroke-dasharray="3 4"', smooth, whole)
            self.use("contour")

    def ford(self, a, b, hw, pebbles=5, whole=False):
        self._rec("ford", whole, a=a, b=b, hw=hw)
        pts = stadium_pts(a, b, hw)
        self._shape("fords", pts, 'fill="#D3B27A" stroke="#854F0B" stroke-width="0.5" stroke-dasharray="2 3"', False,
                    whole, self.ford_polys)
        (ux, uy), L = unit(a, b)
        for t in self.turns(whole):
            for i in range(pebbles):
                f = (i + 0.5) / pebbles
                s = (0.45 if i % 2 else -0.45) * hw
                p = (a[0] + (b[0] - a[0]) * f - uy * s, a[1] + (b[1] - a[1]) * f + ux * s)
                q = ab(rot(p, t))
                self.L["fords"].append(f'<circle cx="{fmt(q[0])}" cy="{fmt(q[1])}" r="1.5" fill="#854F0B"/>')
        self.use("ford")

    def bridge(self, a, b, hw, whole=False):
        self._rec("bridge", whole, a=a, b=b, hw=hw)
        self._shape("bridges", rect_pts(a, b, hw), 'fill="#888780" stroke="#444441" stroke-width="1"', False, whole)
        (ux, uy), L = unit(a, b)
        n = max(1, int(L / 14))
        for t in self.turns(whole):
            d = ""
            for i in range(1, n + 1):
                f = i / (n + 1)
                m = (a[0] + (b[0] - a[0]) * f, a[1] + (b[1] - a[1]) * f)
                p, q = ab(rot((m[0] - uy * hw, m[1] + ux * hw), t)), ab(rot((m[0] + uy * hw, m[1] - ux * hw), t))
                d += f"M{pt(p)}L{pt(q)}"
            self.L["bridges"].append(f'<path d="{d}" stroke="#5F5E5A" stroke-width="0.5" fill="none"/>')
        self.use("bridge")

    def ramp(self, lo, hi, hw, rise, whole=False):
        """A graded ramp from low ground to high ground, rise game units up; the chevrons point uphill."""
        self.ramps.append((dist(lo, hi), rise))
        self._rec("ramp", whole, lo=lo, hi=hi, hw=hw, rise=rise)
        self._shape("ramps", rect_pts(lo, hi, hw), 'fill="#ECE3C8" stroke="#854F0B" stroke-width="0.75"', False, whole)
        (ux, uy), L = unit(lo, hi)
        nx, ny = -uy, ux
        for t in self.turns(whole):
            d = ""
            for f in (0.3, 0.55, 0.8):
                m = (lo[0] + (hi[0] - lo[0]) * f, lo[1] + (hi[1] - lo[1]) * f)
                w = hw * 0.6
                p1 = (m[0] - ux * 4 + nx * w, m[1] - uy * 4 + ny * w)
                p2 = (m[0] + ux * 2, m[1] + uy * 2)
                p3 = (m[0] - ux * 4 - nx * w, m[1] - uy * 4 - ny * w)
                d += "M" + "L".join(pt(ab(rot(p, t))) for p in (p1, p2, p3))
            self.L["ramps"].append(f'<path d="{d}" stroke="#854F0B" stroke-width="1" fill="none" stroke-linejoin="round"/>')
        self.use("ramp")

    def path(self, pts, route=False, whole=False, both=False):
        """A sloping path or road (smooth through the points). both: Team A's mirror image too."""
        for ps in ([pts, self._mirror(pts)] if both else [pts]):
            self._rec("path", whole, pts=smooth_samples(ps, False))
            self._shape("paths", ps, 'fill="none" stroke="#854F0B" stroke-width="2" stroke-dasharray="6 4" stroke-linecap="round"',
                        True, whole, closed=False)
            if route:
                self.routes.append(smooth_samples(ps, False))
        self.use("path")

    def route(self, pts):
        """A way to the hill that isn't drawn as a path (over a shoal), for the checks."""
        self.routes.append(pts)

    def track(self, a, b, n=5, amp=7, both=False, whole=False):
        for (p, q) in ([(a, b), (self._mirror([a])[0], self._mirror([b])[0])] if both else [(a, b)]):
            self._rec("track", whole, a=p, b=q)
            self._shape("tracks", zigzag(p, q, n, amp),
                        'fill="none" stroke="#633806" stroke-width="1.5" stroke-dasharray="2 2" stroke-linecap="round" stroke-linejoin="round"',
                        False, whole, closed=False)
        self.use("track")

    def wall(self, a, b, hw=4.5, whole=False):
        self._rec("wall", whole, a=a, b=b, hw=hw)
        pts = rect_pts(a, b, hw)
        self._shape("walls", pts, 'fill="#888780" stroke="#2C2C2A" stroke-width="1"', False, whole, self.wall_polys)
        (ux, uy), L = unit(a, b)
        n = int(L / 8)
        for t in self.turns(whole):
            d = ""
            for i in range(1, n):
                m = (a[0] + ux * 8 * i, a[1] + uy * 8 * i)
                d += f"M{pt(ab(rot((m[0] - uy * hw, m[1] + ux * hw), t)))}L{pt(ab(rot((m[0] + uy * hw, m[1] - ux * hw), t)))}"
            self.L["walls"].append(f'<path d="{d}" stroke="#2C2C2A" stroke-width="0.5" fill="none"/>')
        self.use("wall")

    def tower(self, p, r=7, whole=False):
        self._rec("tower", whole, c=p, r=r)
        for t in self.turns(whole):
            q = ab(rot(p, t))
            self.L["walls"].append(f'<circle cx="{fmt(q[0])}" cy="{fmt(q[1])}" r="{r}" fill="#888780" stroke="#2C2C2A" stroke-width="1"/>')
            self.wall_polys.append(circle_pts(q, r, 24))

    def door(self, a, b, hw=3.5, whole=False):
        self._shape("doors", rect_pts(a, b, hw), 'fill="#BA7517" stroke="#633806" stroke-width="1"', False, whole)
        self.use("door")

    def wall_with_door(self, a, b, door_len):
        (ux, uy), L = unit(a, b)
        m = ((a[0] + b[0]) / 2, (a[1] + b[1]) / 2)
        d0 = (m[0] - ux * door_len / 2, m[1] - uy * door_len / 2)
        d1 = (m[0] + ux * door_len / 2, m[1] + uy * door_len / 2)
        self.wall(a, d0)
        self.wall(d1, b)
        self.door(d0, d1)

    # things ------------------------------------------------------------

    def metal(self, p, v, kind="own", wet=False, both=False):
        """kind: own (each team's), shared (on a gap, between two teams), hill. both: Team A's mirror image too."""
        for q in ([p, self._mirror([p])[0]] if both else [p]):
            self.spots.append((q, v, kind, wet))
        self.use("metal")

    def geo(self, p, whole=False):
        """A geothermal vent; whole: one only, at the centre."""
        self.geos.append((p, whole))
        self.use("geo")

    def wreck(self, p, seed=0):
        for t in self.turns(False):
            d = ""
            for i in range(5):
                a = seed * 1.7 + i * 2.39996
                rr = 4 + 5 * ((i * 37 + seed * 11) % 7) / 7
                c = ab(rot((p[0] + rr * math.cos(a), p[1] + rr * math.sin(a)), t))
                s = 2.2
                d += f"M{pt((c[0] - s, c[1] - s))}L{pt((c[0] + s, c[1] + s))}M{pt((c[0] - s, c[1] + s))}L{pt((c[0] + s, c[1] - s))}"
            self.L["wrecks"].append(f'<path d="{d}" stroke="#5F5E5A" stroke-width="1.1" fill="none" stroke-linecap="round"/>')
        self.use("wreck")

    def label(self, p, text, anchor="middle", size=10.5, italic=True, color="#5F5E5A"):
        q = ab(p)
        style = ' font-style="italic"' if italic else ""
        self.labels.append(f'<text x="{fmt(q[0])}" y="{fmt(q[1])}" text-anchor="{anchor}" font-size="{size}" fill="{color}"{style}>{escape(text)}</text>')

    # drawing -----------------------------------------------------------

    def _draw_things(self):
        # The hill's capture zone, with its crown and name in the middle.
        r = self.hill_r
        self.L["zone"].append(f'<circle cx="{fmt(CX)}" cy="{fmt(CY)}" r="{fmt(r)}" fill="#F6E3A0" fill-opacity="0.55" stroke="#A6790C" stroke-width="2" stroke-dasharray="7 4"/>')
        crown = [(-9, -4), (-9, -14), (-4.5, -9), (0, -16), (4.5, -9), (9, -14), (9, -4)]
        self.L["zone"].append(f'<path d="{poly_d([ab(p) for p in crown])}" fill="#E0B532" stroke="#7A5A00" stroke-width="0.8" stroke-linejoin="round"/>')
        self.L["zone"].append(f'<text x="{fmt(CX)}" y="{fmt(CY + 13)}" text-anchor="middle" font-size="10" font-weight="700" fill="#6B4E06" letter-spacing="0.6">{escape(self.hill_label)}</text>')
        self.use("zone")
        self._draw_resources()
        for i, (letter, fill, stroke) in enumerate(TEAMS):
            t = 120 * i
            c = ab(rot((0, -BASE_R), t))
            self.L["bases"].append(f'<circle cx="{fmt(c[0])}" cy="{fmt(c[1])}" r="{fmt(BASE_SIZE)}" fill="{fill}" stroke="{stroke}" stroke-width="1.2" data-team="{letter}"/>')
            self.L["bases"].append(f'<text x="{fmt(c[0])}" y="{fmt(c[1])}" text-anchor="middle" dominant-baseline="central" font-size="19" font-weight="700" fill="{stroke}">{letter}</text>')
            for j, s in enumerate(self.start_offsets()):
                q = ab(rot((s[0], -BASE_R + s[1]), t))
                self.L["bases"].append(f'<circle cx="{fmt(q[0])}" cy="{fmt(q[1])}" r="7" fill="#FFFFFF" stroke="{stroke}" stroke-width="1.5" data-start="{letter}{j + 1}"/>')
                self.L["bases"].append(f'<text x="{fmt(q[0])}" y="{fmt(q[1])}" text-anchor="middle" dominant-baseline="central" font-size="9" font-weight="700" fill="{stroke}">{j + 1}</text>')
        self.use("start")

    def _draw_resources(self):
        for p, v, kind, wet in self.spots:
            for t in self.turns(False):
                q = ab(rot(p, t))
                rr = 2.6 + (v - 1.5) * 1.3
                self.L["metal"].append(f'<circle cx="{fmt(q[0])}" cy="{fmt(q[1])}" r="{fmt(rr)}" fill="#2C2C2A" stroke="#F1EFE8" stroke-width="0.8" data-metal="{v}"/>')
        for p, whole in self.geos:
            for t in self.turns(whole):
                q = ab(rot(p, t))
                tri = [(q[0], q[1] - 6), (q[0] + 5.5, q[1] + 4), (q[0] - 5.5, q[1] + 4)]
                self.L["geo"].append(f'<path d="{poly_d(tri)}" fill="#D85A30" stroke="#712B13" stroke-width="0.8" stroke-linejoin="round"/>')

    def start_offsets(self):
        if self.starts == 3:
            return [(0, -27), (-23.4, 13.5), (23.4, 13.5)]
        return [(-27, 0), (27, 0)]

    def svg(self):
        self._draw_things()
        if self.play_disc:
            self.use("edge")
        if self.theme and "water" in self.used:
            self.legend_text["water"] = self.theme["liquid"]
        out = []
        legend, legend_h = self._legend(700)
        H = int(legend_h + 34)
        title = self.title
        desc = " ".join(self.notes)
        out.append(f'<svg xmlns="http://www.w3.org/2000/svg" width="680" height="{H}" viewBox="0 0 680 {H}" role="img">')
        out.append(f"<title>{escape(title)}</title><desc>{escape(desc)}</desc>")
        out.append("<style>text{font-family:'Segoe UI',-apple-system,BlinkMacSystemFont,Helvetica,Arial,sans-serif;fill:#2C2C2A}</style>")
        clip = (f'<circle cx="{fmt(CX)}" cy="{fmt(CY)}" r="{fmt(R_PLAY)}"/>' if self.play_disc
                else '<rect x="20" y="20" width="640" height="640" rx="6"/>')
        out.append(f'<defs><clipPath id="play">{clip}</clipPath></defs>')
        out.append(f'<rect width="680" height="{H}" fill="#FAF9F5"/>')
        out.append(f'<rect x="20" y="20" width="640" height="640" rx="6" fill="{self.ground}" data-height="{self.ground_h}"/>')
        out.append('<g clip-path="url(#play)">')
        for k in LAYERS:
            if k in ("zone", "metal", "geo", "bases"):
                continue
            if self.L[k]:
                out.append(f'<g id="{k}">' + "".join(self.L[k]) + "</g>")
        out.append("</g>")
        if self.play_disc:
            # The corners beyond the play disc: impassable map edge.
            ring = (f"M20 20H660V660H20Z M{fmt(CX - R_PLAY)} {fmt(CY)}A{fmt(R_PLAY)} {fmt(R_PLAY)} 0 1 0 {fmt(CX + R_PLAY)} {fmt(CY)}"
                    f"A{fmt(R_PLAY)} {fmt(R_PLAY)} 0 1 0 {fmt(CX - R_PLAY)} {fmt(CY)}Z")
            out.append(f'<g id="edge"><path d="{ring}" fill="#B4B2A9" fill-rule="evenodd"/>'
                       f'<circle cx="{fmt(CX)}" cy="{fmt(CY)}" r="{fmt(R_PLAY)}" fill="none" stroke="#444441" stroke-width="1"/>'
                       f'<circle cx="{fmt(CX)}" cy="{fmt(CY)}" r="{fmt(R_PLAY + 2.5)}" fill="none" stroke="#444441" stroke-width="5" stroke-dasharray="1 3"/></g>')
        for k in ("zone", "metal", "geo", "bases"):
            if self.L[k]:
                out.append(f'<g id="{k}">' + "".join(self.L[k]) + "</g>")
        out.append('<g id="labels">' + "".join(self.labels) + "</g>")
        out.append('<rect x="20" y="20" width="640" height="640" rx="6" fill="none" stroke="#5F5E5A" stroke-width="0.5"/>')
        out.append(legend)
        out.append("</svg>")
        text = "\n".join(out) + "\n"
        return recolour(text, self.theme) if self.theme else text

    def _legend(self, y):
        parts = [f'<text x="40" y="{y}" font-size="17" font-weight="600">{self.tag} · {escape(self.name)}'
                 + (f' <tspan font-weight="400" fill="#5F5E5A">· planet {escape(self.theme["planet"])}</tspan>' if self.theme else "")
                 + "</text>"]
        y += 20
        parts.append(f'<text x="40" y="{y}" font-size="12" fill="#5F5E5A">{escape(self.subtitle)}</text>')
        y += 8
        for line in self.notes:
            y += 17
            parts.append(f'<text x="40" y="{y}" font-size="12.5">{escape(line)}</text>')
        if self.heights:
            y += 19
            parts.append(f'<text x="40" y="{y}" font-size="12" fill="#5F5E5A">Heights: {escape(self.heights)}</text>')
        y += 16
        parts.append(f'<path d="M40 {y}H640" stroke="#D3D1C7" stroke-width="0.75"/>')
        y += 22
        items = [k for k in LEGEND_ORDER if k in self.used]
        cols = 3
        for i, k in enumerate(items):
            col, row = i % cols, i // cols
            x, yy = 40 + col * 210, y + row * 24
            parts.append(legend_symbol(k, x, yy))
            parts.append(f'<text x="{x + 40}" y="{yy}" dominant-baseline="central" font-size="12">{escape(self.legend_text.get(k, LEGEND_TEXT[k]))}</text>')
        y += ((len(items) + cols - 1) // cols - 1) * 24 + 30
        if self.theme:
            y = self._planet(parts, y - 8)
        parts.append(f'<text x="340" y="{y}" text-anchor="middle" font-size="11" fill="#5F5E5A">{escape(self.footer)}</text>')
        return "\n".join(parts), y

    def _planet(self, parts, y):
        """The planet panel: the world's colours as Beautify and the colour panel take them, then what to place."""
        th = self.theme
        parts.append(f'<path d="M40 {y}H640" stroke="#D3D1C7" stroke-width="0.75"/>')
        y += 22
        parts.append(f'<text x="40" y="{y}" font-size="14" font-weight="600">Planet {escape(th["planet"])}'
                     f'<tspan font-weight="400"> · {escape(th["kind"])}</tspan></text>')
        y += 14
        swatches = [(k, rgb_hex(th["beautify"][k])) for k in ("rock", "dirt", "sand", "mud", "seabed", "ground")]
        swatches += [("sky", rgb_hex(th["sky"])), ("fog", rgb_hex(th["fog"])), ("sun", rgb_hex(th["sun"])),
                     (th["liquid_short"], rgb_hex(th["liquid_colour"]))]
        for i, (name, colour) in enumerate(swatches):
            x = 40 + i * 60
            # Written with a marker so recolour() leaves the world's own colours alone.
            parts.append(f'<rect x="{x}" y="{y}" width="52" height="16" rx="2" fill="{colour.lower()}" stroke="#5F5E5A" stroke-width="0.5"/>'
                         f'<text x="{x + 26}" y="{y + 29}" text-anchor="middle" font-size="10" fill="#5F5E5A">{escape(name)}</text>')
        y += 34
        for head, body in (("Ground", th["ground"]), ("Plants", th["plants"]), ("Features", th["features"]),
                           ("Sky and weather", th["weather"])):
            for j, line in enumerate(wrap(f"{head}: {body}", 100)):
                y += 17
                if j == 0:
                    a, b = line.split(": ", 1)
                    parts.append(f'<text x="40" y="{y}" font-size="12"><tspan font-weight="600">{escape(a)}:</tspan> {escape(b)}</text>')
                else:
                    parts.append(f'<text x="40" y="{y}" font-size="12">{escape(line)}</text>')
        return y + 26

    # checks ------------------------------------------------------------

    def check(self):
        problems = []
        spots = []
        for p, v, kind, wet in self.spots:
            for t in self.turns(False):
                spots.append((ab(rot(p, t)), v, kind, wet))
        starts = []
        for i in range(3):
            for s in self.start_offsets():
                starts.append(ab(rot((s[0], -BASE_R + s[1]), 120 * i)))
        geos = [ab(rot(p, t)) for p, whole in self.geos for t in self.turns(whole)]
        for q, v, kind, wet in spots:
            r = dist(q, (CX, CY))
            if r > R_PLAY - 10:
                problems.append(f"metal at {pt(q)} is {r:.0f} from the hill, beyond the play area")
            if any(inside(q, c) for c in self.cliff_polys):
                problems.append(f"metal at {pt(q)} is on a cliff")
            if any(inside(q, w) for w in self.wall_polys):
                problems.append(f"metal at {pt(q)} is in a wall")
            in_ford = any(inside(q, f) for f in self.ford_polys)
            if self.water_map:
                on_land = any(inside(q, l) for l in self.land_polys) or in_ford
                if wet == on_land:
                    problems.append(f"metal at {pt(q)} is {'on land' if on_land else 'in water'} but marked {'wet' if wet else 'dry'}")
            else:
                in_water = (any(inside(q, w) for w in self.water_polys) and not in_ford
                            and not any(inside(q, l) for l in self.land_polys))
                if wet != in_water:
                    problems.append(f"metal at {pt(q)} is {'in water' if in_water else 'dry'} but marked {'wet' if wet else 'dry'}")
            # Not on the hill's crown or name.
            dx, dy = q[0] - CX, q[1] - CY
            rr = 2.6 + (v - 1.5) * 1.3
            if (-28 - rr < dx < 28 + rr and 2 - rr < dy < 15 + rr) or (-11 - rr < dx < 11 + rr and -17 - rr < dy < -2 + rr):
                problems.append(f"metal at {pt(q)} covers the hill's crown or name")
        for i in range(len(spots)):
            for j in range(i + 1, len(spots)):
                if dist(spots[i][0], spots[j][0]) < 11:
                    problems.append(f"metal at {pt(spots[i][0])} and {pt(spots[j][0])} overlap")
            for s in starts + geos:
                if dist(spots[i][0], s) < 13:
                    problems.append(f"metal at {pt(spots[i][0])} is on a start position or vent")
        for s in starts:
            if any(inside(s, c) for c in self.cliff_polys) or any(inside(s, w) for w in self.water_polys):
                problems.append(f"start at {pt(s)} is on a cliff or in water")
        # 120-degree symmetry of the metal: every spot turned lands on a spot of the same value.
        for q, v, _, _ in spots:
            t = ab(rot((q[0] - CX, q[1] - CY), 120))
            if not any(dist(t, q2) < 0.5 and v2 == v for q2, v2, _, _ in spots):
                problems.append(f"metal at {pt(q)} has no twin a third of a turn round")
        # No team's way to the hill passes another team's base.
        others = [rot((0, -BASE_R), 120), rot((0, -BASE_R), 240)]
        near = min((dist(p, o) for r in self.routes for p in r for o in others), default=999)
        if near < 110:
            problems.append(f"a route to the hill passes {near:.0f} px from another team's base")
        units = self.size * 512 / 640
        # Every ramp must stay well under BAR's 27 degrees for vehicles (the legend says all units use them).
        steepest = 0.0
        for L, rise in self.ramps:
            a = math.degrees(math.atan2(rise, L * units))
            steepest = max(steepest, a)
            if a > 20:
                problems.append(f"a ramp climbs {rise} in {L * units:.0f} units, {a:.0f} degrees: too steep for vehicles")
        own = sum(v for _, v, k, _ in self.spots if k == "own")
        shared = sum(v for _, v, k, _ in self.spots if k == "shared")
        hill = sum(v for _, v, k, _ in self.spots if k == "hill")
        # The way from the base (the shorter if there are two) and the shared stretch on to the hill.
        from_base = [length(r) for r in self.routes if dist(r[0], (0, -BASE_R)) < BASE_SIZE + 30]
        route_len = min(from_base, default=0) + sum(length(r) for r in self.routes if dist(r[0], (0, -BASE_R)) >= BASE_SIZE + 30)
        row = (f"{self.num:02d} {self.name:<18} {self.size}x{self.size}  {self.starts}/team  "
               f"metal: own {own:4.1f} x3, shared {shared:4.1f} x3, hill {hill:4.1f} x3  "
               f"base-hill {BASE_R * units:5.0f}  base-base {BASE_R * math.sqrt(3) * units:5.0f}  "
               f"route {route_len * units:5.0f}  nearest other base {near * units:5.0f}  steepest ramp {steepest:4.1f} deg")
        return row, problems


LEGEND_ORDER = ["king", "peasant", "zone", "start", "metal", "geo", "wreck", "path", "ramp", "track", "bridge", "ford", "water",
                "cliff", "clifftop", "wall", "door", "contour", "edge"]
LEGEND_TEXT = {
    "zone": "The hill (capture zone)", "start": "Base and start spots", "metal": "Metal (bigger = richer)",
    "geo": "Geothermal vent", "wreck": "Wreck field (reclaim)", "path": "Road / sloping path",
    "ramp": "Ramp, uphill (all units)", "track": "Foot track (bots only)", "bridge": "Stone bridge",
    "ford": "Ford (land units cross)", "water": "Deep water", "cliff": "Cliff hill (impassable)",
    "clifftop": "High ground, cliff edges", "wall": "Wall (can be built on)", "door": "Door (destructible)",
    "contour": "Contour (closer = steeper)", "edge": "Map edge (impassable)",
    "king": "King's keep; ring = throne", "peasant": "Peasant base (one player)",
}


def legend_symbol(k, x, y):
    if k == "zone":
        return (f'<circle cx="{x + 14}" cy="{y}" r="8" fill="#F6E3A0" fill-opacity="0.55" stroke="#A6790C" stroke-width="1.5" stroke-dasharray="4 2"/>'
                f'<path d="M{x + 9} {y + 3}V{y - 3}L{x + 11.5} {y}L{x + 14} {y - 4.5}L{x + 16.5} {y}L{x + 19} {y - 3}V{y + 3}Z" fill="#E0B532" stroke="#7A5A00" stroke-width="0.6"/>')
    if k == "start":
        return (f'<circle cx="{x + 8}" cy="{y}" r="8" fill="#F5C4B3" stroke="#993C1D" stroke-width="1"/>'
                f'<circle cx="{x + 25}" cy="{y}" r="6" fill="#FFFFFF" stroke="#993C1D" stroke-width="1.3"/>'
                f'<text x="{x + 25}" y="{y}" text-anchor="middle" dominant-baseline="central" font-size="8" font-weight="700" fill="#993C1D">1</text>')
    if k == "metal":
        return (f'<circle cx="{x + 8}" cy="{y}" r="3" fill="#2C2C2A" stroke="#F1EFE8" stroke-width="0.8"/>'
                f'<circle cx="{x + 22}" cy="{y}" r="5.8" fill="#2C2C2A" stroke="#F1EFE8" stroke-width="0.8"/>')
    if k == "geo":
        return f'<path d="M{x + 14} {y - 6}L{x + 19.5} {y + 4}L{x + 8.5} {y + 4}Z" fill="#D85A30" stroke="#712B13" stroke-width="0.8"/>'
    if k == "wreck":
        d = "".join(f"M{x + cx - 2.2} {y + cy - 2.2}l4.4 4.4M{x + cx - 2.2} {y + cy + 2.2}l4.4 -4.4" for cx, cy in ((7, -3), (15, 2), (23, -2)))
        return f'<path d="{d}" stroke="#5F5E5A" stroke-width="1.1" fill="none" stroke-linecap="round"/>'
    if k == "path":
        return f'<path d="M{x} {y}H{x + 30}" stroke="#854F0B" stroke-width="2" stroke-dasharray="6 4" stroke-linecap="round"/>'
    if k == "ramp":
        return (f'<rect x="{x + 2}" y="{y - 6}" width="26" height="12" fill="#ECE3C8" stroke="#854F0B" stroke-width="0.75"/>'
                f'<path d="M{x + 9} {y - 4}L{x + 14} {y}L{x + 9} {y + 4}M{x + 17} {y - 4}L{x + 22} {y}L{x + 17} {y + 4}" stroke="#854F0B" stroke-width="1" fill="none"/>')
    if k == "track":
        return f'<path d="M{x} {y}L{x + 6} {y - 7}L{x + 12} {y + 7}L{x + 18} {y - 7}L{x + 24} {y + 7}L{x + 30} {y}" stroke="#633806" stroke-width="1.5" stroke-dasharray="2 2" fill="none"/>'
    if k == "bridge":
        return (f'<rect x="{x + 7}" y="{y - 7}" width="16" height="14" rx="2" fill="#888780" stroke="#444441" stroke-width="1"/>'
                f'<path d="M{x + 12} {y - 7}V{y + 7}M{x + 18} {y - 7}V{y + 7}" stroke="#5F5E5A" stroke-width="0.5"/>')
    if k == "ford":
        return f'<rect x="{x + 7}" y="{y - 7}" width="16" height="14" rx="3" fill="#D3B27A" stroke="#854F0B" stroke-width="0.5" stroke-dasharray="2 3"/>'
    if k == "water":
        return f'<rect x="{x + 7}" y="{y - 7}" width="16" height="14" rx="2" fill="#85B7EB"/>'
    if k == "cliff":
        return f'<rect x="{x + 7}" y="{y - 7}" width="16" height="14" rx="2" fill="#B4B2A9" stroke="#444441" stroke-width="1"/>'
    if k == "clifftop":
        return (f'<rect x="{x + 7}" y="{y - 7}" width="16" height="14" rx="2" fill="#EEF2E3" stroke="#444441" stroke-width="0.75"/>'
                f'<path d="M{x + 7} {y - 7}H{x + 23}" stroke="#444441" stroke-width="4" stroke-dasharray="1 2.5"/>')
    if k == "wall":
        return (f'<rect x="{x + 11}" y="{y - 8}" width="8" height="16" fill="#888780" stroke="#2C2C2A" stroke-width="1"/>'
                f'<path d="M{x + 11} {y - 3}H{x + 19}M{x + 11} {y + 3}H{x + 19}" stroke="#2C2C2A" stroke-width="0.5"/>')
    if k == "door":
        return f'<rect x="{x + 12}" y="{y - 8}" width="6" height="16" fill="#BA7517" stroke="#633806" stroke-width="1"/>'
    if k == "contour":
        return f'<path d="M{x} {y}H{x + 30}" stroke="#5F5E5A" stroke-width="0.75" stroke-dasharray="3 4"/>'
    if k == "edge":
        return (f'<rect x="{x + 7}" y="{y - 7}" width="16" height="14" fill="#B4B2A9"/>'
                f'<path d="M{x + 7} {y + 7}H{x + 23}" stroke="#444441" stroke-width="4" stroke-dasharray="1 2.5"/>')
    if k == "king":
        return (f'<circle cx="{x + 14}" cy="{y}" r="10" fill="#F6E3A0" fill-opacity="0.55" stroke="#A6790C" stroke-width="1.3" stroke-dasharray="3 2"/>'
                f'<circle cx="{x + 14}" cy="{y}" r="6.5" fill="#F5C4B3" stroke="#993C1D" stroke-width="1"/>'
                f'<path d="M{x + 10.5} {y + 2}V{y - 2.5}L{x + 12.2} {y - 0.5}L{x + 14} {y - 3.5}L{x + 15.8} {y - 0.5}L{x + 17.5} {y - 2.5}V{y + 2}Z" fill="#E0B532" stroke="#7A5A00" stroke-width="0.5"/>')
    if k == "peasant":
        return (f'<circle cx="{x + 14}" cy="{y}" r="8" fill="#F5C4B3" stroke="#993C1D" stroke-width="1"/>'
                f'<text x="{x + 14}" y="{y}" text-anchor="middle" dominant-baseline="central" font-size="7" font-weight="700" fill="#993C1D">A1</text>')
    raise KeyError(k)


def base_metal(d, v=1.8):
    for p in (bp(-58, -14), bp(58, -14), bp(-46, 40), bp(46, 40)):
        d.metal(p, v, "own")


# ---------------------------------------------------------------- the ten maps

def crown_mesa():
    d = Design(1, "crown_mesa", "Crown Mesa", 16, 2, "#D9E6C0", 260, 58, [
        "The classic. A cliff-edged mesa holds the hill, with one graded ramp facing each base and a bots-only",
        "foot track up each side between the ramps. Edge hills split each flank into an inner and an outer lane,",
        "and two outcrops narrow every ramp's approach.",
    ])
    d.heights = "plateau 260 · lowland 140 · edge hills 300 · the mesa (the hill) 340"
    for r, col, h in [(196, "#CFE0B2", 230), (174, "#C4D89F", 200), (152, "#B6CF8E", 170), (130, "#A9C67E", 140)]:
        d.band(r, col, h)
    d.clifftop(80, "#EEF2E3", 340)
    d.cliff(eblob(234, 60, 40, 22, seed=1), 300)
    d.cliff(eblob(124, 26, 13, 11, seed=2), 240)
    d.cliff(eblob(124, -26, 13, 11, seed=3), 240)
    d.ramp(pol(122, 0), pol(64, 0), 12, 200)
    d.path([pol(192, 0), pol(160, -5), pol(128, 3), pol(100, 0), pol(66, 0), pol(30, 0)], route=True)
    d.path([pol(206, 13), pol(196, 36), pol(184, 60)], both=True)
    d.path([pol(262, 11), pol(276, 34), pol(280, 60)], both=True)
    d.track(pol(126, 60), pol(76, 60), 6, 7)
    base_metal(d)
    d.metal(pol(158, 33), 2.4, both=True)
    d.metal(pol(285, 47), 2.4, both=True)
    d.metal(pol(172, 60), 3.0, "shared")
    d.metal(pol(36, 60), 4.0, "hill")
    return d


def three_rivers():
    d = Design(2, "three_rivers", "Three Rivers", 18, 3, "#D9E6C0", 240, 46, [
        "The hill is an island in a lake, reached by one causeway bridge facing each base. Three rivers run out",
        "from the lake between the teams, so every neighbour is across water: an inner and an outer bridge and a",
        "metal-rich ford in the middle of each river. Hills by the banks make clefts on the way down to them.",
    ])
    d.heights = "plateau 240 · banks 120 · island 260 to 300 · lake and rivers 60 deep, fords 10 deep"
    for r, col, h in [(205, "#CFE0B2", 200), (160, "#C4D89F", 160), (126, "#B6CF8E", 120)]:
        d.band(r, col, h)
    d.water(106)

    def centre(r):
        return pol(r, 60 + 3.5 * math.sin((r - 96) / 28))

    river = [centre(r) for r in range(96, 336, 6)]
    d.water(strip(river, 15))
    d.land(circle_pts((0, 0), 70, 90), "#DDE8C8", 260, whole=True, smooth=False)
    d.band(48, "#E8EEDA", 300, layer="islands")

    def across(r, half):
        c = centre(r)
        ang = math.radians(60 + 3.5 * math.sin((r - 96) / 28))
        t = (math.cos(ang), math.sin(ang))
        return (c[0] - t[0] * half, c[1] - t[1] * half), (c[0] + t[0] * half, c[1] + t[1] * half)

    in0, in1 = across(150, 27)
    out0, out1 = across(264, 27)
    f0, f1 = across(207, 21)
    d.bridge(in0, in1, 10)
    d.bridge(out0, out1, 10)
    d.ford(f0, f1, 13, 5)
    d.bridge(pol(114, 0), pol(62, 0), 10)
    d.cliff(eblob(206, 36, 18, 13, seed=4), 330)
    d.cliff(eblob(206, -36, 18, 13, seed=5), 330)
    d.path([pol(192, 0), pol(160, -5), pol(130, 3), pol(114, 0), pol(62, 0), pol(30, 0)], route=True)
    d.path([pol(204, 13), pol(178, 30), in0], both=True)
    d.path([pol(262, 10), pol(268, 32), out0], both=True)
    d.path([pol(228, 21), f0], both=True)
    base_metal(d)
    d.metal(pol(160, 30), 2.4, both=True)
    d.metal(pol(244, 40), 2.4, both=True)
    d.metal(centre(207), 3.2, "shared")
    d.metal(pol(28, 60), 4.0, "hill")
    d.legend_text["ford"] = "Ford (land units cross)"
    d.label(pol(84, -60), "Lake")
    return d


def caldera():
    d = Design(3, "caldera", "Caldera", 18, 3, "#B6CF8E", 180, 34, [
        "A volcano's rim rings the hill: three breaches face the gaps between the teams, so every way in for",
        "vehicles is shared with a neighbour. Each team has its own bots-only track over the rim, straight down to",
        "a geothermal vent on the caldera floor. The hill is the cone in the middle.",
    ])
    d.heights = "outer flank 180 to 260 · rim 520 · caldera floor 200 · the cone (the hill) 300"
    for r, col, h in [(215, "#C4D89F", 220), (175, "#CFE0B2", 260)]:
        d.band(r, col, h)
    d.band(102, "#D6CBB0", 200)
    d.band(48, "#E3D9C2", 260)
    d.band(32, "#EEE6D2", 300, contour=False)
    d.cliff(arc_band(97, 138, -49, 49, wobble=3), 520, smooth=False)
    d.ramp(pol(98, 60), pol(148, 60), 13, 60)
    d.path([pol(202, 12), pol(184, 34), pol(162, 52), pol(146, 60)], both=True, route=True)
    d.path([pol(146, 60), pol(100, 60), pol(60, 60), pol(24, 60)], route=True)
    d.path([pol(192, 0), pol(152, 0)])
    d.track(pol(152, 0), pol(84, 0), 8, 7)
    d.cliff(eblob(200, 32, 16, 12, seed=6), 300)
    d.cliff(eblob(200, -32, 16, 12, seed=7), 300)
    d.geo(pol(70, 0))
    d.label(pol(118, -24), "Rim", color="#2C2C2A")
    base_metal(d)
    d.metal(pol(158, 22), 2.4, both=True)
    d.metal(pol(188, 60), 3.0, "shared")
    d.metal(pol(70, 32), 2.8, both=True)
    d.metal(pol(21, 60), 4.0, "hill")
    return d


def spiral_ascent():
    d = Design(4, "spiral_ascent", "Spiral Ascent", 16, 2, "#C4D89F", 200, 44, [
        "Three terraces, each walled by a cliff with one ramp per team, every ramp a third of the way further",
        "round than the one below. Your road to the hill spirals clockwise and climbs its last ramp in front of",
        "your neighbour. Bots can cut across on foot tracks; vehicles have to go the long way.",
    ])
    d.heights = "outer ground 200 · first terrace 290 · second terrace 380 · the summit (the hill) 460"
    for r, col, h in [(181, "#CFE0B2", 290), (120, "#D9E6C0", 380), (62, "#E8EEDA", 460)]:
        d.band(r, col, h)
    d.cliff(arc_band(174, 188, 9, 111, wobble=1.5), 330, smooth=False)
    d.cliff(arc_band(114, 126, 49, 151, wobble=1.5), 420, smooth=False)
    d.cliff(arc_band(56, 68, 89, 191, wobble=1), 500, smooth=False)
    d.ramp(pol(200, 0), pol(162, 0), 12, 90)
    d.ramp(pol(136, 40), pol(104, 40), 11, 90)
    d.ramp(pol(78, 80), pol(46, 80), 10, 80)
    d.path([pol(194, 0), pol(168, 0), pol(150, 12), pol(148, 28), pol(122, 40), pol(94, 50), pol(88, 68),
            pol(62, 80), pol(36, 84)], route=True)
    d.track(pol(134, -30), pol(106, -30), 4, 6)
    d.track(pol(76, 12), pol(48, 12), 4, 5)
    d.track(pol(198, 60), pol(164, 60), 4, 6)
    d.cliff(eblob(258, 60, 34, 20, seed=8), 300)
    base_metal(d)
    d.metal(pol(206, 40), 2.2, both=True)
    d.metal(pol(150, -48), 2.4)
    d.metal(pol(144, 60), 2.8, "shared")
    d.metal(pol(92, -12), 3.0)
    d.metal(pol(27, 60), 4.0, "hill")
    return d


def windswept_rise():
    def lobes(r0, amp):
        return lambda a: r0 + amp * math.cos(math.radians(3 * (a - 60)))

    d = Design(5, "windswept_rise", "Windswept Rise", 20, 3, "#B6CF8E", 160, 50, [
        "Open country. The hill is a broad, gentle rise with spurs running out between the teams; no cliffs to",
        "hide behind, only scattered rock outcrops and wreck fields to reclaim. A ring road links the bases and",
        "three geothermal vents sit on the spurs. Fast, wide and vehicle-friendly.",
    ])
    d.heights = "plains 160, rising in gentle steps (190, 220, 250) to the hilltop at 280"
    for r0, amp, col, h in [(222, 10, "#C4D89F", 190), (168, 9, "#CFE0B2", 220), (116, 8, "#D9E6C0", 250), (68, 5, "#E4ECD2", 280)]:
        d.band(polar(lobes(r0, amp)), col, h)
    d.cliff(eblob(140, 22, 12, 9, seed=9), 260)
    d.cliff(eblob(186, -40, 15, 10, seed=10), 230)
    d.cliff(eblob(104, -28, 10, 8, seed=11), 290)
    d.cliff(eblob(250, 60, 18, 12, seed=12), 230)
    d.wreck(pol(206, 26), 1)
    d.wreck(pol(134, 60), 2)
    d.wreck(pol(158, -14), 3)
    d.path([pol(192, 0), pol(160, 6), pol(122, -4), pol(88, 2), pol(34, 0)], route=True)
    d.path(arc(222, 12, 108, 8))
    d.geo(pol(110, 60))
    base_metal(d)
    d.metal(pol(172, -28), 2.2)
    d.metal(pol(170, 32), 2.2)
    d.metal(pol(262, 38), 2.2, both=True)
    d.metal(pol(178, 60), 2.8, "shared")
    d.metal(pol(90, 30), 3.0)
    d.metal(pol(31, 60), 3.6, "hill")
    return d


def hex_bastion():
    d = Design(6, "hex_bastion", "Hex Bastion", 18, 3, "#C4D89F", 200, 48, [
        "A hexagonal fortress holds the hill: ramparts you can build turrets on, towers at the corners, and one",
        "destructible door facing each base. The walls facing the gaps are solid, so the fight at the door is",
        "your own, but a neighbour who breaks theirs is in the same courtyard.",
    ])
    d.heights = "plain 200 to 230 · courtyard 260 · the keep (the hill) 300 · ramparts 120 high"
    d.band(196, "#CFE0B2", 230)
    d.band([pol(100, 30 + 60 * k) for k in range(6)], "#D9E6C0", 260)
    d.band(54, "#E6EDD8", 300)
    d.wall_with_door(pol(100, -30), pol(100, 30), 26)
    d.ramp(pol(108, 0), pol(68, 0), 12, 30)
    d.wall(pol(100, 30), pol(100, 90))
    d.tower(pol(100, 30))
    d.tower(pol(100, 90))
    d.cliff(eblob(184, 60, 30, 18, seed=13), 320)
    d.cliff(eblob(150, 30, 12, 10, seed=14), 300)
    d.cliff(eblob(150, -30, 12, 10, seed=15), 300)
    d.path([pol(192, 0), pol(150, -3), pol(112, 0), pol(86, 0), pol(34, 0)], route=True)
    d.path(arc(128, -60, 60, 6))
    base_metal(d)
    d.metal(pol(196, 32), 2.4, both=True)
    d.metal(pol(264, 60), 3.0, "shared")
    d.metal(pol(64, 60), 3.0, "shared")
    d.metal(pol(30, 60), 4.0, "hill")
    return d


def gorge_run():
    d = Design(7, "gorge_run", "Gorge Run", 18, 3, "#C4D89F", 200, 50, [
        "Each base looks down its own gorge to the hill. Between the gorges stand highlands shared by two",
        "neighbours, each reached by a ramp from either base; from there, artillery covers both gorges. The",
        "hill is a cliff-edged butte with one ramp per gorge, and bots can climb to it from the highland tips.",
    ])
    d.heights = "bases and gorges 200 · highlands 340 · the butte (the hill) 380"
    prof = [(104, 20), (122, 38), (160, 44), (205, 38), (240, 32), (330, 27)]
    hl = [pol(98, 60)] + [pol(r, 60 - hw) for r, hw in prof] + [pol(r, 60 + hw) for r, hw in reversed(prof)]
    d.clifftop(hl, "#E2EBCF", 340)
    d.clifftop(64, "#EEF2E3", 380)
    d.ramp(pol(208, 12), pol(208, 30), 9, 140)
    d.ramp(pol(208, -12), pol(208, -30), 9, 140)
    d.ramp(pol(98, 0), pol(56, 0), 12, 180)
    d.track(pol(100, 60), pol(62, 60), 4, 6)
    d.path([pol(192, 0), pol(160, -4), pol(124, 4), pol(98, 0), pol(34, 0)], route=True)
    d.path([pol(222, 7), pol(208, 14)], both=True)
    d.path([pol(208, 28), pol(214, 45), pol(222, 60)], both=True)
    base_metal(d)
    d.metal(pol(142, 14), 2.4, both=True)
    d.metal(pol(252, 60), 3.0, "shared")
    d.metal(pol(160, 60), 3.2, "shared")
    d.metal(pol(31, 60), 4.0, "hill")
    d.label(pol(150, -40), "Highland")
    d.label(pol(166, 11), "Gorge")
    return d


def king_of_the_pit():
    d = Design(8, "king_of_the_pit", "King of the Pit", 16, 2, "#D9E6C0", 260, 56, [
        "Turned inside out: the hill is a pit. A broad raised rim rings it, crowned with standing stones, and",
        "three graded roads go down from the rim at the gaps between the teams. Elsewhere the pit wall is too",
        "steep for vehicles (about 36°) but not for bots. Whoever holds the pit is looked down on from every side.",
    ], hill_label="THE PIT")
    d.heights = "plateau 260 · rim 300 · the pit wall drops 260 in about 360 · the pit floor (the capture zone) 40"
    for r, col, h in [(208, "#E4ECD2", 300), (96, "#CFE0B2", 240), (90, "#C4D89F", 190), (85, "#B6CF8E", 140),
                      (80, "#A9C67E", 100), (74, "#9DBD70", 70), (68, "#C9B48A", 40)]:
        d.band(r, col, h)
    d.cliff(eblob(180, 30, 13, 11, seed=16), 360)
    d.cliff(eblob(180, -30, 13, 11, seed=17), 360)
    d.ramp(pol(60, 60), pol(124, 60), 13, 260)
    d.path([pol(192, 6), pol(165, 18), pol(148, 42), pol(130, 60)], both=True, route=True)
    d.path([pol(130, 60), pol(96, 60), pol(66, 60), pol(34, 60)], route=True)
    base_metal(d)
    d.metal(pol(176, 0), 2.4)
    d.metal(pol(196, 60), 3.0, "shared")
    d.metal(pol(120, 30), 2.6, both=True)
    d.metal(pol(35, 60), 4.0, "hill")
    d.label(pol(196, -48), "Rim")
    return d


def archipelago_crown():
    d = Design(9, "archipelago_crown", "Archipelago Crown", 20, 3, "#85B7EB", -60, 44, [
        "A water map. Each team holds an island; the hill crowns the central island, reached by one shoal from",
        "each base. Islets between the teams carry a geothermal vent and rich metal, linked to both neighbours",
        "by shoals. Hovercraft, ships and amphibious units rule; underwater metal rewards a navy.",
    ])
    d.heights = "sea 60 deep, shoals 10 deep · islands 60 · the central island rising to 120"
    d.water_map = True
    d.legend_text["ford"] = "Shoal (land units cross)"
    d.use("water")
    d.legend_text["water"] = "Sea, 60 deep"
    d.label(pol(150, -60), "Sea")
    d.land(island(pol(232, 0), 80, seed=1), "#E6D6AA", 10)
    d.land(island(pol(232, 0), 70, seed=1), "#C4D89F", 60, contour=True)
    d.land(polar(lambda a: 82 + 6 * math.cos(math.radians(3 * a)), 4), "#E6D6AA", 10, whole=True)
    d.land(polar(lambda a: 72 + 6 * math.cos(math.radians(3 * a)), 4), "#CFE0B2", 80, whole=True, contour=True)
    d.band(52, "#D9E6C0", 120, layer="islands")
    d.land(island(pol(232, 60), 28, seed=2, n=12), "#E6D6AA", 10)
    d.land(island(pol(232, 60), 20, seed=2, n=12), "#C4D89F", 40)
    d.ford(pol(158, 0), pol(80, 0), 12, 6)
    d.ford(pol(222, 18), pol(230, 52), 10, 4)
    d.ford(pol(222, -18), pol(230, -52), 10, 4)
    d.route([pol(192, 0), pol(158, 0), pol(80, 0), pol(30, 0)])
    d.geo(pol(244, 60))
    base_metal(d)
    d.metal(pol(222, 60), 3.0, "shared")
    d.metal(pol(146, 32), 2.4, wet=True, both=True)
    d.metal(pol(296, 44), 2.0, wet=True, both=True)
    d.metal(pol(27, 60), 4.0, "hill")
    return d


def twin_rings():
    d = Design(10, "twin_rings", "Twin Rings", 18, 3, "#C4D89F", 220, 55, [
        "Two cliff rings with staggered gates. The outer gate faces your base, the inner gates face the gaps,",
        "so every road to the hill turns along the ring corridor and meets a neighbour on the way. Bots can",
        "climb the inner ring straight from their gate; vehicles can't.",
    ])
    d.heights = "outer ground 220 · corridor 280 · inside the inner ring 340 · the hill 380"
    for r, col, h in [(168, "#CFE0B2", 280), (106, "#D9E6C0", 340), (60, "#E4ECD2", 380)]:
        d.band(r, col, h)
    d.cliff(arc_band(150, 168, 10, 110, wobble=1.5), 380, smooth=False)
    d.cliff(arc_band(92, 106, 71, 169, wobble=1.5), 440, smooth=False)
    d.ramp(pol(186, 0), pol(134, 0), 12, 60)
    d.ramp(pol(122, 60), pol(80, 60), 11, 60)
    d.path([pol(192, 0), pol(160, 0), pol(138, 8), pol(134, 30), pol(128, 50), pol(114, 60)], both=True, route=True)
    d.path([pol(114, 60), pol(78, 60), pol(34, 60)], route=True)
    d.track(pol(124, 0), pol(78, 0), 5, 6)
    d.track(pol(188, 60), pol(134, 60), 5, 6)
    base_metal(d)
    d.metal(pol(204, 40), 2.4, both=True)
    d.metal(pol(238, 60), 3.0, "shared")
    d.metal(pol(116, 30), 2.6, both=True)
    d.metal(pol(72, 24), 3.0, both=True)
    d.metal(pol(34, 60), 4.0, "hill")
    return d


MAPS = [crown_mesa, three_rivers, caldera, spiral_ascent, windswept_rise, hex_bastion, gorge_run, king_of_the_pit,
        archipelago_crown, twin_rings]


def index_html(sections):
    """The gallery: each section a heading, a line about it, and its maps with their planets."""
    blocks = []
    for heading, blurb, designs in sections:
        cards = []
        for d in designs:
            th = d.theme
            planet = ""
            if th:
                sw = "".join(f'<i style="background:{rgb_hex(th["beautify"][k])}" title="{k}"></i>'
                             for k in ("rock", "dirt", "sand", "mud", "seabed", "ground"))
                sw += f'<i style="background:{rgb_hex(th["sky"])}" title="sky"></i><i style="background:{rgb_hex(th["liquid_colour"])}" title="{th["liquid_short"]}"></i>'
                ops = escape(" ".join(planet_ops(th)))
                planet = (f'<div class="planet"><b>Planet {escape(th["planet"])}</b> · {escape(th["kind"])} <span class="sw">{sw}</span></div>'
                          f'<details><summary>Textures, plants and features</summary>'
                          f'<p><b>Ground:</b> {escape(th["ground"])}</p><p><b>Plants:</b> {escape(th["plants"])}</p>'
                          f'<p><b>Features:</b> {escape(th["features"])}</p><p><b>Sky and weather:</b> {escape(th["weather"])}</p>'
                          f'<p><b>MapTool:</b></p><code>{ops}</code></details>')
            cards.append(f'<figure><a href="{d.filename}"><img src="{d.filename}" alt="{escape(d.name)}"></a>'
                         f'<figcaption><b>{d.tag} · {escape(d.name)}</b> {escape(d.card)}<br>{escape(" ".join(d.notes))}{planet}</figcaption></figure>')
        blocks.append(f"<h2>{escape(heading)}</h2>\n<p>{escape(blurb)}</p>\n<main>\n" + "\n".join(cards) + "\n</main>")
    return f"""<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>King of the Hill Maps</title>
<style>
body{{margin:0;padding:24px 16px;background:#F1EFE8;color:#2C2C2A;font-family:'Segoe UI',-apple-system,Helvetica,Arial,sans-serif}}
h1{{font-size:22px;margin:0 0 4px}} h2{{font-size:18px;margin:28px 0 4px}} p{{margin:0 0 14px;color:#5F5E5A}}
main{{display:grid;grid-template-columns:repeat(auto-fill,minmax(340px,1fr));gap:20px}}
figure{{margin:0;background:#fff;border:1px solid #D3D1C7;border-radius:8px;padding:10px;min-width:0}}
img{{width:100%;height:auto;display:block}} figcaption{{font-size:13px;line-height:1.45;margin-top:8px;color:#444441}}
.planet{{margin-top:8px}} .sw{{display:inline-flex;gap:2px;vertical-align:middle;margin-left:4px}}
.sw i{{display:inline-block;width:14px;height:14px;border-radius:2px;border:1px solid #0002}}
details{{margin-top:6px}} details p{{margin:4px 0;color:#444441}} summary{{cursor:pointer;color:#185FA5}}
code{{display:block;font-size:11px;word-break:break-all;background:#F1EFE8;padding:6px;border-radius:4px}}
</style></head><body>
<h1>King of the hill maps, each on its own planet</h1>
<p>Click a map for the full drawing. Every map is drawn for Team A and turned about the centre, so every team's land is the same.</p>
{chr(10).join(blocks)}
</body></html>
"""


def planet_ops(th):
    """MapTool operations that give a map this planet's look: Beautify's colours, then the sky, water and weather."""
    b = th["beautify"]
    ops = ["beautify-texture:" + ";".join(f"{k}={','.join(str(v) for v in b[k])}" for k in ("rock", "dirt", "sand", "mud", "seabed", "ground"))]
    for path, value in th["mapinfo"].items():
        v = ",".join(f"{x:g}" for x in value) if isinstance(value, tuple) else f"{value:g}"
        ops.append(f"set:{path}={v}")
    return ops


if __name__ == "__main__":
    import make_maps
    sys.exit(make_maps.main())
