#!/usr/bin/env python3
"""원작 말풍선·글상자 그림(글자를 지운 빈 것)을 사용자가 가진 ROM 에서 떠서 Unity 로컬 에셋(LocalArt/bubble_*.png.bytes)으로 만든다.

  python3 -I bubbles.py <rom.gba> [출력 디렉터리]
  (기본 출력: unity/Assets/Resources/LocalArt — .gitignore 처리됨. 절대 커밋·배포하지 말 것)

실기(한글패치 멈춤수정 v1)에서 그 화면의 배경 층을 VRAM 에서 바로 그린다(투명 = 색 0):
  bubble_god    천계 높으신 신 말풍선(크림색, 꼬리가 위쪽 가운데 — 신)   BG1, 글 564 가 뜨고 글자가 찍히기 전(1324프레임)
  bubble_cupid  천계 큐피트 말풍선(하늘색, 꼬리가 위쪽 왼쪽 — 큐피트)    BG1, 글 565 직전(1747프레임)
  bubble_tip    집 화면 큐피트 한마디(꼬리가 아래 왼쪽)                    BG3, 집 화면 + 1200프레임 "느긋하게 기다리자!"
  bubble_event  사건 장면 아래 글상자(꼬리가 위쪽 왼쪽 — 큐피트)          BG0, 사건 장면
  band_event    사건 장면 제목 띠                                         BG0, 사건 장면
집·사건 화면 것은 글자가 찍힌 뒤라 안쪽 글자를 바탕색으로 지운다(안쪽 사각형만 — 테두리·꼬리는 그대로).
앱은 9칸 늘이기(가장자리 고정)로 크기를 맞춘다: 테두리 값은 GameApp 참고.
"""
import os, sys, struct, collections, subprocess, tempfile, shutil
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from build_local_art import build_capture, intro_script  # noqa: E402
from heaven_frames import prefix  # noqa: E402


def render(mem, bg):
    """capture.c "dump" 결과(팔레트 · VRAM · OAM · IO)에서 배경 층 하나를 RGBA 로 (색 0 투명)."""
    pal, vram, io = mem[:0x400], mem[0x400:0x18400], mem[0x18800:]
    P = [struct.unpack_from('<H', pal, 2 * i)[0] for i in range(256)]
    rgb = lambda v: ((v & 31) << 3 | (v & 31) >> 2, ((v >> 5) & 31) << 3 | ((v >> 5) & 31) >> 2, ((v >> 10) & 31) << 3 | ((v >> 10) & 31) >> 2, 255)
    c = struct.unpack_from('<H', io, 8 + 2 * bg)[0]; cb, sb, size, bpp8 = (c >> 2) & 3, (c >> 8) & 31, c >> 14, (c >> 7) & 1
    W, H = [(32, 32), (64, 32), (32, 64), (64, 64)][size]
    im = Image.new('RGBA', (W * 8, H * 8), (0, 0, 0, 0)); px = im.load()
    for ty in range(H):
        for tx in range(W):
            blk = tx // 32 + (ty // 32) * (2 if W == 64 else 1)
            e = struct.unpack_from('<H', vram, sb * 0x800 + blk * 0x800 + 2 * ((ty % 32) * 32 + tx % 32))[0]
            t, hf, vf, pb = e & 0x3FF, (e >> 10) & 1, (e >> 11) & 1, e >> 12
            for y in range(8):
                for x in range(8):
                    if bpp8: ci = vram[cb * 0x4000 + t * 64 + y * 8 + x]; col = ci
                    else: ci = (vram[cb * 0x4000 + t * 32 + y * 4 + x // 2] >> (4 * (x & 1))) & 15; col = pb * 16 + ci
                    if ci: px[tx * 8 + (7 - x if hf else x), ty * 8 + (7 - y if vf else y)] = rgb(P[col])
    return im


def cut(im, win):
    c = im.crop(win); return c.crop(c.getbbox())


def clean(c, rect, dark_rows=None):
    """rect 안의 바탕색이 아닌 점(글자)을 바탕색으로. dark_rows=(y0,y1): 그 줄들에서는 어두운 점만 지운다(아래 테두리 보호)."""
    px = c.load(); x0, y0, x1, y1 = rect
    fill = collections.Counter(px[x, y] for x in range(x0, x1) for y in range(y0, y1) if px[x, y][3]).most_common(1)[0][0]
    for y in range(y0, y1):
        for x in range(x0, x1):
            if px[x, y][3] and px[x, y] != fill: px[x, y] = fill
    if dark_rows:
        for y in range(*dark_rows):
            for x in range(x0, x1):
                p = px[x, y]
                if p[3] and sum(p[:3]) < 450: px[x, y] = fill
    return c


def main():
    if len(sys.argv) < 2: print(__doc__); return 2
    rom = os.path.abspath(sys.argv[1])
    out = os.path.abspath(sys.argv[2]) if len(sys.argv) > 2 else os.path.join(HERE, "..", "..", "unity", "Assets", "Resources", "LocalArt")
    work = tempfile.mkdtemp(prefix="sk_bubble_")
    try:
        exe = build_capture(work)
        def run(lines):
            open(os.path.join(work, "s.txt"), "w").write("\n".join(lines) + "\n")
            subprocess.check_call([exe, rom, work, os.path.join(work, "s.txt")], stdout=subprocess.DEVNULL)
        run(prefix(1324) + ["dump g"]); run(prefix(1747) + ["dump c"])
        run(intro_script() + ["run 1200 0", "dump h", "run 2400 0", "dump e"])
        mem = lambda n: open(os.path.join(work, n + ".mem"), "rb").read()
        res = {
            "bubble_god": cut(render(mem("g"), 1), (0, 0, 512, 256)),
            "bubble_cupid": cut(render(mem("c"), 1), (0, 0, 512, 256)),
        }
        tip = cut(render(mem("h"), 3), (32, 17, 200, 50))
        res["bubble_tip"] = clean(tip, (5, 4, tip.width - 5, 20), (3, 22))
        ev = render(mem("e"), 0)
        e = cut(ev, (0, 104, 240, 160)); res["bubble_event"] = clean(e, (6, 17, e.width - 6, e.height - 5))
        b = cut(ev, (0, 0, 240, 32)); res["band_event"] = clean(b, (14, 4, b.width - 14, b.height - 4))
        for k, im in res.items():
            im.save(os.path.join(out, k + ".png.bytes"), format="PNG"); print(k, im.size)
    finally:
        shutil.rmtree(work, ignore_errors=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
