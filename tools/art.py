"""Art for the game: scene backgrounds, character portraits and evidence icons.

User images in assets/scenes, assets/portraits and assets/icons take priority (any size;
they are resized, background-removed and converted to GBA BGR555 automatically).
Anything without a user image falls back to the built-in procedural pixel art below.
Run `make preview` to dump PNG previews.
"""
import collections
import math
import os
import random

from PIL import Image, ImageDraw, ImageEnhance, ImageFilter

import art_hd
import art_scenes2

W, H = 240, 160
PORTRAIT_W, PORTRAIT_H = 160, 160   # dialogue bust: full screen height so cut-outs reach the bottom edge
CAPTURE_W, CAPTURE_H = 128, 144     # framed screenshot busts keep their old window size
THUMB_W, THUMB_H = 64, 80           # face crop for the court record / popups
INSET_W, INSET_H = 132, 88          # small framed picture over the middle of the screen (@inset)
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


def roses(d, rng, box, n):
    x0, y0, x1, y1 = box
    for _ in range(n):
        x, y = rng.randrange(x0, x1), rng.randrange(y0, y1)
        r = rng.randrange(2, 4)
        d.ellipse([x - r, y - r, x + r, y + r], fill=rng.choice([(200, 10, 30), (160, 0, 20), (230, 30, 50)]))
        d.point((x, y), fill=(90, 0, 10))


def scene_cabin_roses():
    rng = random.Random(7)
    img, d = cabin_base(rng)
    roses(d, rng, (60, 100, 240, 160), 260)
    roses(d, rng, (160, 70, 240, 100), 60)
    d.ellipse([112, 104, 132, 124], fill=(230, 200, 180))  # the "body" among the roses
    d.polygon([(118, 120), (170, 118), (190, 140), (120, 146)], fill=(24, 24, 30))
    d.rectangle([108, 100, 136, 106], fill=(20, 20, 26))
    tint(img, (60, 0, 20), 0.15)
    return img


def scene_cabin_empty():
    rng = random.Random(7)
    img, d = cabin_base(rng)
    roses(d, rng, (60, 100, 240, 160), 260)
    roses(d, rng, (160, 70, 240, 100), 60)
    for _ in range(14):  # burst rubber scraps
        x, y = rng.randrange(90, 200), rng.randrange(110, 150)
        d.polygon([(x, y), (x + 4, y + 1), (x + 1, y + 3)], fill=(220, 210, 190))
    return img


def scene_hotel():
    img = Image.new("RGB", (W, H))
    d = ImageDraw.Draw(img)
    vgrad(d, (0, 0, W, 104), (120, 86, 56), (90, 60, 40))
    for x in range(0, W, 30):
        d.line([(x, 0), (x, 104)], fill=(80, 54, 34))
    d.rectangle([0, 104, W, H], fill=(70, 40, 34))
    for x in range(0, W, 20):
        d.line([(x, 104), (x - 30, H)], fill=(60, 34, 28))
    d.rectangle([30, 60, 150, 104], fill=(60, 36, 24), outline=(170, 130, 60))  # front desk
    d.rectangle([30, 56, 150, 62], fill=(170, 130, 60))
    d.rectangle([60, 20, 120, 50], fill=(40, 26, 18), outline=(170, 130, 60))  # key board
    for x in range(64, 118, 8):
        for y in (26, 36):
            d.point((x, y), fill=(230, 200, 90))
    d.rectangle([176, 50, 212, 104], fill=(50, 34, 24))  # jade display
    for x, y in [(184, 64), (198, 70), (190, 84)]:
        d.ellipse([x, y, x + 8, y + 6], fill=(60, 170, 110), outline=(30, 90, 60))
    d.ellipse([110, 4, 130, 14], fill=(255, 236, 180))
    glow(img, 120, 10, 50, (255, 220, 150), 0.35)
    night_window(img, d, random.Random(8), (190, 10, 230, 40), frame=(140, 100, 50))
    return img


def scene_theater():
    img = scene_stage()
    d = ImageDraw.Draw(img)
    d.rectangle([90, 38, 150, 102], fill=(40, 20, 30))  # hide the magic box
    d.rectangle([0, 0, W, 5], fill=(200, 160, 60))
    for x in (100, 140):  # ropes from the ceiling rigging
        d.line([(x, 0), (x, 40)], fill=(200, 190, 160))
    d.rectangle([90, 0, 150, 6], fill=(60, 50, 50))
    d.rectangle([106, 70, 134, 100], fill=(120, 60, 30))  # chair
    d.rectangle([108, 60, 132, 72], fill=(120, 60, 30))
    d.ellipse([112, 42, 128, 58], fill=(236, 226, 214), outline=OUTLINE[:3])  # marionette
    d.rectangle([110, 58, 130, 80], fill=(170, 30, 40))
    d.line([(115, 80), (115, 96)], fill=(170, 30, 40), width=3)
    d.line([(125, 80), (125, 96)], fill=(170, 30, 40), width=3)
    return img


def scene_swamp():
    img = Image.new("RGB", (W, H))
    d = ImageDraw.Draw(img)
    rng = random.Random(10)
    vgrad(d, (0, 0, W, 90), (20, 26, 40), (60, 70, 80))
    for x in range(0, W, 3):
        h = 30 + int(12 * math.sin(x * 0.05)) + rng.randrange(8)
        d.line([(x, 90 - h), (x, 90)], fill=(24, 34, 30))
    vgrad(d, (0, 90, W, H), (40, 50, 44), (20, 26, 22))
    for _ in range(80):
        x, y = rng.randrange(W), rng.randrange(96, H)
        d.line([(x, y), (x + rng.randrange(4, 14), y)], fill=(70, 86, 80))
    for x in (30, 200):  # dead trees
        d.line([(x, 90), (x + 4, 30)], fill=(16, 16, 16), width=3)
        d.line([(x + 3, 50), (x + 16, 40)], fill=(16, 16, 16), width=2)
    for y in range(40, 110, 6):  # fog
        for x in range(0, W, 2):
            if rng.random() < 0.18:
                d.point((x, y + rng.randrange(6)), fill=(150, 160, 170))
    return img


def scene_cabin_roses_hand():
    img = scene_cabin_roses()
    d = ImageDraw.Draw(img)
    d.line([(112, 118), (100, 104)], fill=(24, 24, 30), width=3)   # left arm raised a little
    d.ellipse([96, 100, 102, 106], fill=(230, 200, 180))           # hand
    d.line([(99, 100), (97, 84)], fill=(220, 220, 230))            # string
    d.ellipse([90, 70, 104, 86], fill=(230, 60, 70))               # small balloon
    d.point((94, 74), fill=(255, 200, 200))
    return img


BUILTIN_SCENES = {
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
    "police": art_hd.scene_police,
    "cabin_roses": scene_cabin_roses,
    "cabin_empty": scene_cabin_empty,
    "hotel": scene_hotel,
    "hotel_room": art_hd.scene_hotel_room,
    "theater": scene_theater,
    "swamp": scene_swamp,
    "cabin_roses_hand": scene_cabin_roses_hand,
    "cabin_smoke": lambda: art_hd.smoke_over(scene_image("cut_roses")),
    **art_hd.CARDS,
    **art_hd.LOCATIONS,
}
# every drawn location gets the finishing pass (soft bloom, grade, fine grain)
for _k, _f in list(art_hd.LOCATIONS.items()):
    if _k != "title":
        BUILTIN_SCENES[_k] = (lambda f=_f, k=_k: art_scenes2.polish(f(), seed=sum(map(ord, k))))
BUILTIN_SCENES.update(art_scenes2.SCENES2)
BUILTIN_SCENES["lobby_night"] = lambda: art_scenes2.scene_lobby_night(lambda: scene_image("hotel"))
# locations normally taken from anime frames (tools/import_bg.py); drawn stand-ins otherwise
BUILTIN_SCENES["hotel_hall"] = lambda: scene_image("lobby_night")
BUILTIN_SCENES["bridge_up"] = lambda: scene_image("drawbridge")
BUILTIN_SCENES["yumi_tree"] = lambda: scene_image("yumi_room")
BUILTIN_SCENES["cabin_door"] = lambda: scene_image("corridor")
BUILTIN_SCENES["freight_petals"] = lambda: scene_image("freight_yard")
BUILTIN_SCENES["dining_show"] = lambda: scene_image("dining")
BUILTIN_SCENES["cabin_reenact"] = lambda: art_scenes2.polish(art_hd.scene_cabin_roses(), seed=5)

# Anime captures shown with @cut at key moments (assets/cuts/cut_*.png, made by tools/import_screenshots.py).
# Without the capture, a drawn stand-in is used so the story still builds.
BUILTIN_CUTS = {
    "cut_parcel": art_hd.scene_police,
    "cut_roses": art_hd.scene_cabin_roses,
    "cut_balloons": scene_cabin_roses_hand,
    "cut_mario": art_hd.scene_hotel,
    "cut_body": art_hd.card_prologue,
    "cut_marionette": art_hd.scene_theater,
    "cut_yurama": art_hd.scene_theater,
    "cut_fog": art_hd.scene_swamp,
    "cut_sinking": art_hd.scene_swamp,
    "cut_jade": art_hd.scene_hotel_room,
    "cut_bag": art_hd.scene_hotel,
    "cut_takato": art_hd.scene_hotel,
    "cut_reiko": art_hd.card_final,
    "cut_rock": art_hd.card_final,
    "cut_fire": art_hd.card_final,
    "cut_burst": lambda: scene_image("freight_yard"),
    "cut_petals": lambda: scene_image("freight_yard"),
    "cut_akechi_feet": lambda: scene_image("freight_yard"),
    "cut_akechi": lambda: scene_image("freight_yard"),
    "cut_salad_boom": lambda: scene_image("dining_show"),
    "cut_salad_blast": lambda: scene_image("dining_show"),
}
BUILTIN_SCENES.update(BUILTIN_CUTS)


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


def p_saki(d):
    hair = (40, 34, 30)
    body(d, (30, 36, 60), shirt=(240, 240, 240))
    face(d)
    d.ellipse([8, 5, 24, 16], fill=hair)
    d.rectangle([8, 9, 24, 13], fill=hair)
    for x in (10, 17):
        d.rectangle([x, 16, x + 5, 20], outline=(50, 50, 60))
    d.line([(15, 18), (17, 18)], fill=(50, 50, 60))
    eyes(d, y=17)
    mouth(d, "normal", 24)
    d.rectangle([22, 30, 31, 37], fill=(60, 60, 66))  # camcorder
    d.ellipse([27, 31, 31, 35], fill=(20, 20, 30))
    d.point((24, 32), fill=(220, 40, 40))


def p_akechi(d):
    hair = (170, 150, 120)
    d.rectangle([8, 10, 24, 20], fill=hair)
    body(d, (60, 60, 70), shirt=(240, 240, 240))
    d.line([(16, 31), (16, 38)], fill=(40, 60, 120), width=2)
    face(d, skin=(248, 222, 200))
    d.ellipse([7, 3, 25, 15], fill=hair)
    d.polygon([(7, 8), (18, 8), (9, 17)], fill=hair)
    d.polygon([(25, 8), (20, 8), (24, 16)], fill=hair)
    eyes(d, "sharp", color=(40, 60, 80))
    mouth(d, "smile")


def p_yumi(d):
    hair = (40, 100, 120)
    d.polygon([(6, 10), (26, 10), (29, 38), (3, 38)], fill=hair)
    body(d, (30, 120, 150))
    for x, y in [(9, 36), (22, 34), (16, 39)]:
        d.point((x, y), fill=(200, 240, 255))
    face(d, skin=(250, 222, 205))
    d.ellipse([7, 4, 25, 16], fill=hair)
    d.polygon([(9, 9), (20, 9), (10, 16)], fill=hair)
    eyes(d, "female", color=(30, 80, 100))
    mouth(d, "lips")
    d.rectangle([7, 22, 8, 24], fill=(200, 240, 255))


def p_sakonji(d):
    body(d, (200, 60, 60))
    for x in (8, 16, 24):
        d.ellipse([x - 2, 31, x + 2, 35], fill=(250, 230, 80))
    d.polygon([(10, 32), (16, 30), (22, 32), (16, 35)], fill=(250, 250, 250))
    face(d, skin=(245, 245, 245))
    d.polygon([(8, 11), (24, 11), (16, 0)], fill=(60, 60, 170))
    d.ellipse([14, 0, 18, 3], fill=(250, 230, 80))
    d.rectangle([7, 11, 9, 17], fill=(230, 120, 40))
    d.rectangle([23, 11, 25, 17], fill=(230, 120, 40))
    for x in (11, 19):
        d.line([(x, 15), (x + 2, 15)], fill=(40, 40, 60))
        d.line([(x + 1, 21), (x + 1, 22)], fill=(60, 60, 170))
    eyes(d, "smile", color=(30, 30, 40))
    d.ellipse([14, 20, 18, 23], fill=(220, 30, 30))
    d.arc([11, 21, 21, 27], 10, 170, fill=(200, 30, 40))


def p_yurama(d):
    hair = (230, 200, 110)
    body(d, (240, 240, 244), shirt=(40, 40, 50))
    d.polygon([(13, 31), (16, 32), (19, 31), (19, 34), (16, 33), (13, 34)], fill=(200, 170, 60))
    face(d, skin=(248, 220, 196))
    d.ellipse([7, 3, 26, 13], fill=hair)
    d.polygon([(7, 8), (26, 6), (27, 10), (14, 11)], fill=hair)
    d.rectangle([7, 9, 9, 15], fill=hair)
    eyes(d, "sharp", color=(40, 70, 120))
    d.line([(14, 24), (18, 23)], fill=(150, 70, 70))


def p_sakuraba(d):
    hair = (20, 20, 24)
    body(d, (80, 40, 110))
    d.polygon([(11, 30), (16, 38), (21, 30)], fill=(220, 190, 80))
    face(d, skin=(245, 242, 238), wide=1)
    d.ellipse([8, 3, 24, 13], fill=hair)
    d.ellipse([13, 0, 19, 5], fill=hair)
    d.line([(9, 15), (13, 13)], fill=(200, 20, 30), width=1)
    d.line([(23, 15), (19, 13)], fill=(200, 20, 30), width=1)
    d.line([(10, 19), (12, 24)], fill=(200, 20, 30))
    d.line([(22, 19), (20, 24)], fill=(200, 20, 30))
    eyes(d, "normal", color=(20, 20, 20))
    mouth(d, "frown", 25)


def p_satomi(d):
    hair = (90, 50, 30)
    d.ellipse([3, 12, 9, 24], fill=hair)
    d.ellipse([23, 12, 29, 24], fill=hair)
    body(d, (40, 50, 90), collar=(230, 200, 80))
    face(d)
    d.ellipse([8, 5, 24, 16], fill=hair)
    d.rectangle([8, 5, 24, 9], fill=(40, 50, 90))
    d.rectangle([6, 8, 26, 10], fill=(20, 26, 50))
    d.rectangle([14, 5, 18, 7], fill=(230, 200, 80))
    eyes(d, "female", color=(80, 50, 30))
    mouth(d, "smile")
    d.ellipse([23, 29, 29, 35], fill=(245, 235, 225))  # little marionette "로버트"
    d.rectangle([24, 35, 28, 40], fill=(170, 30, 40))
    d.line([(24, 22), (26, 29)], fill=(230, 230, 240))


def p_nagasaki(d):
    body(d, (50, 40, 36), shirt=(240, 240, 236))
    d.polygon([(13, 31), (16, 32), (19, 31), (19, 34), (16, 33), (13, 34)], fill=(20, 60, 50))
    face(d, skin=(232, 196, 164), wide=1)
    d.rectangle([7, 11, 10, 18], fill=(190, 190, 196))
    d.rectangle([22, 11, 25, 18], fill=(190, 190, 196))
    for x in (10, 17):
        d.ellipse([x, 16, x + 5, 20], outline=(90, 80, 60))
    d.line([(15, 18), (17, 18)], fill=(90, 80, 60))
    eyes(d, y=17)
    d.polygon([(12, 23), (16, 22), (20, 23), (19, 25), (13, 25)], fill=(200, 200, 206))
    d.line([(11, 13), (13, 12)], fill=(200, 170, 140))


def p_clown(d):
    cloak = (18, 14, 22)
    d.polygon([(0, 40), (4, 26), (8, 6), (16, 1), (24, 6), (28, 26), (32, 40)], fill=cloak)
    d.ellipse([10, 10, 22, 27], fill=(240, 236, 230))
    d.polygon([(11, 17), (14, 16), (15, 19), (12, 19)], fill=(0, 0, 0))
    d.polygon([(21, 17), (18, 16), (17, 19), (20, 19)], fill=(0, 0, 0))
    d.line([(12, 23), (16, 25), (20, 23)], fill=(200, 20, 30))
    d.ellipse([22, 29, 28, 35], fill=(210, 20, 40))  # blood-red rose
    d.line([(25, 35), (24, 40)], fill=(40, 120, 50))


def p_mario(d):
    body(d, (70, 60, 50))
    d.rectangle([10, 26, 22, 32], fill=(150, 30, 30))  # scarf
    d.ellipse([9, 9, 23, 28], fill=(236, 236, 240))
    d.rectangle([12, 17, 14, 18], fill=(10, 10, 10))
    d.rectangle([18, 17, 20, 18], fill=(10, 10, 10))
    d.line([(14, 24), (18, 24)], fill=(120, 120, 130))
    d.rectangle([9, 6, 23, 11], fill=(40, 34, 30))
    d.rectangle([4, 10, 28, 12], fill=(40, 34, 30))


BUILTIN_PORTRAITS = {
    "kin": p_kin, "miyuki": p_miyuki, "saki": p_saki, "kenmochi": p_kenmochi, "akechi": p_akechi,
    "yamagami": p_yamagami, "yumi": p_yumi, "sakonji": p_sakonji, "yurama": p_yurama,
    "sakuraba": p_sakuraba, "takato": p_takato, "satomi": p_satomi, "nagasaki": p_nagasaki,
    "clown": p_clown, "mario": p_mario,
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


def i_jade(d):
    for x, y in [(4, 14), (14, 8), (17, 18)]:
        d.ellipse([x, y, x + 12, y + 9], fill=(60, 170, 110), outline=(30, 90, 60))
        d.point((x + 3, y + 2), fill=(200, 255, 220))


def i_balloon(d):
    d.ellipse([8, 2, 24, 20], fill=(230, 220, 200))
    d.polygon([(15, 20), (17, 20), (16, 23)], fill=(230, 220, 200))
    d.line([(16, 23), (13, 30)], fill=(200, 200, 210))
    d.point((12, 6), fill=(255, 255, 255))
    for x, y in [(4, 26), (24, 24), (26, 28)]:
        d.polygon([(x, y), (x + 4, y + 1), (x + 1, y + 3)], fill=(220, 210, 190))


def i_rose(d):
    d.ellipse([9, 4, 23, 17], fill=(200, 10, 30))
    d.arc([12, 7, 20, 14], 0, 300, fill=(120, 0, 20))
    d.line([(16, 17), (15, 30)], fill=(40, 120, 50), width=2)
    d.polygon([(15, 22), (8, 20), (12, 25)], fill=(50, 140, 60))


def i_camera(d):
    d.rectangle([3, 10, 22, 24], fill=(60, 60, 66))
    d.polygon([(22, 12), (29, 8), (29, 26), (22, 22)], fill=(40, 40, 46))
    d.ellipse([6, 13, 14, 21], fill=(20, 20, 30), outline=(120, 120, 140))
    d.point((19, 13), fill=(230, 40, 40))


def i_bag(d):
    d.rectangle([6, 12, 26, 28], fill=(120, 70, 40), outline=(70, 40, 20))
    d.arc([11, 4, 21, 16], 180, 360, fill=(70, 40, 20), width=2)
    d.line([(6, 20), (26, 20)], fill=(90, 50, 26))
    d.rectangle([14, 18, 18, 22], fill=(220, 190, 80))


def i_rope(d):
    d.rectangle([4, 2, 28, 5], fill=(90, 80, 80))
    d.ellipse([12, 4, 20, 12], outline=(160, 160, 170), width=2)
    d.line([(13, 10), (8, 30)], fill=(210, 190, 140), width=2)
    d.line([(19, 10), (24, 22)], fill=(210, 190, 140), width=2)
    d.ellipse([20, 20, 28, 30], fill=(170, 30, 40))


def i_scale(d):
    d.rectangle([4, 20, 28, 29], fill=(220, 220, 226), outline=(120, 120, 130))
    d.ellipse([9, 6, 23, 20], fill=(250, 250, 250), outline=(120, 120, 130))
    d.line([(16, 13), (20, 9)], fill=(200, 30, 40))
    for x in (11, 16, 21):
        d.point((x, 8), fill=(40, 40, 40))


def i_nail(d):
    for i, x in enumerate((8, 16, 24)):
        d.rectangle([x - 3, 5, x + 3, 7], fill=(170, 170, 180) if i else (120, 80, 50))
        d.line([(x, 7), (x, 26)], fill=(190, 190, 200) if i else (130, 90, 60), width=2)
    d.rectangle([2, 26, 30, 30], fill=(140, 100, 60))


def i_mask(d):
    d.ellipse([7, 4, 25, 28], fill=(236, 236, 240))
    d.rectangle([11, 13, 14, 15], fill=(10, 10, 10))
    d.rectangle([18, 13, 21, 15], fill=(10, 10, 10))
    d.line([(13, 22), (19, 22)], fill=(120, 120, 130))


def i_train(d):
    d.rectangle([2, 10, 30, 24], fill=(70, 60, 50), outline=(40, 34, 28))
    for x in (5, 14, 23):
        d.rectangle([x, 13, x + 5, 20], fill=(120, 100, 70))
    for x in (7, 24):
        d.ellipse([x - 3, 22, x + 3, 28], fill=(30, 30, 30))


BUILTIN_ICONS = {
    "letter": i_letter, "puppet": i_puppet, "chain": i_chain, "window": i_window, "box": i_box,
    "paper": i_paper, "talk": i_talk, "cough": i_cough, "ice": i_ice, "watch": i_watch,
    "clipboard": i_clipboard, "glove": i_glove, "photo": i_photo, "voice": i_voice, "tape": i_tape,
    "clock": i_clock, "jade": i_jade, "balloon": i_balloon, "rose": i_rose, "camera": i_camera,
    "bag": i_bag, "rope": i_rope, "scale": i_scale, "nail": i_nail, "mask": i_mask, "train": i_train,
}


# ---------------------------------------------------------------- user images

ASSET_DIR = os.environ.get("KMT_ASSETS", os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "assets"))
IMG_EXT = (".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif")


def user_files(sub):
    found = {}
    d = os.path.join(ASSET_DIR, sub)
    if os.path.isdir(d):
        for f in sorted(os.listdir(d)):
            stem, ext = os.path.splitext(f)
            if ext.lower() in IMG_EXT and not stem.startswith("."):
                found[stem] = os.path.join(d, f)
    return found


USER_SCENES = user_files("scenes")
USER_SCENES.update({k: v for k, v in user_files("cuts").items() if k.startswith("cut_")})
USER_PORTRAITS = user_files("portraits")
USER_ICONS = user_files("icons")

SCENES = dict(BUILTIN_SCENES)
SCENES.update({k: None for k in USER_SCENES if k not in SCENES})
PORTRAITS = dict(BUILTIN_PORTRAITS)
PORTRAITS.update({k: None for k in USER_PORTRAITS if k not in PORTRAITS})
ICONS = dict(BUILTIN_ICONS)
ICONS.update({k: None for k in USER_ICONS if k not in ICONS})
ALWAYS_SCENES = ["black", "title"]


def remove_background(img, tol=48):
    """Flood-fill the dominant border colour to transparent (for images without alpha)."""
    w, h = img.size
    px = img.load()
    border = [(x, 0) for x in range(w)] + [(x, h - 1) for x in range(w)] + \
             [(0, y) for y in range(h)] + [(w - 1, y) for y in range(h)]
    counts = collections.Counter(tuple(c // 16 for c in px[p][:3]) for p in border)
    key, n = counts.most_common(1)[0]
    if n < len(border) * 0.4:
        return img  # no uniform background: keep as is
    ref = [sum(px[p][i] for p in border if tuple(c // 16 for c in px[p][:3]) == key) / n for i in range(3)]

    def close(c):
        return abs(c[0] - ref[0]) + abs(c[1] - ref[1]) + abs(c[2] - ref[2]) <= tol

    seen = bytearray(w * h)
    q = collections.deque()
    for x, y in border:
        if not seen[y * w + x] and close(px[x, y]):
            seen[y * w + x] = 1
            q.append((x, y))
    while q:
        x, y = q.popleft()
        px[x, y] = (0, 0, 0, 0)
        for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
            if 0 <= nx < w and 0 <= ny < h and not seen[ny * w + nx] and close(px[nx, ny]):
                seen[ny * w + nx] = 1
                q.append((nx, ny))
    return img


def load_rgba(path, max_w, max_h):
    img = Image.open(path)
    has_alpha = img.mode in ("RGBA", "LA", "PA") or (img.mode == "P" and "transparency" in img.info)
    img = img.convert("RGBA")
    scale = min(1.0, max_w * 3 / img.width, max_h * 3 / img.height)  # shrink huge images first
    if scale < 1.0:
        img = img.resize((max(1, int(img.width * scale)), max(1, int(img.height * scale))), Image.LANCZOS)
    if not has_alpha:  # images with an alpha channel are used as-is (opaque ones become framed busts)
        img = remove_background(img)
    bbox = img.getchannel("A").point(lambda a: 255 if a >= 128 else 0).getbbox()
    return img.crop(bbox) if bbox else img


def fit(img, w, h, anchor_bottom=True):
    scale = min(w / img.width, h / img.height)
    img = img.resize((max(1, round(img.width * scale)), max(1, round(img.height * scale))), Image.LANCZOS)
    canvas = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    canvas.paste(img, ((w - img.width) // 2, h - img.height if anchor_bottom else (h - img.height) // 2))
    return canvas


# ---------------------------------------------------------------- pixel-art conversion of user images
#   KMT_PIXEL=off (default): user images are used exactly as drawn (resized/colour-converted only)
#   KMT_PIXEL=sprite: clean Ace-Attorney-style sprites - transparent cut-out art is scaled to
#                               the bust size, palette-limited and outlined (opaque images fall back to gbc)
#   KMT_PIXEL=gbc: redrawn like the Game Boy Color Kindaichi game - black 1px line art traced
#                            from the reference, flat cel colours from a small palette, GBA resolution
#   KMT_PIXEL=chunky: characters 64x72 and scenes 120x80, shown at 2x
#   KMT_PIXEL=native: GBA resolution with a reduced palette
PIXEL_STYLE = os.environ.get("KMT_PIXEL", "off")


def sprite(img, w, h, colors=48):
    """Transparent illustration -> GBA sprite: fit to w x h, hard alpha, reduced palette, dark outline."""
    img = img.convert("RGBA")
    scale = min(w / img.width, h / img.height)
    img = img.resize((max(1, round(img.width * scale)), max(1, round(img.height * scale))), Image.LANCZOS)
    canvas = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    canvas.paste(img, ((w - img.width) // 2, h - img.height))
    alpha = canvas.getchannel("A").point(lambda v: 255 if v >= 140 else 0)
    rgb = Image.new("RGB", (w, h), (0, 0, 0))
    rgb.paste(canvas.convert("RGB"), (0, 0), alpha)
    flat = rgb.quantize(colors=colors, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE).convert("RGBA")
    flat.putalpha(alpha)
    return outline_rgba(flat, (30, 20, 26, 255))
INK = (22, 16, 24)


def vivid(img, sat=1.35, contrast=1.1):
    """Undo the old-video yellow cast (hue-preserving levels + gentle grey world) and boost colour."""
    import numpy as np
    a = np.asarray(img.convert("RGB"), dtype=np.float32)
    lum = a.mean(axis=2)
    lo, hi = np.percentile(lum, 1), np.percentile(lum, 99)
    a = np.clip((a - lo) / max(hi - lo, 1) * 255, 0, 255)
    mean = a.reshape(-1, 3).mean(axis=0)
    a = np.clip(a * (mean.mean() / mean) ** 0.3, 0, 255)
    img = Image.fromarray(a.astype(np.uint8))
    return ImageEnhance.Color(ImageEnhance.Contrast(img).enhance(contrast)).enhance(sat)


def trace(img, w, h, colors=10, dark=70, rel=38):
    """GBC-style redraw: 1px black line art + flat colours reduced to a small palette."""
    import numpy as np
    img = vivid(img)
    big = img.resize((w * 2, h * 2), Image.LANCZOS)
    lum = np.asarray(big.convert("L"), dtype=np.float32)
    blur = np.asarray(big.convert("L").filter(ImageFilter.GaussianBlur(3)), dtype=np.float32)
    ink = (((lum < dark) | (lum < blur - rel)).reshape(h, 2, w, 2).sum(axis=(1, 3))) >= 2
    base = img.resize((w, h), Image.LANCZOS).filter(ImageFilter.MedianFilter(3))
    flat = base.quantize(colors=colors, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE)
    out = np.asarray(flat.filter(ImageFilter.ModeFilter(3)).convert("RGB")).copy()
    out[ink] = INK
    return Image.fromarray(out)


def outline_rgba(img, color=(24, 16, 28, 255)):
    src = img.copy()
    sp, dp = src.load(), img.load()
    w, h = img.size
    for y in range(h):
        for x in range(w):
            if sp[x, y][3] == 0:
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    nx, ny = x + dx, y + dy
                    if 0 <= nx < w and 0 <= ny < h and sp[nx, ny][3]:
                        dp[x, y] = color
                        break
    return img


def pixelize(img, w, h, colors, scale):
    """Downscale, flatten the palette, outline the silhouette, blow back up with hard pixels."""
    img = img.convert("RGBA")
    rgb = ImageEnhance.Color(img.convert("RGB")).enhance(1.15)
    rgb = ImageEnhance.Contrast(rgb).enhance(1.08)
    small = rgb.resize((w, h), Image.LANCZOS).filter(ImageFilter.UnsharpMask(1, 80, 1))
    small = small.quantize(colors=colors, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE).convert("RGBA")
    alpha = img.getchannel("A").resize((w, h), Image.LANCZOS).point(lambda v: 255 if v >= 120 else 0)
    small.putalpha(alpha)
    if alpha.getextrema()[0] == 0:
        small = outline_rgba(small)
    return small.resize((w * scale, h * scale), Image.NEAREST)


def scene_image(key):
    if key in USER_SCENES:
        img = Image.open(USER_SCENES[key]).convert("RGB")
        scale = max(W / img.width, H / img.height)
        img = img.resize((max(W, round(img.width * scale)), max(H, round(img.height * scale))), Image.LANCZOS)
        x, y = (img.width - W) // 2, (img.height - H) // 2
        img = img.crop((x, y, x + W, y + H))
        if PIXEL_STYLE in ("gbc", "sprite"):
            src = Image.open(USER_SCENES[key]).convert("RGB")
            img = trace(src, W, H, 16)
        elif PIXEL_STYLE == "chunky":
            img = pixelize(img, W // 2, H // 2, 40, 2).convert("RGB")
        elif PIXEL_STYLE == "native":
            img = pixelize(img, W, H, 64, 1).convert("RGB")
        return img
    return BUILTIN_SCENES[key]()


def render_scene(key):
    if key in PETAL_SCENES:
        return [int(v) for v in petal_frames(key)[0].ravel()]
    return to15(scene_image(key))


# Scenes with red rose petals drifting down over another scene (the fake bomb on the train roof).
PETAL_SCENES = {"freight_petals": "freight_yard"}
PETAL_MAX_Y = 100  # petals stay above the name tag and text box (the animated rows)


def _c15(r, g, b):
    return (b >> 3) << 10 | (g >> 3) << 5 | (r >> 3)


# petal shapes (tumbling): 1 bright, 2 mid, 3 dark, 4 highlight
PETAL_SHAPES = [
    [".12..", "11222", "..23."],
    [".1.", "142", "122", ".3."],
    [".11.", "1422", ".23."],
    ["12.", "422", ".23"],
]
PETAL_SMALL = [["12", "23"], ["1.", "22", ".3"], ["122"], [".1", "23"]]
PETAL_COLS = {"1": _c15(236, 40, 56), "2": _c15(196, 20, 40), "3": _c15(120, 8, 24), "4": _c15(255, 150, 160)}
_petal_cache = {}


def petal_frames(key):
    """ANIM_FRAMES 15-bit frames of the base scene with petals falling and swaying in a loop."""
    import math
    import random
    import numpy as np
    if key in _petal_cache:
        return _petal_cache[key]
    base = np.array(render_scene(PETAL_SCENES[key]), dtype=np.uint16).reshape(H, W)
    rnd = random.Random(7)
    span = PETAL_MAX_Y + 12
    big = [[("".join(c * 2 for c in row)) for row in shape for _ in (0, 1)] for shape in PETAL_SHAPES]
    petals = []
    for i in range(80):  # three depths: big and fast in front, small and slow far away
        layer = 0 if i < 16 else 1 if i < 46 else 2
        petals.append(dict(x=rnd.uniform(-10, W), y=rnd.uniform(0, span), k=(3, 2, 1)[layer],
                           sway=rnd.uniform(*((5, 12), (2, 6), (1, 3))[layer]), ph=rnd.random(),
                           rot=rnd.randrange(4), shapes=(big, PETAL_SHAPES, PETAL_SMALL)[layer]))
    frames = []
    for t in range(ANIM_FRAMES):
        f = base.copy()
        for p in petals:
            y = int((p["y"] + t * span * p["k"] / ANIM_FRAMES) % span) - 12
            x = int(p["x"] + p["sway"] * math.sin(2 * math.pi * (t / ANIM_FRAMES + p["ph"])))
            shape = p["shapes"][(p["rot"] + t // 6) % 4]
            for dy, row in enumerate(shape):
                for dx, ch in enumerate(row):
                    yy, xx = y + dy, x + dx
                    if ch != "." and 0 <= yy < PETAL_MAX_Y and 0 <= xx < W:
                        f[yy, xx] = PETAL_COLS[ch]
        frames.append(f)
    _petal_cache[key] = frames
    return frames


ANIM_FRAMES = 48  # one step every 2 frames: a 1.6 s loop
ANIMATE = False  # looping snow animation (off: backgrounds are still pictures)


def scene_anim(key, max_y):
    """Looping background animation for a built-in scene (falling snow, the view streaming
    past train windows). Returns a list of ANIM_FRAMES diffs, entry t turning frame t-1 into
    frame t (entry 0: last frame -> frame 0), each as (y0, y1, [(offset, [pixels])...]),
    limited to rows < max_y; or None for a static scene."""
    import numpy as np
    if key in PETAL_SCENES:
        frames = petal_frames(key)
    else:
        if not ANIMATE or key in USER_SCENES or key not in art_hd.LOCATIONS:
            return None
        frames = []
        for t in range(ANIM_FRAMES):
            art_hd.ANIM = (t, ANIM_FRAMES)
            try:
                frames.append(np.array(to15(art_hd.LOCATIONS[key]()), dtype=np.uint16).reshape(H, W))
            finally:
                art_hd.ANIM = None
            if t == 1 and np.array_equal(frames[0][:max_y], frames[1][:max_y]):
                return None
    out = []
    for t in range(ANIM_FRAMES):
        prev, cur = frames[t - 1][:max_y], frames[t][:max_y]
        diff = prev != cur
        rows = np.nonzero(diff.any(axis=1))[0]
        runs = []
        for y in rows:
            xs = np.nonzero(diff[y])[0]
            start = xs[0]
            for i in range(1, len(xs) + 1):
                if i == len(xs) or xs[i] != xs[i - 1] + 1:
                    end = xs[i - 1] + 1
                    runs.append((int(y) * W + int(start), [int(v) for v in cur[y, start:end]]))
                    if i < len(xs):
                        start = xs[i]
        out.append((int(rows[0]) if len(rows) else 0, int(rows[-1]) + 1 if len(rows) else 0, runs))
    return out


def _procedural_portrait(key):
    img = Image.new("RGBA", (32, 40), (0, 0, 0, 0))
    BUILTIN_PORTRAITS[key](ImageDraw.Draw(img))
    return outline(img)


OUTLINE_PX = 1.0             # dark outline round the characters so they stand out from the scene
OUTLINE_RGB = (22, 14, 28)
KEEP_SIDES = {"sakuraba_stage"}  # wide art shown whole (the arms reach the picture's edges)


def ace_sprite(img, w, h, crop=1.0, trim=True):
    """Cut-out drawing -> Ace-Attorney-style bust: cropped head to waist, scaled with
    premultiplied supersampling so the edges follow the drawing's own line art (no extra
    outline or shadow); partly covered edge pixels are pulled toward the line colour."""
    bb = img.getchannel("A").getbbox()
    img = img.crop(bb) if bb else img
    img = img.crop((0, 0, img.width, max(1, int(img.height * crop))))
    max_w = int(img.height * w / h * 1.1)  # wide art (capes, props): trim the sides to keep the bust big
    if trim and img.width > max_w:
        x0 = (img.width - max_w) // 2
        img = img.crop((x0, 0, x0 + max_w, img.height))
    sc = min(w * 0.86 / img.width, h * 0.84 / img.height)  # leave some of the scene visible around the bust
    tw, th = max(1, round(img.width * sc)), max(1, round(img.height * sc))
    K = 4
    big = img.resize((tw * K, th * K), Image.LANCZOS)
    ow = OUTLINE_PX
    if ow:  # a dark outline all round (not along the bottom), drawn at 4x so its edge stays soft
        pad = round(ow * K)
        padded = Image.new("RGBA", (big.width + 2 * pad, big.height + pad), (0, 0, 0, 0))
        padded.paste(big, (pad, pad))
        ring = padded.getchannel("A").filter(ImageFilter.MaxFilter(2 * pad + 1))
        line = Image.new("RGBA", padded.size, OUTLINE_RGB + (0,))
        line.putalpha(ring)
        big = Image.alpha_composite(line, padded)
        tw, th = big.width // K, big.height // K
        big = big.crop((0, 0, tw * K, th * K))
    a = big.getchannel("A")
    pm = Image.composite(big.convert("RGB"), Image.new("RGB", big.size, (0, 0, 0)), a).resize((tw, th), Image.BOX)
    cov = a.resize((tw, th), Image.BOX)
    out = Image.new("RGBA", (tw, th), (0, 0, 0, 0))
    pp, cp, op = pm.load(), cov.load(), out.load()
    for y in range(th):
        for x in range(tw):
            c = cp[x, y]
            if c < 56:
                continue
            f = 255 / c
            col = [min(255, int(v * f)) for v in pp[x, y]]
            op[x, y] = tuple(col) + (255 if c >= 200 else 128,)  # 128 = soft edge, blended 50% in game
    canvas = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    canvas.paste(out, ((w - tw) // 2, h - th))
    return canvas


def stage_look(img):
    """Cut-out bust -> game look: slight contrast boost, warm rim light on the top-left edge,
    1px ink outline and a dark drop shadow so the figure sits on the scene."""
    from PIL import ImageChops, ImageEnhance
    w, h = img.size
    a = img.getchannel("A").point(lambda v: 255 if v >= 128 else 0)
    rgb = ImageEnhance.Color(ImageEnhance.Contrast(img.convert("RGB")).enhance(1.12)).enhance(1.1)
    shifted = Image.new("L", (w, h), 0)
    shifted.paste(a, (2, 2))
    rim = ImageChops.subtract(a, shifted)
    rgb = Image.composite(Image.blend(rgb, Image.new("RGB", (w, h), (255, 236, 190)), 0.55), rgb, rim)
    ol = a.filter(ImageFilter.MaxFilter(3))
    shadow = Image.new("L", (w, h), 0)
    shadow.paste(ol, (3, 2))
    out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    out.paste(Image.new("RGBA", (w, h), (10, 8, 20, 255)), (0, 0), shadow)
    out.paste(Image.new("RGBA", (w, h), (18, 12, 20, 255)), (0, 0), ol)
    out.paste(rgb.convert("RGBA"), (0, 0), a)
    return out


def framed(img):
    """Opaque (screenshot-style) portraits get a thin frame so they read as a cut-in window."""
    if img.getextrema()[3][0] < 255:
        return img
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, img.width - 1, img.height - 1], outline=(20, 16, 24, 255), width=2)
    d.rectangle([2, 2, img.width - 3, img.height - 3], outline=(200, 160, 70, 255), width=1)
    return img


def portrait_image(key):
    if key in USER_PORTRAITS and PIXEL_STYLE != "off":
        # the whole picture keeps its framing (crop it to 128:144 beforehand); cut-outs get an outline
        src = Image.open(USER_PORTRAITS[key]).convert("RGBA")
        if PIXEL_STYLE == "sprite" and src.getextrema()[3][0] < 255:
            return sprite(src, PORTRAIT_W, PORTRAIT_H)
        if PIXEL_STYLE in ("gbc", "sprite"):
            return framed(trace(Image.open(USER_PORTRAITS[key]), PORTRAIT_W, PORTRAIT_H, 10).convert("RGBA"))
        img = Image.open(USER_PORTRAITS[key]).convert("RGBA").resize((PORTRAIT_W * 2, PORTRAIT_H * 2), Image.LANCZOS)
        if PIXEL_STYLE == "chunky":
            return framed(pixelize(img, PORTRAIT_W // 2, PORTRAIT_H // 2, 20, 2))
        return framed(pixelize(img, PORTRAIT_W, PORTRAIT_H, 32, 1))
    if key in USER_PORTRAITS:
        img = load_rgba(USER_PORTRAITS[key], PORTRAIT_W, PORTRAIT_H)
        if img.getextrema()[3][0] == 255:  # opaque: fill the whole bust area
            canvas = Image.new("RGBA", (PORTRAIT_W, PORTRAIT_H), (0, 0, 0, 0))
            canvas.paste(framed(img.resize((CAPTURE_W, CAPTURE_H), Image.LANCZOS)), ((PORTRAIT_W - CAPTURE_W) // 2, 0))
            return canvas
        return ace_sprite(img, PORTRAIT_W, PORTRAIT_H, trim=key not in KEEP_SIDES)
    small = _procedural_portrait(key).resize((96, 120), Image.NEAREST)
    canvas = Image.new("RGBA", (PORTRAIT_W, PORTRAIT_H), (0, 0, 0, 0))
    canvas.paste(small, ((PORTRAIT_W - 96) // 2, 0))  # keep the face above the text box
    return canvas


def thumb_image(key):
    """Face crop: upper part of the figure, centred."""
    if key not in USER_PORTRAITS:
        return _procedural_portrait(key).resize((THUMB_W, THUMB_H), Image.NEAREST)
    if PIXEL_STYLE != "off":  # pixelized face crop from the (unframed) centre of the portrait
        img = Image.open(USER_PORTRAITS[key]).convert("RGBA")
        w = img.width * 0.62
        box = ((img.width - w) / 2, img.height * 0.04, (img.width + w) / 2, img.height * 0.04 + w * THUMB_H / THUMB_W)
        img = img.crop(tuple(int(v) for v in box))
        if PIXEL_STYLE == "sprite" and img.getextrema()[3][0] < 255:
            return sprite(img, THUMB_W, THUMB_H)
        if PIXEL_STYLE in ("gbc", "sprite"):
            return trace(img, THUMB_W, THUMB_H, 10).convert("RGBA")
        img = img.resize((THUMB_W * 2, THUMB_H * 2), Image.LANCZOS)
        if PIXEL_STYLE == "chunky":
            return pixelize(img, THUMB_W // 2, THUMB_H // 2, 16, 2)
        return pixelize(img, THUMB_W, THUMB_H, 24, 1)
    img = load_rgba(USER_PORTRAITS[key], PORTRAIT_W * 2, PORTRAIT_H * 2)
    cw = min(img.width, int(img.height * 0.6))
    ch = int(cw * THUMB_H / THUMB_W)
    x = (img.width - cw) // 2
    crop = img.crop((x, 0, x + cw, ch))
    return crop.resize((THUMB_W, THUMB_H), Image.LANCZOS)


def render_portrait(key):
    """Portrait pixels: 0xFFFF transparent, bit 15 set = soft edge pixel drawn at 50% over the
    scene (anti-aliased outline on hardware with only on/off transparency), else opaque."""
    img = portrait_image(key)
    px = img.load()
    out = []
    for y in range(img.height):
        for x in range(img.width):
            r, g, b, a = px[x, y]
            if a == 0:
                out.append(0xFFFF)
                continue
            c = (b >> 3) << 10 | (g >> 3) << 5 | (r >> 3)
            if a < 255:
                c |= 0x8000
                if c == 0xFFFF:
                    c = 0xFFFE
            out.append(c)
    return out


USER_INSETS = {k: v for k, v in user_files("cuts").items() if k.startswith("ins_")}


def render_inset(key):
    """@inset picture: assets/cuts/ins_*.png (anime frame, made by tools/import_bg.py), or a plain
    dark card when the capture is missing so the story still builds."""
    if key in USER_INSETS:
        img = Image.open(USER_INSETS[key]).convert("RGB")
        w, h = img.size
        if w * INSET_H > h * INSET_W:   # crop to the inset's shape, centred
            nw = h * INSET_W // INSET_H
            img = img.crop(((w - nw) // 2, 0, (w + nw) // 2, h))
        else:
            nh = w * INSET_H // INSET_W
            img = img.crop((0, (h - nh) // 2, w, (h + nh) // 2))
        img = img.resize((INSET_W, INSET_H), Image.LANCZOS)
    else:
        img = Image.new("RGB", (INSET_W, INSET_H), (24, 20, 36))
    return to15(img, dither=False)


def render_thumb(key):
    return to15(thumb_image(key), dither=False)


def icon_image(key):
    if key in USER_ICONS:
        img = load_rgba(USER_ICONS[key], ICON_SIZE, ICON_SIZE)
        if img.getextrema()[3][0] == 255:
            w = min(img.width, img.height)
            img = img.crop(((img.width - w) // 2, (img.height - w) // 2, (img.width + w) // 2, (img.height + w) // 2))
            if PIXEL_STYLE in ("gbc", "sprite"):
                return trace(img, ICON_SIZE, ICON_SIZE, 10).convert("RGBA")
            img = img.resize((ICON_SIZE * 2, ICON_SIZE * 2), Image.LANCZOS)
            if PIXEL_STYLE == "chunky":
                return pixelize(img, ICON_SIZE // 2, ICON_SIZE // 2, 16, 2)
            if PIXEL_STYLE == "native":
                return pixelize(img, ICON_SIZE, ICON_SIZE, 24, 1)
            return img.resize((ICON_SIZE, ICON_SIZE), Image.LANCZOS)
        return fit(img, ICON_SIZE, ICON_SIZE, anchor_bottom=False)
    if key in art_hd.ICONS_HD:
        return art_hd.ICONS_HD[key]()
    img = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    BUILTIN_ICONS[key](ImageDraw.Draw(img))
    return outline(img).resize((ICON_SIZE, ICON_SIZE), Image.NEAREST)


def render_icon(key):
    return to15(icon_image(key), dither=False)


def write_previews(out):
    os.makedirs(out, exist_ok=True)
    for k in SCENES:
        scene_image(k).resize((W * 2, H * 2), Image.NEAREST).save(os.path.join(out, f"scene_{k}.png"))
    keys = list(PORTRAITS)
    sheet = Image.new("RGBA", (PORTRAIT_W * len(keys), PORTRAIT_H + THUMB_H), (60, 60, 80, 255))
    for i, k in enumerate(keys):
        p = portrait_image(k)
        sheet.paste(p, (i * PORTRAIT_W, 0), p)
        t = thumb_image(k)
        sheet.paste(t, (i * PORTRAIT_W, PORTRAIT_H), t)
    sheet.save(os.path.join(out, "portraits.png"))
    sheet = Image.new("RGBA", (ICON_SIZE * len(ICONS), ICON_SIZE), (60, 60, 80, 255))
    for i, k in enumerate(ICONS):
        p = icon_image(k)
        sheet.paste(p, (i * ICON_SIZE, 0), p)
    sheet.save(os.path.join(out, "icons.png"))
