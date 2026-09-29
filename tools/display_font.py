"""Titles, banners and shouts rendered in a bold serif display font (Nanum Myeongjo ExtraBold).

The dialogue keeps the compact Galmuri pixel font so a lot of text fits on screen; the big
one-off texts ("조사 개시!", chapter titles, endings, the logo...) are pre-rendered here as
outlined, shaded images and blitted by the engine.
"""
import os

from PIL import Image, ImageDraw, ImageFilter, ImageFont

FONT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "assets", "fonts", "NanumMyeongjo-ExtraBold.ttf")
SS = 4  # supersampling

# style: (size px, max width, top colour, bottom colour, outline colour, outline px, line gap)
STYLES = {
    "logo":      (30, 232, (255, 236, 150), (236, 150, 40), (40, 16, 8), 2, 0),
    "logo_sub":  (13, 232, (255, 255, 255), (210, 214, 230), (10, 10, 24), 1, 0),
    "banner":    (28, 190, (255, 255, 255), (200, 214, 255), (10, 12, 30), 2, 0),
    "shout":     (26, 232, (255, 70, 60), (190, 10, 24), (40, 0, 4), 2, 0),
    "chapter":   (18, 228, (255, 236, 160), (236, 170, 60), (20, 10, 6), 1, 2),
    "place":     (14, 200, (255, 230, 150), (236, 180, 80), (16, 10, 6), 1, 0),
    "end_label": (32, 232, (255, 240, 160), (236, 160, 40), (40, 16, 8), 2, 0),
    "bad_label": (32, 232, (255, 90, 80), (170, 10, 20), (30, 0, 4), 2, 0),
    "end_title": (17, 228, (255, 255, 255), (206, 212, 230), (10, 10, 24), 1, 2),
    "intro_epi": (13, 228, (255, 230, 150), (236, 180, 80), (16, 10, 6), 1, 0),
    "intro_name": (27, 232, (255, 255, 255), (206, 214, 240), (12, 10, 24), 2, 0),
}


def _font(size):
    return ImageFont.truetype(FONT, size * SS)


def render(text, style):
    """RGBA image of the text: vertical colour gradient, dark outline, soft drop shadow, 1-bit alpha."""
    size, max_w, top, bot, ol, olw, gap = STYLES[style]
    lines = text.split("\n")
    sizes = [size] * len(lines)
    if style in ("chapter", "end_title") and len(lines) > 1:
        sizes[0] = max(10, int(size * 0.72))      # "제1장" smaller above the subtitle
    while True:
        fonts = [_font(s) for s in sizes]
        widths = [f.getlength(l) for f, l in zip(fonts, lines)]
        if max(widths) <= (max_w - 2 * olw - 2) * SS or min(sizes) <= 9:
            break
        sizes = [s - 1 for s in sizes]
    pad = (olw + 2) * SS
    heights = [f.getbbox(l or " ")[3] for f, l in zip(fonts, lines)]
    W = int(max(widths)) + 2 * pad
    H = sum(heights) + gap * SS * (len(lines) - 1) + 2 * pad
    mask = Image.new("L", (W, H), 0)
    d = ImageDraw.Draw(mask)
    y = pad
    for f, l, w, h in zip(fonts, lines, widths, heights):
        d.text(((W - w) / 2, y), l, font=f, fill=255)
        y += h + gap * SS
    outline = mask.filter(ImageFilter.MaxFilter(2 * olw * SS + 1)) if olw else mask
    # colour: gradient top -> bottom
    grad = Image.new("RGB", (1, H))
    for yy in range(H):
        t = yy / max(1, H - 1)
        grad.putpixel((0, yy), tuple(int(top[i] + (bot[i] - top[i]) * t) for i in range(3)))
    grad = grad.resize((W, H))
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    shadow = Image.new("RGBA", (W, H), ol + (255,))
    img.paste(shadow, (SS, SS), outline)            # drop shadow, offset 1px
    img.paste(Image.new("RGBA", (W, H), ol + (255,)), (0, 0), outline)
    img.paste(grad.convert("RGBA"), (0, 0), mask)
    small = img.resize((W // SS, H // SS), Image.LANCZOS)
    a = small.getchannel("A").point(lambda v: 255 if v >= 110 else 0)
    small.putalpha(a)
    return small.crop(small.getbbox() or (0, 0, 1, 1))


def render_banner(kind):
    """Full "조사 개시!" / "추리 개시!" banner card (240 x 64): gradient band, speed lines,
    gold trim, a drawn icon (magnifying glass / lightning) and the title in gold serif."""
    import math
    W, H = 240, 64
    K = SS
    deduce = kind == "deduce"
    img = Image.new("RGBA", (W * K, H * K), (0, 0, 0, 255))
    d = ImageDraw.Draw(img)
    top, bot = ((70, 6, 12), (18, 0, 4)) if deduce else ((16, 30, 70), (4, 8, 24))
    for y in range(H * K):
        t = abs(y / (H * K - 1) - 0.45) / 0.55
        c = tuple(int(top[i] + (bot[i] - top[i]) * t) for i in range(3))
        d.line([(0, y), (W * K, y)], fill=c + (255,))
    # speed lines radiating from the icon
    cx, cy = 30 * K, H * K // 2
    ray = Image.new("L", img.size, 0)
    rd = ImageDraw.Draw(ray)
    for i in range(48):
        a = i * 2 * math.pi / 48
        w = 0.035 if i % 2 else 0.018
        pts = [(cx, cy)] + [(cx + math.cos(a + s) * W * K * 1.2, cy + math.sin(a + s) * W * K * 1.2) for s in (-w, w)]
        rd.polygon(pts, fill=60 if i % 2 else 34)
    glow = (255, 90, 60) if deduce else (120, 170, 255)
    img.paste(Image.new("RGBA", img.size, glow + (255,)), (0, 0), ray)
    # soft radial glow behind the icon
    halo = Image.new("L", img.size, 0)
    ImageDraw.Draw(halo).ellipse([cx - 26 * K, cy - 26 * K, cx + 26 * K, cy + 26 * K], fill=120)
    halo = halo.filter(ImageFilter.GaussianBlur(10 * K))
    img.paste(Image.new("RGBA", img.size, (255, 230, 170, 255)), (0, 0), halo)
    # gold trim: double lines top and bottom
    gold, dgold = (240, 196, 80), (150, 96, 24)
    for y0, sgn in ((0, 1), (H * K - 1, -1)):
        d.rectangle([0, min(y0, y0 + sgn * 3 * K), W * K, max(y0, y0 + sgn * 3 * K)], fill=dgold)
        d.rectangle([0, min(y0, y0 + sgn * 2 * K), W * K, max(y0, y0 + sgn * 2 * K)], fill=gold)
        yy = y0 + sgn * 5 * K
        d.rectangle([0, min(yy, yy + sgn * K // 2), W * K, max(yy, yy + sgn * K // 2)], fill=gold)
    # icon
    if deduce:
        bolt = [(34, 8), (18, 34), (29, 34), (22, 56), (44, 25), (32, 25), (40, 8)]
        pts = [(x * K, y * K) for x, y in bolt]
        d.polygon([(x + 2 * K, y + 2 * K) for x, y in pts], fill=(30, 0, 0, 255))
        d.polygon(pts, fill=(40, 10, 4, 255))
        inner = [(x * K, y * K) for x, y in [(35, 11), (21, 32), (31, 32), (26, 50), (40, 27), (29, 27), (37, 11)]]
        d.polygon(inner, fill=(255, 216, 90, 255))
        d.polygon([(x * K, y * K) for x, y in [(35, 11), (27, 24), (31, 24), (37, 11)]], fill=(255, 250, 210, 255))
    else:
        gx, gy, r = 26 * K, 27 * K, 13 * K
        d.line([(gx + 8 * K, gy + 8 * K), (gx + 21 * K, gy + 23 * K)], fill=(30, 16, 6, 255), width=9 * K)
        d.line([(gx + 9 * K, gy + 9 * K), (gx + 20 * K, gy + 22 * K)], fill=(150, 80, 30, 255), width=6 * K)
        d.line([(gx + 9 * K, gy + 9 * K), (gx + 20 * K, gy + 22 * K)], fill=(200, 120, 50, 255), width=2 * K)
        d.ellipse([gx - r - 2 * K, gy - r - 2 * K, gx + r + 2 * K, gy + r + 2 * K], fill=(30, 16, 6, 255))
        d.ellipse([gx - r, gy - r, gx + r, gy + r], fill=(230, 180, 70, 255))
        d.ellipse([gx - r + 3 * K, gy - r + 3 * K, gx + r - 3 * K, gy + r - 3 * K], fill=(90, 150, 210, 255))
        d.ellipse([gx - r + 5 * K, gy - r + 5 * K, gx + r - 3 * K, gy + r - 3 * K], fill=(40, 80, 140, 255))
        d.ellipse([gx - 7 * K, gy - 8 * K, gx - 1 * K, gy - 3 * K], fill=(235, 250, 255, 255))
    # title text: gold gradient, thick dark outline, drop shadow
    text = "추리 개시!" if deduce else "조사 개시!"
    f = _font(30)
    tw = f.getlength(text)
    tx, ty = 56 * K + (180 * K - tw) / 2, 9 * K
    mask = Image.new("L", img.size, 0)
    ImageDraw.Draw(mask).text((tx, ty), text, font=f, fill=255)
    ol = mask.filter(ImageFilter.MaxFilter(5 * K // 2 * 2 + 1))
    ol2 = mask.filter(ImageFilter.MaxFilter(7 * K // 2 * 2 + 1))
    img.paste(Image.new("RGBA", img.size, (0, 0, 0, 255)), (2 * K, 2 * K), ol2)
    img.paste(Image.new("RGBA", img.size, (250, 214, 110, 255) if not deduce else (255, 220, 120, 255)), (0, 0), ol2)
    img.paste(Image.new("RGBA", img.size, (20, 6, 2, 255)), (0, 0), ol)
    grad = Image.new("RGBA", img.size)
    gd = ImageDraw.Draw(grad)
    g_top, g_bot = ((255, 255, 240), (255, 170, 40)) if deduce else ((255, 255, 255), (170, 200, 255))
    for y in range(H * K):
        t = min(1, max(0, (y - ty) / (34 * K)))
        gd.line([(0, y), (W * K, y)], fill=tuple(int(g_top[i] + (g_bot[i] - g_top[i]) * t) for i in range(3)) + (255,))
    img.paste(grad, (0, 0), mask)
    # caption
    cf = ImageFont.truetype(FONT, 8 * K)
    cap = "DEDUCTION" if deduce else "INVESTIGATION"
    cw = cf.getlength(cap)
    ImageDraw.Draw(img).text((56 * K + (180 * K - cw) / 2, 47 * K), cap, font=cf, fill=(240, 200, 110, 255))
    return img.resize((W, H), Image.LANCZOS).convert("RGBA")
