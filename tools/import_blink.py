#!/usr/bin/env python3
"""Import a character's expressions with their closed-eye versions (for the idle blink).

    python3 tools/import_blink.py [--key kin|miyuki|...] <folder with pairs/<표정>/open.png, closed.png> [...]
    python3 tools/import_blink.py --chars <folder with <인물>/<표정>/open.png, closed.png>

Each pair shares one canvas (only the eyes differ). open.png becomes the expression portrait
assets/portraits/<key>[_<expr>].png, closed.png becomes the same name + _blink; both get the
crop of the open picture, so they line up. build_assets.py stores only the changed eye
rectangle of the blink. The images stay out of git like the other personal pictures.
"""
import glob
import os
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "assets", "portraits")

# expression folder name -> portrait suffix (기본 = the base portrait)
EXPR = {"기본": "", "화남": "_serious", "놀람": "_surprised", "매우 놀람": "_shock", "매우 화남": "_angry",
        "슬픔": "_sad", "긴장": "_tense", "장난": "_playful", "즐거움": "_happy"}


def import_pair(d, out):
    """d/open.png -> portraits/<out>.png, d/closed.png -> portraits/<out>_blink.png (same crop)."""
    opened = Image.open(os.path.join(d, "open.png")).convert("RGBA")
    box = opened.getchannel("A").getbbox()  # crop both like the open portrait
    for fn, suffix in (("open.png", ""), ("closed.png", "_blink")):
        img = Image.open(os.path.join(d, fn)).convert("RGBA").crop(box)
        img.thumbnail((512, 640), Image.LANCZOS)
        img.save(os.path.join(OUT, out + suffix + ".png"))
    return img.size


def import_chars(src):
    """One folder per character (named like the art files, see import_charart.NAMES)."""
    from import_charart import NAMES
    for c in sorted(os.listdir(src)):
        if not os.path.isdir(os.path.join(src, c)):
            continue
        if c not in NAMES:
            print(f"skip {c} (unknown character)")
            continue
        for d in sorted(glob.glob(os.path.join(src, c, "*"))):
            name = os.path.basename(d)
            if name in EXPR:
                size = import_pair(d, NAMES[c] + EXPR[name])
        print(f"{c} -> portraits/{NAMES[c]}*.png {size}")


def main():
    args = sys.argv[1:]
    if args and args[0] == "--chars":
        for src in args[1:]:
            import_chars(src)
        return
    key = "kin"
    if "--key" in args:
        i = args.index("--key")
        key = args[i + 1]
        del args[i:i + 2]
    for src in args:
        for d in sorted(glob.glob(os.path.join(src, "pairs", "*"))):
            name = os.path.basename(d)
            if name not in EXPR:
                print(f"skip {name} (unknown expression)")
                continue
            out = key + EXPR[name]
            size = import_pair(d, out)
            print(f"{name} -> portraits/{out}.png, {out}_blink.png {size}")


if __name__ == "__main__":
    main()
