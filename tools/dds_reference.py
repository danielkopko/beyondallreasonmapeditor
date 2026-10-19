"""Reference DDS reader and DXT decoder, independent of the C# code. Used to cross-check MapCore in tests.

Usage:
    python -I tools/dds_reference.py <file.dds> [--decode-blocks N --out <raw.bin>]

Prints JSON describing the header and level layout. With --decode-blocks, decodes the first N 4x4 blocks
of mip 0, face 0 and writes them to <raw.bin> as RGBA: 64 bytes per block, pixels in row order within
the block. Uncompressed textures are written block by block in the same way. Standard library only.

Decoding rules (from the DXT1/DXT5 specification): 5/6-bit endpoints expand by bit replication;
DXT1 uses 4 colours when c0 > c1, else 3 colours plus transparent black; DXT5 colour blocks always use
4 colours; DXT5 alpha uses 8 values when a0 > a1, else 6 values plus 0 and 255.
"""
import json
import struct
import sys

DDPF_ALPHAPIXELS, DDPF_FOURCC, DDPF_RGB = 0x1, 0x4, 0x40
DDSCAPS2_CUBEMAP, DDSCAPS2_ALLFACES = 0x200, 0xFC00


def expand(v, bits):
    return (v << (8 - bits)) | (v >> (2 * bits - 8))


def colour_palette(c0, c1, four):
    a = (expand(c0 >> 11, 5), expand((c0 >> 5) & 63, 6), expand(c0 & 31, 5))
    b = (expand(c1 >> 11, 5), expand((c1 >> 5) & 63, 6), expand(c1 & 31, 5))
    pal = [a + (255,), b + (255,)]
    if four:
        pal.append(tuple((2 * a[i] + b[i]) // 3 for i in range(3)) + (255,))
        pal.append(tuple((a[i] + 2 * b[i]) // 3 for i in range(3)) + (255,))
    else:
        pal.append(tuple((a[i] + b[i]) // 2 for i in range(3)) + (255,))
        pal.append((0, 0, 0, 0))
    return pal


def decode_colour(block, always_four):
    c0, c1, bits = struct.unpack('<HHI', block)
    pal = colour_palette(c0, c1, c0 > c1 or always_four)
    return [list(pal[(bits >> (2 * i)) & 3]) for i in range(16)]


def alpha_palette(a0, a1):
    if a0 > a1:
        return [a0, a1] + [((7 - i) * a0 + i * a1 + 3) // 7 for i in range(1, 7)]
    return [a0, a1] + [((5 - i) * a0 + i * a1 + 2) // 5 for i in range(1, 5)] + [0, 255]


def decode_dxt5(block):
    pixels = decode_colour(block[8:16], True)
    pal = alpha_palette(block[0], block[1])
    bits = int.from_bytes(block[2:8], 'little')
    for i in range(16):
        pixels[i][3] = pal[(bits >> (3 * i)) & 7]
    return pixels


def channel(value, mask):
    if mask == 0:
        return None
    shift = (mask & -mask).bit_length() - 1
    top = mask >> shift
    v = (value & mask) >> shift
    return v if top == 255 else (v * 255 + top // 2) // top


def describe(data):
    if data[:4] != b'DDS ':
        sys.exit('not a DDS file')
    (size, flags, height, width, pitch, depth, mips) = struct.unpack_from('<7I', data, 4)
    pf_size, pf_flags, fourcc, bitcount, rmask, gmask, bmask, amask = struct.unpack_from('<II4sIIIII', data, 76)
    caps, caps2 = struct.unpack_from('<II', data, 108)
    mips = max(1, mips)
    faces = bin(caps2 & DDSCAPS2_ALLFACES).count('1') if caps2 & DDSCAPS2_CUBEMAP else 1
    info = {
        'headerSize': size, 'flags': flags, 'width': width, 'height': height, 'pitchOrLinearSize': pitch,
        'mipCount': mips, 'pixelFlags': pf_flags, 'fourCC': fourcc.decode('latin-1').rstrip('\0') if pf_flags & DDPF_FOURCC else None,
        'bitCount': bitcount, 'masks': [rmask, gmask, bmask, amask if pf_flags & DDPF_ALPHAPIXELS else 0],
        'caps': caps, 'caps2': caps2, 'faces': faces, 'dataOffset': 128, 'actualSize': len(data),
    }
    block_bytes = {'DXT1': 8, 'DXT5': 16}.get(info['fourCC'])
    levels = []
    for m in range(mips):
        w, h = max(1, width >> m), max(1, height >> m)
        if block_bytes:
            levels.append(max(1, (w + 3) // 4) * max(1, (h + 3) // 4) * block_bytes)
        elif info['fourCC'] is None:
            levels.append((w * bitcount + 7) // 8 * h)
    info['levelBytes'] = levels
    info['expectedSize'] = 128 + faces * sum(levels) if levels else None
    return info


def decode_blocks(data, info, count):
    width, height = info['width'], info['height']
    across = max(1, (width + 3) // 4)
    out = bytearray()
    fourcc = info['fourCC']
    for k in range(count):
        if fourcc in ('DXT1', 'DXT5'):
            size = 8 if fourcc == 'DXT1' else 16
            block = data[128 + k * size: 128 + (k + 1) * size]
            pixels = decode_colour(block, False) if fourcc == 'DXT1' else decode_dxt5(block)
        elif fourcc is None:
            bx, by = k % across, k // across
            bpp = info['bitCount'] // 8
            stride = (width * info['bitCount'] + 7) // 8
            pixels = []
            for y in range(4):
                for x in range(4):
                    sx, sy = min(bx * 4 + x, width - 1), min(by * 4 + y, height - 1)
                    off = 128 + sy * stride + sx * bpp
                    v = int.from_bytes(data[off:off + bpp], 'little')
                    r, g, b, a = (channel(v, m) for m in info['masks'])
                    pixels.append([r, g, b, 255 if a is None else a])
        else:
            sys.exit('cannot decode format %s' % fourcc)
        for p in pixels:
            out += bytes(p)
    return bytes(out)


if __name__ == '__main__':
    args = sys.argv[1:]
    if not args or (len(args) not in (1, 5)):
        sys.exit(__doc__)
    data = open(args[0], 'rb').read()
    info = describe(data)
    if len(args) == 5:
        count = int(args[args.index('--decode-blocks') + 1])
        open(args[args.index('--out') + 1], 'wb').write(decode_blocks(data, info, count))
    print(json.dumps(info, indent=1))
