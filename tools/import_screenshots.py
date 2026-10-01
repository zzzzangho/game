#!/usr/bin/env python3
"""Rebuild the local (git-ignored) art in assets/ from anime screenshots.

    python3 tools/import_screenshots.py            # download the posts' screenshots, then crop
    python3 tools/import_screenshots.py --no-download

Screenshots are cached in build/screens/p1..p4. Only crop coordinates live here; the images themselves
stay out of git. The build (tools/art.py) turns these crops into pixel art (KMT_PIXEL=chunky|native|off).
"""
import argparse
import os
import re
import subprocess
import urllib.request

from PIL import Image

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
CACHE = os.path.join(ROOT, "build", "screens")
POSTS = ["3504144", "3504159", "3504175", "3504204"]  # File 1-4
UA = "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 Chrome/124.0 Safari/537.36"
IMG_RE = re.compile(r"https://blog\.kakaocdn\.net/dn/[A-Za-z0-9]+/[A-Za-z0-9]+/[A-Za-z0-9]+/img\.jpg")


def fetch(url, referer=None):
    req = urllib.request.Request(url, headers={"User-Agent": UA, **({"Referer": referer} if referer else {})})
    with urllib.request.urlopen(req, timeout=60) as r:
        return r.read()


def download():
    for part, post in enumerate(POSTS, 1):
        page = f"https://cafe.daum.net/_c21_/bbs_search_read?grpid=1IHuH&fldid=ReHf&datanum={post}&q=t"
        html = fetch(page, f"https://cafe.daum.net/subdued20club/ReHf/{post}").decode("utf-8", "replace")
        urls = list(dict.fromkeys(IMG_RE.findall(html)))
        d = os.path.join(CACHE, f"p{part}")
        os.makedirs(d, exist_ok=True)
        jobs = []
        for i, u in enumerate(urls, 1):
            f = os.path.join(d, f"{i:03d}.jpg")
            if not os.path.exists(f) or os.path.getsize(f) == 0:
                jobs.append(f"{u} {f}")
        if jobs:
            subprocess.run(["xargs", "-P", "8", "-n", "2", "sh", "-c", 'curl -sS -L --retry 3 -o "$2" "$1"', "_"],
                           input="\n".join(jobs), text=True, check=False)
        print(f"part {part}: {len(urls)} screenshots")


def P(x0, w=427):  # full-height bust box starting at x0
    return (x0, 0, x0 + w, 480)


PORTRAITS = {  # frontal shots, 128:144 boxes; each crop is cut out with the anime segmentation model
    "kin": ("p4/016", P(100)),
    "kin_serious": ("p4/053", P(130)),
    "kin_surprised": ("p1/350", P(106)),
    "miyuki": ("p1/104", (300, 40, 640, 422)),
    "miyuki_surprised": ("p2/289", P(80)),
    "saki": ("p1/119", (40, 20, 420, 447)),
    "kenmochi": ("p4/358", (40, 30, 440, 480)),
    "akechi": ("p4/367", P(107)),
    "yamagami": ("p1/126", P(107)),
    "yumi": ("p2/255", P(107)),
    "sakonji": ("p2/257", P(107)),
    "yurama": ("p1/169", P(100)),
    "yurama_angry": ("p1/195", P(100)),
    "sakuraba": ("p4/267", P(107)),
    "takato": ("p4/161", P(107)),
    "takato_surprised": ("p2/256", P(107)),
    "takato_smile": ("p4/189", P(107)),
    "takato_shock": ("p4/230", P(107)),
    "takato_cold": ("p4/199", P(160)),
    "satomi": ("p4/003", (160, 0, 480, 360)),
    "nagasaki": ("p2/030", P(107)),
    "mario": ("p1/384", P(107)),
    "clown": ("p1/041", P(107)),
}


def S(y0):  # full-width 3:2 scene box starting at y0
    return (0, y0, 640, y0 + 427)


# Captures shown with @cut at key moments of the story (the regular backgrounds are drawn art).
CUTS = {
    "cut_parcel": ("p1/076", S(20)),      # the parcel opened at police HQ
    "cut_roses": ("p1/335", S(20)),       # Yamagami among the roses
    "cut_balloons": ("p1/332", S(20)),    # Saki's video: the hand held up by balloons
    "cut_mario": ("p2/027", S(20)),       # the masked guest checking out
    "cut_body": ("p1/407", S(20)),        # Yamagami hanging from strings (the shipped image is the user-supplied capture)
    "cut_vase": ("p2/109", S(20)),      # Yumi smashes the vase
    "cut_fall": ("p2/120", S(20)),      # Kindaichi trips on the drawbridge
    "cut_lever": ("p2/126", S(20)),     # the broken switch lever in Kindaichi's hand
    "cut_psychic": ("p2/130", S(20)),   # Sakuraba raises a drum by telekinesis
    "cut_drum": ("p2/131", S(20)),      # the drum floats over Sakuraba's head
    "cut_drumsticks": ("p2/132", S(20)), # the drumsticks beat by themselves
    "cut_cuffs": ("p2/133", S(20)),     # Yumi handcuffed under water
    "cut_tank": ("p2/134", S(20)),      # the mermaid locked in the water tank
    "cut_cloth": ("p2/135", S(20)),     # the tank under a black cloth
    "cut_empty": ("p2/136", S(20)),     # the empty tank
    "cut_dress": ("p2/137", S(20)),     # Yumi appears on the tank in a blue dress
    "cut_crowd": ("p2/138", S(20)),     # the cheering audience
    "cut_wink": ("p2/140", S(20)),      # Satomi winks at the audience
    "cut_filming": ("p2/141", S(20)),   # Saki filming, Kindaichi blushing
    "cut_glare": ("p2/142", S(20)),     # Miyuki glares at Kindaichi
    "cut_curtain": ("p2/129", S(20)),  # the curtain rises on the stage
    "cut_satomi_stage": ("p2/139", S(20)),  # Satomi alone on the stage
    "cut_mari_face": ("p2/145", S(20)),  # the marionette's painted face
    "cut_mari_scissors": ("p2/149", S(20)),  # the marionette raises the scissors
    "cut_mari_snip": ("p2/150", S(20)),  # the strings snipped
    "cut_mari_fall": ("p2/151", S(20)),  # the marionette collapses
    "cut_audience": ("p2/154", S(20)),  # Saki, Kindaichi and Miyuki watching
    "cut_bike": ("p2/158", S(20)),  # the marionette rides a bicycle
    "cut_tumble": ("p2/161", S(20)),  # the marionette tumbles about
    "cut_mari_worry": ("p2/162", S(20)),  # the marionette, worried
    "cut_mari_look": ("p2/165", S(20)),  # the marionette looks up
    "cut_mari_tired": ("p2/170", S(20)),  # the marionette back on its strings
    "cut_lights": ("p2/175", S(20)),  # the stage lights
    "cut_flash_petals": ("p2/180", S(20)),  # rose petals in the flash
    "cut_satomi": ("p2/144", S(20)),      # Satomi as the living marionette, hanging from strings
    "cut_marionette": ("p2/152", S(20)),  # the living marionette dancing
    "cut_yurama": ("p2/188", S(20)),      # Yurama on the marionette chair under the red light
    "cut_fog": ("p1/392", S(20)),         # a figure walking in the fog
    "cut_sinking": ("p2/366", S(20)),     # Kindaichi sinking in the swamp
    "cut_jade": ("p4/013", S(20)),        # the heavy jade stone
    "cut_bag": ("p4/118", S(20)),         # the small bag with the false bottom
    "cut_takato": ("p4/166", S(0)),       # Takato adjusts his glasses
    "cut_reiko": ("p4/217", S(0)),        # Reiko in the London park
    "cut_rock": ("p4/331", S(20)),        # Sakonji's floating rock
    "cut_fire": ("p4/349", S(20)),        # the rock in flames
}

ICONS = {
    "puppet": ("p1/085", (40, 0, 600, 480)),
}


def crop(src, box):
    im = Image.open(os.path.join(CACHE, src + ".jpg")).convert("RGB")
    return im.crop(box)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--no-download", action="store_true")
    args = ap.parse_args()
    if not args.no_download:
        download()
    for sub, table in (("portraits", PORTRAITS), ("cuts", CUTS), ("icons", ICONS)):
        d = os.path.join(ROOT, "assets", sub)
        os.makedirs(d, exist_ok=True)
        for key, (src, box) in table.items():
            im = crop(src, box)
            if sub != "cuts":
                im = im.convert("RGBA")  # opaque RGBA: the build pixelizes it and frames it
            im.save(os.path.join(d, key + ".png"))
        print(sub, len(table))


if __name__ == "__main__":
    main()
