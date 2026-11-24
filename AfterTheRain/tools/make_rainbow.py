"""Generates Textures/AfterTheRain/Rainbow.png (1024x512 RGBA).

The arcs are centred on the bottom-middle of the image. Radii are fractions of the
image height (512 px):
  - primary bow: red outside (0.80) to violet inside (0.69), pastel, full alpha at the core
  - secondary bow: reversed colours, 0.85-0.98, about a third as strong
  - a faint lightening of the sky inside the primary bow
The final on-screen opacity is set by the material colour in C#, so the texture keeps
its alpha high to survive DXT compression.

Run with any Python that has Pillow and numpy:  python tools/make_rainbow.py
"""
import pathlib

import numpy as np
from PIL import Image

W, H = 1024, 512
OUT = pathlib.Path(__file__).resolve().parents[1] / "Textures" / "AfterTheRain" / "Rainbow.png"

# outside -> inside
SPECTRUM = np.array([
    (1.00, 0.36, 0.36),  # red
    (1.00, 0.62, 0.30),  # orange
    (1.00, 0.92, 0.38),  # yellow
    (0.45, 0.92, 0.45),  # green
    (0.38, 0.72, 1.00),  # blue
    (0.52, 0.45, 0.95),  # indigo
    (0.80, 0.48, 0.95),  # violet
])
PASTEL = 0.30  # mix with white


def spectrum(t):
    """t in [0,1] (0 = outer edge) -> RGB, pastel."""
    t = np.clip(t, 0.0, 1.0) * (len(SPECTRUM) - 1)
    i = np.clip(np.floor(t).astype(int), 0, len(SPECTRUM) - 2)
    f = (t - i)[..., None]
    rgb = SPECTRUM[i] * (1 - f) + SPECTRUM[i + 1] * f
    return rgb * (1 - PASTEL) + PASTEL


def smoothstep(a, b, x):
    x = np.clip((x - a) / (b - a), 0.0, 1.0)
    return x * x * (3 - 2 * x)


def band_alpha(t):
    """Soft bump over the band, t in [0,1], with feathered shoulders outside it."""
    # super-gaussian: flat-ish top, soft shoulders (0.5 at the band edges, ~0 at 0.25 beyond)
    return np.exp(-((t - 0.5) / 0.55) ** 4)


def main():
    ys, xs = np.mgrid[0:H, 0:W].astype(np.float64)
    dx = (xs + 0.5 - W / 2) / H
    dy = (H - (ys + 0.5)) / H          # height above the bottom edge, 0..1
    r = np.sqrt(dx * dx + dy * dy)

    # primary
    p_out, p_in = 0.80, 0.69
    tp = (p_out - r) / (p_out - p_in)
    rgb = spectrum(tp)
    a = band_alpha(tp)

    # secondary (reversed: violet outside -> red inside)
    s_out, s_in = 0.98, 0.85
    ts = (s_out - r) / (s_out - s_in)
    a2 = band_alpha(ts) * 0.33
    rgb2 = spectrum(1 - ts)

    # brighter sky inside the primary bow
    inner = 0.14 * smoothstep(0.25, p_in, r) * (1 - smoothstep(p_in - 0.01, p_in + 0.05, r))

    # composite secondary + inner glow under the primary (straight alpha "over")
    def over(c_top, a_top, c_bot, a_bot):
        a_out = a_top + a_bot * (1 - a_top)
        c = (c_top * a_top[..., None] + c_bot * (a_bot * (1 - a_top))[..., None]) / np.maximum(a_out, 1e-6)[..., None]
        return c, a_out

    white = np.ones_like(rgb)
    c, al = over(rgb2, a2, white, inner)
    c, al = over(rgb, a, c, al)

    # dissolve the feet a little and clear the very bottom rows
    al *= 0.45 + 0.55 * smoothstep(0.0, 0.45, dy)
    al *= smoothstep(0.0, 0.02, dy)
    # nothing outside the secondary
    al *= 1 - smoothstep(0.97, 1.0, r)

    img = np.dstack([np.clip(c, 0, 1), np.clip(al, 0, 1)])
    OUT.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray((img * 255 + 0.5).astype(np.uint8), "RGBA").save(OUT)
    print(f"wrote {OUT} ({W}x{H}), max alpha {al.max():.2f}")


if __name__ == "__main__":
    main()
