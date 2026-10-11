#!/usr/bin/env python3
"""시대·집 등급별 집 그림과 방 자리 (로컬 전용 — 원작 추출 그림, 배포·커밋 금지).

  python3 house_rom.py ROM.gba 상태폴더 [LocalArt 폴더]

원작 집 정의 표 0x08581768 + 716·집번호 (집번호 = 등급 + 4·시대, 원작 0x080171BC; 등급 = 0x0202C6AF 아래 4비트, 시대 = 가족 +0x682):
  +0x20 폭(px)·높이(px)…, +0x2E 타일 자원 · +0x40~ 자원 번호 20개(2 팔레트 · 3 시간대 팔레트 · 5 뒤 맵 · 6 앞 맵 · 7~ 방 가구 조각),
  +0x80~ 32바이트 항목들의 +0x10: 방 (x, y, 폭, 높이 — 타일, 종류)  ← 그림 속 방과 정확히 겹침(로컬 확인).
그림 자원은 자원 표 0x08003AB0 (타일 = 4bpp 무압축, 팔레트 = 15비트 색, 맵 = 폭×높이 u16 글자 BG 형식).
팔레트: 공용 3587(256색) 위에 집 팔레트(0~6), 시간대 팔레트 칸(8~11, 원작 0x08017AF8 이 장면 시각으로 고름 — HouseClock).
방 가구는 원작이 방마다 조각 맵을 덧씌운 작업용 맵(실기 RAM, 앞 맵 0x02004834 근처)을 쓰므로, 상태폴더의
st_e<시대>g<등급>.state(실기 새 게임 가족 만들기 끝에 시대·등급만 바꿔 심은 상태 — capture bpload)에서 RAM 을 떠 그 맵을 쓴다.
출력: house_e<시대>g<등급>_s<칸>.png.bytes (칸 0 낮 · 2 저녁 · 4 밤 · 6 새벽) + manifest.json "houses".
"""
import json, os, re, glob, struct, subprocess, sys, tempfile, shutil
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
SLOTS = (0, 2, 4, 6)


class Rom:
    def __init__(self, path):
        self.b = open(path, 'rb').read()
    def u32(self, a): return struct.unpack_from('<I', self.b, a - 0x08000000)[0]
    def res(self, i): return 0x08A00000 + self.u32(0x08A00004 + 16 * i) - 0x08000000
    def colors(self, i, slot=None):
        a = self.res(i)
        if slot is None: n, _, off = struct.unpack_from('<3I', self.b, a)
        else:
            n, _ = struct.unpack_from('<2I', self.b, a); off = struct.unpack_from('<I', self.b, a + 8 + 4 * slot)[0]
        return [struct.unpack_from('<H', self.b, a + off + 2 * k)[0] for k in range(n)]
    def house_def(self, h):
        b = 0x08581768 - 0x08000000 + 716 * h
        e1 = struct.unpack_from('<16h', self.b, b + 32)
        ids = [e1[14]] + [struct.unpack_from('<H', self.b, b + 64 + 4 * k)[0] for k in range(20)]
        rooms = []
        for k in range(4, 16):
            e = struct.unpack_from('<16h', self.b, b + 32 * k)
            x, y, w, hh, kind = e[8], e[9], e[10], e[11], e[12]
            if w > 0 and hh > 0: rooms.append([x * 8, (x + w) * 8, y * 8, (y + hh) * 8, kind])
        return e1, ids, rooms
    def tiles(self, i):
        a = self.res(i); n = struct.unpack_from('<I', self.b, a + 4)[0]; off = struct.unpack_from('<I', self.b, a + 12)[0]
        T = self.b[a + off:a + off + 32 * n]
        return [[(T[t * 32 + y * 4 + x // 2] >> (4 * (x & 1))) & 15 for y in range(8) for x in range(8)] for t in range(n)]
    def rom_map(self, i):
        a = self.res(i); w, h, off = struct.unpack_from('<3I', self.b, a)
        return w, h, self.b[a + off:a + off + 2 * w * h]


def rgb(v): return ((v & 31) << 3 | (v & 31) >> 2, ((v >> 5) & 31) << 3 | ((v >> 5) & 31) >> 2, ((v >> 10) & 31) << 3 | ((v >> 10) & 31) >> 2)


def ram_map(rom, ram, i):
    """작업용 맵: ROM 맵의 한 줄이 같은 곳을 RAM 에서 찾아 그 자리의 맵(가구 조각이 덧씌워진 것)을 쓴다."""
    w, h, data = rom.rom_map(i)
    for r in range(1, h):
        row = data[2 * w * r:2 * w * r + 2 * w]
        if len(set(row)) < 6: continue
        k = ram.find(row) if ram else -1
        if k >= 0: return w, h, ram[k - 2 * w * r:k - 2 * w * r + 2 * w * h]
    return w, h, data


def render(T, P, w, h, mp, out):
    px = out.load()
    for ty in range(h):
        for tx in range(w):
            e = struct.unpack_from('<H', mp, 2 * (ty * w + tx))[0]; t = e & 0x3FF; hf = (e >> 10) & 1; vf = (e >> 11) & 1; p = e >> 12
            if t >= len(T): continue
            src = T[t]
            for y in range(8):
                for x in range(8):
                    c = src[y * 8 + x]
                    if c: px[tx * 8 + (7 - x if hf else x), ty * 8 + (7 - y if vf else y)] = P[p * 16 + c]


def house_images(rom, h, ram):
    e1, ids, rooms = rom.house_def(h)
    T = rom.tiles(ids[0])
    maps = [ram_map(rom, ram, ids[5]), ram_map(rom, ram, ids[6])]   # 뒤 → 앞
    out = {}
    for s in SLOTS:
        pal = [0] * 256
        for k, v in enumerate(rom.colors(3587)[:256]): pal[k] = v
        for k, v in enumerate(rom.colors(ids[2])): pal[k] = v
        for k, v in enumerate(rom.colors(ids[3], s)): pal[128 + k] = v
        P = [rgb(v) for v in pal]
        w, hh = maps[0][0], maps[0][1]
        im = Image.new('RGB', (w * 8, hh * 8), P[0])
        for mw, mh, mp in maps: render(T, P, mw, mh, mp, im)
        out[s] = im
    return e1, rooms, out


def ramdump(exe, rom_path, state, work):
    d = tempfile.mkdtemp(dir=work); shutil.copy(state, os.path.join(d, 'm.state'))
    open(os.path.join(d, 's.txt'), 'w').write('loadstate m\nrun 5\nramdump r\n')
    subprocess.check_call([exe, rom_path, d, os.path.join(d, 's.txt')], stdout=subprocess.DEVNULL)
    return open(os.path.join(d, 'r.ram'), 'rb').read()


def main():
    if len(sys.argv) < 3: print(__doc__); return 2
    rom_path, sdir = os.path.abspath(sys.argv[1]), os.path.abspath(sys.argv[2])
    out = os.path.abspath(sys.argv[3]) if len(sys.argv) > 3 else os.path.join(HERE, "..", "..", "unity", "Assets", "Resources", "LocalArt")
    work = tempfile.mkdtemp(prefix="sk_house_", dir=os.environ.get("ART_TMP"))
    exe = os.path.join(work, "capture")
    subprocess.check_call(["gcc", "-O2", "-DUSE_DEBUGGERS", "-o", exe, os.path.join(HERE, "capture.c"), "-lmgba"])
    rom = Rom(rom_path)
    mpath = os.path.join(out, "manifest.json")
    man = json.load(open(mpath, encoding="utf8")) if os.path.exists(mpath) else {"schema": 1}
    houses = man["houses"] = {}
    for era in range(4):
        for g in range(4):
            st = os.path.join(sdir, "st_e%dg%d.state" % (era, g))
            ram = ramdump(exe, rom_path, st, work) if os.path.exists(st) else None
            e1, rooms, ims = house_images(rom, g + 4 * era, ram)
            key = "e%dg%d" % (era, g); files = {}
            for s, im in ims.items():
                name = "house_%s_s%d" % (key, s); im.save(os.path.join(out, name + ".png.bytes"), format="PNG"); files[str(s)] = name
            houses[key] = {"file": files["0"], "slots": files, "width": ims[0].width, "height": ims[0].height, "rooms": rooms,
                           "furnished": ram is not None}
            print(key, ims[0].size, "방", len(rooms), "가구" if ram else "가구 없음(ROM 맵)", flush=True)
    with open(mpath, "w", encoding="utf8") as f: json.dump(man, f, ensure_ascii=False, indent=1)
    if not os.environ.get("KEEP_WORK"): shutil.rmtree(work, ignore_errors=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
