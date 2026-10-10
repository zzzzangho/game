#!/usr/bin/env python3
"""사건 장면 그림 뽑기 (로컬 전용 — 원작 그래픽이므로 결과물을 저장소에 넣지 않는다).

원작 사건 장면은 층으로 나뉜다(실기 캡처로 확인): BG0 제목 띠·대사 상자 · BG1 액자 테두리 · BG2 뒤 무늬(햇살) ·
BG3 액자 안 배경 그림(56,40 에서 128×64) · OBJ 인물·큐피트·감정 말풍선.
결과 기록(0x30 바이트)의 +0x20 제목 띠 모양, +0x24 뒤 무늬, +0x28 액자 배경 묶음 포인터가 그림을 고른다
(한 기록의 값을 다른 기록 값으로 바꾸면 그 부분만 바뀐다 — 분석용 ROM 복사본으로 확인).

방법: 사건이 막 시작하는 세이브 상태(사건 기록 R 이 뜨는 상태)에서, ROM 복사본의 R 의 필드를 각 값으로 바꿔 가며
헤드리스 캡처(capture.c)로 층 하나만 켜서 찍는다. 제목 글은 R 의 +0x0C 를 빈 글로 돌려 띠만 남긴다.

  python3 event_art.py <capture 실행 파일> <ROM> <세이브상태(.state, 확장자 빼고)> <기록 R 주소> <사건 시작 뒤 프레임> <orig_rules.json> <출력 디렉터리>
출력(앱 LocalArt 이름, .png.bytes): ev_pic_<포인터> (128×64, BG3) · ev_band_<포인터> (240×24, BG0 위쪽, 글 없음) ·
      ev_back_<포인터> (240×160, BG2) · ev_frame (BG1) · ev_cupid (32×40, OBJ) · ev_emo_<번호> (감정 말풍선 32×19, OBJ) ·
      ev_npc_<인자 5바이트> (가족이 아닌 사람 32×48, OBJ) — 회색 바탕(99,99,99)은 투명.
감정 말풍선: 기록 R 대사 머리의 "1A 0E 02 (동작) (인물)" 의 동작 번호를 0~0x14 로 바꿔 OBJ 층을 찍고 첫 인물 머리 위
(액자 안 33,0 ~ 65,19)를 자른다 — 이 자리는 기록 0x08923648(인물 둘) 기준이다. 0x15 부터는 말풍선 없이 자세만 바뀐다.
"""
import json, os, shutil, struct, subprocess, sys
from PIL import Image


# 감정 말풍선 움직임 (실기 프레임 단위 캡처로 확인, 기록 0x08923648 의 ♪ 기준):
# 작게 4 → 빈 풍선 4 → 내용 (C6·D8·C6·E8·C6·D8·C6·E8·C6·D8·C6) → 빈 풍선 4 → 작게 4 → 숨김 30, 122프레임마다 되풀이.
# 감정마다 첫 인물만 말풍선을 띄우고(둘째 인물은 말풍선 없는 0x15) 한 주기 넘게 매 프레임 찍어, 숨김 프레임과 다른 픽셀만 남긴
# 그림을 순서대로 모은다. 결과: ev_emo_<번호>_<k> (k = 처음 나온 순서) · ev_emo_anim.json
# {"crop": [첫 인물 OBJ 왼쪽 기준 x, 화면 y, 폭, 높이], "<번호>": [[k, 프레임 수], ...] (k = −1 숨김)} · ev_emo_<번호> (내용 첫 그림, 정지 그림용).
EMO_FRAMES = 260


def emotions(shot, with_, base, toks, save, out):
    import hashlib
    actor_x = 89                                   # 기록 0x08923648 첫 인물 OBJ 왼쪽 (두 사람, 실기 OAM)
    box = (actor_x - 8, 16, actor_x + 40, 72)     # 말풍선이 들어갈 만한 넓은 영역(화면 좌표)
    runs, frames_all = {}, {}
    for e in range(0x15):
        raw = [(toks[0] + 3, bytes([e]))] + [(t + 3, b'\x15') for t in toks[1:]]
        ims = shot(4, with_(0x28, base[3][1]), raw, extra=[1] * EMO_FRAMES)
        crops = [im.crop(box) for im in ims]
        keys = [hashlib.md5(c.tobytes()).hexdigest() for c in crops]
        # 숨김 프레임 = 그림 픽셀(회색 바탕이 아닌 칸)이 가장 적은 프레임 (말풍선이 없고 인물 머리만 남은 때)
        hid = keys[min(range(len(crops)), key=lambda i: sum(1 for q in crops[i].getdata() if q != (99, 99, 99)))]
        hidden = crops[keys.index(hid)]
        # 한 주기: 숨김이 끝난 첫 프레임부터 다음 숨김 끝까지
        ends = [i for i in range(1, len(keys)) if keys[i - 1] == hid and keys[i] != hid]
        if len(ends) < 2: print('말풍선 주기 못 찾음', e); continue
        a, b = ends[0], ends[1]
        order, seq = [], []
        for i in range(a, b):
            k = -1 if keys[i] == hid else (order.index(keys[i]) if keys[i] in order else (order.append(keys[i]) or len(order) - 1))
            if seq and seq[-1][0] == k: seq[-1][1] += 1
            else: seq.append([k, 1])
        runs['%02X' % e] = seq
        frames_all[e] = [crops[keys.index(h)] for h in order]
        hp = list(hidden.getdata())
        frames_all[e] = (frames_all[e], hp)
    # 모든 감정의 말풍선 픽셀을 덮는 공통 자르기 영역
    x0 = y0 = 10 ** 9; x1 = y1 = -1; W = box[2] - box[0]
    for e, (fs, hp) in frames_all.items():
        for f in fs:
            for i, p in enumerate(f.getdata()):
                if p != hp[i]: x, y = i % W, i // W; x0, y0, x1, y1 = min(x0, x), min(y0, y), max(x1, x), max(y1, y)
    for e, (fs, hp) in frames_all.items():
        for k, f in enumerate(fs):
            im = Image.new('RGBA', f.size, (0, 0, 0, 0)); fp = list(f.getdata())
            im.putdata([(0, 0, 0, 0) if fp[i] == hp[i] else fp[i] + (255,) for i in range(len(fp))])
            im = im.crop((x0, y0, x1 + 1, y1 + 1))
            im.save(os.path.join(out, 'ev_emo_%02X_%d.png.bytes' % (e, k)), 'PNG')
            if k == 2: im.save(os.path.join(out, 'ev_emo_%02X.png.bytes' % e), 'PNG')   # 내용 첫 그림(작게·빈 풍선 다음)
    json.dump({"crop": [box[0] + x0 - actor_x, box[1] + y0, x1 - x0 + 1, y1 - y0 + 1], **runs},
              open(os.path.join(out, 'ev_emo_anim.json.bytes'), 'w'), separators=(',', ':'))
    print('말풍선 움직임', len(runs), '가지, 자르기', [box[0] + x0 - actor_x, box[1] + y0, x1 - x0 + 1, y1 - y0 + 1])


# 뒤 무늬(BG2) 움직임: 색이 도는 무늬가 있다(기록 0x08923648 의 햇살 = 6장 × 16프레임 = 96프레임 주기, 실기). 다른 층은 움직이지 않는다.
# 무늬마다 BG2 만 켜고 매 프레임 BACK_FRAMES 장 찍어, 바뀌는 순서대로 그림과 프레임 수를 모은다.
# 결과: ev_back_<포인터>_<k> · ev_back_<포인터>(첫 그림) · ev_back_anim.json {"<포인터>": [[k, 프레임 수], ...]} (한 장뿐이면 [[0, 1]]).
BACK_FRAMES = 200


def backs(shot, with_, values, out):
    import hashlib
    table = {}
    for v in values:
        ims = shot(2, with_(0x24, v), extra=[1] * BACK_FRAMES)
        keys = [hashlib.md5(im.tobytes()).hexdigest() for im in ims]
        order, seq = [], []
        for i, k in enumerate(keys):
            if k not in order:
                order.append(k)
                ims[i].convert('RGBA').save(os.path.join(out, 'ev_back_%08X_%d.png.bytes' % (v, len(order) - 1)), 'PNG')
            j = order.index(k)
            if seq and seq[-1][0] == j: seq[-1][1] += 1
            else: seq.append([j, 1])
        if len(order) > 1:   # 처음과 끝은 잘린 조각 → 첫 그림이 다시 시작하는 곳부터 한 주기
            starts = [i for i in range(1, len(seq)) if seq[i][0] == seq[1][0]]
            cyc = seq[starts[0]:starts[1]] if len(starts) >= 2 else seq[1:-1]
        else: cyc = [[0, 1]]
        table['%08X' % v] = cyc
        ims[0].convert('RGBA').save(os.path.join(out, 'ev_back_%08X.png.bytes' % v), 'PNG')
    json.dump(table, open(os.path.join(out, 'ev_back_anim.json.bytes'), 'w'), separators=(',', ':'))
    print('뒤 무늬 움직임', sum(1 for c in table.values() if len(c) > 1), '/', len(table))


def main():
    if len(sys.argv) < 8:
        print(__doc__); return 2
    cap, rom_path, state, rec, frames, rules_path, out = sys.argv[1:8]
    only_emo = len(sys.argv) > 8 and sys.argv[8] in ('emo', 'back')   # 말풍선만(emo) · 뒤 무늬 움직임만(back) 다시 뽑기
    only_back = len(sys.argv) > 8 and sys.argv[8] == 'back'
    rec, frames = int(rec, 16), int(frames)
    os.makedirs(out, exist_ok=True)
    work = os.path.join(out, '_work'); os.makedirs(work, exist_ok=True)
    wrom = os.path.join(work, 'work.gba'); shutil.copyfile(rom_path, wrom)
    shutil.copyfile(state + '.state', os.path.join(work, 'st.state'))
    rom = open(rom_path, 'rb').read()
    R = rec - 0x08000000
    rules = json.load(open(rules_path, encoding='utf8'))
    recs = sorted(set(int(d, 16) for d in rules.get('variantData', {})))
    vals = {0x20: set(), 0x24: set(), 0x28: set()}
    for r in recs:
        f = [struct.unpack_from('<I', rom, r - 0x08000000 + o)[0] for o in (0x20, 0x24, 0x28)]
        for o, v in zip((0x20, 0x24, 0x28), f): vals[o].add(v)
    empty = rom.find(b'\x1a\xff', 0x00800000) + 0x08000000   # 빈 글 (제목 지우기)

    def shot(layer, patch, raw=(), extra=3):
        with open(wrom, 'r+b') as f:
            for o, v in patch: f.seek(R + o); f.write(struct.pack('<I', v))
            for a, b in raw: f.seek(a); f.write(b)
        steps = extra if isinstance(extra, list) else [extra]
        lines = ['loadstate st', 'run %d 0' % frames] + ['layer %d %d' % (j, 1 if j == layer else 0) for j in range(5)]
        for k, n in enumerate(steps): lines += ['run %d 0' % n, 'shot cur%d' % k]
        open(os.path.join(work, 's.txt'), 'w').write('\n'.join(lines) + '\n')
        subprocess.run([cap, wrom, work, os.path.join(work, 's.txt')], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=300, cwd=work)
        ims = [Image.open(os.path.join(work, 'cur%d.ppm' % k)).convert('RGB') for k in range(len(steps))]
        return ims if isinstance(extra, list) else ims[0]

    def save(im, name, clear=True):
        im = im.convert('RGBA')
        if clear: im.putdata([(0, 0, 0, 0) if p[:3] == (99, 99, 99) else p for p in list(im.getdata())])
        im.save(os.path.join(out, name + '.png.bytes'), 'PNG')

    def ok(v): return 0x08880000 <= v < 0x08890000   # 그림 표(0x0888xxxx)를 가리키지 않는 값(특수 기록 몇 개)은 건너뛴다
    base = [(o, struct.unpack_from('<I', rom, R + o)[0]) for o in (0x0C, 0x20, 0x24, 0x28)]
    def with_(o, v): return [(a, (v if a == o else (empty if a == 0x0C else b))) for a, b in base]
    if not only_emo: save(shot(1, with_(0x28, base[3][1])), 'ev_frame')
    if not only_emo:
        for v in sorted(filter(ok, vals[0x28])): save(shot(3, with_(0x28, v)).crop((56, 40, 184, 104)), 'ev_pic_%08X' % v, False)
        for v in sorted(filter(ok, vals[0x20])): save(shot(0, with_(0x20, v)).crop((0, 0, 240, 24)), 'ev_band_%08X' % v)
        backs(shot, with_, sorted(filter(ok, vals[0x24])), out)
        save(shot(4, with_(0x28, base[3][1])).crop((8, 64, 40, 104)), 'ev_cupid')   # 큐피트 (OBJ, 화면 14~32, 70~100)
    dlg = struct.unpack_from('<I', rom, R + 0x14)[0] - 0x08000000
    toks, i = [], rom.find(b'\x1a\x0e\x02', dlg, dlg + 64)
    while i >= 0 and i < dlg + 64: toks.append(i); i = rom.find(b'\x1a\x0e\x02', i + 1, dlg + 64)
    if only_back:
        backs(shot, with_, sorted(filter(ok, vals[0x24])), out); return 0
    emotions(shot, with_, base, toks, save, out)
    if only_emo: return 0
    # 가족이 아닌 사람(1A 0E 04 인자 5바이트): 원작 대사에 나오는 조합마다 R 의 첫 04 토큰 인자를 바꾸고, 동작을 말풍선 없는 0x15 로 두고
    # OBJ 층에서 왼쪽 인물(액자 안 32~64, 16~64 — 발은 아래에서 6줄 위)을 자른다. R 은 04 → 01 순서로 두 사람이 나오는 기록이어야 한다.
    combos = set()
    for r in recs:
        p = struct.unpack_from('<I', rom, r - 0x08000000 + 0x14)[0]
        if not 0x08000000 <= p < 0x0A000000: continue
        o = p - 0x08000000
        for _ in range(6000):
            if rom[o] == 0x1A:
                op = rom[o + 1]
                if op in (0xFF, 0x12): break
                if op == 0x0E:
                    sub = rom[o + 2]
                    if sub == 4: combos.add(rom[o + 3:o + 8])
                    o += {0: 4, 3: 4, 1: 7, 2: 5, 4: 8}.get(sub, 3); continue
                o += {1: 2, 2: 2, 9: 2, 0xD: 2, 3: 3, 0xA: 3, 5: 4, 6: 4, 8: 4, 0xB: 4, 0xF: 4, 0x10: 5}.get(op, 2)
            else: o += 2 if rom[o] >= 0x80 else 1
    s4 = rom.find(b'\x1a\x0e\x04', dlg, dlg + 64)
    if s4 >= 0:
        for cb in sorted(combos):
            im = shot(4, with_(0x28, base[3][1]), [(s4 + 3, cb)] + [(t + 3, b'\x15') for t in toks])
            save(im.crop((56 + 32, 40 + 16, 56 + 64, 40 + 64)), 'ev_npc_' + cb.hex().upper())
    print('배경 %d · 띠 %d · 무늬 %d (포인터가 아닌 값 포함 수) · 말풍선 21 · 가족이 아닌 사람 %d' % (len(vals[0x28]), len(vals[0x20]), len(vals[0x24]), len(combos)))


if __name__ == '__main__':
    sys.exit(main())
