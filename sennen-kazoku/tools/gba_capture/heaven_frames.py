#!/usr/bin/env python3
"""원작 도입부 천계 장면(높으신 신 · 큐피트, 글상자 없이)을 사용자가 가진 ROM 에서 프레임으로 떠서 Unity 로컬 에셋
(LocalArt/heaven_frames.bin.bytes)으로 묶는다. 형식은 opening_frames.py 와 같다("SKF1"). 앱은 이 위에 원작 대사(천계 글 564~ · 288~)를 띄운다.

  python3 -I heaven_frames.py <rom.gba> [출력 디렉터리]
  (기본 출력: unity/Assets/Resources/LocalArt — .gitignore 처리됨. 절대 커밋·배포하지 말 것)

실기(한글패치 멈춤수정 v1): 제목 → 처음부터 → 천계(곡 1). 글 567 "안녕! 잘 부탁해♪"(프레임 1985~)에서 글 넘김을 기다리는 동안
신과 큐피트가 제자리에서 움직인다. 글상자는 BG1 이라 그것만 끄고(BG2 를 함께 끄면 기둥·바닥도 사라진다) 4초(20fps)를 뜬다.
"""
import os, sys, glob, subprocess, tempfile, shutil

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from build_local_art import build_capture, intro_script  # noqa: E402
from opening_frames import pack, STEP  # noqa: E402

AT, SECONDS = 2110, 4


def prefix(until):
    """intro_script 의 앞부분을 until 프레임까지 (tap = 6 + 대기)."""
    out, f = [], 0
    for line in intro_script():
        p = line.split()
        n = 6 + int(p[2]) if p[0] == "tap" else int(p[1]) if p[0] == "run" else 0
        if f + n > until: break
        out.append(line); f += n
    out.append("run %d 0" % (until - f))
    return out


def main():
    if len(sys.argv) < 2:
        print(__doc__); return 2
    rom = os.path.abspath(sys.argv[1])
    out = os.path.abspath(sys.argv[2]) if len(sys.argv) > 2 else os.path.join(HERE, "..", "..", "unity", "Assets", "Resources", "LocalArt")
    os.makedirs(out, exist_ok=True)
    work = tempfile.mkdtemp(prefix="sk_heaven_")
    try:
        exe = build_capture(work)
        script = os.path.join(work, "s.txt")
        open(script, "w").write("\n".join(prefix(AT) + ["layer 1 0", "runshot %d 0 %d f" % (SECONDS * 60, STEP)]) + "\n")
        subprocess.check_call([exe, rom, work, script], stdout=subprocess.DEVNULL)
        shots = sorted(glob.glob(os.path.join(work, "f_*.ppm")))[1:]   # 첫 장은 층을 끄기 전 화면
        n, uniq = pack(shots, os.path.join(out, "heaven_frames.bin.bytes"))
        print("천계 장면 %d장 (서로 다른 화면 %d장) → %s" % (n, uniq, os.path.join(out, "heaven_frames.bin.bytes")))
    finally:
        if os.environ.get("KEEP_WORK"): print(work)
        else: shutil.rmtree(work, ignore_errors=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
