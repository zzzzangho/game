#!/usr/bin/env python3
"""원작 파트 조합 미리보기 시트 (로컬 확인용, 결과물 공개 금지).
사용: python3 -I preview_looks.py <rom.gba> <출력.png> [프리셋.json ...]
위 줄: 프리셋(갤러리 대조 결과) / 아래 줄: 무작위 조합(머리 8색·피부 4색·의상색 표 사용)."""
import json, os, random, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import romparts as R, charcompose as C
from export_parts import palettes
from PIL import Image

def rgb(c): return ((c & 31) * 255 // 31, ((c >> 5) & 31) * 255 // 31, ((c >> 10) & 31) * 255 // 31)

def render(lib, look, pal):
    idx = C.compose(lib, look); im = Image.new("RGBA", (32, 64), (0, 0, 0, 0))
    im.putdata([(*rgb(pal[v]), 255) if v else (0, 0, 0, 0) for v in idx]); return im

def palette_for(P, look):
    p = list(P["base"])
    if look.get("palette"):
        for i, c in enumerate(look["palette"]):
            if i and c >= 0: p[i] = c
    if look.get("hairColor", -1) >= 0: p[3:6] = P["hair"][look["hairColor"]]
    if look.get("skinColor", -1) >= 0: p[6:11] = P["skin"][look["skinColor"]]
    if look.get("outfitColor", -1) >= 0: p[11:15] = P["outfitTable"][1 + 48 * look["outfitColor"] + 4 * look.get("outfit", 0)]
    return p

if __name__ == "__main__":
    rom = R.load(sys.argv[1]); lib = C.Library(rom); P = palettes(rom)
    presets = []
    for f in sys.argv[3:]: presets += list(json.load(open(f)).values())
    rnd = random.Random(7); row1 = presets[:: max(1, len(presets) // 16)][:16]; row2 = []
    for i in range(16):
        g = i % 2
        row2.append({"body": 48 + 12 * g + rnd.randrange(4), "face": rnd.randrange(len(lib.cat["face"])), "hair": rnd.randrange(len(lib.cat["hairfront"])),
                     "eyes": rnd.randrange(len(lib.cat["eyes"])), "nose": rnd.randrange(len(lib.cat["nose"])), "mouth": rnd.randrange(len(lib.cat["mouth"])),
                     "hairColor": rnd.randrange(8), "skinColor": rnd.randrange(4), "outfit": rnd.randrange(4), "outfitColor": rnd.randrange(4)})
    sheet = Image.new("RGBA", (16 * 36, 2 * 68), (40, 40, 48, 255))
    for r, row in enumerate((row1, row2)):
        for i, look in enumerate(row):
            im = render(lib, look, palette_for(P, look)); sheet.alpha_composite(im, (i * 36 + 2, r * 68 + 2))
    sheet.resize((sheet.width * 3, sheet.height * 3), Image.NEAREST).save(sys.argv[2])
