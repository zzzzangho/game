#!/usr/bin/env python3
"""원작 화살 연출(쏘기 하늘 장면 + 집에서 꽂히는 화살·터짐)을 사용자가 가진 ROM 에서 떠서 Unity 로컬 에셋으로 만든다.

  python3 -I arrow_fx.py <rom.gba> [출력 디렉터리]
  (기본 출력: unity/Assets/Resources/LocalArt — .gitignore 처리됨. 절대 커밋·배포하지 말 것)

실기(한글패치 멈춤수정 v1, 집 화면에서 → 로 다른 사람을 고르고 A · A · 힘내라 A — 고른 프레임 = 0):
  0 효과음 0x116 → 37 집이 닦이듯 사라지고 하늘 장면(76 0x1C2 큐피트 등장 · "조금 따끔할 거야~" · 131 0x1C3 · 154 0x117 "얍~!" 쏘기 · 화살이 왼쪽으로)
  → 227 하늘이 닦이듯 사라짐 → 245 집 → 241 0x123 화살이 오른쪽 화면 밖(x+79)에서 1프레임 10점씩 날아와 인물 그림(32×64) 안 (12, 36)에 꽂힘
  → 261 0x10C · 263 터짐(64×64, 인물 그림 기준 (−16, 11)) 3장 × 3프레임 + 빈 3프레임 되풀이 45프레임 → 306 0x10F(힘내라) / 0x110(진정해).
  진정해도 하늘 장면·화살·터짐 그림이 같다(마지막 효과음만 다름).
출력: arrow_shot_frames.bin.bytes (하늘 장면 37~227, 20fps — opening_frames.py 형식), fx_arrow.png.bytes (32×8), fx_burst_0~2.png.bytes (64×64).
"""
import os, sys, glob, subprocess, tempfile, shutil, hashlib

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from build_local_art import build_capture, intro_script  # noqa: E402
from opening_frames import pack, STEP  # noqa: E402
import oam_extract  # noqa: E402

PICK = ["run 30 0", "tap 16 40", "tap 1 60", "tap 1 40", "tap 1 6"]   # → 다른 사람 · 활쏘기 · 화살 고르기 창 · 힘내라 (끝 = 188프레임에 고름)
CHOSEN = 188
REEL_FROM, REEL_TO = 37, 227


def main():
    if len(sys.argv) < 2: print(__doc__); return 2
    rom = os.path.abspath(sys.argv[1])
    out = os.path.abspath(sys.argv[2]) if len(sys.argv) > 2 else os.path.join(HERE, "..", "..", "unity", "Assets", "Resources", "LocalArt")
    work = tempfile.mkdtemp(prefix="sk_arrow_")
    try:
        exe = build_capture(work)
        def run(lines):
            open(os.path.join(work, "s.txt"), "w").write("\n".join(lines) + "\n")
            subprocess.check_call([exe, rom, work, os.path.join(work, "s.txt")], stdout=subprocess.DEVNULL)
        run(intro_script())                                                     # → main.state (집 화면)
        a = CHOSEN + REEL_FROM - STEP + 1 - 194                                 # PICK 이 끝난 194프레임 뒤부터
        run(["loadstate main"] + PICK + ["run %d 0" % a, "runshot %d 0 %d f" % (REEL_TO - REEL_FROM + STEP, STEP),
             "run %d 0" % (CHOSEN + 252 - (CHOSEN + REEL_TO + 1)), "rundump 20 0 1 h"])
        shots = sorted(glob.glob(os.path.join(work, "f_*.ppm")))
        n, uniq = pack(shots, os.path.join(out, "arrow_shot_frames.bin.bytes"))
        print("하늘 장면 %d장 (서로 다른 화면 %d장)" % (n, uniq))
        arrow, bursts = None, []
        for f in sorted(glob.glob(os.path.join(work, "h_*.mem"))):
            for i, x, y, w, h, img in oam_extract.decode(open(f, "rb").read()):
                if (w, h) == (32, 8) and arrow is None: arrow = img
                if (w, h) == (64, 64):
                    k = hashlib.md5(img.tobytes()).hexdigest()
                    if k not in [b[0] for b in bursts]: bursts.append((k, img))
        if arrow is None or len(bursts) < 3: sys.exit("화살·터짐 그림을 찾지 못했다 (화살 %s, 터짐 %d장)" % (arrow is not None, len(bursts)))
        arrow.save(os.path.join(out, "fx_arrow.png.bytes"), format="PNG")
        for k, (_, img) in enumerate(bursts[:3]): img.save(os.path.join(out, "fx_burst_%d.png.bytes" % k), format="PNG")
        print("화살 32×8, 터짐 %d장" % min(3, len(bursts)))
    finally:
        if os.environ.get("KEEP_WORK"): print(work)
        else: shutil.rmtree(work, ignore_errors=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
