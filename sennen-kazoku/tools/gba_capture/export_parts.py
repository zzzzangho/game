#!/usr/bin/env python3
"""원작 ROM 캐릭터 파트를 Unity 로컬 에셋(LocalArt/parts.bin + parts.json)으로 내보낸다.
출력은 원작 추출물이므로 저장소에 넣지 않는다(unity/.gitignore 의 LocalArt/).
사용: python3 -I export_parts.py <rom.gba> <출력 디렉터리> [프리셋 json ...(gallery_fit.py 결과)]
분류(주소 범위)는 docs/07_원작화면관찰.md 의 'ROM 캐릭터 파트' 절 참고 — 갤러리 대조로 확인한 범위."""
import json, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import romparts as R

RANGES = [  # (끝 주소, 분류) — 시작은 0xA147A8
    (0xA2A0EC, "hairback"), (0xA2DA7C, "faceB"), (0xA31160, "eyes"), (0xA3D800, "hairfront"),
    (0xA3E8EC, "mouth"), (0xA3EC14, "extra"), (0xA3F6A4, "nose"), (0xA43500, "face"), (0xA90000, "body")]

def category(addr):
    for end, c in RANGES:
        if addr < end: return c
    return None

def u16s(rom, a, n): return [rom[a + 2 * i] | rom[a + 2 * i + 1] << 8 for i in range(n)]

def palettes(rom):
    """색 표. 주소는 색 선택 화면 캡처의 OBJ 팔레트(뱅크 3)와 ROM 을 대조해 찾았다.
    머리 8색(칸 3~5): 0xA436E8 부터 32바이트 간격(캡처 h0~h7 과 일치, h7 은 별도 위치 0xA436C8).
    피부 4색(칸 6~10): 0xA49900 부터 48바이트 간격 (s2 는 0xA498D0 — 순서 추정).
    의상색(칸 11~14): 0xA44070 + 8*항목, 항목 = 1 + 48*색 + 4*의상 (+체형/성별 보정 미해명, 추정)."""
    base = [0x7FFF, 0, 0x7FFF] + [0] * 13          # 0 투명, 1 외곽선(검정), 2 흰색
    base[15] = 0x021C
    hair = [u16s(rom, 0xA436E8 + 32 * k, 3) for k in range(7)] + [u16s(rom, 0xA436C8, 3)]
    skin = [u16s(rom, a, 5) for a in (0xA49900, 0xA49930, 0xA49960, 0xA498D0)]
    outfit = [u16s(rom, 0xA44070 + 8 * e, 4) for e in range(192)]
    return {"base": base, "hair": hair, "skin": skin, "outfitTable": outfit,
            "outfitEntry": "1 + 48*color + 4*outfit (추정)", "slots": {"outline": 1, "hair": [3, 5], "skin": [6, 10], "outfit": [11, 14]}}

def main(rom_path, out_dir, presets=None):
    rom = R.load(rom_path)
    groups = [g for g in R.all_groups(rom, 0xA14000, 0xA90000) if category(g["addr"])]
    blob = bytearray(); cats = {}
    for g in groups:
        c = category(g["addr"]); parts = []
        for p in g["parts"]:
            m = p["meta"]
            s8 = lambda b: b - 256 if b > 127 else b
            parts.append({"w": p["w"], "h": p["h"], "ax": s8(m[4]) if p["w"] else 0, "ay": s8(m[5]) if p["w"] else 0,
                          "ext": list(m[6:]), "off": len(blob)})
            blob += bytes(R.part_pixels(p)) if p["w"] else b""
        # hdr: 묶음 머리 2바이트 — 원작 0x080972EC 가 나이 구분(0x08097398)별 칸을 고른다:
        # 7~12세 hdr0 아래 4비트 · 13~34세 hdr0 위 4비트 · 35~60세 hdr1 아래 4비트 · 61세~ hdr1 위 4비트
        cats.setdefault(c, []).append({"rom": "0x%X" % g["addr"], "hdr": [rom[g["addr"]], rom[g["addr"] + 1]], "parts": parts})
    os.makedirs(out_dir, exist_ok=True)
    open(os.path.join(out_dir, "parts.bin.bytes"), "wb").write(blob)
    pre = {}
    for f in presets or []:
        pre.update(json.load(open(f)))
    json.dump({"format": 1, "source": "rom", "categories": cats, "palettes": palettes(rom), "presets": pre}, open(os.path.join(out_dir, "parts.json"), "w"), separators=(",", ":"))
    print({c: len(v) for c, v in cats.items()}, len(blob), "bytes")

if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2], sys.argv[3:])
