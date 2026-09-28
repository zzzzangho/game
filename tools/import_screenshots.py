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
    "miyuki": ("p2/279", (190, 0, 617, 480)),
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


SCENES = {
    "title": ("p1/004", S(20)),
    "platform": ("p1/008", S(0)),
    "corridor": ("p1/321", S(20)),
    "stage": ("p1/127", S(20)),
    "dining": ("p1/096", S(20)),
    "snowfield": ("p1/230", S(20)),
    "cabin_roses": ("p1/335", S(20)),
    "cabin_roses_hand": ("p1/332", S(20)),
    "cabin_empty": ("p1/348", S(20)),
    "hotel": ("p1/370", S(0)),
    "theater": ("p2/082", S(0)),
    "swamp": ("p1/392", S(20)),
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
    for sub, table in (("portraits", PORTRAITS), ("scenes", SCENES), ("icons", ICONS)):
        d = os.path.join(ROOT, "assets", sub)
        os.makedirs(d, exist_ok=True)
        for key, (src, box) in table.items():
            im = crop(src, box)
            if sub != "scenes":
                im = im.convert("RGBA")  # opaque RGBA: the build pixelizes it and frames it
            im.save(os.path.join(d, key + ".png"))
        print(sub, len(table))


if __name__ == "__main__":
    main()
