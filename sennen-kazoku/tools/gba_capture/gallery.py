#!/usr/bin/env python3
"""원작 '캐릭터 선택' 목록(7페이지×12) 추출: 가운데 선택 캐릭터의 OAM 셀을 인덱스(4bpp) 그대로 저장한다 (로컬 전용).
사용: python3 -I gallery.py <capture 실행파일> <rom> <상태 디렉터리> <상태이름> <출력 디렉터리> <접두어>
출력: <접두어>_<번호>.idx (u16 폭, u16 높이, 인덱스 바이트) + <접두어>_pal.json (OBJ 팔레트 뱅크) + 미리보기 PNG"""
import sys, os, glob, struct, json, subprocess, hashlib
from PIL import Image

SH = {0: [(8, 8), (16, 16), (32, 32), (64, 64)], 1: [(16, 8), (32, 8), (32, 16), (64, 32)], 2: [(8, 16), (8, 32), (16, 32), (32, 64)]}

def cel_indices(mem, entry):
    vram = mem[0x400:0x18400]; oam = mem[0x18400:0x18800]
    a0, a1, a2 = struct.unpack_from('<HHH', oam, entry * 8)
    w, h = SH[(a0 >> 14) & 3][(a1 >> 14) & 3]; tile = a2 & 1023; tw = w // 8
    idx = bytearray(w * h)
    for ty in range(h // 8):
        for tx in range(tw):
            base = 0x10000 + ((tile + ty * tw + tx) & 1023) * 32
            for yy in range(8):
                for xx in range(8):
                    b = vram[base + yy * 4 + xx // 2]
                    idx[(ty * 8 + yy) * w + tx * 8 + xx] = (b >> ((xx & 1) * 4)) & 15
    return w, h, bytes(idx), a2 >> 12

def find_center(mem):
    oam = mem[0x18400:0x18800]; best = None
    for i in range(128):
        a0, a1, a2 = struct.unpack_from('<HHH', oam, i * 8)
        if (a0 >> 8) & 3 == 2 or (a0 >> 14) != 2 or (a1 >> 14) != 3: continue    # 32x64 만
        d = abs((a1 & 511) + 16 - 120) + abs((a0 & 255) - 60)
        if best is None or d < best[0]: best = (d, i)
    return best[1] if best else None

def main():
    exe, rom, sdir, state, out, pre = sys.argv[1:7]
    os.makedirs(out, exist_ok=True)
    work = os.path.join(sdir, "gal_" + pre); os.makedirs(work, exist_ok=True)
    for f in glob.glob(os.path.join(work, "*.mem")): os.remove(f)
    import shutil; shutil.copy(os.path.join(sdir, state + ".state"), work)
    L = ["loadstate " + state]
    for p in range(7):
        for i in range(12): L += ["run 6 0", "rundump 1 0 1 g%d_%02d" % (p, i), "tap 16 24"]
        L += ["tap 128 40"]
    sp = os.path.join(work, "s.txt"); open(sp, "w").write("\n".join(L) + "\n")
    subprocess.check_call([exe, rom, work, sp], stdout=subprocess.DEVNULL)
    seen = {}; pal = None; n = 0
    for f in sorted(glob.glob(os.path.join(work, "g*.mem"))):
        mem = open(f, "rb").read(); e = find_center(mem)
        if e is None: continue
        w, h, idx, bank = cel_indices(mem, e)
        k = hashlib.md5(idx).hexdigest()
        if k in seen: continue
        seen[k] = n
        pal = [struct.unpack_from('<H', mem, 0x200 + bank * 32 + c * 2)[0] for c in range(16)]
        with open(os.path.join(out, "%s_%02d.idx" % (pre, n)), "wb") as fo: fo.write(struct.pack("<HH", w, h) + idx)
        rgb = [((c & 31) * 8, ((c >> 5) & 31) * 8, ((c >> 10) & 31) * 8) for c in pal]
        im = Image.new("RGBA", (w, h)); im.putdata([(0, 0, 0, 0) if v == 0 else rgb[v] + (255,) for v in idx])
        im.save(os.path.join(out, "%s_%02d.png" % (pre, n)))
        n += 1
    json.dump({"bank_palette_bgr555": pal}, open(os.path.join(out, pre + "_pal.json"), "w"))
    print(pre, "고유 캐릭터", n)

if __name__ == "__main__":
    main()
