"""Procedural pixel art for the game: scene backgrounds, character portraits and evidence icons.

Everything is drawn with Pillow and converted to GBA BGR555. Deterministic (fixed random seeds).
Run `python3 tools/build_assets.py ... --preview DIR` to dump PNG previews.
"""
import math
import os
import random

from PIL import Image, ImageDraw

W, H = 240, 160
PORTRAIT_W, PORTRAIT_H = 64, 80
ICON_SIZE = 64
OUTLINE = (28, 20, 32, 255)

BAYER = [[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]


def rgb15(hexstr):
    hexstr = hexstr.lstrip("#")
    r, g, b = (int(hexstr[i:i + 2], 16) for i in (0, 2, 4))
    return (b >> 3) << 10 | (g >> 3) << 5 | (r >> 3)


def to15(img, dither=True):
    px = img.load()
    out = []
    for y in range(img.height):
        for x in range(img.width):
            p = px[x, y]
            if len(p) == 4 and p[3] < 128:
                out.append(0xFFFF)
                continue
            d = (BAYER[y & 3][x & 3] - 7.5) / 2 if dither else 0
            r, g, b = (max(0, min(255, int(v + d))) >> 3 for v in p[:3])
            out.append(b << 10 | g << 5 | r)
    return out


# ---------------------------------------------------------------- helpers

def mix(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def vgrad(d, box, top, bottom):
    x0, y0, x1, y1 = box
    for y in range(y0, y1):
        t = (y - y0) / max(1, y1 - y0 - 1)
        d.line([(x0, y), (x1 - 1, y)], fill=mix(top, bottom, t))


def glow(img, cx, cy, radius, color, strength=0.6):
    px = img.load()
    for y in range(max(0, cy - radius), min(img.height, cy + radius)):
        for x in range(max(0, cx - radius), min(img.width, cx + radius)):
            dist = math.hypot(x - cx, y - cy) / radius
            if dist < 1:
                t = (1 - dist) ** 2 * strength
                px[x, y] = mix(px[x, y][:3], color, t)


def tint(img, color, t):
    px = img.load()
    for y in range(img.height):
        for x in range(img.width):
            px[x, y] = mix(px[x, y][:3], color, t)


def snow(d, rng, box, n, colors=((235, 240, 255), (180, 190, 220))):
    x0, y0, x1, y1 = box
    for _ in range(n):
        x, y = rng.randrange(x0, x1), rng.randrange(y0, y1)
        c = rng.choice(colors)
        if rng.random() < 0.25:
            d.rectangle([x, y, x + 1, y + 1], fill=c)
        else:
            d.point((x, y), fill=c)


def night_window(img, d, rng, box, frame=(70, 50, 40)):
    x0, y0, x1, y1 = box
    vgrad(d, (x0, y0, x1, y1), (10, 14, 40), (40, 50, 90))
    # distant snowy hills
    for x in range(x0, x1):
        hy = int(y1 - 6 - 4 * math.sin(x * 0.09) - 2 * math.sin(x * 0.23))
        d.line([(x, hy), (x, y1 - 1)], fill=(150, 160, 190))
    snow(d, rng, (x0, y0, x1, y1), (x1 - x0) * (y1 - y0) // 30)
    d.rectangle([x0 - 2, y0 - 2, x1 + 1, y1 + 1], outline=frame, width=2)


# ---------------------------------------------------------------- scenes

def scene_title():
    img = Image.new("RGB", (W, H))
    d = ImageDraw.Draw(img)
    rng = random.Random(1)
    vgrad(d, (0, 0, W, H), (6, 6, 24), (60, 30, 70))
    for _ in range(90):
        x, y = rng.randrange(W), rng.randrange(100)
        d.point((x, y), fill=rng.choice([(255, 255, 255), (200, 200, 255), (255, 240, 200)]))
    d.ellipse([180, 14, 206, 40], fill=(250, 240, 200))
    d.ellipse([186, 12, 212, 38], fill=(14, 10, 34))
    glow(img, 190, 28, 30, (120, 110, 160), 0.3)
    for x in range(W):  # mountains
        hy = int(100 - 22 * abs(math.sin(x * 0.021 + 1)) - 8 * math.sin(x * 0.07))
        d.line([(x, hy), (x, H)], fill=(30, 28, 55))
        d.line([(x, hy), (x, hy + 2)], fill=(200, 205, 230))
    d.rectangle([0, 118, W, H], fill=(18, 16, 34))
    for x in range(0, W, 24):  # viaduct arches
        d.rectangle([x, 118, x + 22, 160], fill=(40, 34, 56))
        d.ellipse([x + 3, 128, x + 19, 160], fill=(12, 10, 26))
    d.rectangle([0, 114, W, 118], fill=(60, 50, 70))
    # train silhouette
    d.polygon([(8, 113), (8, 96), (20, 90), (44, 90), (44, 113)], fill=(20, 16, 22))
    d.rectangle([22, 82, 28, 90], fill=(20, 16, 22))
    for i in range(4):
        x = 48 + i * 46
        d.rounded_rectangle([x, 92, x + 42, 113], radius=3, fill=(60, 18, 26))
        d.line([(x, 96), (x + 42, 96)], fill=(170, 130, 60))
        for j in range(4):
            wx = x + 5 + j * 9
            d.rectangle([wx, 99, wx + 5, 105], fill=(255, 220, 120))
            glow(img, wx + 3, 102, 7, (255, 200, 100), 0.25)
    glow(img, 10, 104, 26, (255, 240, 180), 0.5)
    d.polygon([(0, 96), (8, 102), (0, 108)], fill=(255, 250, 220))
    snow(d, rng, (0, 0, W, H), 160)
    return img


def scene_platform():
    img = Image.new("RGB", (W, H))
    d = ImageDraw.Draw(img)
    rng = random.Random(2)
    vgrad(d, (0, 0, W, 70), (10, 12, 32), (30, 34, 60))
    d.rectangle([0, 0, W, 8], fill=(40, 38, 48))  # roof
    for x in range(0, W, 60):
        d.rectangle([x + 28, 8, x + 31, 70], fill=(50, 48, 58))
        d.rectangle([x + 22, 18, x + 37, 22], fill=(230, 220, 170))
        glow(img, x + 30, 22, 28, (255, 230, 160), 0.35)
    # train side
    d.rectangle([0, 34, W, 96], fill=(90, 24, 34))
    d.rectangle([0, 38, W, 41], fill=(190, 150, 70))
    d.rectangle([0, 90, W, 93], fill=(190, 150, 70))
    for x in range(6, W, 34):
        d.rectangle([x, 48, x + 24, 72], fill=(40, 30, 40))
        d.rectangle([x + 2, 50, x + 22, 70], fill=(255, 214, 130))
        d.line([(x + 12, 50), (x + 12, 70)], fill=(120, 80, 60))
    d.rectangle([0, 96, W, 104], fill=(30, 26, 30))
    # platform floor with perspective
    vgrad(d, (0, 104, W, H), (120, 120, 132), (70, 70, 84))
    d.rectangle([0, 108, W, 110], fill=(230, 200, 60))
    for x in range(-40, W + 40, 20):
        d.line([(x, 110), (x - 30, H)], fill=(96, 96, 108))
    snow(d, rng, (0, 0, W, H), 220)
    for _ in range(30):  # snow drifts on the platform
        x, y = rng.randrange(W), 114 + rng.randrange(44)
        d.line([(x, y), (x + 4, y)], fill=(210, 215, 232))
    return img


def scene_corridor():
    img = Image.new("RGB", (W, H))
    d = ImageDraw.Draw(img)
    rng = random.Random(3)
    vp = (150, 62)
    d.rectangle([0, 0, W, H], fill=(96, 60, 40))
    d.polygon([(0, 0), (W, 0), (vp[0] + 22, vp[1] - 22), (vp[0] - 22, vp[1] - 22)], fill=(200, 180, 150))  # ceiling
    d.polygon([(0, H), (W, H), (vp[0] + 22, vp[1] + 26), (vp[0] - 22, vp[1] + 26)], fill=(120, 26, 36))  # carpet
    d.polygon([(0, 0), (vp[0] - 22, vp[1] - 22), (vp[0] - 22, vp[1] + 26), (0, H)], fill=(110, 70, 46))  # left wall
    d.polygon([(W, 0), (vp[0] + 22, vp[1] - 22), (vp[0] + 22, vp[1] + 26), (W, H)], fill=(130, 86, 56))  # right wall
    d.rectangle([vp[0] - 22, vp[1] - 22, vp[0] + 22, vp[1] + 26], fill=(70, 44, 30))  # far door
    d.rectangle([vp[0] - 10, vp[1] - 16, vp[0] + 10, vp[1] + 26], fill=(50, 30, 22))
    d.rectangle([vp[0] - 6, vp[1] - 12, vp[0] + 6, vp[1] - 2], fill=(240, 210, 140))
    def wall_y(x, f, left):
        """y at fraction f (0=ceiling edge, 1=floor edge) of a side wall at screen x."""
        t = x / (vp[0] - 22) if left else (W - x) / (W - vp[0] - 22)
        top = t * (vp[1] - 22)
        bottom = H + t * (vp[1] + 26 - H)
        return int(top + f * (bottom - top))

    for xa, xb in [(4, 44), (60, 90), (100, 120)]:  # windows on the left wall
        quad = [(xa, wall_y(xa, 0.2, True)), (xb, wall_y(xb, 0.2, True)),
                (xb, wall_y(xb, 0.58, True)), (xa, wall_y(xa, 0.58, True))]
        d.polygon(quad, fill=(20, 26, 60))
        for _ in range(xb - xa):
            x = rng.randrange(xa + 1, xb)
            y0, y1 = wall_y(x, 0.2, True) + 1, wall_y(x, 0.58, True)
            if y1 > y0:
                d.point((x, rng.randrange(y0, y1)), fill=(230, 235, 255))
        d.polygon(quad, outline=(60, 36, 24))
    for xa, xb in [(236, 204), (190, 178)]:  # compartment doors on the right wall
        d.polygon([(xa, wall_y(xa, 0.1, False)), (xb, wall_y(xb, 0.1, False)),
                   (xb, wall_y(xb, 0.95, False)), (xa, wall_y(xa, 0.95, False))], fill=(92, 58, 38),
                  outline=(60, 36, 24))
        hx = xb + (xa - xb) // 5
        d.point((hx, wall_y(hx, 0.55, False)), fill=(230, 200, 90))
    for i in range(4):  # ceiling lamps
        t = i / 4
        x = int(W / 2 * (1 - t) + vp[0] * t)
        y = int(10 * (1 - t) + (vp[1] - 20) * t)
        r = int(8 * (1 - t) + 2)
        d.ellipse([x - r, y - r // 2, x + r, y + r // 2], fill=(255, 240, 190))
        glow(img, x, y, r * 4, (255, 220, 150), 0.3)
    return img


def scene_stage():
    img = Image.new("RGB", (W, H))
    d = ImageDraw.Draw(img)
    vgrad(d, (0, 0, W, H), (20, 10, 20), (40, 20, 30))
    d.rectangle([30, 20, 210, 100], fill=(30, 18, 40))
    for x in range(0, 52, 6):  # curtains
        c = (150, 20, 30) if (x // 6) % 2 == 0 else (110, 12, 22)
        d.rectangle([x, 0, x + 5, 118 - x // 3], fill=c)
        d.rectangle([W - x - 6, 0, W - x - 1, 118 - x // 3], fill=c)
    for x in range(0, W, 8):
        d.polygon([(x, 0), (x + 8, 0), (x + 4, 16)], fill=(160, 24, 34))
    d.rectangle([0, 0, W, 5], fill=(200, 160, 60))
    d.polygon([(20, 100), (220, 100), (240, 126), (0, 126)], fill=(110, 70, 40))  # stage floor
    for x in range(0, W, 16):
        d.line([(x, 100), (x - 20 + x // 6, 126)], fill=(90, 56, 32))
    glow(img, 120, 80, 70, (255, 240, 200), 0.45)
    # magic box
    d.rectangle([98, 50, 142, 100], fill=(20, 16, 40), outline=(210, 170, 60))
    d.rectangle([98, 46, 142, 50], fill=(210, 170, 60))
    for sx, sy in [(106, 60), (128, 66), (112, 82), (134, 88), (120, 72)]:
        d.polygon([(sx, sy - 3), (sx + 1, sy - 1), (sx + 3, sy), (sx + 1, sy + 1), (sx, sy + 3),
                   (sx - 1, sy + 1), (sx - 3, sy), (sx - 1, sy - 1)], fill=(250, 220, 90))
    for i in range(3):
        x = 104 + i * 16
        d.line([(x, 40), (x + 4, 60)], fill=(210, 210, 220), width=2)
        d.rectangle([x - 2, 37, x + 3, 40], fill=(180, 140, 60))
    d.rectangle([0, 126, W, H], fill=(16, 10, 16))  # audience
    for x in range(-6, W, 22):
        d.ellipse([x, 128, x + 16, 146], fill=(34, 24, 34))
        d.rectangle([x - 2, 142, x + 18, 160], fill=(34, 24, 34))
    return img


def cabin_base(rng):
    img = Image.new("RGB", (W, H))
    d = ImageDraw.Draw(img)
    vgrad(d, (0, 0, W, 112), (180, 150, 110), (150, 120, 90))
    for x in range(0, W, 12):
        d.line([(x, 0), (x, 112)], fill=(160, 130, 96))
    d.rectangle([0, 112, W, H], fill=(96, 40, 40))
    for y in range(116, H, 8):
        d.line([(0, y), (W, y)], fill=(84, 34, 34))
    d.rectangle([0, 104, W, 112], fill=(100, 70, 50))
    night_window(img, d, rng, (84, 16, 156, 70))
    d.line([(120, 16), (120, 70)], fill=(70, 50, 40), width=2)
    # bed
    d.rectangle([160, 74, 240, 124], fill=(90, 60, 44))
    d.rectangle([164, 70, 240, 92], fill=(230, 226, 214))
    d.rectangle([168, 66, 196, 76], fill=(250, 250, 244))
    d.rectangle([164, 90, 240, 118], fill=(60, 80, 130))
    # door with chain
    d.rectangle([8, 12, 60, 112], fill=(110, 70, 44), outline=(70, 44, 28), width=2)
    d.rectangle([16, 22, 52, 56], outline=(90, 56, 36))
    d.ellipse([50, 60, 56, 66], fill=(220, 190, 90))
    for i in range(6):
        d.ellipse([34 + i * 3, 50 + (i % 2), 37 + i * 3, 53 + (i % 2)], outline=(200, 200, 210))
    # wall lamp
    d.polygon([(70, 30), (80, 30), (78, 22), (72, 22)], fill=(240, 220, 160))
    glow(img, 75, 28, 30, (255, 220, 150), 0.35)
    return img, d


def scene_cabin():
    img, _ = cabin_base(random.Random(4))
    return img


def scene_cabin_crime():
    rng = random.Random(4)
    img, d = cabin_base(rng)
    d.polygon([(70, 126), (150, 120), (170, 134), (90, 146), (60, 140)], fill=(40, 30, 40))
    d.ellipse([56, 124, 76, 140], fill=(40, 30, 40))
    d.polygon([(60, 132), (110, 128), (130, 150), (70, 158), (40, 150)], fill=(130, 10, 20))
    # hanging puppet
    px, py = 128, 50
    for sx in (px - 8, px, px + 8):
        d.line([(sx, 0), (sx, py - 6 if sx == px else py + 4)], fill=(230, 230, 240))
    d.ellipse([px - 5, py - 12, px + 5, py - 2], fill=(245, 235, 225), outline=OUTLINE[:3])
    d.point((px - 2, py - 8), fill=(0, 0, 0))
    d.point((px + 2, py - 8), fill=(0, 0, 0))
    d.line([(px - 2, py - 5), (px + 2, py - 5)], fill=(200, 20, 30))
    d.polygon([(px - 7, py - 13), (px + 7, py - 13), (px, py - 22)], fill=(180, 30, 40))
    d.rectangle([px - 5, py - 2, px + 5, py + 12], fill=(30, 30, 60))
    d.line([(px - 5, py), (px - 9, py + 5)], fill=(30, 30, 60), width=2)
    d.line([(px + 5, py), (px + 9, py + 5)], fill=(30, 30, 60), width=2)
    d.line([(px - 3, py + 12), (px - 4, py + 22)], fill=(30, 30, 60), width=2)
    d.line([(px + 3, py + 12), (px + 4, py + 22)], fill=(30, 30, 60), width=2)
    tint(img, (120, 0, 20), 0.25)
    return img


def scene_dining():
    img = Image.new("RGB", (W, H))
    d = ImageDraw.Draw(img)
    rng = random.Random(5)
    vgrad(d, (0, 0, W, 100), (120, 80, 50), (90, 56, 36))
    for x in range(8, W, 58):
        night_window(img, d, rng, (x, 18, x + 42, 58), frame=(170, 130, 60))
        d.rectangle([x + 16, 62, x + 26, 66], fill=(250, 220, 150))
        glow(img, x + 21, 62, 22, (255, 210, 140), 0.3)
    d.rectangle([0, 100, W, H], fill=(70, 30, 30))
    for x in range(10, W, 76):
        d.polygon([(x, 104), (x + 60, 104), (x + 66, 124), (x - 6, 124)], fill=(245, 242, 232))
        d.rectangle([x - 6, 124, x + 66, 128], fill=(220, 216, 206))
        d.rectangle([x + 26, 90, x + 28, 104], fill=(220, 190, 90))
        d.ellipse([x + 22, 86, x + 32, 92], fill=(255, 240, 180))
        d.ellipse([x + 6, 108, x + 18, 114], fill=(200, 200, 210))
        d.ellipse([x + 40, 108, x + 52, 114], fill=(200, 200, 210))
        d.rectangle([x + 2, 128, x + 8, 150], fill=(90, 50, 30))
        d.rectangle([x + 52, 128, x + 58, 150], fill=(90, 50, 30))
    return img


def scene_baggage():
    img = Image.new("RGB", (W, H))
    d = ImageDraw.Draw(img)
    vgrad(d, (0, 0, W, H), (40, 40, 50), (20, 20, 26))
    for x in range(0, W, 16):
        d.line([(x, 0), (x, 118)], fill=(50, 50, 60))
    d.rectangle([0, 118, W, H], fill=(46, 40, 36))
    for y in range(122, H, 7):
        d.line([(0, y), (W, y)], fill=(38, 32, 28))
    for x, y, w, h in [(6, 70, 44, 48), (14, 36, 34, 34), (196, 64, 40, 54), (178, 88, 22, 30)]:
        d.rectangle([x, y, x + w, y + h], fill=(130, 96, 56), outline=(80, 56, 32))
        d.line([(x, y), (x + w, y + h)], fill=(96, 70, 40))
        d.line([(x + w, y), (x, y + h)], fill=(96, 70, 40))
    # water tank
    d.rectangle([76, 40, 164, 120], fill=(30, 70, 110))
    for y in range(44, 120, 4):
        d.line([(78, y), (162, y)], fill=(40, 90, 136) if y % 8 else (34, 80, 124))
    d.rectangle([76, 36, 164, 44], fill=(200, 230, 245))  # ice layer
    d.rectangle([72, 30, 168, 36], fill=(120, 124, 130))
    d.rectangle([76, 40, 164, 120], outline=(150, 160, 170), width=2)
    d.rectangle([114, 22, 126, 32], fill=(190, 160, 60))
    d.arc([115, 14, 125, 26], 180, 360, fill=(190, 160, 60), width=2)
    glow(img, 120, 80, 50, (80, 160, 220), 0.25)
    d.line([(120, 0), (120, 8)], fill=(20, 20, 20))
    d.ellipse([116, 8, 124, 16], fill=(255, 240, 180))
    glow(img, 120, 12, 40, (255, 220, 150), 0.3)
    return img


def scene_snowfield():
    img = Image.new("RGB", (W, H))
    d = ImageDraw.Draw(img)
    rng = random.Random(6)
    vgrad(d, (0, 0, W, H), (20, 24, 50), (90, 96, 130))
    d.rectangle([0, 112, W, H], fill=(210, 215, 235))
    for x in range(W):
        d.point((x, 112 + int(2 * math.sin(x * 0.2))), fill=(170, 176, 200))
    d.rectangle([0, 76, W, 112], fill=(70, 20, 30))
    d.rectangle([0, 76, W, 80], fill=(240, 240, 250))
    for x in range(6, W, 30):
        d.rectangle([x, 86, x + 18, 100], fill=(255, 214, 130))
    d.rectangle([0, 108, W, 116], fill=(30, 26, 30))
    for _ in range(160):
        x, y = rng.randrange(W), rng.randrange(H)
        d.line([(x, y), (x - 6, y + 3)], fill=(220, 225, 245))
    return img


def scene_black():
    return Image.new("RGB", (W, H), (0, 0, 0))


SCENES = {
    "black": scene_black,
    "title": scene_title,
    "platform": scene_platform,
    "corridor": scene_corridor,
    "stage": scene_stage,
    "cabin": scene_cabin,
    "cabin_crime": scene_cabin_crime,
    "dining": scene_dining,
    "baggage": scene_baggage,
    "snowfield": scene_snowfield,
}
ALWAYS_SCENES = ["black", "title"]


def render_scene(key):
    return to15(SCENES[key]())


# ---------------------------------------------------------------- portraits (drawn at 32x40, scaled 2x)

SKIN = (250, 214, 180)


def face(d, skin=SKIN, wide=0):
    d.rectangle([14, 25, 18, 31], fill=mix(skin, (150, 90, 70), 0.25))
    d.ellipse([9 - wide, 9, 23 + wide, 28], fill=skin)
    d.ellipse([7 - wide, 16, 10 - wide, 21], fill=skin)
    d.ellipse([22 + wide, 16, 25 + wide, 21], fill=skin)


def eyes(d, style="normal", color=(40, 30, 40), y=18):
    if style == "female":
        for x in (11, 18):
            d.rectangle([x, y, x + 2, y + 3], fill=color)
            d.point((x, y), fill=(255, 255, 255))
            d.line([(x - 1, y - 1), (x + 3, y - 1)], fill=(30, 20, 30))
    elif style == "smile":
        for x in (11, 18):
            d.line([(x, y + 1), (x + 1, y), (x + 2, y + 1)], fill=color)
    elif style == "sharp":
        for x in (11, 18):
            d.rectangle([x, y + 1, x + 2, y + 2], fill=color)
        d.line([(10, y - 2), (13, y - 1)], fill=(30, 20, 20))
        d.line([(21, y - 2), (18, y - 1)], fill=(30, 20, 20))
    else:
        for x in (11, 19):
            d.rectangle([x, y, x + 1, y + 2], fill=color)
            d.point((x, y), fill=(255, 255, 255))


def mouth(d, style="normal", y=24):
    if style == "smile":
        d.line([(14, y), (16, y + 1), (18, y)], fill=(160, 60, 60))
    elif style == "lips":
        d.line([(15, y), (17, y)], fill=(200, 40, 70))
    elif style == "frown":
        d.line([(14, y + 1), (16, y), (18, y + 1)], fill=(120, 60, 60))
    else:
        d.line([(15, y), (17, y)], fill=(150, 70, 70))


def body(d, color, shirt=None, collar=None):
    d.polygon([(2, 40), (4, 33), (11, 30), (21, 30), (28, 33), (30, 40)], fill=color)
    if shirt:
        d.polygon([(13, 30), (19, 30), (16, 37)], fill=shirt)
    if collar:
        d.polygon([(11, 30), (16, 36), (13, 30)], fill=collar)
        d.polygon([(21, 30), (16, 36), (19, 30)], fill=collar)


def p_kin(d):
    hair = (40, 36, 48)
    d.polygon([(22, 12), (30, 20), (29, 32), (24, 24)], fill=hair)  # ponytail
    body(d, (36, 44, 80), shirt=(240, 240, 240))
    d.rectangle([15, 30, 17, 40], fill=(36, 44, 80))
    face(d)
    d.ellipse([7, 3, 25, 17], fill=hair)
    for x in range(8, 24, 3):
        d.polygon([(x, 10), (x + 4, 10), (x + 2, 15)], fill=hair)
    d.polygon([(8, 8), (10, 20), (7, 18)], fill=hair)
    d.polygon([(24, 8), (22, 20), (25, 18)], fill=hair)
    d.line([(10, 16), (13, 15)], fill=(30, 20, 20))
    d.line([(19, 15), (22, 16)], fill=(30, 20, 20))
    eyes(d)
    mouth(d, "smile")


def p_miyuki(d):
    hair = (100, 56, 40)
    d.rectangle([6, 10, 26, 38], fill=hair)
    body(d, (240, 240, 244), collar=(40, 50, 100))
    d.polygon([(13, 33), (19, 33), (16, 37)], fill=(210, 40, 50))
    face(d)
    d.ellipse([7, 4, 25, 18], fill=hair)
    d.polygon([(9, 9), (23, 9), (22, 14), (17, 12), (16, 15), (14, 12), (10, 14)], fill=hair)
    eyes(d, "female", color=(80, 50, 40))
    mouth(d, "smile")
    d.point((10, 22), fill=(250, 170, 160))
    d.point((22, 22), fill=(250, 170, 160))


def p_kenmochi(d):
    body(d, (170, 140, 90), shirt=(230, 230, 220))
    d.line([(16, 31), (16, 39)], fill=(140, 40, 40), width=2)
    face(d, skin=(236, 196, 160), wide=1)
    d.ellipse([8, 5, 24, 13], fill=(70, 64, 64))
    d.rectangle([8, 9, 10, 16], fill=(70, 64, 64))
    d.rectangle([22, 9, 24, 16], fill=(70, 64, 64))
    d.line([(10, 15), (14, 15)], fill=(40, 30, 30), width=1)
    d.line([(18, 15), (22, 15)], fill=(40, 30, 30), width=1)
    eyes(d)
    d.rectangle([15, 19, 17, 23], fill=(220, 170, 140))
    mouth(d, "frown", 25)


def p_yamagami(d):
    body(d, (24, 24, 30), shirt=(245, 245, 245))
    d.polygon([(13, 31), (16, 32), (19, 31), (19, 34), (16, 33), (13, 34)], fill=(200, 20, 30))
    face(d, skin=(240, 200, 170))
    d.rectangle([8, 12, 10, 20], fill=(180, 180, 190))
    d.rectangle([22, 12, 24, 20], fill=(180, 180, 190))
    d.rectangle([10, 0, 22, 9], fill=(20, 20, 26))
    d.rectangle([10, 7, 22, 8], fill=(160, 20, 30))
    d.rectangle([6, 9, 26, 11], fill=(20, 20, 26))
    eyes(d, "sharp")
    d.ellipse([17, 16, 22, 21], outline=(220, 190, 80))
    d.polygon([(11, 23), (16, 22), (21, 23), (23, 21), (21, 24), (16, 23), (11, 24), (9, 21)], fill=(170, 170, 180))
    mouth(d, "smile", 26)


def p_reika(d):
    hair = (110, 56, 130)
    d.rectangle([7, 12, 25, 26], fill=hair)
    body(d, (170, 20, 50))
    d.point((10, 35), fill=(255, 230, 120))
    d.point((22, 36), fill=(255, 230, 120))
    d.point((16, 38), fill=(255, 230, 120))
    face(d, skin=(248, 220, 200))
    d.ellipse([11, 0, 21, 8], fill=hair)
    d.ellipse([7, 5, 25, 16], fill=hair)
    d.polygon([(8, 10), (16, 10), (9, 17)], fill=hair)
    d.rectangle([7, 22, 8, 24], fill=(250, 210, 80))
    d.rectangle([24, 22, 25, 24], fill=(250, 210, 80))
    eyes(d, "female", color=(90, 40, 90))
    mouth(d, "lips")


def p_kuroki(d):
    body(d, (30, 30, 34))
    d.polygon([(6, 34), (10, 31), (10, 40), (5, 40)], fill=(190, 150, 120))
    d.polygon([(26, 34), (22, 31), (22, 40), (27, 40)], fill=(190, 150, 120))
    face(d, skin=(206, 160, 120), wide=1)
    d.ellipse([8, 4, 24, 14], fill=(30, 26, 26))
    d.rectangle([7, 9, 25, 12], fill=(180, 30, 30))
    d.polygon([(25, 10), (29, 14), (27, 16), (24, 12)], fill=(180, 30, 30))
    eyes(d, "sharp")
    for x, y in [(12, 25), (14, 26), (18, 26), (20, 25), (16, 27)]:
        d.point((x, y), fill=(120, 90, 70))
    mouth(d, "frown", 23)


def p_okada(d):
    body(d, (34, 44, 84))
    for y in (33, 36, 39):
        d.point((16, y), fill=(230, 200, 80))
    face(d, skin=(236, 200, 170))
    d.rectangle([8, 12, 10, 17], fill=(170, 170, 176))
    d.rectangle([22, 12, 24, 17], fill=(170, 170, 176))
    d.rectangle([8, 4, 24, 11], fill=(34, 44, 84))
    d.rectangle([6, 10, 26, 12], fill=(20, 24, 50))
    d.rectangle([14, 6, 18, 9], fill=(230, 200, 80))
    for x in (10, 17):
        d.ellipse([x, 16, x + 5, 21], outline=(60, 60, 70))
    d.line([(15, 18), (17, 18)], fill=(60, 60, 70))
    eyes(d, y=18)
    d.line([(11, 24), (12, 25)], fill=(200, 160, 130))
    mouth(d, "normal", 25)


def p_izumi(d):
    hair = (70, 44, 34)
    d.rectangle([7, 10, 25, 26], fill=hair)
    body(d, (200, 170, 120), shirt=(250, 250, 250))
    d.line([(10, 31), (20, 40)], fill=(40, 40, 40), width=1)
    face(d)
    d.ellipse([7, 4, 25, 17], fill=hair)
    d.polygon([(8, 9), (24, 9), (24, 13), (8, 14)], fill=hair)
    for x in (10, 17):
        d.ellipse([x, 16, x + 5, 21], outline=(150, 40, 50))
    d.line([(15, 18), (17, 18)], fill=(150, 40, 50))
    eyes(d, "female", color=(70, 50, 40), y=17)
    mouth(d, "smile")


def p_takato(d):
    hair = (26, 24, 34)
    d.rectangle([8, 10, 24, 24], fill=hair)
    body(d, (20, 20, 26), shirt=(240, 240, 240))
    d.line([(16, 31), (16, 38)], fill=(120, 20, 40), width=2)
    face(d, skin=(250, 226, 206))
    d.ellipse([7, 3, 25, 16], fill=hair)
    d.polygon([(8, 8), (16, 8), (11, 18), (9, 20)], fill=hair)
    d.polygon([(24, 8), (16, 8), (21, 18), (23, 20)], fill=hair)
    eyes(d, "smile", color=(40, 20, 40))
    mouth(d, "smile")


def p_puppet(d):
    cloak = (18, 14, 22)
    d.polygon([(0, 40), (4, 26), (8, 6), (16, 1), (24, 6), (28, 26), (32, 40)], fill=cloak)
    d.ellipse([10, 10, 22, 27], fill=(240, 236, 230))
    d.polygon([(11, 17), (14, 16), (15, 19), (12, 19)], fill=(0, 0, 0))
    d.polygon([(21, 17), (18, 16), (17, 19), (20, 19)], fill=(0, 0, 0))
    d.line([(12, 23), (16, 25), (20, 23)], fill=(200, 20, 30))
    d.point((13, 21), fill=(90, 150, 220))


PORTRAITS = {
    "kin": p_kin, "miyuki": p_miyuki, "kenmochi": p_kenmochi, "yamagami": p_yamagami, "reika": p_reika,
    "kuroki": p_kuroki, "okada": p_okada, "izumi": p_izumi, "takato": p_takato, "puppet": p_puppet,
}


def outline(img):
    src = img.copy()
    sp, dp = src.load(), img.load()
    for y in range(img.height):
        for x in range(img.width):
            if sp[x, y][3] == 0:
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    nx, ny = x + dx, y + dy
                    if 0 <= nx < img.width and 0 <= ny < img.height and sp[nx, ny][3]:
                        dp[x, y] = OUTLINE
                        break
    return img


def portrait_image(key):
    img = Image.new("RGBA", (32, 40), (0, 0, 0, 0))
    PORTRAITS[key](ImageDraw.Draw(img))
    return outline(img).resize((PORTRAIT_W, PORTRAIT_H), Image.NEAREST)


def render_portrait(key):
    return to15(portrait_image(key), dither=False)


# ---------------------------------------------------------------- evidence icons (32x32, scaled 2x)

def i_letter(d):
    d.rectangle([3, 8, 28, 24], fill=(240, 230, 210))
    d.line([(3, 8), (16, 18), (28, 8)], fill=(170, 150, 120))
    d.ellipse([13, 15, 19, 21], fill=(190, 20, 30))


def i_puppet(d):
    d.line([(8, 2), (26, 2)], fill=(150, 110, 60), width=2)
    for x, y in [(10, 16), (16, 8), (22, 16)]:
        d.line([(x, 3), (x, y)], fill=(230, 230, 240))
    d.ellipse([12, 6, 20, 14], fill=(245, 235, 225))
    d.line([(14, 12), (18, 12)], fill=(200, 20, 30))
    d.rectangle([12, 14, 20, 24], fill=(40, 40, 90))
    d.line([(12, 16), (9, 20)], fill=(40, 40, 90), width=2)
    d.line([(20, 16), (23, 20)], fill=(40, 40, 90), width=2)
    d.rectangle([13, 24, 14, 30], fill=(40, 40, 90))
    d.rectangle([18, 24, 19, 30], fill=(40, 40, 90))


def i_chain(d):
    for i in range(5):
        d.ellipse([3 + i * 5, 12 + (i % 2) * 2, 9 + i * 5, 18 + (i % 2) * 2], outline=(200, 200, 214), width=2)
    d.line([(4, 26), (28, 20)], fill=(250, 250, 255))
    d.line([(4, 27), (28, 21)], fill=(130, 180, 230))


def i_window(d):
    d.rectangle([4, 4, 27, 27], fill=(20, 30, 70))
    d.rectangle([4, 4, 27, 27], outline=(120, 80, 50), width=2)
    d.rectangle([4, 22, 27, 27], fill=(235, 240, 255))
    for x, y in [(9, 8), (19, 11), (13, 15), (23, 7), (8, 17)]:
        d.point((x, y), fill=(255, 255, 255))


def i_box(d):
    d.rectangle([6, 8, 26, 28], fill=(20, 16, 40), outline=(210, 170, 60))
    d.rectangle([8, 24, 24, 27], fill=(0, 0, 0))
    d.line([(8, 24), (16, 20)], fill=(210, 170, 60))
    for sx, sy in [(11, 13), (20, 16)]:
        d.point((sx, sy), fill=(250, 220, 90))
        d.point((sx + 1, sy), fill=(250, 220, 90))


def i_paper(d):
    d.rectangle([7, 3, 25, 29], fill=(245, 242, 230), outline=(150, 140, 120))
    for y in range(7, 27, 4):
        d.line([(10, y), (22, y)], fill=(80, 80, 100))
    d.line([(10, 15), (22, 15)], fill=(200, 30, 40))


def i_talk(d):
    d.ellipse([2, 4, 20, 18], fill=(240, 240, 250), outline=(80, 80, 100))
    d.polygon([(6, 16), (4, 22), (10, 17)], fill=(240, 240, 250))
    d.ellipse([12, 13, 30, 27], fill=(250, 230, 180), outline=(80, 80, 100))
    d.polygon([(25, 25), (28, 30), (22, 26)], fill=(250, 230, 180))


def i_cough(d):
    d.ellipse([3, 4, 29, 24], fill=(240, 240, 250), outline=(80, 80, 100))
    d.polygon([(8, 21), (6, 29), (14, 23)], fill=(240, 240, 250))
    for x in (11, 19):
        d.rectangle([x, 8, x + 2, 16], fill=(200, 30, 40))
        d.rectangle([x, 18, x + 2, 20], fill=(200, 30, 40))


def i_ice(d):
    d.polygon([(4, 12), (16, 6), (28, 12), (16, 18)], fill=(220, 240, 255))
    d.polygon([(4, 12), (16, 18), (16, 26), (4, 20)], fill=(150, 200, 240))
    d.polygon([(28, 12), (16, 18), (16, 26), (28, 20)], fill=(110, 170, 225))
    d.line([(10, 10), (14, 12)], fill=(255, 255, 255))


def i_watch(d):
    d.ellipse([10, 1, 16, 7], outline=(210, 180, 70), width=2)
    d.rectangle([12, 5, 14, 8], fill=(210, 180, 70))
    d.ellipse([4, 7, 26, 29], fill=(210, 180, 70))
    d.ellipse([7, 10, 23, 26], fill=(250, 248, 236))
    d.line([(15, 18), (11, 15)], fill=(20, 20, 20))
    d.line([(15, 18), (10, 18)], fill=(20, 20, 20))
    d.rectangle([26, 16, 29, 18], fill=(210, 180, 70))


def i_clipboard(d):
    d.rectangle([6, 4, 26, 30], fill=(150, 110, 60))
    d.rectangle([8, 7, 24, 28], fill=(245, 242, 230))
    d.rectangle([12, 2, 20, 6], fill=(170, 170, 180))
    for y in range(10, 27, 4):
        d.line([(10, y), (21, y)], fill=(40, 40, 80))


def i_glove(d):
    d.polygon([(8, 28), (8, 14), (10, 6), (13, 6), (13, 13), (14, 4), (17, 4), (17, 13), (18, 5), (21, 5),
               (21, 14), (22, 8), (25, 9), (24, 20), (22, 28)], fill=(90, 70, 60))
    for x, y in [(12, 20), (18, 23), (15, 26)]:
        d.point((x, y), fill=(120, 180, 240))
        d.point((x, y + 1), fill=(120, 180, 240))


def i_photo(d):
    d.rectangle([2, 6, 30, 26], fill=(240, 236, 220))
    d.rectangle([4, 8, 28, 24], fill=(150, 130, 100))
    for x in (7, 12, 17, 22):
        d.ellipse([x, 11, x + 4, 15], fill=(90, 70, 50))
        d.rectangle([x, 15, x + 4, 24], fill=(70, 55, 40))
    d.ellipse([24, 17, 27, 20], fill=(90, 70, 50))


def i_voice(d):
    d.ellipse([3, 4, 29, 24], fill=(230, 210, 245), outline=(110, 56, 130))
    d.polygon([(22, 21), (26, 29), (18, 23)], fill=(230, 210, 245))
    for x in (9, 15, 21):
        d.ellipse([x, 12, x + 2, 14], fill=(110, 56, 130))


def i_tape(d):
    d.rectangle([2, 7, 29, 25], fill=(40, 40, 46), outline=(120, 120, 130))
    d.rectangle([6, 10, 25, 17], fill=(240, 230, 200))
    d.ellipse([8, 11, 13, 16], fill=(40, 40, 46))
    d.ellipse([18, 11, 23, 16], fill=(40, 40, 46))
    d.polygon([(8, 25), (10, 20), (21, 20), (23, 25)], fill=(80, 80, 90))


def i_clock(d):
    d.ellipse([3, 3, 29, 29], fill=(245, 242, 230), outline=(80, 60, 50), width=2)
    d.line([(16, 16), (16, 7)], fill=(20, 20, 20), width=2)
    d.line([(16, 16), (22, 19)], fill=(200, 30, 40), width=2)


ICONS = {
    "letter": i_letter, "puppet": i_puppet, "chain": i_chain, "window": i_window, "box": i_box,
    "paper": i_paper, "talk": i_talk, "cough": i_cough, "ice": i_ice, "watch": i_watch,
    "clipboard": i_clipboard, "glove": i_glove, "photo": i_photo, "voice": i_voice, "tape": i_tape,
    "clock": i_clock,
}


def icon_image(key):
    img = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    ICONS[key](ImageDraw.Draw(img))
    return outline(img).resize((ICON_SIZE, ICON_SIZE), Image.NEAREST)


def render_icon(key):
    return to15(icon_image(key), dither=False)


def write_previews(out):
    os.makedirs(out, exist_ok=True)
    for k, f in SCENES.items():
        f().resize((W * 2, H * 2), Image.NEAREST).save(os.path.join(out, f"scene_{k}.png"))
    sheet = Image.new("RGBA", (PORTRAIT_W * len(PORTRAITS), PORTRAIT_H), (60, 60, 80, 255))
    for i, k in enumerate(PORTRAITS):
        p = portrait_image(k)
        sheet.paste(p, (i * PORTRAIT_W, 0), p)
    sheet.resize((sheet.width * 2, sheet.height * 2), Image.NEAREST).save(os.path.join(out, "portraits.png"))
    sheet = Image.new("RGBA", (ICON_SIZE * len(ICONS), ICON_SIZE), (60, 60, 80, 255))
    for i, k in enumerate(ICONS):
        p = icon_image(k)
        sheet.paste(p, (i * ICON_SIZE, 0), p)
    sheet.save(os.path.join(out, "icons.png"))
