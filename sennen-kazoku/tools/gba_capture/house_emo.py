#!/usr/bin/env python3
"""집 화면 감정 말풍선(그림 자원 3288, 인물 작업 +0xDE0 의 스프라이트) 그림과 움직임 순서를 실기 화면에서 뽑는다 (로컬 전용 — 결과 커밋 금지).

원작 규칙(0x080133EA~, 말풍선 스프라이트 +0x20 = 움직임 번호):
  2 관심사 분류 3 · 3 성격 궁합 보통 · 4 성격과 맞음 · 5 잠(작업 +0xDD0 비트 0x20) · 6 성격과 안 맞음 · 7 사랑(랭크>3) · 8 꿈(랭크>5) · 9 새 꿈(랭크>6)
집 화면 상태(m.state, 2005-01-01 집, 인물 1 = 엄마가 400프레임 뒤 거실에 서 있음)에서 인물 1 의 조건만 바꿔 각 번호가 나오게 하고,
매 프레임 OAM·VRAM 덤프에서 인물 1 말풍선 OBJ 를 그대로 푼다(빈 그림 = 숨김).

사용: python3 -I house_emo.py <capture 실행 파일> <ROM> <m.state 가 있는 작업 폴더> <출력 폴더>
결과: house_emo_<번호>_<k>.png.bytes(32×32, 인물 OBJ 32×64 의 왼쪽 위에 겹쳐 그린다) · house_emo_<번호>.png.bytes · house_emo_anim.json.bytes
  {"crop": [0, 0, 32, 32], "<번호>": [[k, 프레임 수], ...] (k = −1 숨김)}
"""
import sys, os, json, subprocess, hashlib
from PIL import Image

REC1 = 0x0202CA94          # 인물 1 레코드 (0x0202C6C4 + 976)
TASK1 = 0x02011960         # 인물 1 작업
RANK = 0x0202C66E
BODY_TILE = 800            # 인물 1 몸 OBJ 의 VRAM 타일 (인물 0~3 = 768·800·832·864, 실기 OAM)
START, FRAMES = 380, 300
START5, TILE5 = 2200, 449   # 잠: 인물 0 이 자는 때(이 상태 2200~2600 프레임). 자는 사람은 몸 OBJ 가 없고(침대 그림) 말풍선만 16줄 아래 — 말풍선 타일(449)을 바로 따라간다

CASES = {   # 번호 → 넣을 값 [(주소, 값, 크기)]
    3: [],
    4: [(REC1 + 0x80, 2, 2), (REC1 + 0x82, 175, 2), (REC1 + 0x32, 0x11, 1)],    # 관심사 성격 1 = 인물 성격 1
    2: [(REC1 + 0x80, 3, 2), (REC1 + 0x82, 0, 2)],                              # 분류 3 관심사
    6: [(REC1 + 0x80, 2, 2), (REC1 + 0x82, 175, 2)],                            # 관심사 성격 1 ≠ 인물 성격 2
    7: [(RANK, 7, 2), (REC1 + 0x80, 1, 2), (REC1 + 0x82, 70, 2)],               # +0x11 = 0
    9: [(RANK, 7, 2), (REC1 + 0x80, 1, 2), (REC1 + 0x82, 22, 2)],               # +0x11 = 1
    8: [(RANK, 7, 2), (REC1 + 0x80, 0, 2), (REC1 + 0x82, 16, 2), (REC1 + 0x3E, 0, 2)],   # 꿈 0 의 관련 관심사
    5: [],                                                                      # 잠: 밤까지 돌린다(START5)
}


def frames(cap, rom, work, n, pokes):
    """인물 1 말풍선 OBJ(32×32, 몸 OBJ 32×64 와 같은 자리)를 매 프레임 OAM·VRAM 에서 그대로 푼다 — 배경·카메라와 상관없다."""
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import oam_extract as oe, struct
    d = os.path.join(work, 'he%d' % n); os.makedirs(d, exist_ok=True)
    for f in os.listdir(d): os.remove(os.path.join(d, f))
    import shutil; shutil.copy(os.path.join(work, 'm.state'), os.path.join(d, 'm.state'))
    lines = ['loadstate m'] + ['poke 0x%08X %d %d' % p for p in pokes] + ['run %d 0' % (START5 if n == 5 else START), 'rundump %d 0 1 m' % (FRAMES * 2 if n == 5 else FRAMES)]
    open(os.path.join(d, 's.txt'), 'w').write('\n'.join(lines) + '\n')
    subprocess.run([cap, rom, d, os.path.join(d, 's.txt')], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=900)
    fs = sorted(f for f in os.listdir(d) if f.startswith('m_') and f.endswith('.mem'))
    tile, out = (TILE5 if n == 5 else None), []
    for f in fs:
        mem = open(os.path.join(d, f), 'rb').read(); oam = mem[0x18400:0x18800]
        objs = oe.decode(mem)
        tiles = {i: struct.unpack_from('<H', oam, i * 8 + 4)[0] & 1023 for i, *_ in objs}
        if tile is None:   # 인물 1 몸 OBJ(32×64, VRAM 타일 BODY_TILE)와 같은 자리에 처음 나온 32×32 의 타일을 그 뒤로 따라간다
            body = [(x, y) for i, x, y, w, h, im in objs if (w, h) == (32, 64) and tiles[i] == BODY_TILE]
            pairs = [i for i, x, y, w, h, im in objs if (w, h) == (32, 32) and (x, y) in body]
            if not pairs: out.append(None); continue
            tile = tiles[pairs[0]]
        im = next((im for i, x, y, w, h, im in objs if (w, h) == (32, 32) and tiles[i] == tile), None)
        out.append(im if im is not None else Image.new('RGBA', (32, 32), (0, 0, 0, 0)))
    return out


def main():
    cap, rom, work, out = sys.argv[1:5]
    os.makedirs(out, exist_ok=True)
    runs = {}
    only = [int(x) for x in sys.argv[5].split(',')] if len(sys.argv) > 5 else None
    for n, pokes in CASES.items():
        if only and n not in only: continue
        ims = [im if im is not None else Image.new('RGBA', (32, 32), (0, 0, 0, 0)) for im in frames(cap, rom, work, n, pokes)]
        empty = lambda im: im.getbbox() is None
        keys = ['' if empty(im) else hashlib.md5(im.tobytes()).hexdigest() for im in ims]
        ends = [i for i in range(1, len(keys)) if keys[i - 1] == '' and keys[i] != '']
        if len(ends) < 2: print('주기 못 찾음', n, len(set(keys))); continue
        a, b = ends[0], ends[1]
        order, seq = [], []
        for i in range(a, b):
            k = -1 if keys[i] == '' else (order.index(keys[i]) if keys[i] in order else (order.append(keys[i]) or len(order) - 1))
            if seq and seq[-1][0] == k: seq[-1][1] += 1
            else: seq.append([k, 1])
        runs[str(n)] = seq
        for k, h in enumerate(order):
            im = ims[keys.index(h)]
            im.save(os.path.join(out, 'house_emo_%d_%d.png.bytes' % (n, k)), 'PNG')
            if k == 2: im.save(os.path.join(out, 'house_emo_%d.png.bytes' % n), 'PNG')
        print(n, '주기', b - a, '그림', len(order), seq)
    json.dump({"crop": [0, 0, 32, 32], **runs}, open(os.path.join(out, 'house_emo_anim.json.bytes'), 'w'), separators=(',', ':'))


if __name__ == '__main__':
    main()
