#!/usr/bin/env python3
"""Import hand-drawn character busts (white background) as transparent portraits.

    python3 tools/import_charart.py <folder with 김전일.png, 미유키.png, ...>

The white background is flood-filled from the top edge and the upper part of the side
edges only, so white clothing that runs off the bottom of the picture is kept (the line
art separates it from the background). Output goes to assets/portraits/<key>.png, which
stays out of git like the other personal images.
"""
import collections
import os
import sys

from PIL import Image, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "assets", "portraits")

# file name (without .png) -> portrait key
NAMES = {
    "김전일": "kin", "미유키": "miyuki", "사키": "saki", "켄모치": "kenmochi", "아케치": "akechi",
    "야마가미": "yamagami", "유미": "yumi", "사콘지": "sakonji", "유라마": "yurama",
    "사쿠라바": "sakuraba", "사쿠라바_무대분장": "sakuraba_stage", "타카토": "takato",
    "사토미": "satomi", "나가사키": "nagasaki", "트네 마리오": "mario",
}


def cut_out(img, tol=8, side_frac=0.45):
    img = img.convert("RGBA")
    w, h = img.size
    px = img.load()

    def bg(c):
        return c[0] >= 255 - tol and c[1] >= 255 - tol and c[2] >= 255 - tol

    seeds = [(x, 0) for x in range(w)]
    seeds += [(0, y) for y in range(int(h * side_frac))] + [(w - 1, y) for y in range(int(h * side_frac))]
    seen = bytearray(w * h)
    q = collections.deque()
    for x, y in seeds:
        if not seen[y * w + x] and bg(px[x, y]):
            seen[y * w + x] = 1
            q.append((x, y))
    while q:
        x, y = q.popleft()
        for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
            if 0 <= nx < w and 0 <= ny < h and not seen[ny * w + nx] and bg(px[nx, ny]):
                seen[ny * w + nx] = 1
                q.append((nx, ny))
    mask = Image.frombytes("L", (w, h), bytes(0 if s else 255 for s in seen))
    # pull the edge in by a pixel so no white fringe survives the downscale
    mask = mask.filter(ImageFilter.MinFilter(3))
    img.putalpha(mask)
    return img.crop(mask.getbbox())


def main():
    src = sys.argv[1] if len(sys.argv) > 1 else "."
    os.makedirs(OUT, exist_ok=True)
    for fn in sorted(os.listdir(src)):
        stem, ext = os.path.splitext(fn)
        if ext.lower() not in (".png", ".jpg", ".jpeg", ".webp") or stem not in NAMES:
            if ext.lower() in (".png", ".jpg", ".jpeg", ".webp"):
                print(f"skip {fn} (unknown name)")
            continue
        img = cut_out(Image.open(os.path.join(src, fn)))
        # keep it reasonably small; art.py fits it into the 128x144 bust area
        img.thumbnail((512, 640), Image.LANCZOS)
        img.save(os.path.join(OUT, NAMES[stem] + ".png"))
        print(f"{fn} -> portraits/{NAMES[stem]}.png {img.size}")


if __name__ == "__main__":
    main()
