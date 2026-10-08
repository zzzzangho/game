#!/usr/bin/env python3
"""capture 의 rundump 메모리 덤프(.mem)에서 OAM 스프라이트 셀을 해독해 고유한 그림만 PNG 로 저장한다 (로컬 전용).
사용: python3 -I oam_extract.py <덤프 디렉터리> <출력 디렉터리> [최소폭 최소높이]"""
import sys, os, glob, struct, hashlib
from PIL import Image

SH = {0: [(8, 8), (16, 16), (32, 32), (64, 64)], 1: [(16, 8), (32, 8), (32, 16), (64, 32)], 2: [(8, 16), (8, 32), (16, 32), (32, 64)]}

def bgr(c): return ((c & 31) * 255 // 31, ((c >> 5) & 31) * 255 // 31, ((c >> 10) & 31) * 255 // 31, 255)

def decode(mem):
    pal = mem[0:0x400]; vram = mem[0x400:0x18400]; oam = mem[0x18400:0x18800]; io = mem[0x18800:]
    one_d = (struct.unpack_from('<H', io, 0)[0] >> 6) & 1
    out = []
    for i in range(128):
        a0, a1, a2 = struct.unpack_from('<HHH', oam, i * 8)
        if (a0 >> 8) & 3 == 2: continue                    # 숨김
        shape, size = (a0 >> 14) & 3, (a1 >> 14) & 3
        if shape == 3: continue
        w, h = SH[shape][size]; b8 = (a0 >> 13) & 1; tile = a2 & 1023; pb = a2 >> 12
        img = Image.new('RGBA', (w, h), (0, 0, 0, 0)); px = img.load()
        tw = w // 8
        for ty in range(h // 8):
            for tx in range(tw):
                if b8: t = tile + (ty * tw + tx) * 2 if one_d else tile + ty * 32 + tx * 2
                else:  t = tile + ty * tw + tx if one_d else tile + ty * 32 + tx
                base = 0x10000 + (t & 1023) * 32
                for yy in range(8):
                    for xx in range(8):
                        if b8:
                            ci = vram[base + yy * 8 + xx] if base + 64 <= len(vram) else 0
                            col = ci and struct.unpack_from('<H', pal, 0x200 + ci * 2)[0]
                        else:
                            byte = vram[base + yy * 4 + xx // 2] if base + 32 <= len(vram) else 0
                            ci = (byte >> ((xx & 1) * 4)) & 15
                            col = ci and struct.unpack_from('<H', pal, 0x200 + pb * 32 + ci * 2)[0]
                        if ci: px[tx * 8 + xx, ty * 8 + yy] = bgr(col)
        if (a1 >> 12) & 1 and not (a0 >> 8) & 1: img = img.transpose(Image.FLIP_LEFT_RIGHT)
        if (a1 >> 13) & 1 and not (a0 >> 8) & 1: img = img.transpose(Image.FLIP_TOP_BOTTOM)
        out.append((i, a1 & 511, a0 & 255, w, h, img))
    return out

def main():
    src, dst = sys.argv[1], sys.argv[2]; mw = int(sys.argv[3]) if len(sys.argv) > 3 else 1; mh = int(sys.argv[4]) if len(sys.argv) > 4 else 1
    os.makedirs(dst, exist_ok=True); seen = {}
    for f in sorted(glob.glob(os.path.join(src, '*.mem'))):
        for i, x, y, w, h, img in decode(open(f, 'rb').read()):
            if w < mw or h < mh or img.getbbox() is None: continue
            k = hashlib.md5(img.tobytes()).hexdigest()[:12]
            if k in seen: seen[k][1] += 1; continue
            seen[k] = [os.path.basename(f), 1]
            img.save(os.path.join(dst, '%dx%d_%s.png' % (w, h, k)))
    print('고유 셀', len(seen))

if __name__ == "__main__":
    main()
