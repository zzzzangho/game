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
  그리는 순서: 뒷머리 → 몸통 → 얼굴 → 앞머리 → 입 → 분류3 → 코 → 눈 (원작 0x08098EC0, 나중 OBJ 가 위)
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
        # 코·분류3 은 원작 자원 표 순서(export_parts.py 와 같음): 분류3 = 자원 539~552, 코 = 553~592
        self.cat["extra"] = [R.group_res(rom, R.resource(rom, 539 + i)) for i in range(14)]
        self.cat["nose"] = [R.group_res(rom, R.resource(rom, 553 + i)) for i in range(40)]

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

def at(g, age):
    """연령 칸 — 묶음 파트 수가 모자라면 마지막 칸 (C# PartGroup.At 과 같음)."""
    return g["parts"][max(0, min(age, len(g["parts"]) - 1))]

AGE_KEYS = ("body", "face", "hair", "feat")

def ages(age):
    """연령 칸: 정수 하나(전부 같은 칸) 또는 분류별 dict {body, face, hair, feat(눈·코·입)}."""
    if isinstance(age, dict): return {k: age.get(k, 1) for k in AGE_KEYS}
    return {k: age for k in AGE_KEYS}

def compose_back(lib, look, age=1, W=32, H=64, O=(15, 64), block=0):
    """뒷모습: 뒷통수(faceB, 얼굴과 같은 번호) → 몸통(뒷모습 묶음) → 뒷머리(머리 번호 + 104).
    본게임 걷기 캡처 6장과 일치(원점 x 가 앞모습보다 1 왼쪽)."""
    c = [0] * (W * H); Ox, Oy = O; A = ages(look.get("ages", age))
    b = at(lib.cat["body"][block * 24 + look["body"] % 24], A["body"]); bm = b["meta"]
    f = at(lib.cat["face"][look["face"]], A["face"]); m = list(f["meta"][4:])
    fb = at(lib.cat["faceB"][look["face"]], A["face"])
    nx, ny = Ox + bm[6] - 16, Oy + bm[7] - 32; ref = ny - m[0]
    _put(c, W, H, fb, nx - fb["meta"][6] + s8(fb["meta"][4]), ref + m[1] - 2 + s8(fb["meta"][5]))
    _put(c, W, H, b, Ox, Oy - 16)   # 뒷통수 → 몸통 순서 (실기 사건 장면 뒷모습에서 목·옷깃이 겹치는 인물로 확인)
    hb = at(lib.cat["hairback"][104 + look["hair"]], A["hair"])
    if hb["w"]: _put(c, W, H, hb, nx, ref + m[2] + 16 - hb["meta"][7])
    return c

def render(lib, look, ages_=1, pose=2, outfit_set=0):
    """Unity CharacterComposer.Compose(lib, look, AgeSlots, Pose, set) 와 같은 호출 규약."""
    block = (outfit_set & 3) * 4 + pose
    if pose in (0, 1): return compose_back(lib, look, age=ages_, block=block)
    return compose(lib, look, age=ages_, block=block)

def compose(lib, look, age=1, W=32, H=64, O=(16, 64), shift=None, block=None):
    """look: body, face, hair, eyes, nose, mouth (번호) + eyesFlip/noseFlip/mouthFlip.
    shift: 조사용 층별 세로 보정 {body, face, feat, hair} (규칙 탐색에만 사용)."""
    c = [0] * (W * H); Ox, Oy = O; A = ages(look.get("ages", age)); S = shift or {}
    bi = look["body"] if block is None else block * 24 + look["body"] % 24
    b = at(lib.cat["body"][bi], A["body"]); bm = b["meta"]
    f = at(lib.cat["face"][look["face"]], A["face"]); m = list(f["meta"][4:])
    nx, ny = Ox + bm[6] - 16, Oy + bm[7] - 32; ref = ny - m[0]
    hb = at(lib.cat["hairback"][look["hair"]], A["hair"])
    if hb["w"]: _put(c, W, H, hb, nx, ref + m[2] + 16 - hb["meta"][7] + S.get("hair", 0))
    _put(c, W, H, b, Ox, Oy - 16 + S.get("body", 0))
    _put(c, W, H, f, nx - m[7] + s8(f["meta"][4]), ref + m[1] - 2 + (1 if A["face"] == 0 else 0) + s8(f["meta"][5]) + S.get("face", 0))
    # 원작 0x08098EC0 순서(아래부터, 나중에 넣은 OBJ 가 위): 앞머리 → 입 → 분류3(레코드 +3, 수염) → 코 → 눈.
    # 분류3 칸의 머리 6바이트째가 0 이면 입 대신 입 자리에, 아니면 입은 두고 m6 자리에 그린다.
    ex = at(lib.cat["extra"][look["extra"]], A["feat"]) if look.get("extra", -1) >= 0 else None
    keep = ex is None or ex["meta"][6] != 0
    _put(c, W, H, at(lib.cat["hairfront"][look["hair"]], A["hair"]), nx, ref + m[1] + S.get("hair", 0))
    if keep: _put(c, W, H, at(lib.cat["mouth"][look["mouth"]], A["feat"]), nx, ref + m[5] + S.get("feat", 0), bool(look.get("mouthFlip")))
    if ex is not None and ex["w"]: _put(c, W, H, ex, nx - ex["w"] // 2 + s8(ex["meta"][4]), ref + (m[6] if keep else m[5]) + S.get("feat", 0))   # 수염: 폭의 절반이 기준
    for k, mi in (("nose", 4), ("eyes", 3)):
        _put(c, W, H, at(lib.cat[k][look[k]], A["feat"]), nx, ref + m[mi] + S.get("feat", 0), bool(look.get(k + "Flip")))
    return c
