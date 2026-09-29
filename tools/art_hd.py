"""Detailed built-in illustrations: chapter cards, a few backgrounds and the evidence icons.

Everything is painted at 4x resolution with soft lighting layers and then reduced to GBA size,
which gives smooth shading and crisp detail instead of flat blocks. User images in assets/
still take priority over all of this (see art.py).
"""
import math
import random

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

S = 4  # supersampling factor


def lerp(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(len(a)))


class Canvas:
    """Draw in final-pixel coordinates; the canvas is S times larger underneath."""

    def __init__(self, w, h, bg=(0, 0, 0), alpha=False):
        self.w, self.h = w, h
        mode = "RGBA" if alpha else "RGB"
        self.img = Image.new(mode, (w * S, h * S), bg + ((0,) if alpha else ()))
        self.d = ImageDraw.Draw(self.img)
        self.alpha = alpha

    # -------- primitives (coordinates in final pixels, floats allowed)
    @staticmethod
    def p(v):
        return int(round(v * S))

    def pts(self, pts):
        return [(self.p(x), self.p(y)) for x, y in pts]

    def rect(self, x0, y0, x1, y1, fill, outline=None, width=1):
        self.d.rectangle([self.p(x0), self.p(y0), self.p(x1) - 1, self.p(y1) - 1], fill=fill,
                         outline=outline, width=int(width * S))

    def rrect(self, x0, y0, x1, y1, r, fill, outline=None, width=1):
        self.d.rounded_rectangle([self.p(x0), self.p(y0), self.p(x1) - 1, self.p(y1) - 1], radius=self.p(r),
                                 fill=fill, outline=outline, width=int(width * S))

    def poly(self, pts, fill, outline=None):
        self.d.polygon(self.pts(pts), fill=fill, outline=outline)

    def ell(self, x0, y0, x1, y1, fill, outline=None, width=1):
        self.d.ellipse([self.p(x0), self.p(y0), self.p(x1), self.p(y1)], fill=fill, outline=outline,
                       width=int(width * S))

    def line(self, pts, fill, width=1):
        self.d.line(self.pts(pts), fill=fill, width=max(1, int(width * S)), joint="curve")

    def arc(self, box, a0, a1, fill, width=1):
        self.d.arc([self.p(v) for v in box], a0, a1, fill=fill, width=max(1, int(width * S)))

    def vgrad(self, x0, y0, x1, y1, stops):
        """stops: [(t, colour), ...] from top (t=0) to bottom (t=1)."""
        top, bot = self.p(y0), self.p(y1)
        for y in range(top, bot):
            t = (y - top) / max(1, bot - top - 1)
            for i in range(len(stops) - 1):
                if stops[i][0] <= t <= stops[i + 1][0]:
                    u = (t - stops[i][0]) / max(1e-6, stops[i + 1][0] - stops[i][0])
                    c = lerp(stops[i][1], stops[i + 1][1], u)
                    break
            self.d.line([(self.p(x0), y), (self.p(x1) - 1, y)], fill=c)

    # -------- soft layers
    def layer(self):
        lay = Image.new("RGBA", self.img.size, (0, 0, 0, 0))
        return lay, ImageDraw.Draw(lay)

    def put(self, lay, blur=0):
        if blur:
            lay = lay.filter(ImageFilter.GaussianBlur(blur * S))
        base = self.img.convert("RGBA")
        base.alpha_composite(lay)
        self.img = base if self.alpha else base.convert("RGB")
        self.d = ImageDraw.Draw(self.img)

    def glow(self, cx, cy, r, color, strength=0.8):
        """Additive radial light."""
        a = np.asarray(self.img).astype(np.float32)
        H, W = a.shape[:2]
        x0, x1 = max(0, self.p(cx - r)), min(W, self.p(cx + r))
        y0, y1 = max(0, self.p(cy - r)), min(H, self.p(cy + r))
        if x0 >= x1 or y0 >= y1:
            return
        yy, xx = np.mgrid[y0:y1, x0:x1]
        dist = np.hypot(xx - cx * S, yy - cy * S) / (r * S)
        f = np.clip(1 - dist, 0, 1) ** 2 * strength
        for i in range(3):
            a[y0:y1, x0:x1, i] += f * color[i]
        self.img = Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), self.img.mode)
        self.d = ImageDraw.Draw(self.img)

    def vignette(self, strength=0.55):
        a = np.asarray(self.img).astype(np.float32)
        H, W = a.shape[:2]
        yy, xx = np.mgrid[0:H, 0:W]
        d = np.hypot((xx - W / 2) / (W / 2), (yy - H / 2) / (H / 2)) / 1.41
        f = 1 - strength * np.clip(d, 0, 1) ** 2
        a[..., :3] *= f[..., None]
        self.img = Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), self.img.mode)
        self.d = ImageDraw.Draw(self.img)

    def snow(self, rng, n, box=None, big=0.2, color=(240, 244, 255)):
        x0, y0, x1, y1 = box or (0, 0, self.w, self.h)
        lay, d = self.layer()
        for _ in range(n):
            x, y = rng.uniform(x0, x1), rng.uniform(y0, y1)
            r = rng.uniform(0.35, 0.7) * (2.2 if rng.random() < big else 1)
            a = rng.randrange(150, 255)
            d.ellipse([self.p(x - r), self.p(y - r), self.p(x + r), self.p(y + r)], fill=color + (a,))
        self.put(lay, blur=0.15)

    def finish(self, colors=0):
        img = self.img.resize((self.w, self.h), Image.LANCZOS)
        if colors and not self.alpha:
            img = img.quantize(colors=colors, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE).convert("RGB")
        return img


# =========================================================== scenery helpers

def stars(c, rng, n, y1):
    for _ in range(n):
        x, y = rng.uniform(0, c.w), rng.uniform(0, y1)
        r = rng.choice([0.3, 0.3, 0.4, 0.6])
        col = rng.choice([(255, 255, 255), (210, 220, 255), (255, 240, 210)])
        c.ell(x - r, y - r, x + r, y + r, col)


def moon(c, x, y, r):
    c.glow(x, y, r * 5, (70, 80, 120), 0.8)
    c.glow(x, y, r * 2.2, (120, 120, 150), 0.7)
    c.ell(x - r, y - r, x + r, y + r, (250, 246, 225))
    for dx, dy, rr in [(-0.3, -0.2, 0.25), (0.35, 0.25, 0.18), (-0.1, 0.45, 0.12)]:
        c.ell(x + dx * r - rr * r, y + dy * r - rr * r, x + dx * r + rr * r, y + dy * r + rr * r, (228, 222, 200))


def mountains(c, rng, base, amp, col, snow_col, seed_phase, step=None):
    """Jagged ridge line with snow on the upper slopes and shaded gullies."""
    a = np.asarray(c.img).astype(np.float32)
    H, W = a.shape[:2]
    xs = np.arange(W) / S
    ridge = base - amp * (0.5 * np.abs(np.sin(xs * 0.021 + seed_phase)) + 0.25 * np.sin(xs * 0.057 + seed_phase * 2)
                          + 0.12 * np.sin(xs * 0.17 + seed_phase) + 0.06 * np.sin(xs * 0.53 + seed_phase * 3))
    noise = np.cumsum(np.random.default_rng(int(seed_phase * 1000)).normal(0, 0.18, W))
    ridge = ridge + (noise - np.linspace(noise[0], noise[-1], W)) / S * 2
    top = (ridge * S).astype(int)
    yy = np.arange(H)[:, None]
    depth = (yy - top[None, :]) / S                    # pixels below the ridge
    inside = depth >= 0
    slope = np.gradient(ridge)[None, :]               # light from the left: left-facing slopes are lit
    lit = np.clip(0.5 - slope * 3, 0, 1)
    snow_line = 9 + 5 * np.sin(xs * 0.3 + seed_phase)[None, :] + 4 * np.sin(xs * 1.7 + seed_phase)[None, :]
    snowy = inside & (depth < snow_line)
    rock = np.array(col, np.float32)
    snow = np.array(snow_col, np.float32)
    shade = (0.75 + 0.45 * lit)[..., None]
    fade = np.clip(depth / 24, 0, 1)[..., None]       # lower slopes sink into the dark
    rock_px = rock * (1 - 0.6 * fade) * (0.9 + 0.2 * lit[..., None])
    snow_px = np.clip(snow * shade, 0, 255)
    colour = np.where(snowy[..., None], snow_px, rock_px)
    a[..., :3] = np.where(inside[..., None], colour, a[..., :3])
    c.img = Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), c.img.mode)
    c.d = ImageDraw.Draw(c.img)


def marionette(c, x, y, k=1.0, twisted=True):
    """A jointed wooden clown puppet; (x, y) is the top of its head."""
    wood = (236, 222, 200)
    suit, suit2 = (120, 40, 140), (230, 200, 70)
    ol = (50, 30, 40)

    def limb(pts, w, colour):
        pts = [(x + px * k, y + py * k) for px, py in pts]
        c.line(pts, ol, (w + 1.2) * k)
        c.line(pts, colour, w * k)
        for px, py in pts:
            c.ell(px - 1.3 * k, py - 1.3 * k, px + 1.3 * k, py + 1.3 * k, wood, ol, 0.4)

    # strings (some slack or cut when twisted)
    for sx, ex, ey in ((-8, -8, 12), (8, 10, 11), (0, 0, 0)):
        c.line([(x + sx * k, y - 40 * k), (x + ex * k, y + ey * k)], (190, 190, 200), 0.35)
    if twisted:
        arms = [[(-3, 12), (-10, 16), (-12, 6)], [(3, 12), (11, 10), (15, 16)]]
        legs = [[(-2, 25), (-9, 30), (-4, 37)], [(2, 25), (10, 28), (6, 38)]]
    else:
        arms = [[(-3, 12), (-8, 18), (-9, 24)], [(3, 12), (8, 18), (9, 24)]]
        legs = [[(-2, 25), (-3, 32), (-3, 39)], [(2, 25), (3, 32), (3, 39)]]
    for pts in legs:
        limb(pts, 2.4, suit)
    # torso with diamond pattern
    c.poly([(x - 5 * k, y + 10 * k), (x + 5 * k, y + 10 * k), (x + 4 * k, y + 26 * k), (x - 4 * k, y + 26 * k)], suit, ol)
    for i in range(3):
        cy = y + (14 + i * 4) * k
        c.poly([(x, cy - 1.8 * k), (x + 1.8 * k, cy), (x, cy + 1.8 * k), (x - 1.8 * k, cy)], suit2)
    for pts in arms:
        limb(pts, 2.0, suit)
    # ruffled collar
    for i in range(7):
        a = math.radians(200 + i * 23)
        c.ell(x + 6 * k * math.cos(a) - 2.2 * k, y + 10 * k + 2 * k * math.sin(a) - 1.6 * k,
              x + 6 * k * math.cos(a) + 2.2 * k, y + 10 * k + 2 * k * math.sin(a) + 1.6 * k, (250, 248, 244), ol, 0.3)
    # head, tilted when twisted
    tilt = 2.5 if twisted else 0
    hx = x + tilt * k
    c.ell(hx - 4.5 * k, y, hx + 4.5 * k, y + 9 * k, wood, ol, 0.5)
    c.ell(hx - 2.4 * k, y + 3.2 * k, hx - 1.0 * k, y + 4.6 * k, (30, 20, 20))
    c.ell(hx + 1.0 * k, y + 3.2 * k, hx + 2.4 * k, y + 4.6 * k, (30, 20, 20))
    c.line([(hx - 1.6 * k, y + 6.8 * k), (hx + 1.6 * k, y + 6.3 * k)], (190, 20, 40), 0.6 * k)
    c.ell(hx - 0.8 * k, y + 4.6 * k, hx + 0.8 * k, y + 6.0 * k, (220, 40, 50))
    c.poly([(hx - 4.5 * k, y + 1.5 * k), (hx + 1 * k, y - 7 * k), (hx + 4.5 * k, y + 1.5 * k)], suit, ol)
    c.ell(hx + 0.2 * k, y - 8.2 * k, hx + 2.2 * k, y - 6.2 * k, suit2)


def pine(c, x, y, h, col, snow_col=None):
    w = h * 0.42
    for i in range(4):
        t = i / 4
        yy = y - h * t * 0.85
        ww = w * (1 - t * 0.7)
        c.poly([(x - ww, yy), (x, yy - h * 0.38), (x + ww, yy)], col)
        if snow_col:
            c.poly([(x - ww * 0.55, yy - h * 0.12), (x, yy - h * 0.38), (x + ww * 0.3, yy - h * 0.16)], snow_col)
    c.rect(x - h * 0.04, y, x + h * 0.04, y + h * 0.1, (30, 22, 20))


def bare_tree(c, rng, x, y, h, col, width=2.2, depth=5, angle=-90.0):
    def branch(x0, y0, length, ang, w, d):
        x1 = x0 + length * math.cos(math.radians(ang))
        y1 = y0 + length * math.sin(math.radians(ang))
        c.line([(x0, y0), (x1, y1)], col, w)
        if d > 0:
            for da in (rng.uniform(-38, -18), rng.uniform(15, 36)):
                branch(x1, y1, length * rng.uniform(0.62, 0.78), ang + da, max(0.35, w * 0.62), d - 1)
    branch(x, y, h * 0.4, angle, width, depth)


# =========================================================== chapter cards

def card_prologue():
    """Police HQ at night: the parcel under a desk lamp, Tokyo lights behind the blinds."""
    rng = random.Random(21)
    c = Canvas(240, 160)
    c.vgrad(0, 0, 240, 160, [(0, (14, 16, 30)), (1, (8, 8, 16))])
    # window with city at night
    c.rect(28, 10, 212, 92, (10, 16, 40))
    c.vgrad(30, 12, 210, 90, [(0, (12, 18, 50)), (0.7, (40, 34, 80)), (1, (90, 50, 80))])
    for i in range(26):
        x = 30 + i * 7 + rng.uniform(-2, 2)
        h = rng.uniform(18, 58)
        col = lerp((22, 24, 48), (36, 34, 64), rng.random())
        c.rect(x, 90 - h, x + rng.uniform(5, 9), 90, col)
        for wy in range(int(90 - h + 3), 88, 4):
            for wx in range(int(x + 1), int(x + 6), 3):
                if rng.random() < 0.35:
                    c.rect(wx, wy, wx + 1.4, wy + 1.6, rng.choice([(255, 220, 140), (240, 240, 200), (170, 200, 255)]))
    c.rect(96, 16, 100, 60, (200, 60, 60))  # Tokyo tower-ish red mast
    c.poly([(90, 60), (106, 60), (98, 22)], (170, 50, 50))
    c.glow(98, 20, 6, (255, 80, 60), 0.9)
    # blinds
    for y in range(12, 90, 4):
        c.rect(30, y, 210, y + 1.4, (40, 42, 58))
    c.rect(28, 10, 212, 92, None, (70, 70, 80), 2)
    c.rect(118, 10, 122, 92, (60, 60, 70))
    # desk
    c.poly([(0, 118), (240, 118), (240, 160), (0, 160)], (40, 26, 20))
    c.poly([(0, 110), (240, 110), (240, 120), (0, 120)], (70, 44, 30))
    # lamp and its pool of light
    lay, d = c.layer()
    d.polygon(c.pts([(168, 64), (184, 64), (236, 124), (104, 124)]), fill=(255, 220, 150, 70))
    c.put(lay, blur=3)
    c.glow(170, 116, 60, (130, 100, 50), 0.8)
    c.line([(196, 118), (192, 76), (178, 62)], (30, 30, 34), 2.2)
    c.poly([(164, 66), (186, 56), (192, 64), (172, 72)], (40, 90, 60))
    c.glow(176, 68, 6, (255, 240, 200), 0.9)
    c.ell(186, 114, 204, 120, (30, 30, 34))
    # the box with the marionette
    c.poly([(118, 98), (170, 98), (178, 118), (110, 118)], (150, 104, 60))
    c.poly([(118, 98), (170, 98), (166, 92), (122, 92)], (186, 140, 90))
    c.poly([(110, 118), (178, 118), (178, 124), (110, 124)], (110, 72, 40))
    # the twisted marionette lying in the box
    marionette(c, 142, 60, 0.95)
    # rose
    c.line([(154, 110), (170, 104)], (40, 90, 40), 1)
    c.ell(166, 99, 174, 107, (200, 16, 36))
    c.arc((167, 100, 173, 106), 200, 20, (255, 90, 100), 0.6)
    c.glow(170, 103, 8, (120, 0, 10), 0.6)
    # letter
    c.poly([(80, 112), (108, 108), (110, 120), (82, 124)], (236, 230, 214))
    c.line([(84, 115), (104, 112)], (120, 30, 40), 0.6)
    c.line([(85, 118), (100, 116)], (120, 30, 40), 0.6)
    c.vignette(0.65)
    return c.finish()


def viaduct_train(c, rng, rail_y, lit=True):
    # viaduct
    c.rect(0, rail_y, 240, rail_y + 4, (34, 30, 48))
    for x in range(-10, 250, 26):
        c.rect(x, rail_y + 4, x + 24, 160, (30, 28, 44))
        c.ell(x + 3, rail_y + 14, x + 21, rail_y + 46, (14, 16, 30))
        c.rect(x + 3, rail_y + 30, x + 21, 160, (14, 16, 30))
    c.rect(0, rail_y - 1, 240, rail_y + 0.6, (140, 150, 180))
    # the train (engine on the left, heading left)
    cars = [(40, 86), (92, 138), (140, 186), (188, 234)]
    for x0, x1 in cars:
        c.rrect(x0, rail_y - 20, x1, rail_y - 2, 2.5, (70, 90, 150))
        c.rect(x0, rail_y - 20, x1, rail_y - 17.5, (190, 200, 220))       # silver roof
        c.rect(x0, rail_y - 6, x1, rail_y - 4.8, (230, 200, 90))          # gold stripe
        c.rect(x0, rail_y - 4.8, x1, rail_y - 2, (40, 50, 90))
        for wx in range(int(x0) + 4, int(x1) - 5, 7):
            col = (255, 226, 150) if lit else (60, 70, 100)
            c.rect(wx, rail_y - 15, wx + 5, rail_y - 9, col)
        for wx in (x0 + 8, x1 - 8):
            c.ell(wx - 2.4, rail_y - 3.4, wx + 2.4, rail_y + 1.4, (24, 24, 30))
    # locomotive
    c.poly([(6, rail_y - 2), (8, rail_y - 16), (16, rail_y - 21), (38, rail_y - 21), (38, rail_y - 2)], (150, 30, 50))
    c.rect(8, rail_y - 6, 38, rail_y - 4.8, (230, 200, 90))
    c.poly([(10, rail_y - 15), (16, rail_y - 19), (22, rail_y - 19), (22, rail_y - 13), (10, rail_y - 12)], (170, 210, 240))
    c.rect(26, rail_y - 24, 30, rail_y - 21, (40, 40, 50))  # pantograph
    c.line([(24, rail_y - 28), (34, rail_y - 24)], (40, 40, 50), 0.8)
    for wx in (14, 30):
        c.ell(wx - 2.4, rail_y - 3.4, wx + 2.4, rail_y + 1.4, (24, 24, 30))
    if lit:
        for x0, x1 in cars:
            for wx in range(int(x0) + 4, int(x1) - 5, 7):
                c.glow(wx + 2.5, rail_y - 12, 7, (120, 90, 30), 0.5)
        # headlight beam
        lay, d = c.layer()
        d.polygon(c.pts([(8, rail_y - 9), (8, rail_y - 5), (-60, rail_y + 12), (-60, rail_y - 30)]),
                  fill=(255, 250, 210, 110))
        c.put(lay, blur=2.5)
        c.glow(8, rail_y - 7, 10, (255, 250, 220), 1.0)


def card_ch1():
    """The Silver Star night train crossing a viaduct under the moon, snow falling."""
    rng = random.Random(31)
    c = Canvas(240, 160)
    c.vgrad(0, 0, 240, 160, [(0, (6, 8, 26)), (0.55, (34, 36, 86)), (1, (70, 60, 110))])
    stars(c, rng, 120, 80)
    moon(c, 188, 24, 10)
    mountains(c, rng, 72, 36, (70, 76, 118), (200, 206, 236), 0.7)
    mountains(c, rng, 86, 20, (40, 46, 80), (150, 160, 200), 2.1)
    for i in range(18):  # snowy pines on the slope
        x = rng.uniform(0, 240)
        pine(c, x, 88 + rng.uniform(-3, 3), rng.uniform(8, 12), (18, 30, 40), (170, 180, 210))
    viaduct_train(c, rng, 98)
    c.snow(rng, 420, big=0.25)
    c.vignette(0.5)
    return c.finish()


def hotel_building(c, rng, x0, y0, lit_frac=0.5):
    """An old western-style hotel: gabled roofs, rows of windows."""
    x1 = x0 + 110
    c.poly([(x0 - 4, y0), (x0 + 30, y0 - 22), (x0 + 64, y0)], (46, 36, 50))
    c.poly([(x0 + 50, y0), (x0 + 84, y0 - 26), (x1 + 4, y0)], (52, 40, 56))
    c.rect(x0 + 24, y0 - 34, x0 + 30, y0 - 18, (40, 32, 44))  # chimney
    c.rect(x0, y0, x1, y0 + 56, (72, 60, 70))
    c.rect(x0, y0, x1, y0 + 2, (120, 110, 130))
    for row in range(3):
        for col in range(9):
            wx, wy = x0 + 6 + col * 11.5, y0 + 8 + row * 15
            lit = rng.random() < lit_frac
            c.rect(wx, wy, wx + 6, wy + 9, (255, 214, 130) if lit else (30, 30, 50))
            c.rect(wx + 2.6, wy, wx + 3.4, wy + 9, (60, 44, 40))
            if lit:
                c.glow(wx + 3, wy + 4.5, 8, (110, 80, 30), 0.5)
    c.rect(x0 + 48, y0 + 40, x0 + 62, y0 + 56, (255, 200, 120))  # entrance
    c.glow(x0 + 55, y0 + 48, 18, (140, 90, 30), 0.7)


def card_ch2():
    """The hotel standing in the middle of a foggy swamp, the theatre beside it."""
    rng = random.Random(41)
    c = Canvas(240, 160)
    c.vgrad(0, 0, 240, 160, [(0, (10, 14, 26)), (0.6, (40, 52, 66)), (1, (22, 30, 34))])
    stars(c, rng, 50, 50)
    moon(c, 40, 26, 8)
    mountains(c, rng, 80, 20, (18, 24, 34), (90, 100, 120), 1.3)
    # theatre (dome and marquee)
    c.rect(168, 56, 226, 100, (60, 44, 44))
    c.ell(176, 40, 218, 70, (70, 52, 52))
    c.rect(174, 60, 220, 64, (200, 160, 70))
    for x in range(176, 220, 6):
        c.ell(x, 61, x + 2, 63, (255, 240, 170))
    c.glow(197, 62, 22, (120, 80, 20), 0.5)
    c.rect(188, 80, 206, 100, (30, 18, 18))
    hotel_building(c, rng, 40, 50, 0.45)
    # swamp water with reflections
    c.vgrad(0, 104, 240, 160, [(0, (30, 40, 44)), (1, (12, 16, 18))])
    lay, d = c.layer()
    for _ in range(90):
        x, y = rng.uniform(0, 240), rng.uniform(106, 160)
        w = rng.uniform(4, 18)
        d.line(c.pts([(x, y), (x + w, y)]), fill=(255, 210, 130, rng.randrange(20, 90)) if 40 < x < 160
               else (150, 170, 190, rng.randrange(20, 70)), width=S // 2)
    c.put(lay)
    for x in (12, 150, 232):
        bare_tree(c, rng, x, 118, 80, (14, 16, 18), 2.4)
    for _ in range(26):  # reeds
        x = rng.uniform(0, 240)
        c.line([(x, 160), (x + rng.uniform(-3, 3), 140 - rng.uniform(0, 14))], (20, 26, 20), 0.6)
    # fog banks
    lay, d = c.layer()
    for _ in range(22):
        x, y = rng.uniform(-20, 240), rng.uniform(84, 130)
        d.ellipse(c.pts([(x, y), (x + rng.uniform(40, 90), y + rng.uniform(6, 14))]), fill=(190, 200, 210, 60))
    c.put(lay, blur=4)
    c.vignette(0.55)
    return c.finish()


def card_ch3():
    """Blizzard night: a lit hotel window, a snow-laden tree with a rope hanging from a branch."""
    rng = random.Random(51)
    c = Canvas(240, 160)
    c.vgrad(0, 0, 240, 160, [(0, (20, 24, 44)), (1, (60, 66, 96))])
    # hotel wall (right side, receding)
    c.poly([(120, 0), (240, 0), (240, 160), (120, 160)], (70, 62, 74))
    for y in range(0, 160, 8):  # siding boards
        c.line([(120, y), (240, y)], (60, 52, 64), 0.8)
    for wy in (14, 76):
        for wx in (140, 196):
            lit = (wx, wy) == (140, 14)
            c.rect(wx - 2, wy - 2, wx + 30, wy + 42, (44, 34, 40))
            c.rect(wx, wy, wx + 28, wy + 40, (255, 210, 130) if lit else (24, 26, 44))
            c.rect(wx + 13, wy, wx + 15, wy + 40, (44, 34, 40))
            c.rect(wx, wy + 19, wx + 28, wy + 21, (44, 34, 40))
            c.rect(wx - 4, wy + 40, wx + 32, wy + 43, (230, 234, 244))  # snow on sill
            if lit:
                c.glow(wx + 14, wy + 20, 44, (130, 90, 30), 0.8)
    # jade stones glinting on the lit sill
    for i, jx in enumerate((144, 150, 156)):
        c.ell(jx, 48, jx + 5, 52, (40, 170, 110), (20, 80, 50), 0.4)
        c.ell(jx + 1, 48.6, jx + 2.4, 49.8, (200, 255, 220))
    # the tree
    c.line([(40, 160), (46, 90), (60, 40)], (26, 22, 26), 7)
    bare_tree(c, rng, 58, 50, 70, (26, 22, 26), 3.2, depth=4, angle=-70)
    c.line([(46, 92), (110, 64), (128, 60)], (26, 22, 26), 3.2)  # the branch toward the window
    for (x0, y0), (x1, y1) in [((46, 92), (110, 64)), ((110, 64), (128, 60))]:
        c.poly([(x0, y0 - 1.6), (x1, y1 - 1.6), (x1, y1 - 3.2), (x0, y0 - 3.6)], (236, 240, 250))
    # rope hanging from the branch, swaying
    c.line([(96, 70), (97, 100), (95, 118)], (200, 180, 140), 0.9)
    c.ell(92, 116, 98.5, 123, None, (200, 180, 140), 0.8)
    # snow ground
    c.poly([(0, 140), (120, 132), (120, 160), (0, 160)], (210, 216, 236))
    lay, d = c.layer()
    for _ in range(260):  # diagonal blizzard streaks
        x, y = rng.uniform(-40, 240), rng.uniform(0, 160)
        l = rng.uniform(3, 9)
        d.line(c.pts([(x, y), (x + l, y + l * 0.45)]), fill=(240, 244, 255, rng.randrange(90, 200)), width=S // 2)
    c.put(lay, blur=0.2)
    c.snow(rng, 260, big=0.3)
    c.vignette(0.6)
    return c.finish()


def card_final():
    """An empty stage in a spotlight: the clown's card and scattered roses."""
    rng = random.Random(61)
    c = Canvas(240, 160)
    c.rect(0, 0, 240, 160, (10, 4, 8))
    # curtains
    for side in (0, 1):
        for i in range(9):
            x = i * 9 if side == 0 else 240 - (i + 1) * 9
            col = lerp((120, 10, 30), (60, 0, 14), (i % 2) * 0.6)
            c.rect(x, 0, x + 9, 160, col)
            c.rect(x + 6 if side == 0 else x, 0, x + 9 if side == 0 else x + 3, 160, lerp(col, (0, 0, 0), 0.35))
    c.rect(0, 0, 240, 18, (140, 16, 36))
    for x in range(0, 240, 12):
        c.ell(x, 10, x + 12, 26, (140, 16, 36))
    c.rect(0, 0, 240, 3, (220, 180, 80))
    # stage floor
    c.poly([(0, 112), (240, 112), (240, 160), (0, 160)], (50, 30, 24))
    for x in range(-100, 340, 18):
        c.line([(120 + (x - 120) * 0.45, 112), (x, 160)], (38, 22, 18), 0.6)
    # spotlight
    lay, d = c.layer()
    d.polygon(c.pts([(110, 0), (130, 0), (170, 136), (70, 136)]), fill=(255, 245, 220, 70))
    c.put(lay, blur=4)
    lay, d = c.layer()
    d.ellipse(c.pts([(66, 126), (174, 146)]), fill=(255, 245, 225, 110))
    c.put(lay, blur=2)
    # marionette chair
    c.rect(104, 88, 136, 92, (120, 64, 30))
    c.rect(106, 92, 110, 118, (100, 50, 24))
    c.rect(130, 92, 134, 118, (100, 50, 24))
    c.rect(106, 64, 134, 70, (120, 64, 30))
    c.rect(106, 64, 110, 92, (120, 64, 30))
    c.rect(130, 64, 134, 92, (120, 64, 30))
    # strings from above, cut
    for x in (112, 128):
        c.line([(x, 0), (x, 56 + (x % 7))], (200, 200, 210), 0.35)
    # the joker card on the seat
    c.poly([(112, 84), (128, 80), (131, 88), (115, 92)], (246, 242, 232))
    c.line([(118, 85), (125, 83)], (180, 20, 40), 0.8)
    c.ell(119, 85.5, 122, 88, (40, 30, 60))
    # roses
    for _ in range(14):
        x, y = rng.uniform(80, 160), rng.uniform(124, 146)
        r = rng.uniform(2, 3.2)
        c.ell(x - r, y - r * 0.7, x + r, y + r * 0.7, (190, 10, 34))
        c.arc((x - r * 0.6, y - r * 0.5, x + r * 0.6, y + r * 0.5), 180, 20, (255, 80, 100), 0.4)
    c.glow(120, 136, 50, (60, 40, 20), 0.6)
    c.vignette(0.6)
    return c.finish()


# =========================================================== backgrounds

def scene_police():
    """Metropolitan Police office in daylight."""
    rng = random.Random(71)
    c = Canvas(240, 160)
    # back wall
    c.vgrad(0, 0, 240, 104, [(0, (196, 200, 192)), (1, (166, 170, 162))])
    c.rect(0, 96, 240, 104, (120, 116, 104))
    # big window with blinds and city
    c.vgrad(16, 8, 152, 82, [(0, (130, 170, 220)), (1, (200, 220, 236))])
    for i in range(20):
        x = 16 + i * 7
        h = rng.uniform(16, 50)
        c.rect(x, 82 - h, x + rng.uniform(5, 8), 82, lerp((110, 130, 160), (150, 160, 180), rng.random()))
        for wy in range(int(82 - h + 3), 80, 4):
            c.rect(x + 1, wy, x + 5, wy + 1.2, (190, 206, 226))
    for y in range(8, 60, 4):  # half-raised blinds
        c.rect(16, y, 152, y + 2.4, (226, 228, 222))
        c.rect(16, y + 2.4, 152, y + 3, (170, 172, 168))
    c.rect(14, 6, 154, 84, None, (110, 110, 112), 2)
    c.rect(82, 6, 86, 84, (110, 110, 112))
    # light on the floor
    lay, d = c.layer()
    d.polygon(c.pts([(16, 84), (152, 84), (200, 160), (-30, 160)]), fill=(255, 250, 220, 40))
    c.put(lay, blur=3)
    # filing cabinets and a notice board
    for i, x in enumerate((166, 196)):
        c.rect(x, 34, x + 28, 104, (120, 124, 120))
        c.rect(x + 27, 34, x + 28, 104, (90, 92, 90))
        for y in range(40, 104, 16):
            c.rect(x + 2, y, x + 26, y + 13, (140, 144, 140), (100, 104, 100), 0.6)
            c.rect(x + 11, y + 5, x + 17, y + 7, (200, 200, 190))
    c.rect(166, 8, 224, 30, (160, 120, 80), (110, 80, 50), 1)
    for _ in range(9):
        x, y = rng.uniform(168, 216), rng.uniform(10, 24)
        c.rect(x, y, x + rng.uniform(6, 10), y + rng.uniform(5, 8), rng.choice([(250, 250, 240), (250, 240, 170), (230, 240, 250)]))
        c.ell(x + 2, y, x + 3.4, y + 1.4, (200, 30, 30))
    # floor
    c.vgrad(0, 104, 240, 160, [(0, (110, 112, 118)), (1, (80, 82, 90))])
    for x in range(-200, 440, 22):
        c.line([(120 + (x - 120) * 0.35, 104), (x, 160)], (96, 98, 104), 0.6)
    # desk in the foreground
    c.poly([(20, 118), (220, 118), (236, 142), (4, 142)], (130, 92, 60))
    c.poly([(4, 142), (236, 142), (236, 150), (4, 150)], (96, 64, 40))
    c.rect(12, 150, 22, 160, (70, 46, 30))
    c.rect(218, 150, 228, 160, (70, 46, 30))
    # things on the desk: papers, phone, mug, the parcel with the marionette
    c.poly([(30, 124), (62, 122), (64, 134), (32, 136)], (244, 242, 234))
    c.poly([(34, 121), (64, 119), (66, 131), (36, 133)], (250, 250, 244))
    for i in range(4):
        c.line([(38, 124 + i * 2.4), (58, 122.5 + i * 2.4)], (150, 150, 160), 0.4)
    c.rrect(176, 120, 196, 132, 2, (40, 40, 44))
    c.rrect(178, 116, 194, 121, 2, (30, 30, 34))
    c.rrect(204, 120, 212, 132, 1.5, (236, 236, 236))
    c.arc((210, 122, 216, 129), 270, 90, (236, 236, 236), 1)
    # parcel
    c.poly([(90, 112), (150, 112), (160, 136), (82, 136)], (170, 124, 76))
    c.poly([(90, 112), (150, 112), (144, 104), (96, 104)], (200, 156, 104))
    c.poly([(82, 136), (160, 136), (160, 141), (82, 141)], (130, 90, 50))
    marionette(c, 120, 78, 0.8)
    c.line([(140, 124), (152, 120)], (40, 100, 40), 1)
    c.ell(148, 115, 156, 123, (210, 20, 40))
    c.arc((149, 116, 155, 122), 200, 20, (255, 110, 120), 0.6)
    return c.finish()


def scene_hotel_room():
    """A room in the swamp hotel at night: snow and the tree outside, jade on the dresser."""
    rng = random.Random(81)
    c = Canvas(240, 160)
    # wallpaper with a damask-ish pattern
    c.vgrad(0, 0, 240, 116, [(0, (150, 120, 100)), (1, (120, 92, 76))])
    for y in range(6, 116, 14):
        for x in range(4 + (y // 14 % 2) * 8, 240, 16):
            c.poly([(x, y - 3), (x + 2.2, y), (x, y + 3), (x - 2.2, y)], (136, 106, 88))
    c.rect(0, 0, 240, 5, (90, 60, 44))
    c.rect(0, 104, 240, 116, (100, 66, 46))
    c.rect(0, 104, 240, 105.5, (150, 110, 70))
    # window with the snowy tree (painted separately, then set into the wall)
    wx0, wy0, wx1, wy1 = 70, 12, 170, 92
    v = Canvas(wx1 - wx0, wy1 - wy0)
    v.vgrad(0, 0, v.w, v.h, [(0, (12, 16, 40)), (1, (46, 54, 96))])
    stars(v, rng, 20, 30)
    mountains(v, rng, v.h - 4, 8, (70, 80, 120), (170, 180, 210), 0.3)
    v.line([(26, 80), (30, 48), (40, 24), (70, 6)], (22, 20, 26), 3.4)
    v.line([(30, 48), (12, 32)], (22, 20, 26), 1.6)
    v.line([(40, 24), (30, 8)], (22, 20, 26), 1.2)
    v.line([(34, 38), (90, 32)], (22, 20, 26), 2)
    v.line([(34, 36.4), (90, 30.4)], (230, 236, 250), 0.8)
    v.snow(rng, 180, big=0.3)
    c.img.paste(v.img, (c.p(wx0), c.p(wy0)))
    c.d = ImageDraw.Draw(c.img)
    # frame, curtains
    c.rect(wx0 - 3, wy0 - 3, wx1 + 3, wy1 + 3, None, (90, 60, 40), 3)
    c.rect(118.5, wy0, 121.5, wy1, (90, 60, 40))
    c.rect(wx0, 51, wx1, 53, (90, 60, 40))
    c.rect(wx0 - 8, wy1 + 2, wx1 + 8, wy1 + 6, (110, 76, 50))
    for side in (0, 1):
        x0 = wx0 - 22 if side == 0 else wx1 + 4
        for i in range(4):
            col = lerp((140, 30, 40), (100, 16, 26), i % 2)
            c.rect(x0 + i * 4.5, 6, x0 + i * 4.5 + 4.5, 100, col)
    c.rect(wx0 - 26, 4, wx1 + 26, 8, (170, 130, 70))
    # wall lamp glow
    c.ell(24, 30, 36, 44, (255, 230, 170))
    c.rect(28, 44, 32, 50, (170, 130, 70))
    c.glow(30, 38, 50, (120, 80, 30), 0.8)
    # floor carpet
    c.vgrad(0, 116, 240, 160, [(0, (110, 30, 34)), (1, (70, 16, 22))])
    for x in range(-100, 340, 20):
        c.line([(120 + (x - 120) * 0.4, 116), (x, 160)], (96, 24, 30), 0.6)
    c.rect(20, 124, 220, 126, (190, 150, 70))
    # bed (right)
    c.poly([(176, 90), (240, 90), (240, 150), (160, 150)], (236, 232, 222))
    c.poly([(160, 150), (240, 150), (240, 158), (160, 158)], (120, 80, 56))
    c.rect(222, 70, 240, 150, (110, 72, 50))
    c.rrect(184, 92, 214, 104, 4, (250, 250, 246))
    c.poly([(166, 112), (240, 112), (240, 150), (160, 150)], (150, 40, 50))
    # dresser (left) with three jade stones and a mirror
    c.rect(6, 76, 58, 132, (110, 72, 48))
    c.rect(6, 76, 58, 80, (150, 104, 66))
    for y in (86, 102, 118):
        c.rect(10, y, 54, y + 12, (120, 80, 54), (80, 50, 34), 0.6)
        c.rect(29, y + 5, 35, y + 7, (210, 180, 90))
    c.rect(14, 52, 50, 76, (180, 190, 200), (150, 104, 66), 2)
    lay, d = c.layer()
    d.line(c.pts([(20, 72), (34, 54)]), fill=(255, 255, 255, 120), width=S)
    c.put(lay)
    for jx in (14, 27, 40):
        c.ell(jx, 69, jx + 9, 76.5, (46, 170, 110), (20, 90, 56), 0.5)
        c.ell(jx + 2, 70, jx + 4.2, 71.8, (210, 255, 230))
    c.vignette(0.4)
    return c.finish()


def smoke_over(base, seed=11):
    """Thick white smoke filling a room (for Saki's video)."""
    rng = random.Random(seed)
    c = Canvas(base.width, base.height)
    c.img = base.convert("RGB").resize((base.width * S, base.height * S), Image.LANCZOS)
    c.d = ImageDraw.Draw(c.img)
    for passes, (n, rmin, rmax, alpha, blur) in enumerate(((46, 30, 70, (150, 220), 9), (70, 14, 34, (70, 140), 5))):
        lay, d = c.layer()
        for _ in range(n):
            x, y = rng.uniform(-30, 250), rng.uniform(-20, 160)
            r = rng.uniform(rmin, rmax)
            g = rng.randrange(218, 248)
            d.ellipse(c.pts([(x - r, y - r * 0.6), (x + r, y + r * 0.6)]), fill=(g, g, min(255, g + 6), rng.randrange(*alpha)))
        c.put(lay, blur=blur)
    # a vent at the top still pouring smoke
    c.glow(120, 0, 40, (40, 40, 40), 0.6)
    return c.finish()


# =========================================================== evidence icons (64x64, transparent)

def icon_canvas():
    return Canvas(64, 64, bg=(0, 0, 0), alpha=True)


def icon_done(c):
    img = c.img.resize((64, 64), Image.LANCZOS)
    # crisp alpha and a dark outline, like the rest of the UI
    a = np.asarray(img).copy()
    alpha = a[..., 3] > 110
    a[..., 3] = np.where(alpha, 255, 0)
    out = alpha.copy()
    grown = alpha.copy()
    for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)):
        grown |= np.roll(np.roll(alpha, dy, 0), dx, 1)
    edge = grown & ~out
    a[edge] = (28, 20, 32, 255)
    return Image.fromarray(a, "RGBA")


def shine(c, x, y, r=3):
    c.ell(x - r, y - r * 0.6, x + r, y + r * 0.6, (255, 255, 255, 200))


def i_letter():
    c = icon_canvas()
    c.poly([(6, 18), (58, 14), (60, 50), (8, 54)], (236, 228, 208, 255))
    c.poly([(6, 18), (58, 14), (33, 36)], (250, 246, 232, 255))
    c.line([(6, 18), (33, 36), (58, 14)], (190, 176, 150, 255), 0.8)
    c.ell(27, 30, 39, 42, (170, 16, 30, 255))
    c.ell(30, 33, 34, 36, (230, 80, 90, 255))
    c.line([(12, 48), (30, 46)], (120, 20, 30, 255), 0.8)
    return icon_done(c)


def i_paper():
    c = icon_canvas()
    c.poly([(14, 6), (46, 6), (54, 14), (54, 58), (14, 58)], (246, 244, 236, 255))
    c.poly([(46, 6), (54, 14), (46, 14)], (210, 206, 196, 255))
    c.rect(14, 54, 54, 58, (220, 216, 206, 255))
    for i in range(7):
        c.line([(19, 18 + i * 5), (49 - (i % 3) * 6, 18 + i * 5)], (90, 90, 110, 255), 0.9)
    c.rect(19, 12, 34, 14.5, (180, 30, 40, 255))
    return icon_done(c)


def i_train():
    c = icon_canvas()
    c.rrect(4, 18, 60, 48, 6, (70, 90, 150, 255))
    c.rect(4, 18, 60, 24, (190, 200, 220, 255))
    c.rect(4, 40, 60, 43, (230, 200, 90, 255))
    for x in (10, 24, 38):
        c.rect(x, 28, x + 10, 36, (255, 226, 150, 255))
    c.rect(50, 28, 56, 40, (40, 50, 90, 255))
    for x in (14, 48):
        c.ell(x - 5, 44, x + 5, 54, (30, 30, 36, 255))
        c.ell(x - 2, 47, x + 2, 51, (140, 140, 150, 255))
    shine(c, 16, 22, 5)
    return icon_done(c)


def i_rose():
    c = icon_canvas()
    c.line([(32, 60), (30, 36)], (40, 110, 40, 255), 2.4)
    c.poly([(31, 46), (18, 40), (22, 50)], (50, 140, 50, 255))
    c.poly([(31, 52), (44, 46), (40, 56)], (50, 140, 50, 255))
    c.ell(16, 6, 48, 36, (180, 10, 30, 255))
    c.ell(20, 8, 44, 30, (210, 20, 44, 255))
    c.arc((22, 10, 42, 28), 180, 360, (255, 90, 110, 255), 1.4)
    c.arc((26, 14, 38, 26), 0, 200, (140, 0, 20, 255), 1.4)
    c.arc((28, 16, 36, 24), 180, 360, (255, 120, 140, 255), 1)
    return icon_done(c)


def i_balloon():
    c = icon_canvas()
    c.line([(34, 40), (30, 50), (36, 60)], (220, 220, 230, 255), 0.8)
    c.ell(14, 4, 50, 42, (220, 40, 60, 255))
    c.ell(18, 6, 46, 36, (240, 70, 90, 255))
    shine(c, 24, 14, 4)
    c.poly([(30, 42), (34, 42), (32, 38)], (190, 30, 50, 255))
    # torn pieces
    for x, y in ((8, 52), (46, 54), (52, 46)):
        c.poly([(x, y), (x + 6, y + 1), (x + 3, y + 5)], (200, 40, 60, 255))
    return icon_done(c)


def i_window():
    c = icon_canvas()
    c.rect(8, 6, 56, 58, (110, 80, 56, 255))
    c.vgrad(12, 10, 52, 54, [(0, (20, 26, 60)), (1, (60, 70, 120))])
    for x, y in ((18, 16), (40, 22), (26, 36), (46, 44), (16, 46)):
        c.ell(x, y, x + 1.6, y + 1.6, (240, 244, 255, 255))
    c.rect(12, 30, 52, 34, (110, 80, 56, 255))
    c.rect(12, 40, 52, 54, (180, 200, 220, 160))   # lower pane raised 10cm
    c.line([(6, 34), (58, 34)], (230, 200, 90, 255), 1)
    return icon_done(c)


def i_chain():
    c = icon_canvas()
    c.rect(4, 36, 60, 48, (140, 110, 80, 255))
    c.rect(4, 36, 60, 38, (180, 150, 110, 255))
    for x in range(10, 56, 6):  # scrape marks
        c.line([(x, 38), (x + 4, 46)], (90, 60, 40, 255), 1)
    c.line([(20, 4), (28, 36)], (220, 200, 150, 255), 1.6)   # string
    c.line([(44, 4), (36, 36)], (220, 200, 150, 255), 1.6)
    return icon_done(c)


def i_camera():
    c = icon_canvas()
    c.rrect(6, 18, 46, 48, 5, (50, 50, 58, 255))
    c.rrect(6, 18, 46, 24, 3, (80, 80, 90, 255))
    c.poly([(46, 24), (60, 18), (60, 48), (46, 42)], (40, 40, 46, 255))
    c.ell(14, 26, 34, 46, (20, 20, 26, 255))
    c.ell(18, 30, 30, 42, (60, 80, 140, 255))
    shine(c, 22, 33, 2.4)
    c.ell(38, 22, 43, 27, (230, 40, 40, 255))
    c.rect(10, 12, 26, 18, (70, 70, 80, 255))
    return icon_done(c)


def i_bag():
    c = icon_canvas()
    c.arc((20, 6, 44, 30), 180, 360, (70, 40, 24, 255), 3)
    c.rrect(6, 18, 58, 56, 5, (130, 76, 40, 255))
    c.rrect(6, 18, 58, 28, 4, (160, 96, 52, 255))
    c.rect(6, 44, 58, 47, (90, 50, 28, 255))   # the false bottom seam
    c.rect(28, 24, 36, 32, (220, 190, 90, 255))
    shine(c, 16, 22, 4)
    return icon_done(c)


def i_mask():
    c = icon_canvas()
    c.rect(10, 6, 54, 14, (40, 40, 44, 255))       # top hat brim / crown
    c.rect(18, 0, 46, 8, (40, 40, 44, 255))
    c.rect(18, 5, 46, 7, (160, 20, 40, 255))
    c.ell(12, 12, 52, 60, (246, 244, 240, 255))
    c.ell(20, 28, 30, 36, (20, 20, 26, 255))
    c.ell(34, 28, 44, 36, (20, 20, 26, 255))
    c.arc((22, 40, 42, 52), 20, 160, (160, 20, 40, 255), 1.6)
    c.line([(25, 38), (25, 46)], (60, 120, 200, 255), 1)  # painted tear
    return icon_done(c)


def i_clipboard():
    c = icon_canvas()
    c.rrect(10, 8, 54, 60, 3, (150, 104, 60, 255))
    c.rect(14, 14, 50, 56, (248, 246, 238, 255))
    c.rrect(22, 4, 42, 14, 2, (170, 170, 180, 255))
    for i in range(6):
        c.line([(18, 22 + i * 5.5), (46 - (i % 2) * 8, 22 + i * 5.5)], (90, 90, 110, 255), 0.9)
    c.line([(18, 44), (30, 44)], (190, 30, 40, 255), 1.2)
    return icon_done(c)


def i_clock():
    c = icon_canvas()
    c.ell(8, 10, 56, 58, (210, 170, 70, 255))
    c.ell(12, 14, 52, 54, (250, 248, 240, 255))
    c.rect(28, 4, 36, 10, (210, 170, 70, 255))
    for k in range(12):
        a = math.radians(k * 30)
        c.line([(32 + 16 * math.cos(a), 34 + 16 * math.sin(a)), (32 + 18 * math.cos(a), 34 + 18 * math.sin(a))],
               (60, 60, 70, 255), 1)
    c.line([(32, 34), (32, 22)], (30, 30, 40, 255), 1.6)
    c.line([(32, 34), (42, 38)], (30, 30, 40, 255), 1.4)
    c.line([(32, 34), (24, 44)], (200, 20, 30, 255), 0.7)
    shine(c, 22, 22, 3)
    return icon_done(c)


def i_rope():
    c = icon_canvas()
    c.rect(4, 2, 60, 8, (90, 70, 60, 255))
    c.ell(24, 2, 40, 18, (170, 170, 180, 255))   # pulley
    c.ell(29, 7, 35, 13, (90, 90, 100, 255))
    for x0 in (25, 39):
        pts = [(x0, 10 + i * 4) for i in range(13)]
        c.line(pts, (210, 180, 130, 255), 2.2)
        for i in range(12):
            c.line([(x0 - 1, 12 + i * 4), (x0 + 1, 14 + i * 4)], (150, 120, 80, 255), 0.6)
    c.rrect(33, 50, 45, 62, 2, (170, 30, 40, 255))   # the puppet weight
    return icon_done(c)


def i_scale():
    c = icon_canvas()
    c.poly([(12, 56), (52, 56), (48, 32), (16, 32)], (220, 220, 224, 255))
    c.rect(10, 54, 54, 60, (150, 150, 160, 255))
    c.ell(22, 12, 42, 32, (250, 250, 246, 255), (120, 120, 130, 255), 1.2)
    c.line([(32, 22), (38, 16)], (200, 20, 30, 255), 1)
    for k in range(9):
        a = math.radians(200 + k * 17.5)
        c.line([(32 + 8 * math.cos(a), 22 + 8 * math.sin(a)), (32 + 9.6 * math.cos(a), 22 + 9.6 * math.sin(a))],
               (60, 60, 70, 255), 0.6)
    return icon_done(c)


def i_nail():
    c = icon_canvas()
    c.rect(4, 40, 60, 60, (120, 80, 50, 255))
    c.rect(4, 40, 60, 43, (150, 104, 66, 255))
    for (x, y, rust) in ((14, 8, True), (40, 12, False)):
        col = (150, 90, 60, 255) if rust else (200, 204, 214, 255)
        c.ell(x - 6, y - 2, x + 6, y + 3, col)
        c.poly([(x - 2, y + 2), (x + 2, y + 2), (x + 0.6, y + 34), (x - 0.6, y + 34)], col)
        if not rust:
            c.line([(x - 1, y + 4), (x - 1, y + 28)], (250, 250, 255, 255), 0.6)
    return icon_done(c)


def i_jade():
    c = icon_canvas()
    for (x, y, s) in ((6, 30, 1.0), (30, 36, 0.8), (22, 12, 0.9)):
        w, h = 26 * s, 20 * s
        c.poly([(x, y + h * 0.4), (x + w * 0.3, y), (x + w * 0.8, y + h * 0.05), (x + w, y + h * 0.5),
                (x + w * 0.7, y + h), (x + w * 0.2, y + h * 0.9)], (40, 160, 100, 255))
        c.poly([(x + w * 0.3, y), (x + w * 0.8, y + h * 0.05), (x + w * 0.55, y + h * 0.45)], (110, 220, 160, 255))
        shine(c, x + w * 0.35, y + h * 0.3, 2.2)
    return icon_done(c)


def i_talk():
    c = icon_canvas()
    c.rrect(4, 8, 48, 38, 10, (250, 250, 246, 255))
    c.poly([(14, 36), (12, 48), (24, 37)], (250, 250, 246, 255))
    c.rrect(22, 26, 60, 52, 9, (250, 226, 150, 255))
    c.poly([(48, 50), (54, 60), (42, 51)], (250, 226, 150, 255))
    for x in (16, 26, 36):
        c.ell(x - 2, 21, x + 2, 25, (80, 80, 100, 255))
    return icon_done(c)


def i_puppet():
    c = icon_canvas()
    c.line([(20, 0), (22, 20)], (200, 200, 210, 255), 0.5)
    c.line([(44, 0), (42, 26)], (200, 200, 210, 255), 0.5)
    c.ell(22, 8, 38, 24, (244, 236, 226, 255))
    c.poly([(22, 10), (30, 0), (38, 10)], (60, 40, 110, 255))
    c.ell(26, 14, 28, 16, (30, 20, 20, 255))
    c.ell(32, 14, 34, 16, (30, 20, 20, 255))
    c.line([(27, 20), (33, 20)], (180, 20, 40, 255), 0.8)
    c.line([(30, 24), (22, 38), (40, 34), (28, 50)], (70, 50, 130, 255), 4)
    c.line([(32, 26), (44, 22), (46, 10)], (70, 50, 130, 255), 2.6)
    c.line([(26, 28), (14, 30), (10, 20)], (70, 50, 130, 255), 2.6)
    c.line([(30, 48), (20, 58)], (70, 50, 130, 255), 2.6)
    c.line([(30, 48), (44, 60)], (70, 50, 130, 255), 2.6)
    c.ell(44, 44, 54, 54, (200, 16, 36, 255))
    return icon_done(c)


ICONS_HD = {
    "letter": i_letter, "paper": i_paper, "train": i_train, "rose": i_rose, "balloon": i_balloon,
    "window": i_window, "chain": i_chain, "camera": i_camera, "bag": i_bag, "mask": i_mask,
    "clipboard": i_clipboard, "clock": i_clock, "rope": i_rope, "scale": i_scale, "nail": i_nail,
    "jade": i_jade, "talk": i_talk, "puppet": i_puppet,
}

CARDS = {
    "card_prologue": card_prologue, "card_ch1": card_ch1, "card_ch2": card_ch2, "card_ch3": card_ch3,
    "card_final": card_final,
}
