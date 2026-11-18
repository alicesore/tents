"""Draws Tents' icon (original artwork) and writes Tents.ico. Pure Python, no packages.

    python src/Tents.App/Assets/make-icon.py

A white tent with a door on a rounded blue square, at the sizes Windows asks for (tray, Explorer, taskbar). Each size
is drawn from the shapes, 4x4 supersampled, and stored as PNG inside the .ico.
"""
import os
import struct
import zlib

SIZES = [16, 20, 24, 32, 40, 48, 64, 256]
BACKGROUND = (0x2A, 0x6F, 0xB0)
TENT = (0xF2, 0xF4, 0xF6)
SAMPLES = 4


def inside_rounded_square(x, y, radius=0.2):
    cx = min(max(x, radius), 1 - radius)
    cy = min(max(y, radius), 1 - radius)
    return (x - cx) ** 2 + (y - cy) ** 2 <= radius ** 2


def inside_triangle(x, y, apex, left, right):
    def side(p, a, b):
        return (p[0] - b[0]) * (a[1] - b[1]) - (a[0] - b[0]) * (p[1] - b[1])

    p = (x, y)
    d1, d2, d3 = side(p, apex, left), side(p, left, right), side(p, right, apex)
    negative = d1 < 0 or d2 < 0 or d3 < 0
    positive = d1 > 0 or d2 > 0 or d3 > 0
    return not (negative and positive)


def colour_at(x, y):
    """RGBA of one sample point in the unit square."""
    if not inside_rounded_square(x, y):
        return (0, 0, 0, 0)
    if inside_triangle(x, y, (0.5, 0.45), (0.38, 0.80), (0.62, 0.80)):
        return BACKGROUND + (255,)  # the door
    if inside_triangle(x, y, (0.5, 0.17), (0.13, 0.80), (0.87, 0.80)):
        return TENT + (255,)
    return BACKGROUND + (255,)


def render(size):
    rows = []
    for py in range(size):
        row = bytearray()
        for px in range(size):
            r = g = b = a = 0
            for sy in range(SAMPLES):
                for sx in range(SAMPLES):
                    c = colour_at((px + (sx + 0.5) / SAMPLES) / size, (py + (sy + 0.5) / SAMPLES) / size)
                    r += c[0] * c[3]
                    g += c[1] * c[3]
                    b += c[2] * c[3]
                    a += c[3]
            n = SAMPLES * SAMPLES
            if a:
                row += bytes((round(r / a), round(g / a), round(b / a), round(a / n)))
            else:
                row += bytes(4)
        rows.append(bytes(row))
    return rows


def png(size, rows):
    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)

    raw = b"".join(b"\x00" + row for row in rows)
    header = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)  # 8-bit RGBA
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header) + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")


def ico(images):
    header = struct.pack("<HHH", 0, 1, len(images))
    offset = 6 + 16 * len(images)
    directory = b""
    data = b""
    for size, payload in images:
        dim = 0 if size >= 256 else size
        directory += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(payload), offset + len(data))
        data += payload
    return header + directory + data


def main():
    images = [(size, png(size, render(size))) for size in SIZES]
    path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "Tents.ico")
    with open(path, "wb") as f:
        f.write(ico(images))
    print(f"wrote {path} ({os.path.getsize(path)} bytes, sizes {SIZES})")


if __name__ == "__main__":
    main()
