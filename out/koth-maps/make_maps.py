#!/usr/bin/env python3
"""Writes every map: the ten single-hill maps (make_koth_maps.py) and the seven king maps (make_kings_maps.py),
each on its own planet (planets.py), with index.html, the gallery; then prints the checks.

Run: python make_maps.py           (python make_maps.py --plain draws them in the plain design colours)
"""
import os
import sys

import make_kings_maps
import make_koth_maps
from make_koth_maps import HERE, index_html
from planets import THEMES


def main():
    plain = "--plain" in sys.argv[1:]
    sections = [
        ("Single hill: three-way king of the hill",
         "Three teams, one hill. Each map is drawn for Team A's third and turned 120° and 240°.",
         [m() for m in make_koth_maps.MAPS]),
        ("Kings and peasants: three to five kings",
         "Each team is a king, who starts in a keep round its throne, and three peasants out in the field. "
         "Hold an enemy's throne to topple that king.",
         [m() for m in make_kings_maps.MAPS]),
    ]
    bad = 0
    for _, _, designs in sections:
        for d in designs:
            if not plain:
                d.theme = THEMES.get(d.key)
                if d.theme is None:
                    print(f"   ! {d.name} has no planet in planets.py")
                    bad += 1
            with open(os.path.join(HERE, d.filename), "w", encoding="utf-8", newline="\n") as f:
                f.write(d.svg())
            row, problems = d.check()
            print(row)
            for p in problems:
                print("   !", p)
            bad += len(problems)
    with open(os.path.join(HERE, "index.html"), "w", encoding="utf-8", newline="\n") as f:
        f.write(index_html(sections))
    count = sum(len(s[2]) for s in sections)
    print(f"{count} maps written to {HERE}; {bad} problem(s)")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
