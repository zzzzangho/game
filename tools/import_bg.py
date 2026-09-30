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
    "platform": ("p1/004", C32),          # Ueno at night, the train at the platform
    "corridor": ("p1/018", (60, 0, 580, 346)),  # sleeper corridor (above the subtitle band)
    "cabin_door": ("p1/346", C32),        # the A-5 compartment door
    "cabin_roses": ("p1/332", C32),       # compartment 5: roses, balloons, the body
    "cabin_empty": ("p1/348", C32),       # the rose carpet after the body vanished
    "train_toilet": ("p3/159", C32),      # the washroom window, rope mark on the frame
    "dining": ("p1/095", C32),            # dining car window: lamp, green hills
    "dining_show": ("p1/124", C32),       # the dining-car aisle where Yamagami opens the show
    "freight_yard": ("p1/230", C32),      # the train stopped among green fields
    "country_station": ("p1/355", C32),   # Shikotsugahara station, arched roof
    "hotel_exterior": ("p1/369", C32),    # the red-brick station hotel
    "hotel": ("p2/086", C32),             # hotel lounge, pink walls
    "hotel_hall": ("p2/094", C32),        # hotel corridor at night
    "swamp": ("p2/038", C32),             # marsh at night
    "drawbridge": ("p2/116", C32),        # bridge to the theater
    "bridge_up": ("p2/219", C32),         # the drawbridge raised
    "theater": ("p2/226", C32),           # stage and seats
    "catwalk": ("p2/243", C32),           # looking down on the stage from the rigging
    "hotel_room": ("p3/311", C32),        # blue room, purple curtains
    "yumi_room": ("p3/312", C32),         # Yumi's room
    "room_below": ("p3/326", C32),        # the room below
    "mario_room": ("p3/330", C32),        # Tsune Mario's room
    "sickroom": ("p3/329", C32),          # room with the mirror (morning)
    "yumi_tree": ("p3/319", C32),         # the branch with the rope
    "airport": ("p3/239", C32),           # airport lobby
    "london_park": ("p4/218", C32),       # the London park in the evening sun
    "magic_hall": ("p4/328", C32),        # Tokyo hall: crossed spotlights on the stage
    "prison": ("p4/322", C32),            # the cell, light from the window
}

# Small pictures shown in a frame over the middle of the screen (@inset): key -> (frame, crop)
INSETS = {
    "ins_rose": ("p1/037", C32),          # the gloved hand through the berth curtain, holding a rose
    "ins_salad_served": ("p1/099", C32),  # the waitress brings Kenmochi the rose salad
    "ins_salad": ("p1/100", C32),         # the rose salad
    "ins_robert": ("p1/110", C32),        # Robert greets the dining car
}
CUTS = os.path.join(HERE, "..", "assets", "cuts")

# Full-screen anime captures for @cut: key -> (frame, crop)
CUT_FRAMES = {
    "cut_burst": ("p1/288", C32),         # the fake bomb bursts on the train roof
    "cut_petals": ("p1/292", C32),        # red rose petals raining from the sky
    "cut_akechi_feet": ("p1/259", C32),   # Akechi's shoes crunching over the gravel
    "cut_akechi": ("p1/278", C32),        # Akechi's face, smiling, the sky behind
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
    os.makedirs(CUTS, exist_ok=True)
    for key, (frame, box) in CUT_FRAMES.items():
        found = glob.glob(os.path.join(src, frame + ".*"))
        if not found:
            print(f"skip {key}: {frame} not found")
            continue
        process(Image.open(found[0]), box).save(os.path.join(CUTS, key + ".png"))
        print(f"{frame} -> cuts/{key}.png")
    for key, (frame, box) in INSETS.items():
        found = glob.glob(os.path.join(src, frame + ".*"))
        if not found:
            print(f"skip {key}: {frame} not found")
            continue
        img = Image.open(found[0]).convert("RGB").crop(box).filter(ImageFilter.MedianFilter(3))
        ImageEnhance.Color(img).enhance(1.08).resize((264, 176), Image.LANCZOS).save(os.path.join(CUTS, key + ".png"))
        print(f"{frame} -> cuts/{key}.png")


if __name__ == "__main__":
    main()
