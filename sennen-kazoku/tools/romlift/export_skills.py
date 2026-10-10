#!/usr/bin/env python3
"""스킬 이름·설명 (로컬 전용 — 원문·번역 모두 저장소에 넣지 않는다).

스킬 표 0x088A309C: 스킬 번호(레코드 +0x62~0x64 값) → 항목 포인터. 항목은
  +0 이름 글 포인터 (번역 패치가 한국어 이름 0x0944xxxx 로 바꿔 둠) · +4 설명 글 포인터 ·
  +8 능력 보정 s16×4 (지력·체력·매력·운) · +0x10 바이트 2개 · +0x12 바이트 2개,
그리고 항목 바로 뒤(+0x14)에 다음 스킬의 원래 이름 글(일본어)이 붙어 있다 — 스킬 k 의 원래 이름 = 항목 k−1 의 +0x14 글.
설명은 패치가 옮기지 않아 일본어 그대로다("본문 / （효과）", 효과 줄은 색 토큰 1A 03 xx 로 감쌈).

  python3 export_skills.py ROM.gba 원문.json                       — 원문(일본어)·패치 이름·능력 보정을 뽑는다(번역용)
  python3 export_skills.py ROM.gba 원문.json orig_text.json 번역.json — 번역(이름·설명·효과)을 orig_text.json "skills" 로 넣는다
번역.json = [{"name": ..., "desc": ..., "effect": ...}, ...] (스킬 번호 순, 설명의 줄바꿈은 "/").
"""
import json, re, struct, sys

SKILLS = 0x088A309C


def sjis(rom, p, limit=400):
    """원래 일본어 글 (Shift-JIS). 1A 01 → "/", 색 토큰 1A 03 xx 는 뺀다."""
    o, out = p - 0x08000000, b''
    while len(out) < limit and rom[o] != 0:
        c = rom[o]
        if c == 0x1A:
            op = rom[o + 1]
            if op == 0xFF: break
            if op == 0x01: out += b'/'
            o += 3 if op in (0x03, 0x0A) else 2
            continue
        if c >= 0x80: out += rom[o:o + 2]; o += 2
        else: out += bytes([c]); o += 1
    return out.decode('cp932', errors='replace').replace('　', ' ')


def entries(rom):
    r = []
    for k in range(256):
        e = struct.unpack_from('<I', rom, SKILLS - 0x08000000 + 4 * k)[0]
        if not 0x08000000 <= e < 0x0A000000: break
        r.append(e)
    return r


def main():
    if len(sys.argv) < 3:
        print(__doc__); return 2
    rom = open(sys.argv[1], 'rb').read()
    es = entries(rom)
    if len(sys.argv) == 3:
        rows = []
        for k, e in enumerate(es):
            desc = sjis(rom, struct.unpack_from('<I', rom, e - 0x08000000 + 4)[0])
            body, _, eff = desc.partition('/（')
            rows.append({"k": k, "ja_name": sjis(rom, es[k - 1] + 0x14, 60) if k else "", "ja_desc": body,
                         "ja_effect": eff.rstrip('）'), "mods": list(struct.unpack_from('<4h', rom, e - 0x08000000 + 8))})
        json.dump(rows, open(sys.argv[2], 'w', encoding='utf8'), ensure_ascii=False, indent=1)
        print('스킬 %d개 원문' % len(rows)); return 0
    pack = json.load(open(sys.argv[3], encoding='utf8'))
    ko = json.load(open(sys.argv[4], encoding='utf8'))
    if len(ko) != len(es): print('번역 개수 %d ≠ 스킬 %d' % (len(ko), len(es))); return 1
    for x in ko:
        if re.search(r'[぀-ヿ]', x['name'] + x['desc'] + x.get('effect', '')): print('가나가 남은 번역:', x); return 1
    pack['skills'] = [{"name": x['name'], "desc": x['desc'].replace('/', '\n'), "effect": x.get('effect', '')} for x in ko]
    json.dump(pack, open(sys.argv[3], 'w', encoding='utf8'), ensure_ascii=False, separators=(',', ':'))
    print('스킬 번역 %d개를 넣음' % len(ko))


if __name__ == '__main__':
    sys.exit(main())
