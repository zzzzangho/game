#!/usr/bin/env python3
"""원작 배경음악·효과음을 사용자가 가진 ROM 에서 녹음해 Unity 로컬 에셋(LocalAudio/)으로 만든다.

  python3 -I audio_rip.py <rom.gba> [출력 디렉터리] [--all] [--only 1,3,0x84]
  (기본 출력: unity/Assets/Resources/LocalAudio — .gitignore 처리됨. 절대 커밋·배포하지 말 것)

원작 소리 엔진(표준 m4a 가 아닌 자체 엔진, docs/01): 머리 0x08266498 의 +0x10 = 곡 표(512칸), +0x1C = 곡마다 재생기 번호 표,
재생기 12개(0x0203C4BC, 0x50 바이트씩) · 트랙 45개(0x0203C87C, 0x30 바이트씩, +0x0C = 악보 읽는 위치).
재생 0x08248D80(곡, 음량, 팬) · 멈춤 0x08248E94(곡, 페이드) · 모두 멈춤 0x08248EE0. 재생기 0·1 = 음악(곡 104개), 나머지 = 효과음.

녹음 방법: 천계 첫 대화(신님 글 대기 — 아무것도 바뀌지 않는 화면) 직전 상태에서 원작이 부르는 첫 재생(곡 1)의 곡 번호를 바꾸고,
그 뒤의 재생·멈춤은 모두 막은 채 mGBA 소리 출력을 그대로 녹음한다(capture.c 의 audio · bpreg).
반복 구간은 소리 파형이 아니라 프레임마다 기록한 트랙 읽기 위치로 찾는다(원작 박자가 프레임에 딱 맞지 않아 파형은 반복마다 조금씩 어긋난다):
트랙은 악보 끝(그 트랙에서 가장 뒤 주소)에서 반복 시작으로 되돌아간다. 그 되돌아감이 일어난 프레임들의 간격 = 반복 길이 P(소수 프레임까지 평균),
첫 되돌아감 E1 − P = 반복 시작 S. 트랙 안의 짧은 되풀이(악보 끝이 아닌 곳에서의 되돌아감)는 무시한다.
결과: 반복하는 곡 = <이름>_a(S 까지 앞부분, 없을 수 있음) + <이름>_b(두 번째 반복 한 바퀴, 끝을 시작 직전과 이어 붙임), 한 번 곡 = <이름>.
목록: audio_index.json {"rate", "songs": {"번호": {"group", "intro", "loop"} | {"group", "once"}}}

ffmpeg 가 있으면 ogg(Vorbis), 없으면 wav 로 쓴다. Unity 는 둘 다 AudioClip 으로 가져온다.
"""
import os, sys, json, struct, shutil, subprocess, tempfile
from concurrent.futures import ThreadPoolExecutor
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from build_local_art import build_capture  # noqa: E402

DEFAULT_OUT = os.path.join(HERE, "..", "..", "unity", "Assets", "Resources", "LocalAudio")
RATE = 32768                                  # capture.c AUDIO_RATE
SPF = RATE * 280896 / 16777216                # 표본 / 프레임 (GBA 1프레임 = 280896 클럭)
HEAD, SONGS, NSONG, GROUPS = 0x08266498, 0x088A6A64, 512, 0x088A72A0
TRK, NTRK = 0x0203C87C, 45
MUSIC_FRAMES, SE_FRAMES = 21600, 900          # 음악은 6분(앞부분 + 반복 두 바퀴), 효과음 15초
# 앱이 쓰는 효과음 (--all 이 아니면 이것만): 단추·글 넘기기·확인, 오프닝 화살, 천계 배경 소리
APP_SE = [0xC9, 0xD3, 0xD4, 0xD7, 0xD8, 0xDA, 0xDC, 0xE7]

# 천계 첫 대화 직전까지 (한글패치 멈춤수정 v1 에서 확인: 이 상태 뒤 9프레임에 곡 1 재생, 그 뒤 글 대기)
BASE_SCRIPT = ["run 600 0", "tap 8 120", "tap 1 120", "tap 1 120"] + ["tap 1 40"] * 5 + ["savestate base"]
BLOCK = ["bpreg 0x08248d80 r0 0", "bpreg 0x08248ddc r0 0", "bpreg 0x08248e44 r0 0",
         "bpreg 0x08248e94 r0 0xda 1",          # 원작이 곡 1 직전에 천계 배경 소리(0xDA)를 끈다 — 그 한 번은 둔다
         "bpreg 0x08248e94 r0 0", "bpreg 0x08248ee0 pc 0x082492ac"]   # 모두 멈춤은 빈 함수(bx lr)로 돌린다


def rom_songs(rom):
    R = lambda a: struct.unpack_from("<I", rom, a - 0x08000000)[0]
    if R(HEAD + 0x10) != SONGS or R(HEAD + 0x1C) != GROUPS or rom[HEAD - 0x08000000 + 8] != 12:
        sys.exit("이 ROM 의 소리 표 위치가 다르다 (천년가족 한글패치 멈춤수정 v1 기준 도구)")
    base = R(R(HEAD + 0x14))                  # 재생기 표의 첫 칸 (재생기 k → base + 4k)
    out = {}
    for i in range(NSONG):
        if R(SONGS + 4 * i):
            out[i] = (R(GROUPS + 4 * i) - base) // 4
    return out


def record(exe, rom_path, base_state, song, frames, work):
    d = os.path.join(work, str(song)); os.makedirs(d, exist_ok=True)
    shutil.copy(base_state, os.path.join(d, "base.state"))
    L = ["loadstate base", f"bpreg 0x08248d80 r0 {song} 1"] + BLOCK + \
        ["audio a.wav", f"ramlog {frames} 0 1 {TRK:#x} {NTRK * 0x30:#x} trk.bin", "audio off"]
    open(os.path.join(d, "s.txt"), "w").write("\n".join(L) + "\n")
    subprocess.check_call([exe, rom_path, d, os.path.join(d, "s.txt")], stdout=subprocess.DEVNULL)
    x = np.fromfile(os.path.join(d, "a.wav"), dtype="<i2", offset=44).reshape(-1, 2)
    b = np.fromfile(os.path.join(d, "trk.bin"), dtype=np.uint8).reshape(-1, NTRK * 0x30 + 4)[:, 4:]
    p = b.reshape(-1, NTRK, 0x30)[:, :, 0xC:0x10].copy().view("<u4").reshape(-1, NTRK)
    shutil.rmtree(d)
    return x, p


def seq_loop(p):
    """트랙 읽기 위치 기록 p[프레임, 트랙] → ((S, P) 또는 None, 첫 재생 프레임, 마지막으로 움직인 프레임). S·P 는 소수 프레임."""
    act = [k for k in range(p.shape[1]) if len(np.unique(p[:, k])) > 2]
    if not act: return None, 0, 0
    q = p[:, act].astype(np.int64); F = len(q)
    mv = np.where((q[1:] != q[:-1]).any(axis=1))[0]          # 처음 바뀐 프레임 = 재생 시작 (그 전 값은 앞 곡이 남긴 것)
    if not len(mv): return None, 0, 0
    first, last = int(mv[0]) + 1, int(mv[-1]) + 1
    best = None
    for j in range(q.shape[1]):
        t = q[first:, j]; top = t.max()
        ends = [first + f for f in np.where(np.diff(t) < 0)[0] if t[f] >= top - 4]   # 악보 끝에서의 되돌아감 (읽기 위치는 되돌아갈 때만 준다)
        if len(ends) < 2: continue
        P = (ends[-1] - ends[0]) / (len(ends) - 1)
        if best is None or P > best[1] + 2: best = (ends[0] + 1, P)          # 가장 긴 반복을 가진 트랙 기준
    if best is None: return None, first, last
    E1, P = best
    return (max(float(first), E1 - P), P), first, last


def cut(x, p):
    lp, first, last = seq_loop(p)
    o = int(first * SPF)
    if lp is None:
        a = np.abs(x.astype(np.int32)).max(axis=1); nz = np.where(a > 8)[0]
        end = int(nz[-1]) + int(0.1 * RATE) if len(nz) else o
        end = min(end, int((last + 180) * SPF))           # 악보가 멈춘 뒤 3초까지 (길게 끄는 음은 거기서 자른다)
        return {"once": x[o:max(o, end)]}
    S, P = lp
    s, L = int(round(S * SPF)), int(round(P * SPF))
    a = s + L                                        # 두 번째 바퀴 시작 (앞 바퀴 끝 소리가 이어지는 곳)
    if a + L > len(x): a = s                        # 녹음 안에 두 번째 바퀴가 다 없으면 첫 바퀴
    cf = int(0.02 * RATE)
    loop = blend(x[a:a + L], x[a - cf:a], cf)
    intro = blend(x[o:s], x[a - cf:a], cf) if s - o > RATE // 10 else None   # 0.1초보다 짧은 앞부분은 반복 바퀴로 바로 시작
    return {"intro": intro, "loop": loop, "S": S - first, "P": P}


def blend(clip, prev, cf):
    """clip 끝 cf 표본을 prev(clip 시작 바로 앞에 원래 있던 소리)로 서서히 바꾼다 → 끝에서 시작으로 이어져도 튀지 않는다."""
    cf = min(cf, len(clip), len(prev))
    if cf <= 0: return clip
    w = np.linspace(0, 1, cf)[:, None]
    out = clip.astype(np.float64).copy()
    out[-cf:] = out[-cf:] * (1 - w) + prev[-cf:].astype(np.float64) * w
    return out


def write(clip, path_noext, use_ogg):
    clip = np.clip(np.round(clip), -32768, 32767).astype("<i2")
    wav = path_noext + ".wav"
    with open(wav, "wb") as f:
        n = len(clip)
        f.write(b"RIFF" + struct.pack("<I", 36 + n * 4) + b"WAVEfmt " + struct.pack("<IHHIIHH", 16, 1, 2, RATE, RATE * 4, 4, 16))
        f.write(b"data" + struct.pack("<I", n * 4)); f.write(clip.tobytes())
    if not use_ogg: return os.path.basename(wav)
    ogg = path_noext + ".ogg"
    subprocess.check_call(["ffmpeg", "-loglevel", "error", "-y", "-i", wav, "-c:a", "libvorbis", "-q:a", "3", ogg])
    os.remove(wav)
    return os.path.basename(ogg)


def main():
    args = [a for a in sys.argv[1:]]
    if not args: sys.exit(__doc__)
    rom_path = os.path.abspath(args.pop(0))
    every = "--all" in args
    only = None
    if "--only" in args:
        i = args.index("--only"); only = [int(v, 0) for v in args[i + 1].split(",")]; del args[i:i + 2]
    args = [a for a in args if a != "--all"]
    out = os.path.abspath(args[0] if args else DEFAULT_OUT)
    rom = open(rom_path, "rb").read()
    songs = rom_songs(rom)
    pick = only if only else sorted(s for s, g in songs.items() if every or g in (0, 1) or s in APP_SE)
    use_ogg = shutil.which("ffmpeg") is not None
    os.makedirs(out, exist_ok=True)
    work = tempfile.mkdtemp(prefix="sk_audio_")
    try:
        exe = build_capture(work)
        bdir = os.path.join(work, "base"); os.makedirs(bdir)
        open(os.path.join(bdir, "s.txt"), "w").write("\n".join(BASE_SCRIPT) + "\n")
        subprocess.check_call([exe, rom_path, bdir, os.path.join(bdir, "s.txt")], stdout=subprocess.DEVNULL)
        base_state = os.path.join(bdir, "base.state")
        idx_path = os.path.join(out, "audio_index.json")
        index = json.load(open(idx_path, encoding="utf-8")) if os.path.exists(idx_path) else {"rate": RATE, "songs": {}}

        def one(s):
            g = songs.get(s)
            if g is None: return s, None, "없는 곡"
            x, p = record(exe, rom_path, base_state, s, MUSIC_FRAMES if g in (0, 1) else SE_FRAMES, work)
            c = cut(x, p)
            name = ("bgm_%03d" if g in (0, 1) else "se_%03d") % s
            for old in os.listdir(out):                    # 같은 곡의 이전 결과는 지운다 (앞부분이 없어졌을 수 있다)
                if old.split(".")[0] in (name, name + "_a", name + "_b"): os.remove(os.path.join(out, old))
            if "once" in c:
                if len(c["once"]) < RATE // 50: return s, None, "소리 없음"
                return s, {"group": g, "once": write(c["once"], os.path.join(out, name), use_ogg)[:-4]}, "한 번 %.1f초" % (len(c["once"]) / RATE)
            e = {"group": g, "loop": write(c["loop"], os.path.join(out, name + "_b"), use_ogg)[:-4]}
            if c["intro"] is not None: e["intro"] = write(c["intro"], os.path.join(out, name + "_a"), use_ogg)[:-4]
            return s, e, "앞 %.1f초 + 반복 %.1f초" % (c["S"] / 59.7275, len(c["loop"]) / RATE)

        with ThreadPoolExecutor(max(1, min(4, os.cpu_count() or 1))) as ex:
            for s, e, msg in ex.map(one, pick):
                print("%3d (0x%03X) %s" % (s, s, msg), flush=True)
                if e: index["songs"][str(s)] = e
        index["rate"] = RATE
        json.dump(index, open(idx_path, "w", encoding="utf-8"), ensure_ascii=False, indent=1, sort_keys=True)
        print("→", out, "(%d곡)" % len(index["songs"]))
    finally:
        shutil.rmtree(work, ignore_errors=True)


if __name__ == "__main__":
    main()
