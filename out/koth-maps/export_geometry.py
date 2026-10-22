#!/usr/bin/env python3
"""Writes a map drawing's geometry as JSON, for building the map from it: every part (water, land, halls, ramps,
switchbacks, bridges, fords, paths), metal spot, vent, keep and peasant base, turned for every team, in the drawing's
pixels (map area x, y 20-660; the centre at 340, 340).

Run: python export_geometry.py five_rivers <out.json>
"""
import json
import sys

import make_kings_maps
import make_koth_maps
from make_koth_maps import CX, CY, ab, rot
from planets import THEMES


def turned(value, t):
    if isinstance(value, tuple) and len(value) == 2 and all(isinstance(v, (int, float)) for v in value):
        return [round(c, 3) for c in ab(rot(value, t))]
    if isinstance(value, list) and value and isinstance(value[0], tuple):
        return [[round(c, 3) for c in ab(rot(p, t))] for p in value]
    return value


def export(d):
    teams = d.turns(False)
    parts = []
    for kind, whole, fields in d.parts:
        for i, t in enumerate(d.turns(whole)):
            parts.append(dict(kind=kind, team=None if whole else i, **{k: turned(v, t) for k, v in fields.items()}))
    spots = [dict(x=q[0], y=q[1], income=v, kind=kind, wet=wet, team=i)
             for p, v, kind, wet in d.spots for i, t in enumerate(teams) for q in [turned(p, t)]]
    vents = [dict(x=q[0], y=q[1], team=None if whole else i)
             for p, whole in d.geos for i, t in enumerate(d.turns(whole)) for q in [turned(p, t)]]
    kings = [dict(x=q[0], y=q[1], team=i) for p in d.kings for i, t in enumerate(teams) for q in [turned(p, t)]]
    peasants = [dict(x=q[0], y=q[1], team=i, n=j + 1) for j, p in enumerate(d.peasants) for i, t in enumerate(teams)
                for q in [turned(p, t)]]
    return dict(name=d.name, key=d.key, size=d.size, fold=d.fold, axis=d.axis, square=not d.play_disc,
                ground=d.ground_h, centre=[CX, CY], play_radius=make_koth_maps.R_PLAY,
                units_per_pixel=d.size * 512 / 640, origin=20, heights=d.heights, parts=parts, spots=spots,
                vents=vents, kings=kings, peasants=peasants, theme=THEMES.get(d.key))


def main():
    key, out = sys.argv[1], sys.argv[2]
    makers = {m.__name__: m for m in make_kings_maps.MAPS + make_koth_maps.MAPS}
    d = makers[key]()
    with open(out, "w", encoding="utf-8", newline="\n") as f:
        json.dump(export(d), f, indent=1)
    print(f"{d.name}: {len(d.parts)} parts, {len(d.spots)} spots a team, written to {out}")


if __name__ == "__main__":
    main()
