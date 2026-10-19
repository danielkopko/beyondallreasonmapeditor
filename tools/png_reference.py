"""Writes PNG test files with an encoder independent of the C# code, plus the pixels each should decode to.

Usage:
    python -I tools/png_reference.py <out-dir>

For each case <name>.png it writes <name>.rgba (8-bit RGBA, row by row) or, for 16-bit grey, <name>.gray16
(16-bit big-endian samples). Cases cover every row filter, palette + tRNS, sub-byte grey, 16-bit grey,
grey with a tRNS colour key, and Adam7 interlacing. Standard library only.
"""
import os
import struct
import sys
import zlib


def chunk(kind, data):
    return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data) & 0xffffffff)


def paeth(a, b, c):
    p = a + b - c
    pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
    return a if pa <= pb and pa <= pc else (b if pb <= pc else c)


def filter_row(kind, row, prev, bpp):
    out = bytearray([kind])
    for i, v in enumerate(row):
        a = row[i - bpp] if i >= bpp else 0
        b = prev[i] if prev else 0
        c = prev[i - bpp] if prev and i >= bpp else 0
        pred = [0, a, b, (a + b) >> 1, paeth(a, b, c)][kind]
        out.append((v - pred) & 0xff)
    return bytes(out)


def pack_rows(rows, bpp):
    data, prev = b'', None
    for y, row in enumerate(rows):
        data += filter_row(y % 5, row, prev, bpp)  # cycle through all five filters
        prev = row
    return data


def png(width, height, depth, colour, raw, extra=b'', interlace=0):
    ihdr = struct.pack('>IIBBBBB', width, height, depth, colour, 0, 0, interlace)
    return b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', ihdr) + extra + chunk(b'IDAT', zlib.compress(raw)) + chunk(b'IEND', b'')


def write(out, name, data, expected, suffix='.rgba'):
    open(os.path.join(out, name + '.png'), 'wb').write(data)
    open(os.path.join(out, name + suffix), 'wb').write(expected)


def main(out):
    os.makedirs(out, exist_ok=True)

    # RGB 8-bit, every filter.
    w, h = 13, 7
    pix = [[((x * 37 + y * 11) % 256, (x * x + y * 5) % 256, (255 - x * 13 - y * 17) % 256) for x in range(w)] for y in range(h)]
    rows = [bytes(c for p in r for c in p) for r in pix]
    write(out, 'rgb8_filters', png(w, h, 8, 2, pack_rows(rows, 3)), bytes(c for r in pix for p in r for c in p + (255,)))

    # 16-bit grey, every filter.
    w, h = 9, 5
    vals = [[(x * 7919 + y * 104729) % 65536 for x in range(w)] for y in range(h)]
    rows = [b''.join(struct.pack('>H', v) for v in r) for r in vals]
    write(out, 'gray16', png(w, h, 16, 0, pack_rows(rows, 2)), b''.join(struct.pack('>H', v) for r in vals for v in r), '.gray16')

    # 4-bit palette with tRNS (entry 2 half transparent, entry 3 fully transparent).
    w, h = 10, 4
    palette = [(255, 0, 0), (0, 255, 0), (0, 0, 255), (10, 20, 30), (200, 100, 50)]
    alpha = [255, 255, 128, 0]
    idx = [[(x + y) % 5 for x in range(w)] for y in range(h)]
    rows = []
    for r in idx:
        packed = bytearray()
        for i in range(0, w, 2):
            packed.append(r[i] << 4 | (r[i + 1] if i + 1 < w else 0))
        rows.append(bytes(packed))
    extra = chunk(b'PLTE', bytes(c for p in palette for c in p)) + chunk(b'tRNS', bytes(alpha))
    expected = bytes(c for r in idx for i in r for c in palette[i] + ((alpha[i] if i < len(alpha) else 255),))
    write(out, 'palette4_trns', png(w, h, 4, 3, pack_rows(rows, 1), extra), expected)

    # 2-bit grey.
    w, h = 7, 3
    g = [[(x + y * 3) % 4 for x in range(w)] for y in range(h)]
    rows = []
    for r in g:
        packed = bytearray((w * 2 + 7) // 8)
        for i, v in enumerate(r):
            packed[i // 4] |= v << (6 - 2 * (i % 4))
        rows.append(bytes(packed))
    write(out, 'gray2', png(w, h, 2, 0, pack_rows(rows, 1)), bytes(c for r in g for v in r for c in (v * 85,) * 3 + (255,)))

    # 8-bit grey with a tRNS colour key (grey 50 is transparent).
    w, h = 6, 4
    g = [[(x * 40 + y * 10) % 256 for x in range(w)] for y in range(h)]
    g[1][2] = 50
    g[3][5] = 50
    rows = [bytes(r) for r in g]
    expected = bytes(c for r in g for v in r for c in (v, v, v, 0 if v == 50 else 255))
    write(out, 'gray8_key', png(w, h, 8, 0, pack_rows(rows, 1), chunk(b'tRNS', struct.pack('>H', 50))), expected)

    # RGBA 8-bit, Adam7 interlaced.
    w, h = 11, 9
    pix = [[(x * 23 % 256, y * 29 % 256, (x * y * 7) % 256, (x * 31 + y * 17) % 256) for x in range(w)] for y in range(h)]
    raw = b''
    for (x0, y0, dx, dy) in [(0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4), (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2)]:
        sub = [bytes(c for x in range(x0, w, dx) for c in pix[y][x]) for y in range(y0, h, dy)]
        if sub and sub[0]:
            raw += pack_rows(sub, 4)
    write(out, 'rgba8_adam7', png(w, h, 8, 6, raw, interlace=1), bytes(c for r in pix for p in r for c in p))


if __name__ == '__main__':
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    main(sys.argv[1])
