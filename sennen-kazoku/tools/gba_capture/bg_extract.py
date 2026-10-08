#!/usr/bin/env python3
"""메모리 덤프(.mem)에서 텍스트 모드 BG 레이어의 타일맵 전체를 그림으로 복원한다 (로컬 전용).
사용: python3 -I bg_extract.py <dump.mem> <출력 접두어>"""
import sys, struct
from PIL import Image

def bgr(c): return ((c & 31) * 255 // 31, ((c >> 5) & 31) * 255 // 31, ((c >> 10) & 31) * 255 // 31, 255)

def render(mem, n):
    pal = mem[0:0x400]; vram = mem[0x400:0x18400]; io = mem[0x18800:]
    cnt = struct.unpack_from('<H', io, 8 + n * 2)[0]
    cb = ((cnt >> 2) & 3) * 0x4000; b8 = (cnt >> 7) & 1; sb = ((cnt >> 8) & 31) * 0x800; size = cnt >> 14
    tw, th = [(32, 32), (64, 32), (32, 64), (64, 64)][size]
    img = Image.new('RGBA', (tw * 8, th * 8), (0, 0, 0, 0)); px = img.load()
    for ty in range(th):
        for tx in range(tw):
            blk = (tx // 32) + (ty // 32) * (2 if tw == 64 else 1)
            e = struct.unpack_from('<H', vram, sb + blk * 0x800 + ((ty % 32) * 32 + tx % 32) * 2)[0]
            t = e & 1023; hf = (e >> 10) & 1; vf = (e >> 11) & 1; pb = e >> 12
            for yy in range(8):
                for xx in range(8):
                    sx = 7 - xx if hf else xx; sy = 7 - yy if vf else yy
                    if b8:
                        a = cb + t * 64 + sy * 8 + sx
                        ci = vram[a] if a < 0x10000 else 0; col = struct.unpack_from('<H', pal, ci * 2)[0]
                    else:
                        a = cb + t * 32 + sy * 4 + sx // 2
                        ci = (vram[a] >> ((sx & 1) * 4)) & 15 if a < 0x10000 else 0; col = struct.unpack_from('<H', pal, pb * 32 + ci * 2)[0]
                    if ci: px[tx * 8 + xx, ty * 8 + yy] = bgr(col)
    return img, dict(cnt=hex(cnt), charbase=cb, screenbase=sb, size=(tw * 8, th * 8), bpp8=b8, prio=cnt & 3)

if __name__ == '__main__':
    mem = open(sys.argv[1], 'rb').read()
    disp = struct.unpack_from('<H', mem[0x18800:], 0)[0]
    print('DISPCNT', hex(disp), 'mode', disp & 7)
    for n in range(4):
        img, info = render(mem, n); print('BG%d' % n, info, 'on' if (disp >> (8 + n)) & 1 else 'off')
        img.save('%s_bg%d.png' % (sys.argv[2], n))
