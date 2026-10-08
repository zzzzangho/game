#!/usr/bin/env python3
"""가로 스크롤 캡처 프레임을 이어 붙여 파노라마를 만든다 (로컬 전용).
사용: python3 -I stitch.py <프레임 glob> <출력.png> [y0 y1]   y0~y1 행만 정합에 사용"""
import sys, glob
import numpy as np
from PIL import Image

def edges(a):
    g = a.astype(np.int32).sum(axis=2)
    return (np.abs(np.diff(g, axis=1)) > 30).astype(np.int8)[:, :]

def main():
    fs = sorted(glob.glob(sys.argv[1])); out = sys.argv[2]
    y0, y1 = (int(sys.argv[3]), int(sys.argv[4])) if len(sys.argv) > 4 else (24, 120)
    frames = [np.asarray(Image.open(f).convert('RGB')) for f in fs]
    pos = [0]
    for a, b in zip(frames, frames[1:]):
        ea, eb = edges(a[y0:y1]), edges(b[y0:y1]); best, bd = 0, None
        for dx in range(-40, 41):                       # b 가 a 보다 dx 만큼 오른쪽으로 이동한 화면
            if dx >= 0: x = ea[:, dx:] ; y = eb[:, :eb.shape[1] - dx]
            else: x = ea[:, :dx]; y = eb[:, -dx:]
            d = (x != y).mean()
            if bd is None or d < bd: bd, best = d, dx
        pos.append(pos[-1] + best)
    mn, mx = min(pos), max(pos)
    W = mx - mn + 240; H = frames[0].shape[0]
    canvas = Image.new('RGB', (W, H))
    for p, f in zip(pos, frames): canvas.paste(Image.fromarray(f), (p - mn, 0))
    canvas.save(out); print('프레임', len(fs), '너비', W)

main()
