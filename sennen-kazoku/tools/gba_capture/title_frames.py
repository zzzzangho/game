#!/usr/bin/env python3
"""원작 제목 화면(로고 "천년가족" · 집 · 큐피트 · PRESS START · 낮 → 저녁 → 밤으로 바뀌는 하늘)을 사용자가 가진 ROM 에서 프레임으로 떠서
Unity 로컬 에셋(LocalArt/title_frames.bin.bytes)으로 묶는다. 형식은 opening_frames.py 와 같다("SKF1").

  python3 -I title_frames.py <rom.gba> [출력 디렉터리]
  (기본 출력: unity/Assets/Resources/LocalArt — .gitignore 처리됨. 절대 커밋·배포하지 말 것)

실기(한글패치 멈춤수정 v1): 전원을 켜고 로고(Nintendo · indies zero — 상표라 넣지 않는다) 중에 START(프레임 600~605)를 누르면
흰 화면에서 제목이 밝아진다(606 제목 곡 0x84 시작). 아무것도 누르지 않으면 약 52초 뒤(3746 곡 페이드, 3875 모두 멈춤) 오프닝 데모로 간다.
뜨는 구간: 프레임 606 ~ 3870 (3프레임마다 = 20fps). 제목 곡 0x84 와 같은 시각에 시작한다.
"""
import os, sys, glob, subprocess, tempfile, shutil

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from build_local_art import build_capture  # noqa: E402
from opening_frames import pack, STEP  # noqa: E402

PRESS_AT, START_FRAME, END_FRAME = 600, 606, 3870


def main():
    if len(sys.argv) < 2:
        print(__doc__); return 2
    rom = os.path.abspath(sys.argv[1])
    out = os.path.abspath(sys.argv[2]) if len(sys.argv) > 2 else os.path.join(HERE, "..", "..", "unity", "Assets", "Resources", "LocalArt")
    os.makedirs(out, exist_ok=True)
    work = tempfile.mkdtemp(prefix="sk_title_")
    try:
        exe = build_capture(work)
        script = os.path.join(work, "s.txt")
        with open(script, "w") as s:
            s.write("run %d 0\nrun 6 8\n" % PRESS_AT)                                   # 로고 중 START
            s.write("run %d 0\n" % (START_FRAME - PRESS_AT - 6 - STEP + 1))
            s.write("runshot %d 0 %d f\n" % (END_FRAME - START_FRAME + STEP, STEP))
        subprocess.check_call([exe, rom, work, script], stdout=subprocess.DEVNULL)
        shots = sorted(glob.glob(os.path.join(work, "f_*.ppm")))
        n, uniq = pack(shots, os.path.join(out, "title_frames.bin.bytes"))
        print("제목 화면 %d장 (서로 다른 화면 %d장) → %s" % (n, uniq, os.path.join(out, "title_frames.bin.bytes")))
    finally:
        if os.environ.get("KEEP_WORK"): print(work)
        else: shutil.rmtree(work, ignore_errors=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
