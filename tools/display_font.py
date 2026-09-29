"""Titles, banners and shouts rendered in a bold display font (Black Han Sans).

The dialogue keeps the compact Galmuri pixel font so a lot of text fits on screen; the big
one-off texts ("조사 개시!", chapter titles, endings, the logo...) are pre-rendered here as
outlined, shaded images and blitted by the engine.
"""
import os

from PIL import Image, ImageDraw, ImageFilter, ImageFont

FONT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "assets", "fonts", "BlackHanSans-Regular.ttf")
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
