"""Manga marks drawn over a speaker's face (이름[표정+땀] in the script).

Each mark is a few small RGBA frames, drawn at 4x and scaled down so the edges are smooth and the
fills keep their gradients. The game blends them over the portrait with 17 alpha levels; where a
frame is drawn is decided in main.c (mark_draw), from the portrait's eye rectangle.
"""
from PIL import Image, ImageDraw, ImageFilter
import math

SS = 4  # supersampling


def _canvas(w, h):
    return Image.new("RGBA", (w * SS, h * SS), (0, 0, 0, 0))


def _down(img, w, h):
    return img.resize((w, h), Image.LANCZOS)


def _lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(len(a)))


def sweat():
    """A big anime sweat drop: dark blue outline, pale cyan body lighter at the top, white shine."""
    w, h = 11, 17
    img = _canvas(w, h)
    d = ImageDraw.Draw(img)
    cx, r = w * SS / 2, w * SS / 2 - 3
    cy = h * SS - r - 3
    tip = 2
    # simpler: polygon from tip down both sides into the round bottom
    def drop(grow):
        pts = [(cx, tip - grow)]
        for k in range(1, 40):
            t = k / 40
            y = tip + (cy - tip) * t
            x = (r + grow) * math.sin(t * math.pi / 2) ** 1.5
            pts.append((cx + x, y))
        for k in range(0, 181, 6):
            a = math.radians(k)
            pts.append((cx + (r + grow) * math.cos(a), cy + (r + grow) * math.sin(a)))
        for k in range(39, 0, -1):
            t = k / 40
            y = tip + (cy - tip) * t
            x = (r + grow) * math.sin(t * math.pi / 2) ** 1.5
            pts.append((cx - x, y))
        return pts
    d.polygon(drop(2), fill=(30, 70, 150, 255))
    body = _canvas(w, h)
    bd = ImageDraw.Draw(body)
    bd.polygon(drop(-2), fill=(255, 255, 255, 255))
    grad = Image.new("RGBA", img.size)
    gd = ImageDraw.Draw(grad)
    for y in range(img.size[1]):
        gd.line([(0, y), (img.size[0], y)], fill=_lerp((235, 250, 255, 255), (120, 190, 240, 255), y / img.size[1]))
    img.paste(grad, (0, 0), body)
    # shine: a small arc on the upper left of the round part and a dot
    d = ImageDraw.Draw(img)
    d.arc([cx - r + 5, cy - r + 5, cx + r - 5, cy + r - 5], 170, 250, fill=(255, 255, 255, 255), width=5)
    d.ellipse([cx + r * 0.25, cy + r * 0.2, cx + r * 0.25 + 5, cy + r * 0.2 + 5], fill=(255, 255, 255, 230))
    return [_down(img, w, h)]


def blush():
    """One cheek: just a soft rise of warm colour, no lines; frame 2 is a touch warmer."""
    w, h = 26, 13
    frames = []
    for strength in (0.30, 0.40):
        glow = Image.new("RGBA", (w * SS, h * SS), (0, 0, 0, 0))
        px = glow.load()
        for y in range(h * SS):
            for x in range(w * SS):
                dx = (x - w * SS / 2) / (w * SS / 2)
                dy = (y - h * SS / 2) / (h * SS / 2)
                q = 1 - (dx * dx + dy * dy)
                if q > 0:
                    px[x, y] = (255, 110, 120, int(255 * strength * q ** 1.4))
        frames.append(_down(glow.filter(ImageFilter.GaussianBlur(4)), w, h))
    return frames


def anger():
    """The throbbing vein mark: four thick curved strokes around a cross-shaped gap, bright red
    with a dark rim; the second frame is larger (the pulse)."""
    frames = []
    for size in (19, 23):
        S = size * SS
        c = S / 2

        def strokes(width, col, trim):
            img = _canvas(size, size)
            d = ImageDraw.Draw(img)
            for qx in (-1, 1):
                for qy in (-1, 1):
                    # an arc centred out in the corner, so its curve bows toward the middle
                    cxq, cyq, rr = c + qx * S * 0.42, c + qy * S * 0.42, S * 0.33
                    start = {(-1, -1): 0, (1, -1): 90, (1, 1): 180, (-1, 1): 270}[(qx, qy)]
                    d.arc([cxq - rr, cyq - rr, cxq + rr, cyq + rr], start + trim, start + 90 - trim, fill=col, width=int(width))
            return img
        img = strokes(S * 0.19, (110, 0, 12, 255), 2)
        img.alpha_composite(strokes(S * 0.12, (235, 30, 45, 255), 6))
        img.alpha_composite(strokes(S * 0.035, (255, 140, 140, 255), 22))
        frames.append(_down(img, size, size))
    return frames


def gloom(w=60, h=42):
    """Shadow over the forehead with blue-violet lines hanging down; the lines creep downward."""
    frames = []
    for f in range(4):
        img = Image.new("RGBA", (w * SS, h * SS), (0, 0, 0, 0))
        px = img.load()
        for y in range(h * SS):
            t = y / (h * SS)
            a = int(255 * 0.62 * (1 - t) ** 1.1)
            for x in range(w * SS):
                e = min(x, w * SS - 1 - x) / (w * SS * 0.18)
                px[x, y] = (30, 20, 70, int(a * min(1, e)))
        d = ImageDraw.Draw(img)
        lines = [(0.06, 0.55), (0.15, 0.80), (0.24, 0.62), (0.33, 0.92), (0.42, 0.70), (0.51, 0.86), (0.60, 0.60), (0.69, 0.90), (0.78, 0.66), (0.87, 0.82), (0.95, 0.52)]
        for i, (fx, ln) in enumerate(lines):
            L = ln * (0.80 + 0.20 * ((f + i) % 4) / 3)
            x = fx * w * SS
            steps = 24
            for s in range(steps):
                y0, y1 = s / steps * L * h * SS, (s + 1) / steps * L * h * SS
                a = int(235 * (1 - s / steps) ** 0.8)
                d.line([(x, y0), (x, y1)], fill=(60, 50, 150, a), width=4)
        frames.append(_down(img, w, h))
    return frames


def tears():
    """One eye, as the series draws it: the lower lid wells up with a glossy shine, then a single
    clear drop rolls down to the middle of the cheek, leaving a faint trail that fades.
    Frames 0-9 are the left eye, 10-19 the same mirrored for the right eye.
    0: welling; 1-7: the drop rolls down; 8-9: the trail dries."""
    w, h = 9, 18
    cx = w * SS / 2
    left = []
    for f in range(10):
        img = _canvas(w, h)
        d = ImageDraw.Draw(img)
        # the wet lower lid: a thin bright crescent and a sparkle
        d.arc([cx - 13, -10, cx + 13, 7], 35, 145, fill=(235, 248, 255, 120), width=2)
        d.ellipse([cx + 3, 1, cx + 6, 4], fill=(255, 255, 255, 170))
        if 1 <= f <= 9:
            p = min(f, 7) / 7  # how far the drop has rolled
            yd = 6 + p * (h * SS - 16)
            fade = 1.0 if f <= 7 else (0.55 if f == 8 else 0.25)
            # the trail: a thin clear line that is a bit stronger near the drop
            n = 20
            for k in range(n):
                y0 = 6 + (yd - 6) * k / n
                y1 = 6 + (yd - 6) * (k + 1) / n
                x0 = cx - 1 + 2.5 * (y0 / (h * SS)) ** 2
                x1 = cx - 1 + 2.5 * (y1 / (h * SS)) ** 2
                a = int((50 + 80 * (k / n)) * fade)
                d.line([(x0, y0), (x1, y1)], fill=(215, 240, 255, a), width=4)
                d.line([(x0 - 1, y0), (x1 - 1, y1)], fill=(255, 255, 255, int(a * 1.2)), width=1)
            if f <= 7:
                xd = cx - 1 + 2.5 * (yd / (h * SS)) ** 2
                d.ellipse([xd - 6, yd - 6, xd + 6, yd + 8], fill=(175, 220, 250, 175))
                d.ellipse([xd - 6, yd - 6, xd + 6, yd + 8], outline=(120, 175, 225, 150), width=1)
                d.ellipse([xd - 4, yd - 4, xd - 1, yd - 1], fill=(255, 255, 255, 240))
        left.append(_down(img, w, h))
    return left + [fr.transpose(Image.FLIP_LEFT_RIGHT) for fr in left]


MARKS = [("blush", blush), ("sweat", sweat), ("gloom", gloom), ("anger", anger), ("tear", tears)]


def build():
    """-> list of (mark name, [frames as RGBA images]) in MARK_* order (1-based in main.c)."""
    return [(name, fn()) for name, fn in MARKS]


if __name__ == "__main__":
    import sys
    out = sys.argv[1] if len(sys.argv) > 1 else "marks_preview.png"
    sets = build()
    W = 8
    sheet = Image.new("RGBA", (W * 64, len(sets) * 64), (200, 170, 150, 255))
    for r, (name, frames) in enumerate(sets):
        for c, fr in enumerate(frames):
            big = fr.resize((fr.width * 3, fr.height * 3), Image.NEAREST)
            sheet.alpha_composite(big, (c * 64 + 2, r * 64 + 2))
    sheet.save(out)
