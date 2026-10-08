#!/usr/bin/env python3
"""원작 캐릭터 조합 규칙(파이썬 기준 구현). Unity 쪽 Core/CharacterParts.cs 와 같은 규칙.
갤러리 252명(아빠·엄마·아들 18세 각 84) 대조로 확인: 193명 픽셀 완전 일치, 나머지 대부분 1~6px 차이(눈·코 좌우 반전 등 미세 차이).

규칙 (원점 O = 발 아래 중앙, 갤러리 셀 32x64 에서 O=(16,64)):
  몸통 b = body[그룹].parts[연령]       좌상단 = (Ox - b.ax, Oy - 16 - b.ay)
  목     = (Ox + b.ext0 - 16, Oy + b.ext1 - 32)
  얼굴 f = face[번호].parts[연령], 메타 m[0..8] (헤더 4바이트째부터)
         ref = 목y - m0;  얼굴 좌상단 = (목x - m7, ref + m1 - 2)
  앞머리 = 기준점 (목x, ref + m1)       뒷머리 = 같은 번호의 뒷머리, 기준점 (목x, ref + m2 + 16 - hb.ext1)
  눈 (목x, ref+m3) · 코 (목x, ref+m4) · 입 (목x, ref+m5)   — 파트 좌상단 = 기준점 - (ax, ay), 좌우반전 시 ax = w - ax
  그리는 순서: 뒷머리 → 몸통 → 얼굴 → 코 → 눈 → 입 → 앞머리
  연령 칸: 갤러리(성인)는 모두 1 — 0/2/3 의 의미는 미확인.
  몸통 그룹: 24개씩 16벌(방향·걸음 프레임 추정), 정면 서기 = 48~71 (남 48~59, 여 60~71). 갤러리는 48~51 / 60~63 만 사용."""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import romparts as R
from export_parts import category

def s8(b): return b - 256 if b > 127 else b

class Library:
    def __init__(self, rom):
        self.cat = {}
        for g in R.all_groups(rom, 0xA14000, 0xA90000):
            c = category(g["addr"])
            if c: self.cat.setdefault(c, []).append(g)

def _put(canvas, W, H, p, ax_abs, ay_abs, flip=False):
    if not p["w"]: return
    w, h = p["w"], p["h"]; px = R.part_pixels(p)
    ax, ay = s8(p["meta"][4]), s8(p["meta"][5])
    if flip: ax = w - ax
    x0, y0 = ax_abs - ax, ay_abs - ay
    for y in range(h):
        for x in range(w):
            v = px[y * w + (w - 1 - x if flip else x)]
            if v and 0 <= x0 + x < W and 0 <= y0 + y < H: canvas[(y0 + y) * W + x0 + x] = v

def compose(lib, look, age=1, W=32, H=64, O=(16, 64)):
    """look: body, face, hair, eyes, nose, mouth (번호) + eyesFlip/noseFlip/mouthFlip."""
    c = [0] * (W * H); Ox, Oy = O
    b = lib.cat["body"][look["body"]]["parts"][age]; bm = b["meta"]
    f = lib.cat["face"][look["face"]]["parts"][age]; m = list(f["meta"][4:])
    nx, ny = Ox + bm[6] - 16, Oy + bm[7] - 32; ref = ny - m[0]
    hb = lib.cat["hairback"][look["hair"]]["parts"][age]
    if hb["w"]: _put(c, W, H, hb, nx, ref + m[2] + 16 - hb["meta"][7])
    _put(c, W, H, b, Ox, Oy - 16)
    _put(c, W, H, f, nx - m[7] + s8(f["meta"][4]), ref + m[1] - 2 + s8(f["meta"][5]))
    for k, mi in (("nose", 4), ("eyes", 3), ("mouth", 5)):
        _put(c, W, H, lib.cat[k][look[k]]["parts"][age], nx, ref + m[mi], bool(look.get(k + "Flip")))
    _put(c, W, H, lib.cat["hairfront"][look["hair"]]["parts"][age], nx, ref + m[1])
    return c
