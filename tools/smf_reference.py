"""Reference SMF/SMT parser, independent of the C# code. Used to cross-check MapCore in tests.

Usage:
    python -I tools/smf_reference.py <path/to/map.smf> [--tile COL ROW <out.bin>] [--metal-spots]

Prints a JSON description of the file: header, extra headers, every section's
offset/length/sha256, gaps between sections, and summary values. Any SMT file
named in the tiles section and found next to the SMF is described too.
With --tile, also decodes mip 0 of the texture tile at grid position (COL, ROW),
found through the tile index and the SMT next to the SMF, and writes it to
<out.bin> as 32x32 RGBA, row by row. With --metal-spots, adds "metalSpots":
the spots BAR's resource spot finder would report. Standard library only.
"""
import hashlib
import json
import os
import struct
import sys
from collections import Counter

HEADER_FMT = '<16s i i i i i i i f f i i i i i i i'  # 80 bytes
HEADER_FIELDS = ['magic', 'version', 'mapid', 'mapx', 'mapy', 'squareSize', 'texelPerSquare', 'tilesize',
                 'minHeight', 'maxHeight', 'heightmapPtr', 'typeMapPtr', 'tilesPtr', 'minimapPtr',
                 'metalmapPtr', 'featurePtr', 'numExtraHeaders']
MINIMAP_SIZE = 699048  # DXT1 1024x1024 with 9 mip levels (down to 4x4)
EXTRA_GRASS = 1

SMT_HEADER_FMT = '<16s i i i i'  # magic, version, numTiles, tileSize, compressionType
SMT_HEADER_SIZE = 32  # struct is padded to 32 bytes on disk


def sha256(b):
    return hashlib.sha256(b).hexdigest()


def read_cstr(data, pos):
    end = data.index(b'\0', pos)
    return data[pos:end].decode('latin-1'), end + 1


def describe_smt(path):
    data = open(path, 'rb').read()
    magic, version, num_tiles, tile_size, comp = struct.unpack_from(SMT_HEADER_FMT, data, 0)
    # DXT1 tile with mip chain: 32x32, 16x16, 8x8, 4x4
    tile_bytes = sum(max(1, (tile_size >> m) // 4) ** 2 * 8 for m in range(4))
    return {
        'file': os.path.basename(path),
        'magic': magic.rstrip(b'\0').decode('latin-1'),
        'version': version,
        'numTiles': num_tiles,
        'tileSize': tile_size,
        'compressionType': comp,
        'tileBytes': tile_bytes,
        'expectedSize': SMT_HEADER_SIZE + num_tiles * tile_bytes,
        'actualSize': len(data),
        'sha256': sha256(data),
    }


def describe_smf(path):
    data = open(path, 'rb').read()
    hdr = dict(zip(HEADER_FIELDS, struct.unpack_from(HEADER_FMT, data, 0)))
    hdr['magic'] = hdr['magic'].rstrip(b'\0').decode('latin-1')
    mx, my = hdr['mapx'], hdr['mapy']
    # The engine fixes these; the section sizes below rely on them.
    for field, expected in (('squareSize', 8), ('texelPerSquare', 8), ('tilesize', 32)):
        if hdr[field] != expected:
            sys.exit('unsupported %s=%d (expected %d)' % (field, hdr[field], expected))

    regions = [('header', 0, struct.calcsize(HEADER_FMT))]
    sections = []

    def section(name, offset, length):
        sections.append({'name': name, 'offset': offset, 'length': length,
                         'sha256': sha256(data[offset:offset + length])})
        regions.append((name, offset, length))
        return data[offset:offset + length]

    # Extra headers follow the main header back to back; each starts with (size, type).
    extras = []
    pos = struct.calcsize(HEADER_FMT)
    grass = None
    for _ in range(hdr['numExtraHeaders']):
        size, typ = struct.unpack_from('<ii', data, pos)
        ex = {'offset': pos, 'size': size, 'type': typ}
        if typ == EXTRA_GRASS:
            ex['name'] = 'grass'
            ex['grassPtr'] = struct.unpack_from('<i', data, pos + 8)[0]
            grass = section('grass', ex['grassPtr'], (mx // 4) * (my // 4))
        else:
            ex['name'] = 'unknown'
            ex['raw'] = data[pos:pos + size].hex()
        extras.append(ex)
        regions.append(('extraHeader[%d]' % typ, pos, size))
        pos += size

    hm_count = (mx + 1) * (my + 1)
    hm_bytes = section('heightmap', hdr['heightmapPtr'], hm_count * 2)
    hm = struct.unpack('<%dH' % hm_count, hm_bytes)
    typemap = section('typemap', hdr['typeMapPtr'], (mx // 2) * (my // 2))
    section('minimap', hdr['minimapPtr'], MINIMAP_SIZE)
    metal = section('metalmap', hdr['metalmapPtr'], (mx // 2) * (my // 2))

    # Tiles section: counts, SMT file list, then the tile index (one int per 32x32 texel tile).
    tp = hdr['tilesPtr']
    num_files, num_tiles_total = struct.unpack_from('<ii', data, tp)
    p = tp + 8
    files = []
    for _ in range(num_files):
        n = struct.unpack_from('<i', data, p)[0]
        name, p = read_cstr(data, p + 4)
        files.append({'name': name, 'numTiles': n})
    index_count = (mx * 8 // 32) * (my * 8 // 32)
    index = struct.unpack_from('<%di' % index_count, data, p)
    section('tiles', tp, p + 4 * index_count - tp)

    # Feature section: counts, type names, then 24-byte records.
    fp = hdr['featurePtr']
    num_types, num_feats = struct.unpack_from('<ii', data, fp)
    p = fp + 8
    types = []
    for _ in range(num_types):
        name, p = read_cstr(data, p)
        types.append(name)
    feats = []
    for _ in range(num_feats):
        t, x, y, z, rot, rel = struct.unpack_from('<ifffff', data, p)  # MapFeatureStruct
        feats.append({'type': types[t] if 0 <= t < len(types) else t, 'x': x, 'y': y, 'z': z,
                      'rotation': rot, 'relativeSize': rel})
        p += 24
    section('features', fp, p - fp)

    # Coverage: anything not claimed by the header, extra headers or a section.
    regions.sort(key=lambda r: r[1])
    gaps, overlaps, cursor = [], [], 0
    for name, off, length in regions:
        if off > cursor:
            gaps.append({'offset': cursor, 'length': off - cursor})
        elif off < cursor:
            overlaps.append({'region': name, 'offset': off, 'overlap': cursor - off})
        cursor = max(cursor, off + length)
    if cursor < len(data):
        gaps.append({'offset': cursor, 'length': len(data) - cursor})

    out = {
        'file': os.path.basename(path),
        'size': len(data),
        'sha256': sha256(data),
        'header': hdr,
        'extraHeaders': extras,
        'layout': [r[0] for r in regions],
        'sections': sorted(sections, key=lambda s: s['offset']),
        'gaps': gaps,
        'overlaps': overlaps,
        'heightmap': {'rawMin': min(hm), 'rawMax': max(hm)},
        'typemap': {'histogram': dict(sorted(Counter(typemap).items()))},
        'metalmap': {'histogram': dict(sorted(Counter(metal).items()))},
        'grass': None if grass is None else {'nonzero': sum(1 for b in grass if b)},
        'tiles': {'files': files, 'numTilesTotal': num_tiles_total, 'indexCount': index_count,
                  'uniqueIndices': len(set(index)), 'minIndex': min(index), 'maxIndex': max(index)},
        'features': {'types': types, 'count': num_feats,
                     'byType': dict(Counter(f['type'] for f in feats)), 'first': feats[:5]},
        'smt': [],
    }
    for f in files:
        smt_path = os.path.join(os.path.dirname(path), f['name'])
        if os.path.exists(smt_path):
            out['smt'].append(describe_smt(smt_path))
    return out


def decode_dxt1_block(block):
    """16 RGBA pixels. 4 colours when c0 > c1, else 3 colours plus transparent black; 5/6-bit
    endpoints expand by bit replication."""
    c0, c1, bits = struct.unpack('<HHI', block)

    def rgb(c):
        r, g, b = c >> 11, (c >> 5) & 63, c & 31
        return (r << 3 | r >> 2, g << 2 | g >> 4, b << 3 | b >> 2)
    a, b = rgb(c0), rgb(c1)
    if c0 > c1:
        pal = [a + (255,), b + (255,),
               tuple((2 * a[i] + b[i]) // 3 for i in range(3)) + (255,),
               tuple((a[i] + 2 * b[i]) // 3 for i in range(3)) + (255,)]
    else:
        pal = [a + (255,), b + (255,), tuple((a[i] + b[i]) // 2 for i in range(3)) + (255,), (0, 0, 0, 0)]
    return [pal[(bits >> (2 * i)) & 3] for i in range(16)]


def decode_tile(smf_path, col, row):
    """RGBA bytes (32x32, row by row) of mip 0 of the tile at grid position (col, row)."""
    data = open(smf_path, 'rb').read()
    hdr = dict(zip(HEADER_FIELDS, struct.unpack_from(HEADER_FMT, data, 0)))
    tp = hdr['tilesPtr']
    num_files = struct.unpack_from('<i', data, tp)[0]
    p = tp + 8
    files = []
    for _ in range(num_files):
        n = struct.unpack_from('<i', data, p)[0]
        name, p = read_cstr(data, p + 4)
        files.append((name, n))
    columns = hdr['mapx'] * 8 // 32
    tile = struct.unpack_from('<i', data, p + 4 * (row * columns + col))[0]
    for name, n in files:  # tile numbers run through the files in order
        if tile < n:
            break
        tile -= n
    smt = open(os.path.join(os.path.dirname(smf_path), name), 'rb').read()
    start = SMT_HEADER_SIZE + tile * 680  # 32x32, 16x16, 8x8, 4x4 DXT1 levels per tile
    level0 = smt[start:start + 512]
    out = bytearray(32 * 32 * 4)
    for k in range(64):  # 8x8 blocks, row by row
        for i, px in enumerate(decode_dxt1_block(level0[k * 8:(k + 1) * 8])):
            x, y = (k % 8) * 4 + i % 4, (k // 8) * 4 + i // 4
            out[(y * 32 + x) * 4:(y * 32 + x) * 4 + 4] = bytes(px)
    return bytes(out)


def bar_metal_spots(smf_path):
    """A port of GetSpotsMetal in BAR's common/upgets/api_resource_spot_finder.lua. Scans metal cells row by
    row, skipping the outermost ring (BAR samples from 1.5 cells in to 1.5 cells before the far edge), cuts each
    row into strips of consecutive metal cells, and joins a strip to strips in the row above that overlap it or
    touch it diagonally. A spot's centre is the middle of its bounding box of cell centres; 'raw' is the summed
    raw metal (BAR's worth is raw x maxMetal)."""
    data = open(smf_path, 'rb').read()
    hdr = dict(zip(HEADER_FIELDS, struct.unpack_from(HEADER_FMT, data, 0)))
    w, h = hdr['mapx'] // 2, hdr['mapy'] // 2
    metal = data[hdr['metalmapPtr']:hdr['metalmapPtr'] + w * h]

    parent = {}

    def find(g):
        while parent[g] != g:
            parent[g] = parent[parent[g]]
            g = parent[g]
        return g

    strips = []          # (row, left, right, raw, group)
    previous_row = []
    for z in range(1, h - 1):
        row = []
        x = 1
        while x < w - 1:
            if metal[z * w + x]:
                left, raw = x, 0
                while x < w - 1 and metal[z * w + x]:
                    raw += metal[z * w + x]
                    x += 1
                group = len(parent)
                parent[group] = group
                for (pl, pr, pg) in previous_row:
                    if pl <= x and pr + 1 >= left:  # overlap, or diagonal contact
                        parent[find(pg)] = find(group)
                row.append((left, x - 1, group))
                strips.append((z, left, x - 1, raw, group))
            else:
                x += 1
        previous_row = row

    groups = {}
    for (z, left, right, raw, group) in strips:
        g = groups.setdefault(find(group), {'minX': left, 'maxX': right, 'minZ': z, 'maxZ': z, 'raw': 0})
        g['minX'], g['maxX'] = min(g['minX'], left), max(g['maxX'], right)
        g['minZ'], g['maxZ'] = min(g['minZ'], z), max(g['maxZ'], z)
        g['raw'] += raw
    spots = [{'x': (g['minX'] + g['maxX']) / 2 * 16 + 8, 'z': (g['minZ'] + g['maxZ']) / 2 * 16 + 8, 'raw': g['raw']}
             for g in groups.values()]
    return sorted(spots, key=lambda s: (s['z'], s['x']))


if __name__ == '__main__':
    args = sys.argv[1:]
    if not args:
        sys.exit(__doc__)
    smf_path, options = args[0], args[1:]
    spots = '--metal-spots' in options
    if spots:
        options.remove('--metal-spots')
    if options:
        if len(options) != 4 or options[0] != '--tile':
            sys.exit(__doc__)
        open(options[3], 'wb').write(decode_tile(smf_path, int(options[1]), int(options[2])))
    description = describe_smf(smf_path)
    if spots:
        description['metalSpots'] = bar_metal_spots(smf_path)
    print(json.dumps(description, indent=1))
