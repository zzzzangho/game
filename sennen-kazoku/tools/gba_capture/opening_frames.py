#!/usr/bin/env python3
"""원작 오프닝 데모(전원을 켠 뒤 로고 다음 밤하늘 대화 → 큐피트 → 얼굴 그리드 → 가족 행렬 → 집 → 큐피트 화살 → 타이틀)를
사용자가 가진 ROM 에서 프레임으로 떠서 Unity 로컬 에셋(LocalArt/opening_frames.bin.bytes)으로 묶는다.

  python3 -I opening_frames.py <rom.gba> [출력 디렉터리]
  (기본 출력: unity/Assets/Resources/LocalArt — .gitignore 처리됨. 절대 커밋·배포하지 말 것)

원작은 천년가족·가계 끊김 엔딩 카드 뒤에 다시 시작해(실기: Nintendo · indies zero 로고 → 이 데모 → 타이틀)
같은 데모를 보여 준다. 앱은 엔딩 카드 뒤에 이 프레임을 재생한다. 로고(상표) 구간은 넣지 않는다.

구간: 세이브 없이 켠 뒤 프레임 525 ~ 5010(타이틀 직전 흰 화면) (이 ROM — 한글패치 멈춤수정 v1 — 에서 실기 엔딩 뒤 화면과 맞춰 정함, 3프레임마다 = 20fps).
형식: "SKF1" · u16 장 수 · u16 fps · 장마다 [u16 반복 수 · u32 PNG 길이 · PNG(256색)]. 같은 화면이 이어지면 반복 수로 묶는다.
"""
import os, sys, io, glob, struct, subprocess, tempfile, shutil
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from build_local_art import build_capture  # noqa: E402

START_FRAME, END_FRAME, STEP, FPS = 525, 5010, 3, 20


def pack(paths, out):
    frames, prev = [], None
    for f in paths:
        im = Image.open(f).convert('RGB')
        raw = im.tobytes()
        if prev is not None and raw == prev:
            frames[-1][1] += 1
            continue
        prev = raw
        q = im.quantize(colors=256, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE)
        bio = io.BytesIO(); q.save(bio, 'PNG', optimize=True)
        frames.append([bio.getvalue(), 1])
    with open(out, 'wb') as o:
        o.write(b'SKF1'); o.write(struct.pack('<HH', len(frames), FPS))
        for png, rep in frames:
            o.write(struct.pack('<HI', rep, len(png))); o.write(png)
    return len(paths), len(frames)


def main():
    if len(sys.argv) < 2:
        print(__doc__); return 2
    rom = os.path.abspath(sys.argv[1])
    out = os.path.abspath(sys.argv[2]) if len(sys.argv) > 2 else os.path.join(HERE, "..", "..", "unity", "Assets", "Resources", "LocalArt")
    os.makedirs(out, exist_ok=True)
    work = tempfile.mkdtemp(prefix="sk_opening_")
    try:
        exe = build_capture(work)
        script = os.path.join(work, "s.txt")
        with open(script, "w") as s:
            s.write("run %d 0\n" % (START_FRAME - STEP))
            s.write("runshot %d 0 %d f\n" % (END_FRAME - START_FRAME + STEP, STEP))
        subprocess.check_call([exe, rom, work, script], stdout=subprocess.DEVNULL)
        shots = sorted(glob.glob(os.path.join(work, "f_*.ppm")))
        n, uniq = pack(shots, os.path.join(out, "opening_frames.bin.bytes"))
        print("오프닝 데모 %d장 (서로 다른 화면 %d장) → %s" % (n, uniq, os.path.join(out, "opening_frames.bin.bytes")))
    finally:
        shutil.rmtree(work, ignore_errors=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
