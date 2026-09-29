#!/usr/bin/env python3
"""Backgrounds taken from anime reference frames (the Daum cafe captures, 640x480).

    python3 tools/import_bg.py <cafe capture folder with p1/ p2/ p3/ p4/>

Each location below names a character-free frame and a crop (x0, y0, x1, y1) in the
640x480 frame (3:2 by default, centred). The frame is lightly smoothed so the video noise
does not show on the GBA, and saved as assets/scenes/<key>.png, which stays out of git like
the other personal images. Locations without a clean frame keep their drawn background.
"""
import glob
import os
import sys

from PIL import Image, ImageEnhance, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "assets", "scenes")

C32 = (0, 27, 640, 454)  # centred 3:2 crop of a 4:3 frame

FRAMES = {
    "dining": ("p1/095", C32),            # dining car window: lamp, green hills
    "freight_yard": ("p1/230", C32),      # the train stopped among green fields
    "country_station": ("p1/355", C32),   # Shikotsugahara station, arched roof
    "hotel_exterior": ("p1/369", C32),    # the red-brick station hotel
    "hotel": ("p2/086", C32),             # hotel lounge, pink walls
    "hotel_hall": ("p2/094", C32),        # hotel corridor at night
    "swamp": ("p2/038", C32),             # marsh at night
    "drawbridge": ("p2/116", C32),        # bridge to the theater
    "bridge_up": ("p2/219", C32),         # the drawbridge raised
    "theater": ("p2/226", C32),           # stage and seats
    "hotel_room": ("p3/311", C32),        # blue room, purple curtains
    "yumi_room": ("p3/312", C32),         # Yumi's room
    "room_below": ("p3/326", C32),        # the room below
    "mario_room": ("p3/330", C32),        # Tsune Mario's room
    "sickroom": ("p3/329", C32),          # room with the mirror (morning)
    "yumi_tree": ("p3/319", C32),         # the branch with the rope
    "airport": ("p3/239", C32),           # airport lobby
    "prison": ("p4/322", C32),            # the cell, light from the window
}


def process(img, box):
    img = img.convert("RGB").crop(box)
    img = img.filter(ImageFilter.MedianFilter(3)).filter(ImageFilter.SMOOTH)
    img = ImageEnhance.Color(img).enhance(1.08)
    return img.resize((480, 320), Image.LANCZOS)


def main():
    src = sys.argv[1] if len(sys.argv) > 1 else "."
    os.makedirs(OUT, exist_ok=True)
    for key, (frame, box) in FRAMES.items():
        found = glob.glob(os.path.join(src, frame + ".*"))
        if not found:
            print(f"skip {key}: {frame} not found")
            continue
        process(Image.open(found[0]), box).save(os.path.join(OUT, key + ".png"))
        print(f"{frame} -> scenes/{key}.png")


if __name__ == "__main__":
    main()
