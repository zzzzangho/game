#!/usr/bin/env python3
"""Investigation views (@examine): one full picture per place, built on the anime frame of
that place, with the things to look at drawn in where the frame does not show them.

    python3 tools/exam_scenes.py <cafe capture folder with p1/ p2/ p3/ p4/> [--spots]

Everything is laid out in the 640x480 frame, then the 3:2 crop is saved as
assets/scenes/exam_<place>.png (out of git like the other personal images). --spots prints
the clickable rectangles in 240x160 screen units, ready for the @at lines in story.txt.
"""
import glob
import os
import sys

from PIL import Image, ImageDraw, ImageEnhance, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "assets", "scenes")
C32 = (0, 27, 640, 454)
INK = (34, 24, 38)  # outline colour, like the anime's line art

SRC = "."


def frame(name):
    found = glob.glob(os.path.join(SRC, name + ".*"))
    if not found:
        raise FileNotFoundError(name)
    return Image.open(found[0]).convert("RGB").resize((640, 480), Image.LANCZOS)


def poly(d, pts, fill, width=3):
    d.polygon(pts, fill=fill)
    d.line(pts + [pts[0]], fill=INK, width=width, joint="curve")


def rect(d, box, fill, width=3):
    d.rectangle(box, fill=fill, outline=INK, width=width)


def feather_paste(dst, src, pos, radius=10):
    """Paste src with softened edges (for bits of another frame laid on a flat wall)."""
    m = Image.new("L", src.size, 0)
    ImageDraw.Draw(m).rectangle([radius, radius, src.size[0] - radius, src.size[1] - radius], fill=255)
    dst.paste(src, pos, m.filter(ImageFilter.GaussianBlur(radius / 2)))


def key_paste(dst, src, pos, keep):
    """Paste only the pixels of src for which keep(r, g, b) is true (cut an object out of its frame)."""
    m = Image.new("L", src.size, 0)
    px, mp = src.load(), m.load()
    for y in range(src.size[1]):
        for x in range(src.size[0]):
            if keep(*px[x, y]):
                mp[x, y] = 255
    m = m.filter(ImageFilter.MaxFilter(3)).filter(ImageFilter.MinFilter(5)).filter(ImageFilter.GaussianBlur(1))
    dst.paste(src, pos, m)


# ------------------------------------------------------------------------------ the places
def cabin():
    """Compartment 5 seen from the bed: roses to the door, the window, the vent up by the ceiling."""
    img = frame("p3/151")
    d = ImageDraw.Draw(img)
    # close the door over the people standing in it (roses in front stay)
    px = img.load()
    door = (250, 16, 562, 402)
    for y in range(door[1], door[3]):
        for x in range(door[0], door[2]):
            r, g, b = px[x, y]
            if y > 290 and r > 130 and r - g > 60:
                continue
            shade = 212 - (x - door[0]) // 30
            px[x, y] = (shade, shade + 2, shade - 4)
    d.line([(door[0], door[1]), (door[0], door[3])], fill=INK, width=3)
    d.line([(door[2], door[1]), (door[2], door[3])], fill=INK, width=3)
    d.line([(door[0], door[1]), (door[2], door[1])], fill=INK, width=3)
    d.rectangle([292, 70, 522, 300], outline=(150, 152, 150), width=3)
    rect(d, [382, 36, 432, 58], (214, 176, 70), 2)
    d.text((394, 40), "A-5", fill=INK)
    rect(d, [270, 200, 282, 252], (120, 122, 128), 2)
    # the window on the left wall (from the washroom frame: same carriage, same window)
    win = frame("p3/159").crop((122, 22, 522, 380)).resize((128, 114), Image.LANCZOS)
    feather_paste(img, win, (96, 120), 4)
    # the ceiling vent, up where wall meets ceiling
    rect(d, [92, 70, 214, 106], (140, 144, 150), 3)
    d.rectangle([102, 77, 204, 99], fill=(44, 46, 54))
    for yy in range(80, 98, 5):
        d.line([(104, yy), (202, yy)], fill=(160, 164, 170), width=2)
    for x, y in ((118, 108), (150, 111), (182, 107)):  # soot under the grille
        d.ellipse([x - 10, y - 3, x + 10, y + 5], fill=(170, 156, 110))
    # burst balloon rubber among the roses
    for x, y, c in ((340, 394, (246, 236, 140)), (382, 414, (170, 214, 236)), (424, 392, (246, 196, 214)),
                    (316, 424, (246, 236, 140)), (462, 420, (170, 214, 236)), (500, 400, (246, 196, 214))):
        poly(d, [(x - 9, y - 4), (x + 2, y - 8), (x + 10, y - 1), (x + 4, y + 6), (x - 7, y + 4)], c, 2)
    spots = {
        "roses": (0, 330, 640, 454), "rubber": (296, 376, 516, 440), "window": (96, 120, 224, 234),
        "bed": (0, 250, 96, 400), "vent": (88, 66, 218, 116), "door": (250, 27, 562, 330),
    }
    return img, C32, spots


def toilet():
    """The washroom next door: the window with the rope mark, the basin, the bin."""
    src = frame("p3/159")
    wall = src.crop((20, 300, 110, 460)).resize((1, 1), Image.BOX).getpixel((0, 0))
    img = Image.new("RGB", (640, 480), wall)
    # keep the wall's soft light: the frame blurred right out as the backdrop
    img.paste(src.filter(ImageFilter.GaussianBlur(40)))
    win = src.crop((110, 12, 540, 430)).resize((300, 292), Image.LANCZOS)
    feather_paste(img, win, (40, 40), 8)
    d = ImageDraw.Draw(img)
    # floor
    poly(d, [(0, 404), (640, 404), (640, 480), (0, 480)], (150, 150, 146), 3)
    for x in range(-200, 640, 60):
        d.line([(x, 480), (x + 140, 404)], fill=(132, 132, 128), width=2)
    # basin on the right wall
    rect(d, [400, 250, 600, 404], (196, 170, 128), 3)  # cabinet
    d.line([(500, 290), (500, 404)], fill=INK, width=2)
    for x in (488, 512):
        d.ellipse([x - 4, 330, x + 4, 338], fill=INK)
    poly(d, [(386, 232), (614, 232), (596, 262), (404, 262)], (236, 240, 242), 3)
    d.ellipse([452, 236, 548, 254], fill=(190, 198, 204), outline=INK, width=2)
    rect(d, [492, 194, 508, 238], (170, 176, 184), 2)
    rect(d, [492, 194, 530, 206], (170, 176, 184), 2)
    rect(d, [440, 120, 560, 186], (200, 214, 222), 3)  # mirror
    d.line([(456, 130), (480, 176)], fill=(236, 244, 248), width=4)
    # the bin under the window
    poly(d, [(92, 344), (172, 344), (164, 452), (100, 452)], (120, 126, 136), 3)
    rect(d, [86, 332, 178, 346], (150, 156, 166), 3)
    for x in (112, 132, 152):
        d.line([(x, 352), (x - 1, 444)], fill=(100, 104, 114), width=2)
    poly(d, [(110, 330), (124, 318), (140, 326), (150, 316), (160, 330)], (246, 246, 240), 2)  # tissue
    spots = {"window": (40, 40, 340, 332), "sink": (386, 120, 614, 404), "trash": (84, 314, 180, 454)}
    return img, C32, spots


def corridor():
    """The sleeper corridor: compartment doors on the left, windows on the right, the alarm."""
    img = frame("p1/018")
    d = ImageDraw.Draw(img)
    # a fire alarm on the left wall, between the doors
    rect(d, [236, 148, 262, 186], (206, 40, 40), 2)
    d.ellipse([242, 155, 256, 169], fill=(240, 220, 210), outline=INK, width=2)
    d.rectangle([243, 174, 255, 180], fill=(240, 220, 210))
    spots = {"door": (100, 120, 226, 400), "alarm": (228, 140, 270, 194), "window": (400, 110, 580, 346)}
    return img, (60, 0, 580, 346), spots


def stage():
    """The stage right after the lights came back: Yurama's body over the marionette's seat."""
    img = frame("p3/062")
    spots = {"body": (120, 70, 640, 176), "chair": (330, 176, 580, 400), "floor": (0, 400, 640, 454)}
    return img, C32, spots


def catwalk():
    """Up in the rigging: the pulley and rope, the marionette hanging near it, the old plank."""
    img = frame("p2/243")
    d = ImageDraw.Draw(img)
    # pulley and rope
    d.line([(470, 70), (430, 480)], fill=(214, 196, 150), width=4)
    d.line([(490, 70), (560, 290)], fill=(214, 196, 150), width=4)
    d.ellipse([452, 40, 508, 96], fill=(120, 124, 134), outline=INK, width=3)
    d.ellipse([472, 60, 488, 76], fill=INK)
    rect(d, [470, 0, 490, 44], (90, 92, 100), 2)
    # the marionette hanging from the other end of the rope
    mx, my = 560, 300
    for dx in (-26, 0, 26):
        d.line([(mx + dx, 250), (mx + dx // 2, my + 10)], fill=(230, 226, 214), width=1)
    rect(d, [mx - 40, 246, mx + 40, 254], (150, 110, 70), 2)  # the control bar
    poly(d, [(mx - 16, my + 34), (mx + 16, my + 34), (mx + 22, my + 96), (mx - 22, my + 96)], (200, 50, 50), 3)
    poly(d, [(mx - 6, my + 40), (mx + 6, my + 52), (mx - 6, my + 64), (mx - 18, my + 52)], (240, 200, 60), 2)
    poly(d, [(mx + 8, my + 60), (mx + 18, my + 72), (mx + 8, my + 84), (mx - 2, my + 72)], (240, 200, 60), 2)
    for a, b in (((mx - 16, my + 40), (mx - 34, my + 88)), ((mx + 16, my + 40), (mx + 34, my + 88)),
                 ((mx - 12, my + 96), (mx - 16, my + 140)), ((mx + 12, my + 96), (mx + 16, my + 140))):
        d.line([a, b], fill=INK, width=9)
        d.line([a, b], fill=(200, 50, 50), width=5)
    d.ellipse([mx - 16, my + 2, mx + 16, my + 36], fill=(246, 244, 240), outline=INK, width=3)
    d.ellipse([mx - 8, my + 16, mx - 4, my + 20], fill=INK)
    d.ellipse([mx + 4, my + 16, mx + 8, my + 20], fill=INK)
    d.arc([mx - 7, my + 20, mx + 7, my + 30], 20, 160, fill=(200, 40, 40), width=2)
    poly(d, [(mx - 18, my + 8), (mx, my - 22), (mx + 18, my + 8)], (120, 60, 160), 3)  # hat
    # the old plank laid across the beams
    poly(d, [(30, 350), (310, 286), (322, 326), (44, 392)], (158, 116, 72), 3)
    d.line([(38, 368), (314, 304)], fill=(122, 86, 52), width=2)
    d.line([(42, 382), (318, 318)], fill=(122, 86, 52), width=2)
    for x, y in ((60, 360), (290, 298)):
        d.ellipse([x - 4, y - 4, x + 4, y + 4], fill=(90, 92, 100), outline=INK)
    d.line([(160, 320), (176, 338), (166, 350), (180, 362)], fill=INK, width=3)  # the crack
    spots = {"rope": (420, 30, 520, 454), "doll": (514, 240, 606, 444), "plank": (28, 282, 324, 396)}
    return img, C32, spots


def jade_stand(with_jade):
    """The marble stand from the jade close-up, with or without its two stones."""
    src = frame("p4/024")
    stand = src.crop((40, 110, 620, 480)) if with_jade else src.crop((40, 400, 620, 480))
    return stand


def yumi_room():
    """Yumi's room: the empty jade stand under the window, the vanity, the window itself."""
    img = frame("p3/312")
    d = ImageDraw.Draw(img)
    # vanity where the low cabinet stands
    rect(d, [10, 262, 176, 440], (142, 96, 64), 3)
    d.line([(10, 300), (176, 300)], fill=INK, width=2)
    for y in (330, 380):
        rect(d, [110, y, 166, y + 36], (160, 112, 76), 2)
        d.ellipse([134, y + 14, 142, y + 22], fill=(220, 190, 110))
    d.ellipse([34, 136, 150, 268], fill=(210, 226, 236), outline=INK, width=4)  # mirror
    d.ellipse([44, 146, 140, 258], outline=(142, 96, 64), width=6)
    d.line([(70, 170), (96, 230)], fill=(244, 250, 252), width=5)
    for x, h, c in ((40, 22, (230, 150, 190)), (60, 30, (180, 220, 240)), (84, 16, (250, 230, 150))):
        rect(d, [x, 262 - h, x + 14, 262], c, 2)
    rect(d, [100, 250, 150, 262], (250, 250, 244), 2)  # the script
    # the empty stand on the window bench
    slab = jade_stand(False).resize((150, 22), Image.LANCZOS)
    img.paste(slab, (370, 236))
    d.rectangle([370, 236, 520, 258], outline=INK, width=3)
    spots = {"jade": (360, 206, 530, 266), "window": (180, 0, 360, 236), "desk": (8, 132, 180, 444)}
    return img, C32, spots


def room_below():
    """The empty room below: two jade stones on its stand, the open window, the bed."""
    img = frame("p3/326")
    d = ImageDraw.Draw(img)
    stand = jade_stand(True).resize((190, 122), Image.LANCZOS)
    img.paste(stand, (428, 208))
    d.rectangle([424, 204, 622, 334], outline=(120, 84, 50), width=6)
    d.rectangle([420, 200, 626, 338], outline=INK, width=3)
    # the bed in the corner
    poly(d, [(300, 404), (640, 386), (640, 480), (276, 480)], (70, 96, 160), 3)
    poly(d, [(312, 378), (640, 362), (640, 392), (300, 410)], (236, 238, 244), 3)
    poly(d, [(330, 346), (452, 340), (460, 374), (322, 380)], (246, 248, 252), 3)
    d.line([(340, 366), (446, 360)], fill=(210, 214, 226), width=2)
    for t in range(3):
        d.line([(300 + 30 * t, 480 - 6 * t), (640, 440 - 20 * t)], fill=(56, 78, 136), width=2)
    spots = {"jade": (420, 200, 626, 338), "window": (400, 0, 640, 196), "bed": (276, 340, 640, 454)}
    return img, C32, spots


def dressing_room():
    """The dressing room (drawn): the costume rack, Yurama's mirror desk, the curtain to the stage."""
    sys.path.insert(0, HERE)
    import art_scenes2
    img = art_scenes2.scene_dressing_room().convert("RGB").resize((640, 427), Image.LANCZOS)
    full = Image.new("RGB", (640, 480))
    full.paste(img, (0, 27))
    spots = {"route": (540, 27 + 20, 640, 27 + 200), "desk": (160, 27 + 40, 530, 27 + 170),
             "hanger": (0, 27 + 70, 120, 27 + 250)}
    return full, C32, spots


PLACES = {
    "exam_cabin": cabin, "exam_toilet": toilet, "exam_corridor": corridor, "exam_stage": stage,
    "exam_catwalk": catwalk, "exam_dress": dressing_room, "exam_yumi": yumi_room, "exam_below": room_below,
}


def to_screen(box, crop):
    x0, y0, x1, y1 = crop
    sx, sy = 240 / (x1 - x0), 160 / (y1 - y0)
    a = max(0, round((box[0] - x0) * sx)), max(0, round((box[1] - y0) * sy))
    b = min(240, round((box[2] - x0) * sx)), min(160, round((box[3] - y0) * sy))
    return a[0], a[1], b[0] - a[0], b[1] - a[1]


def main():
    global SRC
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    SRC = args[0] if args else "."
    os.makedirs(OUT, exist_ok=True)
    for key, fn in PLACES.items():
        try:
            img, crop, spots = fn()
        except FileNotFoundError as e:
            print(f"skip {key}: {e} not found")
            continue
        img = img.crop(crop).filter(ImageFilter.MedianFilter(3))
        ImageEnhance.Color(img).enhance(1.08).resize((480, 320), Image.LANCZOS).save(os.path.join(OUT, key + ".png"))
        print(f"-> scenes/{key}.png")
        if "--spots" in sys.argv:
            for name, box in spots.items():
                print("   %-8s @at %d %d %d %d" % ((name,) + to_screen(box, crop)))


if __name__ == "__main__":
    main()
