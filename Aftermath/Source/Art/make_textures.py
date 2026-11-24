"""Procedural textures for Aftermath. Run from anywhere:  python make_textures.py
Writes into ../../Textures and ../../About/Preview.png. Everything is drawn at 4x and downsampled."""
import math
import os
import random

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
TEX = os.path.join(ROOT, "Textures")
SS = 4  # supersampling


def out(rel, img):
    path = os.path.join(TEX, rel + ".png")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    img.save(path)
    print("wrote", os.path.relpath(path, ROOT), img.size)


def canvas(w, h):
    return Image.new("RGBA", (w * SS, h * SS), (0, 0, 0, 0))


def down(img, w, h):
    return img.resize((w, h), Image.LANCZOS)


def S(v):
    return int(round(v * SS))


def outline(img, color=(20, 14, 10, 255), width=1.5):
    """Dark outline around the opaque shape, RimWorld style."""
    a = img.split()[3]
    grown = a.filter(ImageFilter.MaxFilter(S(width) * 2 + 1))
    base = Image.new("RGBA", img.size, color)
    base.putalpha(grown)
    base.alpha_composite(img)
    return base


# ---------------------------------------------------------------- standard (banner on a pole)
# 128x128 canvas. Pole at x=40, crossbar at y=18, cloth from x=42..104, y=22..96 with a swallowtail.
def cloth_polygon():
    x0, x1, y0, y1 = 44, 106, 22, 98
    notch = 14
    return [(x0, y0), (x1, y0), (x1, y1), ((x0 + x1) / 2, y1 - notch), (x0, y1)]


def draw_cloth(d, fill_fn):
    """Cloth with vertical folds: fill_fn(shade 0..1) -> RGBA."""
    poly = cloth_polygon()
    mask = Image.new("L", (128 * SS, 128 * SS), 0)
    ImageDraw.Draw(mask).polygon([(S(x), S(y)) for x, y in poly], fill=255)
    layer = Image.new("RGBA", mask.size, (0, 0, 0, 0))
    px = layer.load()
    mpx = mask.load()
    x0, x1 = S(44), S(106)
    for y in range(mask.size[1]):
        for x in range(x0, x1 + 1):
            if mpx[x, y] == 0:
                continue
            t = (x - x0) / max(1, (x1 - x0))
            fold = 0.5 + 0.5 * math.sin(t * math.pi * 4.0 + 0.6)
            shade = 0.80 + 0.20 * fold - 0.08 * (y / mask.size[1])
            px[x, y] = fill_fn(max(0.0, min(1.0, shade)))
    d.alpha_composite(layer)
    return poly


def standard_base():
    img = canvas(128, 128)
    d = ImageDraw.Draw(img)
    # base stand: a small cross foot
    d.rectangle([S(30), S(118), S(52), S(123)], fill=(70, 52, 34, 255))
    d.rectangle([S(26), S(121), S(56), S(125)], fill=(58, 42, 28, 255))
    # pole
    d.rectangle([S(38), S(12), S(43), S(120)], fill=(112, 82, 52, 255))
    d.rectangle([S(38), S(12), S(39.5), S(120)], fill=(138, 104, 68, 255))
    # crossbar
    d.rectangle([S(36), S(17), S(110), S(21)], fill=(96, 70, 44, 255))
    # finial
    d.polygon([(S(40.5), S(3)), (S(45), S(12)), (S(36), S(12))], fill=(170, 150, 96, 255))
    # off-white cloth (what the crate/menu shows; the tinted cloth is printed over it in game)
    draw_cloth(img, lambda s: (int(214 * s), int(204 * s), int(184 * s), 255))
    # ties
    for x in (50, 68, 86, 102):
        d.rectangle([S(x), S(19), S(x + 2), S(24)], fill=(80, 60, 40, 255))
    return outline(img)


def standard_cloth():
    img = canvas(128, 128)
    draw_cloth(img, lambda s: (int(255 * s), int(255 * s), int(255 * s), 255))
    d = ImageDraw.Draw(img)
    # a darker hem band along the bottom edges and a stitched border, still greyscale
    poly = cloth_polygon()
    d.line([(S(x), S(y)) for x, y in poly + [poly[0]]], fill=(150, 150, 150, 255), width=S(1.6))
    return img


# ---------------------------------------------------------------- plank for braced doors (128x32)
def plank():
    img = canvas(128, 32)
    d = ImageDraw.Draw(img)
    rnd = random.Random(7)
    d.rounded_rectangle([S(2), S(6), S(126), S(26)], radius=S(3), fill=(122, 84, 48, 255))
    for i in range(9):  # grain
        y = rnd.uniform(9, 23)
        d.line([(S(4), S(y)), (S(124), S(y + rnd.uniform(-1.5, 1.5)))], fill=(96, 64, 36, 255), width=S(0.8))
    d.line([(S(4), S(8)), (S(124), S(8))], fill=(150, 108, 66, 255), width=S(1.2))
    for x in (10, 118):  # nails
        d.ellipse([S(x - 3), S(13), S(x + 3), S(19)], fill=(70, 70, 74, 255))
        d.ellipse([S(x - 1.5), S(14), S(x + 0.5), S(16)], fill=(160, 160, 166, 255))
    return outline(img, width=1.2)


# ---------------------------------------------------------------- offerings (64x64)
def flowers():
    img = canvas(64, 64)
    d = ImageDraw.Draw(img)
    stems = [(32, 52, 22, 22), (32, 52, 32, 16), (32, 52, 42, 22), (32, 52, 27, 30), (32, 52, 38, 30)]
    for x0, y0, x1, y1 in stems:
        d.line([(S(x0), S(y0)), (S(x1), S(y1))], fill=(60, 110, 50, 255), width=S(2))
    colors = [(200, 60, 70), (236, 200, 80), (210, 90, 160), (240, 240, 235), (200, 60, 70)]
    for (x0, y0, x1, y1), c in zip(stems, colors):
        for k in range(5):
            a = k * 2 * math.pi / 5
            px, py = x1 + 3.2 * math.cos(a), y1 + 3.2 * math.sin(a)
            d.ellipse([S(px - 2.6), S(py - 2.6), S(px + 2.6), S(py + 2.6)], fill=c + (255,))
        d.ellipse([S(x1 - 1.6), S(y1 - 1.6), S(x1 + 1.6), S(y1 + 1.6)], fill=(250, 220, 90, 255))
    # tied ribbon
    d.polygon([(S(28), S(46)), (S(36), S(46)), (S(38), S(52)), (S(26), S(52))], fill=(180, 40, 50, 255))
    d.line([(S(30), S(52)), (S(26), S(58))], fill=(180, 40, 50, 255), width=S(1.6))
    d.line([(S(34), S(52)), (S(38), S(58))], fill=(180, 40, 50, 255), width=S(1.6))
    return outline(img)


def letter():
    img = canvas(64, 64)
    d = ImageDraw.Draw(img)
    d.polygon([(S(14), S(22)), (S(50), S(18)), (S(52), S(44)), (S(16), S(48))], fill=(232, 224, 200, 255))
    d.line([(S(14), S(22)), (S(33), S(35)), (S(50), S(18))], fill=(170, 160, 140, 255), width=S(1.2))
    d.ellipse([S(29), S(31), S(37), S(39)], fill=(160, 30, 40, 255))  # wax seal
    for i in range(3):
        y = 40 + i * 2.4
        d.line([(S(20), S(y + 1)), (S(30 - i * 2), S(y))], fill=(120, 110, 100, 255), width=S(0.8))
    return outline(img)


def figure():
    img = canvas(64, 64)
    d = ImageDraw.Draw(img)
    wood, dark = (176, 128, 80, 255), (140, 98, 58, 255)
    d.ellipse([S(22), S(50), S(42), S(56)], fill=dark)  # plinth
    d.polygon([(S(25), S(52)), (S(39), S(52)), (S(36), S(30)), (S(28), S(30))], fill=wood)  # robe
    d.ellipse([S(27), S(18), S(37), S(29)], fill=wood)  # head
    d.line([(S(28), S(34)), (S(23), S(42))], fill=dark, width=S(2.2))  # arms
    d.line([(S(36), S(34)), (S(41), S(42))], fill=dark, width=S(2.2))
    d.line([(S(30), S(36)), (S(31), S(50))], fill=dark, width=S(0.8))
    return outline(img)


def candle():
    img = canvas(64, 64)
    d = ImageDraw.Draw(img)
    d.ellipse([S(20), S(50), S(44), S(57)], fill=(120, 110, 100, 255))  # dish
    d.rectangle([S(28), S(28), S(36), S(53)], fill=(236, 226, 196, 255))
    d.rectangle([S(28), S(28), S(30), S(53)], fill=(250, 244, 222, 255))
    d.line([(S(32), S(28)), (S(32), S(24))], fill=(40, 30, 20, 255), width=S(1))
    d.ellipse([S(29), S(13), S(35), S(25)], fill=(255, 190, 70, 255))  # flame
    d.ellipse([S(30.5), S(17), S(33.5), S(24)], fill=(255, 240, 180, 255))
    return outline(img)


# ---------------------------------------------------------------- About/Preview.png (640x360)
def preview(base, cloth, plank_img, offerings):
    W, H = 640, 360
    img = Image.new("RGBA", (W, H), (30, 27, 24, 255))
    d = ImageDraw.Draw(img)
    for y in range(H):  # dusk gradient
        t = y / H
        d.line([(0, y), (W, y)], fill=(int(46 + 30 * t), int(40 + 18 * t), int(44 - 8 * t), 255))
    d.rectangle([0, 250, W, H], fill=(58, 50, 40, 255))  # ground
    # banner, tinted red with a dark cloth
    big = base.resize((240, 240), Image.LANCZOS)
    tint = cloth.resize((240, 240), Image.LANCZOS)
    r, g, b, a = tint.split()
    tinted = Image.merge("RGBA", (r.point(lambda v: int(v * 0.70)), g.point(lambda v: int(v * 0.16)),
                                  b.point(lambda v: int(v * 0.16)), a))
    img.alpha_composite(big, (40, 30))
    img.alpha_composite(tinted, (40, 30))
    # door with two planks
    d.rectangle([300, 120, 400, 262], fill=(70, 70, 76, 255), outline=(20, 20, 22, 255), width=3)
    d.rectangle([312, 132, 388, 262], fill=(150, 112, 70, 255), outline=(40, 28, 18, 255), width=3)
    p = plank_img.resize((150, 38), Image.LANCZOS)
    for ang, dy in ((28, 0), (-28, 30)):
        rp = p.rotate(ang, expand=True, resample=Image.BICUBIC)
        img.alpha_composite(rp, (350 - rp.width // 2, 175 + dy - rp.height // 2))
    # offerings around a bed
    d.rectangle([450, 170, 560, 262], fill=(90, 120, 150, 255), outline=(20, 20, 24, 255), width=3)
    d.rectangle([458, 178, 552, 200], fill=(236, 236, 230, 255))
    glow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    ImageDraw.Draw(glow).ellipse([430, 140, 580, 290], fill=(255, 170, 70, 70))
    img.alpha_composite(glow.filter(ImageFilter.GaussianBlur(24)))
    for o, (x, y) in zip(offerings, ((428, 236), (584, 214), (480, 290), (576, 284))):
        img.alpha_composite(o.resize((52, 52), Image.LANCZOS), (x - 26, y - 26))
    try:
        font = ImageFont.truetype("georgiab.ttf", 44)
        small = ImageFont.truetype("georgia.ttf", 20)
    except OSError:
        font = small = ImageFont.load_default()
    d.text((296, 26), "AFTERMATH", font=font, fill=(236, 222, 196, 255))
    d.text((298, 80), "what a battle leaves behind", font=small, fill=(210, 190, 160, 255))
    path = os.path.join(ROOT, "About", "Preview.png")
    img.convert("RGB").save(path)
    print("wrote", os.path.relpath(path, ROOT), img.size)


def main():
    base = down(standard_base(), 128, 128)
    cloth = down(standard_cloth(), 128, 128)
    out("Things/Building/Aftermath/CapturedStandard", base)
    out("Things/Building/Aftermath/CapturedStandardCloth", cloth)
    pl = down(plank(), 128, 32)
    out("Things/Aftermath/BracedPlank", pl)
    offs = []
    for name, fn in (("Flowers", flowers), ("Letter", letter), ("Figure", figure), ("Candle", candle)):
        o = down(fn(), 64, 64)
        out("Things/Item/Aftermath/Offering_" + name, o)
        offs.append(o)
    preview(base, cloth, pl, offs)


if __name__ == "__main__":
    main()
