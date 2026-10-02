"""Title logo in the style of the series logo: black "소년 탐정", big red rough "김전일" with a blood
splash, yellow "~마술열차 살인사건~" with a red rim, everything on a white sticker outline.

If assets/cuts/logo.png exists (the real logo, not in git) it is used instead; its white
background is removed.
"""
import math
import os
import random

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
FONT = os.path.join(HERE, "..", "assets", "fonts", "NanumMyeongjo-ExtraBold.ttf")
USER_LOGO = os.path.join(HERE, "..", "assets", "cuts", "logo.png")
K = 4  # supersampling


def _text_mask(text, size, spacing=0):
    f = ImageFont.truetype(FONT, size * K)
    lines = text.split("\n")
    ws = [f.getlength(l) for l in lines]
    asc, desc = f.getmetrics()
    lh = asc + desc + spacing * K
    m = Image.new("L", (int(max(ws)) + 8 * K, lh * len(lines) + 8 * K), 0)
    d = ImageDraw.Draw(m)
    for i, l in enumerate(lines):
        d.text((4 * K + (max(ws) - ws[i]) / 2, 4 * K + i * lh), l, font=f, fill=255)
    return m.crop(m.getbbox())


def _rough(mask, amount, seed):
    """Brush-like ragged edges: only pixels near the outline are pushed in or out by smooth noise."""
    rng = np.random.default_rng(seed)
    blurred = np.asarray(mask.filter(ImageFilter.GaussianBlur(amount))).astype(np.float32)
    h, w = blurred.shape
    cell = 3 * K
    noise = rng.normal(0, 1, (h // cell + 2, w // cell + 2)).astype(np.float32)
    noise = np.asarray(Image.fromarray(noise).resize((w, h), Image.BICUBIC))
    edge = (blurred > 20) & (blurred < 235)
    val = np.where(edge, blurred + noise * 45, blurred)
    return Image.fromarray(((val > 128) * 255).astype(np.uint8))


def _grow(mask, px):
    return mask.filter(ImageFilter.MaxFilter(2 * px + 1)) if px > 0 else mask


def _splash(size, seed):
    rng = random.Random(seed)
    m = Image.new("L", (size * 3, size * 3), 0)
    d = ImageDraw.Draw(m)
    c = size * 1.5
    d.ellipse([c - size * 0.5, c - size * 0.5, c + size * 0.5, c + size * 0.5], fill=255)
    for i in range(16):  # spikes and droplets around the blot
        ang = i / 16 * 2 * math.pi + rng.uniform(-0.15, 0.15)
        r0, r1 = size * 0.45, size * rng.uniform(0.75, 1.3)
        wd = size * rng.uniform(0.07, 0.14)
        x0, y0 = c + r0 * math.cos(ang), c + r0 * math.sin(ang)
        x1, y1 = c + r1 * math.cos(ang), c + r1 * math.sin(ang)
        d.line([(x0, y0), (x1, y1)], fill=255, width=int(wd))
        dr = size * rng.uniform(0.06, 0.12)
        d.ellipse([x1 - dr, y1 - dr, x1 + dr, y1 + dr], fill=255)
    return m.crop(m.getbbox())


def _sticker(parts, canvas):
    """parts: [(mask, (x, y), fill, rim, rim_px)] in canvas pixels (already K-scaled)."""
    W, H = canvas
    white = Image.new("L", canvas, 0)
    for m, (x, y), fill, rim, rim_px in parts:
        white.paste(_grow(m, rim_px + 6), (x - rim_px - 6, y - rim_px - 6), _grow(m, rim_px + 6))
    img = Image.new("RGBA", canvas, (0, 0, 0, 0))
    outer = _grow(white, K)
    img.paste((20, 16, 20, 255), (0, 0), outer)        # thin dark line around the sticker
    img.paste((255, 255, 255, 255), (0, 0), white)
    for m, (x, y), fill, rim, rim_px in parts:
        if rim:
            layer = Image.new("L", canvas, 0)
            g = _grow(m, rim_px)
            layer.paste(g, (x - rim_px, y - rim_px), g)
            img.paste(rim + (255,), (0, 0), layer)
        layer = Image.new("L", canvas, 0)
        layer.paste(m, (x, y), m)
        img.paste(fill + (255,), (0, 0), layer)
    return img


def render(max_w=232, max_h=78):
    if os.path.exists(USER_LOGO):
        img = Image.open(USER_LOGO).convert("RGBA")
        a = np.asarray(img).astype(np.int16)
        r, g, b = a[..., 0], a[..., 1], a[..., 2]
        red = (r > 150) & (g < 110) & (b < 110)
        yellow = (r > 180) & (g > 150) & (b < 120)
        dark = np.maximum(np.maximum(r, g), b) < 90
        # thick black strokes only (the sticker's thin outline is opened away)
        dark_img = Image.fromarray((dark * 255).astype(np.uint8))
        thick = np.asarray(dark_img.filter(ImageFilter.MinFilter(7)).filter(ImageFilter.MaxFilter(7))) > 0
        ink = red | yellow | thick
        # keep a thin white rim around the ink; the white filling the gaps inside and between letters goes
        rim = max(3, img.width // 180)
        ink_img = Image.fromarray((ink * 255).astype(np.uint8))
        keep = np.asarray(ink_img.filter(ImageFilter.MaxFilter(2 * rim + 1))) > 0
        edge = np.asarray(ink_img.filter(ImageFilter.MaxFilter(2 * rim + 7))) > 0
        out = np.zeros(a.shape, np.uint8)
        out[edge] = (20, 16, 20, 255)                 # thin dark line around the rim
        out[keep] = (255, 255, 255, 255)              # white rim
        src = np.asarray(img)
        out[ink] = src[ink]
        out[ink, 3] = 255
        img = Image.fromarray(out, "RGBA")
        # open a gap between the title and the "~마술열차 살인사건~" line: cut at the emptiest row
        # in the lower part (where the two meet) and move the subtitle down
        img = img.crop(img.getbbox())
        rows = (np.asarray(img)[..., 3] > 0).sum(1)
        lo, hi = int(img.height * 0.62), int(img.height * 0.9)
        cut = lo + int(np.argmin(rows[lo:hi]))
        gap = img.height // 14
        spaced = Image.new("RGBA", (img.width, img.height + gap), (0, 0, 0, 0))
        spaced.paste(img.crop((0, 0, img.width, cut)), (0, 0))
        bottom = np.asarray(img.crop((0, cut, img.width, img.height))).copy()
        from scipy import ndimage
        lab, n = ndimage.label(bottom[..., 3] > 0)
        if n:
            sizes = ndimage.sum(bottom[..., 3] > 0, lab, range(1, n + 1))
            for i, sz in enumerate(sizes, 1):
                if sz < sizes.max() * 0.05:  # slivers of the title cut off with the subtitle
                    bottom[lab == i] = 0
        spaced.paste(Image.fromarray(bottom, "RGBA"), (0, cut + gap))
        img = spaced
    else:
        shonen = _rough(_text_mask("소년\n탐정", 26, spacing=-4), 0.5 * K, 1)
        kin = _rough(_text_mask("김전일", 52), 0.8 * K, 2)
        sub = _text_mask("~마술열차 살인사건~", 20)
        splash = _splash(int(kin.height * 0.34), 3)
        W = shonen.width + kin.width + 30 * K
        H = max(shonen.height, kin.height) + sub.height + 28 * K
        sx, sy = 10 * K, 14 * K + (kin.height - shonen.height) // 2
        kx, ky = sx + shonen.width + 8 * K, 12 * K
        # the splash sits on the dot above the last letter
        px, py = kx + int(kin.width * 0.80) - splash.width // 2, ky - splash.height // 3
        subx, suby = (W - sub.width) // 2, ky + kin.height + 4 * K
        img = _sticker([
            (shonen, (sx, sy), (16, 12, 14), None, 0),
            (kin, (kx, ky), (236, 16, 20), None, 0),
            (splash, (px, py), (236, 16, 20), None, 0),
            (sub, (subx, suby), (255, 236, 40), (224, 40, 16), 2 * K),
        ], (W, H))
    img = img.crop(img.getbbox())
    scale = min(max_w / img.width, max_h / img.height)
    small = img.resize((max(1, int(img.width * scale)), max(1, int(img.height * scale))), Image.LANCZOS)
    small.putalpha(small.getchannel("A").point(lambda v: 255 if v >= 120 else 0))
    return small
