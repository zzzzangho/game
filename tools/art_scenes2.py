"""More location art: one background per situation (berth, train toilet, freight yard,
country station, hotel exterior, Mario's room, catwalk, dressing room, drawbridge, Yumi's
room, the room below, airport, London park, detention cell, ...) plus a finishing pass
that gives every background soft light, a colour grade and a fine paper grain."""
import random

import numpy as np
from PIL import Image, ImageFilter

from art_hd import Canvas, bare_tree, jade_stone, marionette, moon, mountains, pine, room, stars


# ============================================================ finishing pass

def polish(img, grade=(1.0, 1.0, 1.0), lift=(0, 0, 0), bloom=0.35, grain=1.6, seed=1):
    """Soft bloom on bright areas, a colour grade (multiply + lift) and paper grain."""
    a = np.asarray(img.convert("RGB")).astype(np.float32)
    if bloom:
        lum = a.mean(axis=2)
        bright = np.clip((lum - 170) / 85, 0, 1)[..., None] * a
        blur = np.asarray(Image.fromarray(bright.astype(np.uint8)).filter(ImageFilter.GaussianBlur(4))).astype(np.float32)
        a = a + blur * bloom
    a = a * np.array(grade, np.float32) + np.array(lift, np.float32)
    if grain:
        rng = np.random.default_rng(seed)
        a += rng.normal(0, grain, a.shape[:2])[..., None]
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8))


# ============================================================ helpers

def room_depth(c, back, strength=110):
    """Ambient occlusion for a one-point room: corners and the near floor/ceiling darker,
    light pooling on the back wall."""
    x0, y0, x1, y1 = back
    lay, d = c.layer()
    # side walls darken toward the viewer
    for side in ((0, x0), (240, x1)):
        fx, bx = side
        for i in range(12):
            t = i / 12
            x = fx + (bx - fx) * t
            d.line(c.pts([(x, 0), (x, 160)]), fill=(0, 0, 0, int(strength * (1 - t) * 0.55)), width=max(1, int(abs(bx - fx) / 12 * 4 + 2)))
    # floor/ceiling toward the viewer
    for i in range(10):
        t = i / 10
        d.line(c.pts([(0, 160 - t * (160 - y1)), (240, 160 - t * (160 - y1))]), fill=(0, 0, 0, int(strength * (1 - t) * 0.5)), width=16)
        d.line(c.pts([(0, t * y0), (240, t * y0)]), fill=(0, 0, 0, int(strength * (1 - t) * 0.6)), width=12)
    c.put(lay, blur=6)
    c.glow((x0 + x1) / 2, (y0 + y1) / 2, (x1 - x0) * 0.7, (40, 34, 26), 0.5)



def wood_planks(c, x0, y0, x1, y1, base, n=6, vertical=False, rng=None):
    rng = rng or random.Random(3)
    for i in range(n):
        t0, t1 = i / n, (i + 1) / n
        k = rng.uniform(-10, 10)
        col = tuple(max(0, min(255, int(v + k))) for v in base)
        if vertical:
            c.rect(x0 + (x1 - x0) * t0, y0, x0 + (x1 - x0) * t1, y1, col)
            c.line([(x0 + (x1 - x0) * t0, y0), (x0 + (x1 - x0) * t0, y1)], tuple(int(v * 0.6) for v in base), 0.4)
        else:
            c.rect(x0, y0 + (y1 - y0) * t0, x1, y0 + (y1 - y0) * t1, col)
            c.line([(x0, y0 + (y1 - y0) * t0), (x1, y0 + (y1 - y0) * t0)], tuple(int(v * 0.6) for v in base), 0.4)


def curtain(c, x0, y0, x1, y1, col, folds=6):
    dark = tuple(int(v * 0.6) for v in col)
    light = tuple(min(255, int(v * 1.2)) for v in col)
    c.rect(x0, y0, x1, y1, col)
    w = (x1 - x0) / folds
    for i in range(folds):
        c.rect(x0 + i * w, y0, x0 + i * w + w * 0.25, y1, dark)
        c.rect(x0 + i * w + w * 0.55, y0, x0 + i * w + w * 0.7, y1, light)


def window_frame(c, x0, y0, x1, y1, frame=(60, 40, 30), bars=True):
    c.rect(x0 - 2, y0 - 2, x1 + 2, y0, frame)
    c.rect(x0 - 2, y1, x1 + 2, y1 + 2, frame)
    c.rect(x0 - 2, y0, x0, y1, frame)
    c.rect(x1, y0, x1 + 2, y1, frame)
    if bars:
        c.rect((x0 + x1) / 2 - 0.6, y0, (x0 + x1) / 2 + 0.6, y1, frame)
        c.rect(x0, (y0 + y1) / 2 - 0.6, x1, (y0 + y1) / 2 + 0.6, frame)


def sky_night(c, x0, y0, x1, y1, rng, moon_at=None, snow_n=0):
    v = Canvas(max(1, int(x1 - x0)), max(1, int(y1 - y0)))
    v.vgrad(0, 0, v.w, v.h, [(0, (8, 12, 34)), (1, (40, 50, 92))])
    stars(v, rng, v.w * v.h // 50, v.h * 0.7)
    if moon_at:
        moon(v, moon_at[0], moon_at[1], 5)
    if snow_n:
        v.snow(rng, snow_n, big=0.1)
    c.img.paste(v.img, (c.p(x0), c.p(y0)))
    from PIL import ImageDraw
    c.d = ImageDraw.Draw(c.img)


def lamp(c, x, y, r=10, col=(255, 210, 140), strength=0.9):
    c.ell(x - 2, y - 2, x + 2, y + 2, (255, 244, 210))
    c.glow(x, y, r, col, strength)


# ============================================================ train

def scene_berth():
    """Inside the sleeper berth: two-tier bunks, curtains, a small night window, reading lamp."""
    rng = random.Random(201)
    c = Canvas(240, 160)
    c.vgrad(0, 0, 240, 160, [(0, (70, 52, 44)), (1, (40, 28, 24))])
    # window on the far wall
    sky_night(c, 88, 26, 152, 66, rng, moon_at=(50, 10))
    window_frame(c, 88, 26, 152, 66, (120, 96, 70), bars=False)
    c.rect(86, 66, 154, 70, (150, 120, 88))            # sill
    # bunks left and right (perspective boxes)
    for side in (-1, 1):
        x_out = 0 if side < 0 else 240
        x_in = 70 if side < 0 else 170
        for top, bot, shade in ((8, 64, 0.85), (78, 132, 1.0)):
            # mattress
            c.poly([(x_out, bot - 6), (x_in, bot - 12), (x_in, bot - 4), (x_out, bot + 6)], (220, 220, 230))
            c.poly([(x_out, bot + 6), (x_in, bot - 4), (x_in, bot), (x_out, bot + 12)], (120, 110, 120))
            # blanket
            c.poly([(x_out, bot - 8), (x_in - 14 * -side, bot - 13), (x_in - 10 * -side, bot - 8), (x_out, bot)],
                   tuple(int(v * shade) for v in (60, 70, 130)))
            # curtain edge
            cx = x_in + (6 if side < 0 else -6)
            curtain(c, min(cx, x_in), top, max(cx, x_in) + 0.1, bot - 12, (214, 176, 80), folds=2)
        # ladder
    for y in range(80, 136, 10):
        c.rect(172, y, 186, y + 1.5, (170, 150, 110))
    c.rect(172, 76, 174, 140, (170, 150, 110))
    c.rect(184, 76, 186, 140, (170, 150, 110))
    # floor
    c.poly([(70, 132), (170, 132), (240, 160), (0, 160)], (70, 40, 40))
    # reading lamps
    lamp(c, 60, 20, 22, (255, 200, 130), 0.8)
    lamp(c, 180, 20, 22, (255, 200, 130), 0.8)
    # a paperback crab guide on the lower bunk
    c.rect(30, 116, 50, 124, (230, 70, 40))
    c.rect(32, 118, 48, 120, (255, 230, 120))
    c.vignette(0.55)
    return polish(c.finish(), grade=(1.02, 0.98, 0.95), seed=201)


def scene_train_toilet():
    """The cramped washroom next to cabin 5: steel sink, mirror, a small frosted window."""
    rng = random.Random(202)
    c = Canvas(240, 160)
    back = (70, 20, 170, 120)
    room(c, back, (200, 204, 196), (90, 96, 100), (220, 222, 216), left=(176, 180, 172), right=(186, 190, 182))
    room_depth(c, back)
    # tiles on the back wall
    for x in range(70, 170, 10):
        c.line([(x, 20), (x, 120)], (170, 174, 168), 0.4)
    for y in range(20, 120, 10):
        c.line([(70, y), (170, y)], (170, 174, 168), 0.4)
    # small window high on the back wall, open a crack
    sky_night(c, 96, 28, 144, 50, rng, snow_n=0)
    window_frame(c, 96, 28, 144, 50, (110, 110, 116), bars=False)
    c.rect(96, 28, 144, 44, (210, 220, 228))          # frosted pane pushed up
    for i in range(12):
        c.line([(98 + i * 4, 30), (100 + i * 4, 42)], (230, 236, 240), 0.5)
    # scuff marks on the sill (rope)
    for i in range(5):
        c.line([(110 + i * 3, 50.5), (114 + i * 3, 52)], (140, 90, 60), 0.6)
    # mirror + sink
    c.rect(100, 58, 140, 84, (160, 180, 190), (90, 90, 96), 1)
    c.poly([(104, 60), (116, 60), (104, 72)], (210, 225, 232))
    c.poly([(92, 92), (148, 92), (140, 106), (100, 106)], (200, 205, 210), (120, 124, 130))
    c.ell(112, 94, 128, 100, (150, 156, 164))
    c.rect(118, 86, 122, 94, (170, 170, 176))
    # toilet on the right wall
    c.ell(176, 116, 214, 134, (236, 236, 236), (150, 150, 150))
    c.rect(186, 100, 206, 124, (228, 228, 228), (150, 150, 150))
    # fluorescent light
    c.rect(96, 8, 144, 12, (250, 252, 255))
    c.glow(120, 10, 60, (60, 70, 70), 0.6)
    c.vignette(0.5)
    return polish(c.finish(), grade=(0.95, 1.0, 1.02), seed=202)


# ============================================================ outside, daylight / dusk

def scene_freight_yard():
    """Emergency stop at a snowy freight yard in the morning: our sleeper on the left,
    the red mail train standing on the far track, open white plain and grey sky."""
    rng = random.Random(203)
    c = Canvas(240, 160)
    c.vgrad(0, 0, 240, 90, [(0, (150, 164, 184)), (1, (214, 220, 228))])
    mountains(c, rng, 72, 18, (160, 170, 190), (236, 240, 246), 0.7)
    c.vgrad(0, 70, 240, 160, [(0, (236, 240, 246)), (1, (214, 222, 232))])
    # far track with the mail train (red/brown vans)
    c.rect(0, 84, 240, 86, (110, 104, 100))
    for i in range(6):
        x = 70 + i * 30
        c.rect(x, 66, x + 28, 84, (150, 50, 40), (80, 24, 20), 0.5)
        c.rect(x + 2, 64, x + 26, 66, (120, 110, 110))
        c.rect(x + 10, 72, x + 18, 80, (190, 180, 160))
    c.rect(66, 64, 70, 84, (60, 60, 64))
    # signage
    c.rect(200, 40, 202, 86, (70, 70, 74))
    c.rect(186, 40, 232, 50, (250, 250, 250), (60, 60, 60), 0.6)
    # near track and our blue sleeper on the left, big
    c.rect(0, 118, 240, 122, (100, 94, 90))
    for x in range(0, 240, 8):
        c.rect(x, 122, x + 5, 125, (90, 70, 60))
    c.poly([(0, 30), (70, 52), (70, 118), (0, 124)], (40, 50, 110))
    c.poly([(0, 30), (70, 52), (70, 56), (0, 38)], (200, 200, 210))
    for i in range(3):
        x0 = 6 + i * 20
        y0 = 58 + i * 5
        c.poly([(x0, y0), (x0 + 14, y0 + 4), (x0 + 14, y0 + 20), (x0, y0 + 17)], (200, 214, 230))
    c.poly([(0, 88), (70, 94), (70, 98), (0, 94)], (230, 200, 90))
    # passengers' footprints and luggage in the snow
    for _ in range(30):
        x, y = rng.uniform(80, 230), rng.uniform(128, 158)
        c.ell(x, y, x + 3, y + 1.5, (190, 200, 214))
    for x, y, col in ((110, 130, (120, 70, 50)), (150, 136, (60, 60, 70)), (190, 128, (150, 120, 80))):
        c.rect(x, y, x + 12, y + 9, col, (40, 30, 30), 0.5)
    c.snow(rng, 90, big=0.1)
    c.vignette(0.3)
    return polish(c.finish(), grade=(1.0, 1.0, 1.04), seed=203, bloom=0.2)


def scene_country_station():
    """Shikotsugahara: the little terminal in the marshland at dusk, wooden building and sign."""
    rng = random.Random(204)
    c = Canvas(240, 160)
    c.vgrad(0, 0, 240, 100, [(0, (40, 44, 90)), (0.6, (170, 110, 120)), (1, (240, 170, 120))])
    mountains(c, rng, 84, 14, (80, 70, 100), (200, 180, 200), 1.3)
    c.vgrad(0, 84, 240, 160, [(0, (210, 200, 214)), (1, (170, 170, 190))])
    # platform
    c.poly([(0, 110), (240, 104), (240, 124), (0, 132)], (150, 144, 150))
    c.poly([(0, 132), (240, 124), (240, 128), (0, 136)], (90, 86, 94))
    # station house
    c.rect(120, 58, 210, 106, (120, 84, 60), (60, 40, 30), 0.6)
    c.poly([(114, 60), (165, 38), (216, 60)], (70, 60, 70))
    c.poly([(114, 60), (165, 38), (216, 60), (212, 62), (165, 42), (118, 62)], (240, 240, 248))
    for x in (130, 152, 188):
        c.rect(x, 72, x + 14, 88, (255, 210, 140), (60, 40, 30), 0.6)
        c.glow(x + 7, 80, 16, (140, 90, 30), 0.6)
    c.rect(170, 78, 182, 106, (70, 50, 40))
    # name board
    c.rect(40, 70, 96, 84, (250, 250, 250), (40, 40, 60), 0.8)
    c.rect(44, 76, 92, 78, (40, 40, 80))
    c.rect(52, 84, 54, 108, (60, 60, 70))
    c.rect(84, 84, 86, 108, (60, 60, 70))
    # lamps
    for x in (30, 110, 228):
        c.rect(x, 64, x + 1.5, 110, (50, 50, 56))
        lamp(c, x + 0.7, 64, 14, (255, 200, 120), 0.7)
    # rails
    c.rect(0, 144, 240, 146, (80, 80, 90))
    c.rect(0, 152, 240, 154, (80, 80, 90))
    for x in range(0, 240, 20):
        pine(c, x + rng.uniform(-4, 4), 100, rng.uniform(8, 16), (30, 40, 50), (220, 220, 236))
    c.snow(rng, 60, big=0.1)
    c.vignette(0.45)
    return polish(c.finish(), seed=204)


def scene_hotel_exterior():
    """The old western-style hotel in the marsh at dusk, windows lit, snow on the roofs."""
    rng = random.Random(205)
    c = Canvas(240, 160)
    c.vgrad(0, 0, 240, 110, [(0, (20, 22, 56)), (0.7, (90, 70, 110)), (1, (170, 120, 120))])
    stars(c, rng, 40, 50)
    moon(c, 30, 20, 7)
    mountains(c, rng, 96, 20, (50, 50, 80), (170, 170, 200), 2.1)
    c.vgrad(0, 96, 240, 160, [(0, (180, 180, 200)), (1, (120, 124, 150))])
    # hotel: central block + wings
    body, dark = (140, 110, 90), (70, 50, 44)
    c.rect(60, 50, 180, 118, body, dark, 0.8)
    c.rect(24, 70, 64, 118, body, dark, 0.8)
    c.rect(176, 70, 216, 118, body, dark, 0.8)
    c.poly([(54, 52), (120, 22), (186, 52)], (60, 50, 60))
    c.poly([(54, 52), (120, 22), (186, 52), (180, 54), (120, 27), (60, 54)], (236, 236, 246))
    c.poly([(20, 72), (44, 60), (68, 72)], (60, 50, 60))
    c.poly([(172, 72), (196, 60), (220, 72)], (60, 50, 60))
    c.rect(112, 14, 128, 34, (100, 80, 70), dark, 0.6)    # tower
    c.poly([(110, 16), (120, 6), (130, 16)], (60, 50, 60))
    for y in (60, 80, 98):
        for x in range(68, 176, 14):
            lit = rng.random() < 0.6
            c.rect(x, y, x + 7, y + 10, (255, 206, 130) if lit else (50, 50, 70), dark, 0.4)
            if lit:
                c.glow(x + 3.5, y + 5, 10, (120, 70, 20), 0.35)
    for x0 in (30, 182):
        for x in range(x0, x0 + 30, 12):
            c.rect(x, 84, x + 7, 94, (255, 206, 130), dark, 0.4)
    # entrance
    c.rect(110, 98, 130, 118, (70, 40, 30))
    c.rect(106, 94, 134, 98, (200, 180, 150))
    lamp(c, 104, 102, 18, (255, 200, 120), 0.8)
    lamp(c, 136, 102, 18, (255, 200, 120), 0.8)
    # path, marsh reeds, bare trees
    c.poly([(110, 118), (130, 118), (160, 160), (80, 160)], (200, 196, 210))
    for x, h in ((8, 70), (228, 80), (200, 50)):
        bare_tree(c, rng, x, 124, h, (30, 30, 40), 2.0, depth=5)
    for _ in range(50):
        x = rng.uniform(0, 240)
        if 70 < x < 170:
            continue
        c.line([(x, 160), (x + rng.uniform(-3, 3), 132 - rng.uniform(0, 14))], (60, 60, 70), 0.6)
    c.vignette(0.5)
    return polish(c.finish(), seed=205)


# ============================================================ hotel inside

def scene_mario_room():
    """The room 'Tsune Mario' kept: curtains drawn, a single bulb, strings hanging from a hook."""
    rng = random.Random(206)
    c = Canvas(240, 160)
    back = (60, 24, 180, 116)
    room(c, back, (86, 70, 70), (60, 36, 34), (50, 40, 40), left=(70, 56, 56), right=(76, 60, 60))
    room_depth(c, back)
    # wallpaper stripes
    for x in range(60, 180, 8):
        c.rect(x, 24, x + 3, 116, (96, 78, 76))
    curtain(c, 92, 36, 148, 88, (90, 20, 30), folds=5)
    # bed, suitcase
    c.poly([(170, 110), (240, 120), (240, 150), (160, 134)], (200, 190, 180))
    c.poly([(160, 134), (240, 150), (240, 160), (160, 142)], (120, 100, 90))
    c.rect(20, 118, 60, 142, (70, 50, 40), (30, 20, 16), 0.8)
    c.rect(34, 114, 46, 118, (40, 30, 24))
    # white rubber mask on the table
    c.rect(70, 110, 110, 114, (110, 80, 60))
    c.ell(80, 100, 96, 112, (220, 226, 220), (120, 120, 120), 0.5)
    c.ell(84, 104, 87, 106, (30, 30, 30))
    c.ell(89, 104, 92, 106, (30, 30, 30))
    # hook and strings from the ceiling
    c.ell(117, 8, 123, 14, (150, 150, 150))
    for dx in (-10, -4, 3, 9):
        c.line([(120, 12), (120 + dx * 2.5, 96)], (200, 200, 210), 0.35)
    # bulb
    c.line([(150, 0), (150, 14)], (30, 30, 30), 0.5)
    lamp(c, 150, 16, 60, (190, 110, 60), 0.9)
    c.vignette(0.85)
    return polish(c.finish(), grade=(1.05, 0.92, 0.9), seed=206)


def scene_hotel_room_night():
    """Yumi's room at night: window flung open onto the blizzard and the tree outside."""
    rng = random.Random(207)
    c = Canvas(240, 160)
    back = (50, 20, 190, 118)
    room(c, back, (120, 100, 80), (70, 40, 36), (90, 76, 62), left=(100, 84, 68), right=(106, 88, 72))
    room_depth(c, back)
    sky_night(c, 90, 30, 150, 96, rng, snow_n=70)
    # the tree right outside, a rope over a branch
    bare_tree(c, rng, 150, 96, 70, (20, 20, 26), 3.0, depth=5, angle=-105)
    c.line([(100, 40), (136, 36)], (20, 20, 26), 2.0)
    c.line([(120, 38), (122, 70)], (200, 180, 140), 0.6)
    # open casements
    c.poly([(90, 30), (72, 24), (72, 102), (90, 96)], (140, 160, 180), (60, 40, 30))
    c.poly([(150, 30), (168, 24), (168, 102), (150, 96)], (140, 160, 180), (60, 40, 30))
    window_frame(c, 90, 30, 150, 96, (70, 50, 36), bars=False)
    # blown curtains
    c.poly([(60, 22), (80, 22), (70, 70), (54, 100)], (170, 60, 70))
    c.poly([(160, 22), (180, 22), (192, 100), (174, 76)], (170, 60, 70))
    # empty jade stand, dresser
    c.rect(196, 96, 226, 100, (120, 90, 60))
    c.rect(200, 100, 222, 124, (100, 70, 50))
    c.rect(14, 96, 44, 126, (100, 70, 50), (50, 30, 20), 0.6)
    c.rect(18, 86, 40, 96, (190, 200, 210), (70, 60, 50), 0.6)
    # a toppled chair
    c.poly([(90, 132), (120, 138), (118, 142), (88, 136)], (90, 60, 40))
    c.line([(96, 134), (92, 150)], (90, 60, 40), 1.2)
    c.line([(114, 138), (116, 152)], (90, 60, 40), 1.2)
    c.glow(120, 60, 70, (40, 60, 110), 0.5)
    c.vignette(0.7)
    return polish(c.finish(), grade=(0.92, 0.96, 1.08), seed=207)


def scene_room_below():
    """The empty room below Yumi's: dust sheets, a window with snow on the sill, two jade stones."""
    rng = random.Random(208)
    c = Canvas(240, 160)
    back = (54, 22, 186, 116)
    room(c, back, (110, 104, 96), (64, 50, 46), (96, 92, 86), left=(96, 90, 84), right=(100, 94, 88))
    room_depth(c, back)
    sky_night(c, 150, 32, 178, 80, rng, snow_n=20)
    window_frame(c, 150, 32, 178, 80, (60, 50, 44))
    c.rect(146, 80, 182, 84, (230, 234, 240))           # snow on the sill with a hand print
    c.ell(160, 80, 168, 83, (170, 176, 190))
    # dust-sheeted furniture
    c.poly([(20, 130), (30, 96), (80, 94), (92, 130)], (216, 214, 206))
    c.poly([(196, 136), (204, 108), (236, 106), (240, 136)], (216, 214, 206))
    # table with the two jade stones
    c.rect(96, 104, 150, 110, (110, 80, 56), (50, 34, 24), 0.6)
    c.rect(100, 110, 104, 134, (90, 64, 44))
    c.rect(142, 110, 146, 134, (90, 64, 44))
    jade_stone(c, 102, 88, 20, 16)
    jade_stone(c, 124, 86, 22, 18)
    c.glow(123, 96, 30, (40, 110, 70), 0.4)
    c.glow(164, 56, 50, (40, 50, 80), 0.4)
    c.vignette(0.75)
    return polish(c.finish(), grade=(0.95, 0.98, 1.05), seed=208)


def scene_hotel_room_morning():
    """A hotel room in soft morning light (Kindaichi waking up after the swamp)."""
    rng = random.Random(209)
    c = Canvas(240, 160)
    back = (60, 24, 180, 116)
    room(c, back, (220, 204, 180), (150, 110, 86), (236, 226, 210), left=(206, 190, 166), right=(212, 196, 172))
    room_depth(c, back)
    v = Canvas(48, 50)
    v.vgrad(0, 0, 48, 50, [(0, (190, 214, 240)), (1, (250, 244, 236))])
    mountains(v, rng, 42, 8, (200, 210, 230), (250, 250, 255), 0.4)
    c.img.paste(v.img, (c.p(96), c.p(34)))
    from PIL import ImageDraw
    c.d = ImageDraw.Draw(c.img)
    window_frame(c, 96, 34, 144, 84, (170, 140, 110))
    curtain(c, 80, 30, 96, 92, (230, 220, 200), folds=2)
    curtain(c, 144, 30, 160, 92, (230, 220, 200), folds=2)
    # light beams
    lay, d = c.layer()
    d.polygon(c.pts([(96, 84), (144, 84), (200, 160), (60, 160)]), fill=(255, 240, 210, 50))
    c.put(lay, blur=5)
    # bed with white sheets in front
    c.poly([(0, 118), (130, 110), (150, 160), (0, 160)], (240, 240, 244))
    c.poly([(0, 104), (60, 100), (62, 118), (0, 122)], (250, 250, 252))
    c.rect(186, 104, 214, 130, (160, 120, 90))
    c.ell(194, 96, 206, 106, (230, 240, 250))       # water jug
    c.vignette(0.35)
    return polish(c.finish(), grade=(1.04, 1.0, 0.96), seed=209, bloom=0.5)


def scene_lobby_night(hotel_fn):
    """The hotel lobby late at night: the day lobby darkened, blue shadows, lamps glowing."""
    img = hotel_fn()
    a = np.asarray(img).astype(np.float32)
    lum = a.mean(axis=2, keepdims=True)
    lamps = np.clip((lum - 200) / 55, 0, 1)
    night = a * np.array([0.42, 0.45, 0.62]) + 6
    a = night * (1 - lamps) + a * lamps
    return polish(Image.fromarray(np.clip(a, 0, 255).astype(np.uint8)), bloom=0.6, seed=210)


# ============================================================ theater

def scene_catwalk():
    """Above the stage: narrow planks, the pulley and rope, a 70 kg marionette hanging in the dark."""
    rng = random.Random(211)
    c = Canvas(240, 160)
    c.vgrad(0, 0, 240, 160, [(0, (14, 12, 18)), (1, (40, 30, 34))])
    # trusses
    for x in range(-20, 260, 40):
        c.line([(x, 0), (x + 40, 60)], (60, 50, 50), 1.2)
        c.line([(x + 40, 0), (x, 60)], (60, 50, 50), 1.2)
    c.rect(0, 0, 240, 3, (70, 60, 60))
    c.rect(0, 58, 240, 62, (70, 60, 60))
    # planks walkway (perspective)
    c.poly([(90, 160), (150, 160), (130, 70), (110, 70)], (120, 86, 56))
    for i in range(8):
        t = i / 8
        y = 160 - t * 90
        xl, xr = 90 + t * 20, 150 - t * 20
        c.line([(xl, y), (xr, y)], (60, 40, 26), 0.6)
    # one loose plank, askew
    c.poly([(60, 110), (96, 104), (98, 110), (62, 116)], (140, 100, 64), (60, 40, 26))
    c.ell(64, 110, 66, 112, (180, 180, 180))
    # pulley and rope
    c.ell(170, 14, 186, 30, (90, 90, 96), (40, 40, 44), 1)
    c.ell(176, 20, 180, 24, (40, 40, 44))
    c.line([(172, 22), (160, 160)], (200, 180, 140), 0.9)
    c.line([(184, 22), (200, 90)], (200, 180, 140), 0.9)
    marionette(c, 200, 88, k=1.1, twisted=False)
    # stage light from below
    lay, d = c.layer()
    d.polygon(c.pts([(120, 160), (240, 160), (240, 90)]), fill=(255, 220, 160, 40))
    c.put(lay, blur=6)
    c.vignette(0.8)
    return polish(c.finish(), seed=211)


def scene_dressing_room():
    """Backstage dressing room: bulb-framed mirrors, costumes on a rail, props."""
    rng = random.Random(212)
    c = Canvas(240, 160)
    back = (40, 20, 200, 110)
    room(c, back, (96, 70, 80), (60, 40, 44), (70, 56, 60), left=(84, 62, 70), right=(88, 66, 74))
    room_depth(c, back)
    for mx in (60, 130):
        c.rect(mx, 36, mx + 50, 80, (170, 190, 200), (200, 170, 90), 1.4)
        c.poly([(mx + 4, 40), (mx + 20, 40), (mx + 4, 60)], (220, 230, 236))
        for i in range(6):
            lamp(c, mx + 4 + i * 8.4, 33, 6, (255, 220, 150), 0.5)
    c.rect(44, 84, 196, 92, (120, 90, 70), (60, 40, 30), 0.6)
    for x, col in ((70, (230, 60, 60)), (90, (240, 220, 200)), (150, (90, 60, 160)), (170, (220, 200, 90))):
        c.rect(x, 78, x + 6, 84, col)
    # costume rail on the right
    c.line([(200, 30), (240, 26)], (180, 180, 180), 1.2)
    for i, col in enumerate(((200, 40, 60), (240, 240, 236), (40, 40, 80), (120, 40, 140))):
        x = 206 + i * 9
        c.poly([(x, 30), (x + 8, 30), (x + 10, 90), (x - 2, 90)], col)
    # checkered dress (Miyuki-like) on a dummy at left
    c.poly([(10, 60), (30, 60), (34, 120), (6, 120)], (150, 40, 50))
    for y in range(64, 120, 6):
        for x in range(8, 32, 6):
            c.rect(x, y, x + 3, y + 3, (230, 220, 200))
    c.rect(14, 50, 26, 60, (200, 180, 160))
    c.vignette(0.6)
    return polish(c.finish(), grade=(1.05, 0.98, 0.95), seed=212)


def scene_drawbridge():
    """The theater on its pond at night: the drawbridge raised, lanterns on the water."""
    rng = random.Random(213)
    c = Canvas(240, 160)
    c.vgrad(0, 0, 240, 90, [(0, (10, 12, 30)), (1, (40, 44, 80))])
    stars(c, rng, 60, 60)
    moon(c, 200, 22, 8)
    mountains(c, rng, 74, 12, (30, 34, 60), (120, 124, 160), 0.2)
    c.vgrad(0, 74, 240, 160, [(0, (30, 40, 60)), (1, (10, 14, 24))])
    # theater on the island
    c.rect(120, 40, 220, 80, (120, 90, 80), (50, 36, 30), 0.8)
    c.poly([(114, 42), (170, 20), (226, 42)], (60, 40, 50))
    for x in range(128, 216, 14):
        c.rect(x, 52, x + 8, 66, (255, 200, 120), (50, 36, 30), 0.4)
    c.rect(160, 62, 180, 80, (70, 40, 30))
    # reflections
    lay, d = c.layer()
    for x in range(128, 216, 14):
        for k in range(6):
            d.line(c.pts([(x + rng.uniform(-2, 2), 84 + k * 5), (x + 8 + rng.uniform(-2, 2), 84 + k * 5)]),
                   fill=(255, 200, 120, 60 - k * 8), width=6)
    c.put(lay, blur=0.6)
    # the raised bridge
    c.poly([(40, 120), (110, 84), (114, 88), (46, 126)], (110, 80, 56), (40, 28, 20))
    c.poly([(104, 30), (118, 80), (112, 82), (98, 32)], (110, 80, 56), (40, 28, 20))
    c.line([(100, 28), (60, 110)], (150, 150, 160), 0.5)
    c.rect(96, 82, 122, 90, (70, 60, 56))
    # switch box, broken
    c.rect(28, 110, 38, 124, (80, 90, 100), (30, 30, 30), 0.6)
    c.line([(30, 112), (40, 106)], (200, 60, 50), 0.8)
    # lanterns on posts along the bank
    for x in (16, 60, 236):
        c.rect(x, 110, x + 1.5, 140, (40, 30, 30))
        lamp(c, x + 0.7, 108, 16, (255, 180, 100), 0.7)
    c.vignette(0.6)
    return polish(c.finish(), seed=213)


# ============================================================ flashbacks / epilogue

def scene_airport():
    """Tokyo airport departure lobby, early 90s: big windows, a jet outside, the split-flap board."""
    rng = random.Random(214)
    c = Canvas(240, 160)
    c.vgrad(0, 0, 240, 160, [(0, (200, 206, 214)), (1, (170, 170, 176))])
    # windows with a sunny apron
    c.vgrad(0, 20, 240, 96, [(0, (130, 180, 230)), (1, (220, 236, 246))])
    c.rect(0, 80, 240, 96, (170, 170, 176))
    c.poly([(40, 70), (140, 64), (170, 70), (60, 76)], (240, 240, 244))      # fuselage
    c.poly([(100, 68), (120, 58), (128, 58), (116, 70)], (220, 220, 226))     # tail fin
    c.poly([(80, 72), (60, 86), (70, 86), (100, 72)], (210, 210, 216))       # wing
    c.rect(56, 70, 150, 71, (200, 40, 40))
    for x in range(0, 240, 30):
        c.rect(x, 20, x + 3, 96, (90, 90, 96))
    c.rect(0, 18, 240, 22, (90, 90, 96))
    # departure board
    c.rect(150, 26, 232, 60, (24, 26, 30), (80, 80, 86), 0.8)
    for i in range(5):
        y = 30 + i * 6
        c.rect(154, y, 190, y + 4, (60, 64, 70))
        c.rect(194, y, 228, y + 4, (60, 64, 70))
        c.rect(156, y + 1, 186, y + 3, (240, 200, 90) if i == 1 else (220, 220, 220))
    # floor with reflections
    c.vgrad(0, 96, 240, 160, [(0, (210, 210, 214)), (1, (150, 150, 156))])
    for x in range(-60, 300, 30):
        c.line([(120 + (x - 120) * 0.3, 96), (x, 160)], (190, 190, 196), 0.5)
    # a few silhouettes and a trolley
    for x, h in ((30, 40), (46, 36), (210, 42)):
        c.ell(x - 4, 104 + (44 - h), x + 4, 112 + (44 - h), (90, 80, 90))
        c.rect(x - 6, 112 + (44 - h), x + 6, 150, (80, 80, 100))
    c.rect(180, 130, 200, 142, (150, 150, 160))
    c.vignette(0.3)
    return polish(c.finish(), grade=(1.02, 1.0, 0.98), bloom=0.4, seed=214)


def scene_london_park():
    """A London park in autumn afternoon light: plane trees, a bench, the clock tower beyond."""
    rng = random.Random(215)
    c = Canvas(240, 160)
    c.vgrad(0, 0, 240, 100, [(0, (150, 180, 214)), (1, (240, 214, 170))])
    # clock tower silhouette
    c.rect(186, 30, 200, 96, (120, 110, 120))
    c.poly([(184, 30), (193, 14), (202, 30)], (120, 110, 120))
    c.ell(189, 36, 197, 44, (230, 220, 190))
    c.rect(0, 84, 240, 100, (140, 150, 150))
    c.vgrad(0, 96, 240, 160, [(0, (120, 150, 80)), (1, (80, 110, 50))])
    # path
    c.poly([(90, 160), (170, 160), (140, 96), (124, 96)], (210, 190, 150))
    # trees with autumn leaves
    for x, y, r in ((30, 50, 34), (70, 40, 30), (216, 60, 30), (160, 44, 22)):
        c.rect(x - 3, y, x + 3, 108, (90, 60, 40))
        for _ in range(40):
            dx, dy = rng.uniform(-r, r), rng.uniform(-r * 0.7, r * 0.5)
            if dx * dx + dy * dy > r * r:
                continue
            col = rng.choice(((220, 150, 50), (200, 90, 40), (240, 190, 80), (170, 120, 40)))
            c.ell(x + dx - 6, y + dy - 5, x + dx + 6, y + dy + 5, col)
    # fallen leaves
    for _ in range(80):
        x, y = rng.uniform(0, 240), rng.uniform(104, 160)
        c.ell(x, y, x + 2.5, y + 1.5, rng.choice(((220, 150, 50), (200, 90, 40), (240, 190, 80))))
    # bench
    c.rect(20, 120, 70, 124, (110, 70, 40))
    c.rect(20, 112, 70, 116, (110, 70, 40))
    c.rect(24, 124, 26, 134, (40, 40, 40))
    c.rect(64, 124, 66, 134, (40, 40, 40))
    c.glow(60, 20, 90, (80, 60, 20), 0.5)
    c.vignette(0.4)
    return polish(c.finish(), grade=(1.05, 1.0, 0.9), bloom=0.5, seed=215)


def scene_prison_cell():
    """Asahikawa detention cell: bare concrete, a high barred window, bars in front."""
    rng = random.Random(216)
    c = Canvas(240, 160)
    back = (50, 20, 190, 120)
    room(c, back, (120, 124, 126), (80, 82, 84), (100, 104, 106), left=(104, 108, 110), right=(110, 114, 116))
    room_depth(c, back)
    # stains
    for _ in range(30):
        x, y = rng.uniform(50, 190), rng.uniform(20, 120)
        c.ell(x, y, x + rng.uniform(2, 8), y + rng.uniform(2, 8), (112, 116, 118))
    # high window
    c.rect(106, 30, 134, 46, (180, 200, 220))
    for x in range(110, 134, 6):
        c.rect(x, 30, x + 1.5, 46, (60, 60, 64))
    lay, d = c.layer()
    d.polygon(c.pts([(106, 46), (134, 46), (170, 140), (90, 140)]), fill=(220, 230, 240, 40))
    c.put(lay, blur=4)
    # bunk and blanket
    c.rect(140, 100, 190, 112, (140, 130, 110), (60, 60, 60), 0.6)
    c.rect(150, 96, 180, 100, (200, 196, 180))
    # bars in the foreground
    for x in range(8, 240, 22):
        c.rect(x, 0, x + 5, 160, (50, 52, 56))
        c.rect(x + 1, 0, x + 2, 160, (110, 112, 118))
    c.rect(0, 20, 240, 24, (50, 52, 56))
    c.vignette(0.6)
    return polish(c.finish(), grade=(0.95, 0.98, 1.03), seed=216)


def scene_magic_hall():
    """Tokyo concert hall for Sakonji's solo show: red curtains, spotlight, the rock on its rig."""
    rng = random.Random(217)
    c = Canvas(240, 160)
    c.vgrad(0, 0, 240, 160, [(0, (20, 8, 14)), (1, (50, 16, 20))])
    curtain(c, 0, 0, 240, 112, (150, 70, 170), folds=14)
    c.rect(0, 0, 240, 14, (170, 130, 50))
    for x in range(0, 240, 12):
        c.poly([(x, 14), (x + 12, 14), (x + 6, 22)], (170, 130, 50))
    # stage floor
    c.poly([(0, 112), (240, 112), (240, 130), (0, 130)], (110, 70, 40))
    # rock on the rig
    c.line([(120, 14), (120, 60)], (200, 200, 210), 0.5)
    c.poly([(98, 60), (144, 58), (152, 84), (138, 100), (104, 98), (92, 80)], (130, 120, 110), (60, 50, 50))
    c.poly([(104, 64), (130, 62), (124, 76)], (160, 150, 140))
    # spotlight
    lay, d = c.layer()
    d.polygon(c.pts([(108, 0), (132, 0), (170, 130), (70, 130)]), fill=(255, 240, 200, 60))
    c.put(lay, blur=5)
    # audience heads
    for x in range(-4, 250, 10):
        y = 142 + rng.uniform(-2, 2)
        c.ell(x, y, x + 9, y + 12, (20, 14, 18))
    for x in range(2, 250, 12):
        c.ell(x, 150, x + 10, 164, (14, 10, 12))
    c.vignette(0.55)
    return polish(c.finish(), seed=217)


SCENES2 = {
    "berth": scene_berth, "train_toilet": scene_train_toilet, "freight_yard": scene_freight_yard,
    "country_station": scene_country_station, "hotel_exterior": scene_hotel_exterior,
    "mario_room": scene_mario_room, "yumi_room": scene_hotel_room_night, "room_below": scene_room_below,
    "sickroom": scene_hotel_room_morning, "catwalk": scene_catwalk, "dressing_room": scene_dressing_room,
    "drawbridge": scene_drawbridge, "airport": scene_airport, "london_park": scene_london_park,
    "prison": scene_prison_cell, "magic_hall": scene_magic_hall,
}
