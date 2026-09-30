"""Title screen: white fog drifting over black, with faint lights moving behind it.

The engine composes the picture live (so it loops forever): two horizontally tileable fog
layers and a layer of soft lights, each TEX_W x 160 bytes, scrolled at different speeds; a
32 x 32 table turns (fog, light) amounts into colours.
"""
import numpy as np

TEX_W, H = 256, 160
LEVELS = 32


def _noise(rng, cells_x, cells_y):
    """Smooth value noise, tileable in x, TEX_W x H, 0..1."""
    g = rng.random((cells_y + 1, cells_x))
    xs = np.arange(TEX_W) * cells_x / TEX_W
    ys = np.arange(H) * cells_y / H
    x0 = np.floor(xs).astype(int)
    y0 = np.floor(ys).astype(int)
    fx = xs - x0
    fy = ys - y0
    fx = fx * fx * (3 - 2 * fx)
    fy = fy * fy * (3 - 2 * fy)
    x1 = (x0 + 1) % cells_x
    y1 = np.minimum(y0 + 1, cells_y)
    a = g[y0][:, x0 % cells_x]
    b = g[y0][:, x1]
    c = g[y1][:, x0 % cells_x]
    d = g[y1][:, x1]
    top = a + (b - a) * fx
    bot = c + (d - c) * fx
    return top + (bot - top) * fy[:, None]


def _fbm(seed, base, octaves=4):
    rng = np.random.default_rng(seed)
    out, amp, total = np.zeros((H, TEX_W)), 1.0, 0.0
    for o in range(octaves):
        out += amp * _noise(rng, base * 2 ** o, max(2, base * 2 ** o * H // TEX_W))
        total += amp
        amp *= 0.5
    return out / total


def fog_layers():
    y = np.linspace(0, 1, H)[:, None]
    # lower fog: thick near the ground, thinning upwards
    n1 = _fbm(3, 4)
    ground = np.clip((y - 0.35) / 0.65, 0, 1) ** 1.3
    f1 = np.clip((n1 - 0.38) * 2.6, 0, 1) * (0.2 + 0.8 * ground) * 165
    # thin wisps drifting across the middle
    n2 = _fbm(11, 6)
    band = np.exp(-((y - 0.55) / 0.28) ** 2)
    f2 = np.clip((n2 - 0.48) * 3.2, 0, 1) * (0.15 + 0.85 * band) * 120
    return f1.astype(np.uint8), f2.astype(np.uint8)


def light_layer():
    rng = np.random.default_rng(7)
    L = np.zeros((H, TEX_W))
    yy, xx = np.mgrid[0:H, 0:TEX_W]
    for _ in range(14):
        cx, cy = rng.uniform(0, TEX_W), rng.uniform(30, 150)
        r = rng.uniform(1.2, 3.2)
        peak = rng.uniform(60, 150)
        dx = np.minimum(abs(xx - cx), TEX_W - abs(xx - cx))  # wraps around like the scroll
        g = np.exp(-(dx ** 2 + (yy - cy) ** 2) / (2 * r * r))
        halo = np.exp(-(dx ** 2 + (yy - cy) ** 2) / (2 * (r * 5) ** 2)) * 0.12
        L += (g + halo) * peak
    return np.clip(L, 0, 255).astype(np.uint8)


def colour_table():
    """LEVELS x LEVELS 15-bit colours: [fog level][light level]."""
    out = []
    for f in range(LEVELS):
        fa = f / (LEVELS - 1)
        for l in range(LEVELS):
            la = l / (LEVELS - 1)
            fog = np.array([236, 240, 248]) * fa ** 0.8
            light = np.array([255, 214, 150]) * la * (1 - 0.55 * fa)  # fog veils the lights
            r, g, b = np.clip(fog + light, 0, 255).astype(int)
            out.append((b >> 3) << 10 | (g >> 3) << 5 | (r >> 3))
    return out
