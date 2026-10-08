#!/usr/bin/env python3
"""원작 ROM 의 캐릭터 파트 묶음(sprite group) 해석기 (추출물 로컬 전용).
묶음: 10 ((n-1)<<4|1) b2 s8 | s32 | s32 | u32 표시작(=16+4n) | u32 오프셋×n (묶음 시작 기준)
파트: [2바이트][폭][높이][4바이트 기준값] + 폭×높이/2 바이트(4bpp, 행 우선, 하위 니블이 왼쪽 픽셀)"""
import struct

def load(rom_path): return open(rom_path, "rb").read()

def header_len(rom, q):
    # 기본 8바이트. 일부 얼굴 파트는 기준점 한 쌍이 더 붙은 16바이트(+13..15 = ff ff ff).
    if rom[q + 6:q + 8] != b"\xff\xff" and rom[q + 13:q + 16] == b"\xff\xff\xff": return 16
    return 8

def group_at(rom, p):
    if p + 16 > len(rom) or rom[p] != 0x10 or not 1 <= (rom[p + 1] & 15) <= 3: return None
    n = (rom[p + 1] >> 4) + 1
    if n > 8: return None
    t = struct.unpack_from("<I", rom, p + 12)[0]
    if t != 16 + 4 * n: return None
    offs = [struct.unpack_from("<I", rom, p + 16 + 4 * i)[0] for i in range(n)]
    # 표 뒤에 u16 보조표가 붙는 묶음(몸통 등)이 있어 첫 오프셋은 표 끝 이상이면 된다.
    if not t <= offs[0] <= t + 4 * n or any(o > 0x8000 for o in offs) or any(b <= a for a, b in zip(offs, offs[1:])): return None
    parts = []
    for o in offs:
        q = p + o; w, h = rom[q + 2], rom[q + 3]
        if w > 64 or h > 64 or w % 2: return None
        hl = header_len(rom, q)
        parts.append({"addr": q, "w": w, "h": h, "meta": rom[q:q + hl], "data": rom[q + hl:q + hl + w * h // 2]})
    return {"addr": p, "n": n, "flag": rom[p + 2], "dy": struct.unpack_from("<b", rom, p + 3)[0],
            "a": struct.unpack_from("<i", rom, p + 4)[0], "b": struct.unpack_from("<i", rom, p + 8)[0], "parts": parts}

def all_groups(rom, lo=0x800000, hi=None):
    hi = hi or len(rom) - 32; out = []
    p = lo
    while p < hi:
        g = group_at(rom, p)
        if g:
            out.append(g); last = g["parts"][-1]
            p = last["addr"] + len(last["meta"]) + last["w"] * last["h"] // 2 if last["w"] else p + 16
        else: p += 1
    return out

def part_pixels(part):
    w, h, d = part["w"], part["h"], part["data"]
    return [(d[(y * w + x) // 2] >> ((x & 1) * 4)) & 15 for y in range(h) for x in range(w)]
