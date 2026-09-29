#!/usr/bin/env python3
"""Import closed-eye versions of Kindaichi's expressions for the idle blink.

    python3 tools/import_blink.py <folder with pairs/<표정>/closed.png> [...more folders]

Each closed.png has the same canvas as its open.png (only the eyes differ), so it gets the
same crop as the portrait and is saved as assets/portraits/<portrait>_blink.png (kept out of
git like the other personal images). build_assets.py stores only the changed eye rectangle.
"""
import glob
import os
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "assets", "portraits")

EXPR = {"기본": "kin", "화남": "kin_serious", "놀람": "kin_surprised", "매우 놀람": "kin_shock",
        "매우 화남": "kin_angry", "슬픔": "kin_sad", "긴장": "kin_tense", "장난": "kin_playful",
        "즐거움": "kin_happy"}


def main():
    for src in sys.argv[1:]:
        for d in sorted(glob.glob(os.path.join(src, "pairs", "*"))):
            name = os.path.basename(d)
            if name not in EXPR:
                print(f"skip {name} (unknown expression)")
                continue
            opened = Image.open(os.path.join(d, "open.png")).convert("RGBA")
            closed = Image.open(os.path.join(d, "closed.png")).convert("RGBA")
            box = opened.getchannel("A").getbbox()   # crop both like the open portrait
            img = closed.crop(box)
            img.thumbnail((512, 640), Image.LANCZOS)
            img.save(os.path.join(OUT, EXPR[name] + "_blink.png"))
            print(f"{name} -> portraits/{EXPR[name]}_blink.png {img.size}")


if __name__ == "__main__":
    main()
