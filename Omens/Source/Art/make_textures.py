"""Draws the Omens textures with the Python standard library only (no Pillow).

    .venv/Scripts/python.exe mods/Omens/Source/Art/make_textures.py

Also writes Textures/Things/Omens/ColdCamp.png (64x64): a ring of fire stones around grey ash and two
charred, crossed sticks - a campfire that has been dead for a while.

Writes Textures/Things/Omens/Crow.png (64x64, RGBA): a crow seen from above, head to the top
(RimWorld rotates a mote so that the top of the texture points along its heading), wings spread
with fingered primaries and a fanned tail. Shapes are rasterised at 4x and box-filtered down.
Also writes Source/Art/crow_preview.png (the same bird at 4x on a grass-coloured ground) to look at.
"""
import math
import os
import struct
import zlib

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SIZE = 64
SS = 4


def png(path, w, h, rgba_rows):
    raw = b"".join(b"\x00" + bytes(row) for row in rgba_rows)

    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

    data = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
    data += chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as f:
        f.write(data)
    print("wrote", os.path.relpath(path, ROOT), f"{w}x{h}")


# ---------------------------------------------------------------- shapes (coordinates in 64-px space, y down)

def ellipse(cx, cy, rx, ry, angle_deg=0.0):
    a = math.radians(angle_deg)
    ca, sa = math.cos(a), math.sin(a)

    def inside(x, y):
        dx, dy = x - cx, y - cy
        u = dx * ca + dy * sa
        v = -dx * sa + dy * ca
        return (u / rx) ** 2 + (v / ry) ** 2 <= 1.0
    return inside


def polygon(pts):
    def inside(x, y):
        c = False
        j = len(pts) - 1
        for i in range(len(pts)):
            xi, yi = pts[i]
            xj, yj = pts[j]
            if (yi > y) != (yj > y) and x < (xj - xi) * (y - yi) / (yj - yi) + xi:
                c = not c
            j = i
        return c
    return inside


def mirror(pts):
    return [(SIZE - x, y) for x, y in pts]


SHAPES = []
# body and head
SHAPES.append(ellipse(32, 34, 4.3, 10.5))
SHAPES.append(ellipse(32, 22.5, 3.7, 3.9))
SHAPES.append(polygon([(30.6, 20.2), (33.4, 20.2), (32.0, 14.8)]))          # beak
# tail: a slightly rounded fan
SHAPES.append(polygon([(29.6, 41), (34.4, 41), (37.2, 52), (34.5, 53.6), (32, 54), (29.5, 53.6), (26.8, 52)]))
# wings: broad inner wing, then fingered primaries at the tip
WING = [(33, 27.5), (39, 25.0), (46, 24.0), (52, 24.6), (56.5, 26.2), (57.5, 28.6),
        (54, 31.2), (48, 33.4), (42, 35.0), (36, 35.2), (33, 34.0)]
SHAPES.append(polygon(WING))
SHAPES.append(polygon(mirror(WING)))
for i in range(5):
    cx = 56.6 + i * 0.55
    cy = 26.6 + i * 1.35
    ang = -4 + i * 11            # fan backwards (down) from the leading edge
    length = 4.6 - i * 0.35
    SHAPES.append(ellipse(cx + length * 0.55 * math.cos(math.radians(ang)),
                          cy + length * 0.55 * math.sin(math.radians(ang)), length, 0.95, ang))
    SHAPES.append(ellipse(SIZE - (cx + length * 0.55 * math.cos(math.radians(ang))),
                          cy + length * 0.55 * math.sin(math.radians(ang)), length, 0.95, 180 - ang))


def coverage(shapes=None):
    shapes = SHAPES if shapes is None else shapes
    cov = [[0.0] * SIZE for _ in range(SIZE)]
    step = 1.0 / SS
    for py in range(SIZE):
        for px in range(SIZE):
            hits = 0
            for sy in range(SS):
                for sx in range(SS):
                    x = px + (sx + 0.5) * step
                    y = py + (sy + 0.5) * step
                    if any(s(x, y) for s in shapes):
                        hits += 1
            cov[py][px] = hits / (SS * SS)
    return cov


def main():
    cov = coverage()
    # a soft lighter rim one pixel outside the bird, so it still reads over dark soil and at night
    rim = [[0.0] * SIZE for _ in range(SIZE)]
    for y in range(SIZE):
        for x in range(SIZE):
            if cov[y][x] >= 0.99:
                continue
            m = 0.0
            for dy in (-1, 0, 1):
                for dx in (-1, 0, 1):
                    yy, xx = y + dy, x + dx
                    if 0 <= yy < SIZE and 0 <= xx < SIZE:
                        m = max(m, cov[yy][xx])
            rim[y][x] = m * 0.35
    rows = []
    for y in range(SIZE):
        row = []
        for x in range(SIZE):
            c = cov[y][x]
            # feathers: near-black with a faint blue-grey sheen along the wing's leading half
            shade = 18 + int(10 * max(0.0, 1.0 - abs(y - 27) / 9.0)) if c > 0 else 0
            if c > 0:
                a = c
                r, g, b = shade, shade, shade + 6
            else:
                a = rim[y][x]
                r, g, b = 150, 150, 160
            row += [r, g, b, int(round(255 * min(1.0, a)))]
        rows.append(row)
    png(os.path.join(ROOT, "Textures", "Things", "Omens", "Crow.png"), SIZE, SIZE, rows)

    # preview: 4x nearest-neighbour over a grass tone
    k = 4
    prev = []
    for y in range(SIZE * k):
        row = []
        for x in range(SIZE * k):
            r, g, b, a = rows[y // k][(x // k) * 4:(x // k) * 4 + 4]
            al = a / 255
            row += [int(r * al + 92 * (1 - al)), int(g * al + 104 * (1 - al)), int(b * al + 60 * (1 - al)), 255]
        prev.append(row)
    png(os.path.join(ROOT, "Source", "Art", "crow_preview.png"), SIZE * k, SIZE * k, prev)


def camp():
    """Layers, back to front: ash disc, charred sticks, stones (with dark outline)."""
    import random
    rnd = random.Random(7)
    ash = coverage([ellipse(32, 33, 17, 15)])
    sticks = coverage([ellipse(32, 33, 15, 2.6, 28), ellipse(32, 33, 14, 2.4, -34), ellipse(31, 34, 9, 2.2, 95)])
    stones, outline = [], []
    for i in range(11):
        a = i / 11 * 2 * math.pi + rnd.uniform(-0.12, 0.12)
        cx, cy = 32 + math.cos(a) * 21.5, 33 + math.sin(a) * 19.5
        rx, ry, rot = rnd.uniform(4.2, 5.4), rnd.uniform(3.4, 4.4), rnd.uniform(0, 180)
        stones.append(ellipse(cx, cy, rx, ry, rot))
        outline.append(ellipse(cx, cy, rx + 1.1, ry + 1.1, rot))
    st = coverage(stones)
    ol = coverage(outline)
    rows = []
    for y in range(SIZE):
        row = []
        for x in range(SIZE):
            # ash: grey, mottled, fading at the rim
            n = (math.sin(x * 1.7) * math.cos(y * 2.3) + math.sin((x + y) * 0.9)) * 6
            r, g, b, a = 118 + n, 114 + n, 110 + n, ash[y][x] * 0.85
            # charred sticks over the ash
            k = sticks[y][x]
            r, g, b = r * (1 - k) + 34 * k, g * (1 - k) + 30 * k, b * (1 - k) + 28 * k
            a = max(a, k)
            # stone outline then stones
            o = ol[y][x]
            r, g, b = r * (1 - o) + 40 * o, g * (1 - o) + 38 * o, b * (1 - o) + 36 * o
            a = max(a, o)
            s_ = st[y][x]
            shade = 150 + 25 * (1 - y / SIZE)
            r, g, b = r * (1 - s_) + shade * s_, g * (1 - s_) + (shade - 4) * s_, b * (1 - s_) + (shade - 8) * s_
            row += [int(r), int(g), int(b), int(round(255 * min(1.0, a)))]
        rows.append(row)
    png(os.path.join(ROOT, "Textures", "Things", "Omens", "ColdCamp.png"), SIZE, SIZE, rows)


def splash_and_dust():
    """Rain: a bright droplet ring with a few flung drops (Splash.png). Snow: a soft white puff (Dust.png). 32x32."""
    n = 32
    c = n / 2
    ring, puff = [], []
    for y in range(n):
        rr, pr = [], []
        for x in range(n):
            # 4x4 supersampled ring: radius 8.5, half-width 1.4, plus six drops at radius 13
            hits = 0
            for sy in range(4):
                for sx in range(4):
                    px, py = x + (sx + 0.5) / 4, y + (sy + 0.5) / 4
                    d = math.hypot(px - c, py - c)
                    on = abs(d - 8.5) <= 1.4
                    for k in range(6):
                        a = k / 6 * 2 * math.pi + 0.4
                        if math.hypot(px - (c + math.cos(a) * 13), py - (c + math.sin(a) * 13)) <= 1.5:
                            on = True
                    hits += on
            al = hits / 16
            rr += [215, 232, 255, int(255 * al)]
            d = math.hypot(x + 0.5 - c, y + 0.5 - c) / (n / 2)
            pa = max(0.0, 1 - d) ** 1.6
            pr += [255, 255, 255, int(255 * min(1.0, pa * 1.3))]
        ring.append(rr)
        puff.append(pr)
    png(os.path.join(ROOT, "Textures", "Things", "Omens", "Splash.png"), n, n, ring)
    png(os.path.join(ROOT, "Textures", "Things", "Omens", "Dust.png"), n, n, puff)


if __name__ == "__main__":
    main()
    camp()
    splash_and_dust()
