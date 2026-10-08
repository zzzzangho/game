#!/usr/bin/env python3
"""사용자가 가진 천년가족 ROM 에서 게임용 그래픽을 추출해 Unity 로컬 에셋 폴더를 만든다.

  python3 -I build_local_art.py <rom.gba> [출력 디렉터리]
  (기본 출력: unity/Assets/Resources/LocalArt  — .gitignore 처리됨. 절대 커밋·배포하지 말 것)

동작: libmgba 헤드리스 캡처(capture.c)로 게임을 정해진 입력으로 진행 → 메인 화면에서
  ① 매 프레임 OAM 을 해독해 스프라이트 셀(캐릭터·큐피트·UI) 수집
  ② 카메라 변수(0x020047F0 등)를 움직여 집 전체 배경 파노라마(스프라이트·HUD 레이어 끔)
  ③ 셀 분류 → manifest.json
모든 그림은 PNG 를 '.png.bytes'(TextAsset) 로 저장한다. Unity 가 런타임에 Texture2D.LoadImage 로 읽는다.
"""
import os, sys, json, glob, hashlib, subprocess, shutil, tempfile
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from oam_extract import decode  # noqa: E402

# 이 ROM(한글패치 멈춤수정 v1)에서 확인한 UI 셀의 내용 해시 → 이름
UI_NAMES = {
    "303fc4f0fcde": "heart_empty", "6d1444fce864": "heart_half", "ec4ddf72ac45": "heart_full",
    "305a79e66cf4": "marker_select", "cf3fb9c445d5": "hud_family_a", "e5e2d8364f8c": "hud_family_b",
    "16b77aeb1aef": "gauge_empty", "4208c04c462e": "gauge_mid", "30b098ccfb64": "gauge_full",
    "022d87322d33": "bubble_note_pink", "28382d557487": "bubble_note_green", "683532e8d989": "bubble_note_pink2",
    "5fa3b02864fe": "bubble_note_green2", "c848597a4ae5": "bubble_note_green3", "810f31207722": "bubble_note_white",
    "c21b60b5cbb4": "bubble_note_white2", "dfdcbf2d075e": "bubble_note_white3",
    "0c7284edbaba": "zzz_a", "2f9981c72dc9": "zzz_b", "65bb0347398c": "zzz_c", "fe62957aae51": "zzz_d",
    "5dff2d7c4c68": "outing", "f02827b9a0ce": "btn_bow",
}

CAP_SRC = os.path.join(HERE, "capture.c")


def build_capture(work):
    exe = os.path.join(work, "capture")
    subprocess.check_call(["gcc", "-O2", "-DUSE_DEBUGGERS", "-o", exe, CAP_SRC, "-lmgba"])
    return exe


def run(exe, rom, out, lines, env=None):
    sp = os.path.join(out, "script.txt")
    open(sp, "w").write("\n".join(lines) + "\n")
    e = dict(os.environ); e.update(env or {})
    subprocess.check_call([exe, rom, out, sp], env=e, stdout=subprocess.DEVNULL)


def intro_script():
    """타이틀 → 도입부 → 튜토리얼 → 메인 화면까지 고정 입력."""
    L = ["run 600 0", "tap 8 120", "tap 1 120", "tap 1 120"]
    L += ["tap 1 40"] * (120 + 160 + 200)
    L += ["tap 2 60"] * 4 + ["run 200 0", "tap 16 60", "run 300 0", "tap 256 60", "tap 2 60", "tap 8 60", "tap 128 30",
                             "tap 1 60", "tap 2 60", "tap 2 60", "tap 4 60", "tap 2 60", "tap 512 60", "tap 2 60", "run 1800 0",
                             "savestate main"]
    return L


def edges(a):
    g = a.astype(np.int32).sum(axis=2)
    return np.abs(np.diff(g, axis=1)) > 30


def panorama(exe, rom, work):
    d = os.path.join(work, "pano"); os.makedirs(d); shutil.copy(os.path.join(work, "main.state"), d)
    run(exe, rom, d, ["loadstate main", "layer 4 0", "layer 2 0", "layer 3 0", "sweep 0x020047F0 0 2600 40 W"],
        {"SWEEP_ALL": "1", "SWEEP_SETTLE": "40", "SWEEP_RELOAD": "main"})
    fs = sorted(glob.glob(os.path.join(d, "W_*.ppm")))
    raw = [np.asarray(Image.open(f).convert("RGB")) for f in fs]
    pos = [0]
    for a, b in zip(raw, raw[1:]):
        ea, eb = edges(a[20:120]), edges(b[20:120])
        err, dx = min((((ea[:, k:] != eb[:, :eb.shape[1] - k]).mean(), k) for k in range(0, 120)))
        pos.append(pos[-1] + (dx if err < 0.03 else 30))
    W = pos[-1] + 240
    can = np.zeros((160, W, 3), np.uint8)
    for p, r in zip(pos, raw): can[:, p:p + 240] = r
    Image.fromarray(can).save(os.path.join(work, "pano_full.png"))
    # 세계는 '아파트 → 바깥 → 아파트' 로 순환한다. 시작 200px(현관)의 가장자리 패턴이 다시 나오는 곳 = 주기
    g = edges(can[20:120]); ref = g[:, :200]
    err, period = min((((g[:, P:P + 200] != ref).mean(), P) for P in range(500, W - 240)))
    return Image.fromarray(can[:, :period]), period, err


def find_rooms(img):
    """방 내부(밝은 벽지 띠, y 56~64)가 60px 이상 이어지는 구간 = 방. 4px 이하 끊김은 이어 붙인다. 반환 [[x0, x1], ...] (GBA px)"""
    a = np.asarray(img.convert("RGB")).astype(int)[56:64]
    inside = list(a.mean(axis=2).mean(axis=0) >= 214) + [False]
    x = 0
    while x < len(inside):                      # 짧은 틈 메우기
        if not inside[x]:
            e = x
            while e < len(inside) and not inside[e]: e += 1
            if 0 < x and e < len(inside) and e - x <= 4:
                for k in range(x, e): inside[k] = True
            x = e
        else: x += 1
    rooms, start = [], None
    for x, v in enumerate(inside):
        if v and start is None: start = x
        elif not v and start is not None:
            if x - start >= 60: rooms.append([start, x])
            start = None
    return rooms


def collect_cels(exe, rom, work):
    d = os.path.join(work, "dump"); os.makedirs(d); shutil.copy(os.path.join(work, "main.state"), d)
    L = ["loadstate main"]
    for i in range(10):                       # 선택 인물을 바꿔 가며(초상화) + 시간 경과(걷기·애니메이션)
        L += ["tap 16 30", "rundump 400 0 5 a%02d" % i]
    run(exe, rom, d, L)
    cels = {}
    for f in sorted(glob.glob(os.path.join(d, "*.mem"))):
        for _, x, y, w, h, img in decode(open(f, "rb").read()):
            if img.getbbox() is None: continue
            k = hashlib.md5(img.tobytes()).hexdigest()[:12]
            if k not in cels: cels[k] = img
    return cels


def colors(img):
    a = np.asarray(img); m = a[:, :, 3] > 0
    return a[m][:, :3]


def is_highlight(img):
    """화살 효과 프레임: 바깥 외곽선(투명과 맞닿은 픽셀)이 검정 대신 빨강/분홍이다."""
    a = np.asarray(img).astype(int); m = a[:, :, 3] > 0
    pad = np.pad(m, 1)
    edge = m & ~(pad[:-2, 1:-1] & pad[2:, 1:-1] & pad[1:-1, :-2] & pad[1:-1, 2:])
    c = a[edge][:, :3]
    if len(c) == 0: return False
    return float(((c[:, 0] - c[:, 1]) > 60).mean()) > 0.4


def body_key(img):
    """옷 색(몸통) — 앞·뒷모습을 같은 인물로 묶는 기준."""
    bb = img.getbbox()
    if not bb: return None
    a = np.asarray(img)[max(bb[3] - 12, bb[1]):bb[3] - 3]
    m = a[:, :, 3] > 0; c = a[m][:, :3].astype(int)
    c = c[(c.sum(axis=1) > 150) & (np.abs(c[:, 0] - c[:, 2]) < 200)]
    if len(c) == 0: return None
    q = (c // 32) * 32
    vals, cnt = np.unique(q, axis=0, return_counts=True)
    return tuple(int(v) for v in vals[cnt.argmax()])


def crop_top(img):
    bb = img.getbbox()
    return img.crop((0, bb[1], img.width, img.height)) if bb else img


def hair_key(img, rows):
    a = np.asarray(crop_top(img))[rows[0]:rows[1]]
    m = a[:, :, 3] > 0; c = a[m][:, :3].astype(int)
    if len(c) == 0: return None
    c = c[(c.sum(axis=1) > 120) & (c.sum(axis=1) < 700)]        # 외곽선·하이라이트 제외
    if len(c) == 0: return None
    q = (c // 24) * 24
    vals, cnt = np.unique(q, axis=0, return_counts=True)
    return tuple(int(v) for v in vals[cnt.argmax()])


def skin_count(img, rows=(6, 22)):
    a = np.asarray(crop_top(img))[rows[0]:rows[1]].astype(int)
    m = a[:, :, 3] > 0
    r, g, b = a[:, :, 0], a[:, :, 1], a[:, :, 2]
    return int((m & (r > 220) & (g > 160) & (g < 230) & (b > 110) & (b < 200)).sum())


def classify(cels):
    ui, chars, heads, cupid, other = {}, {}, {}, [], []
    for k, img in cels.items():
        if k in UI_NAMES: ui[UI_NAMES[k]] = k; continue
        w, h = img.size
        if (w, h) == (32, 64):
            if is_highlight(img): continue                         # 화살 효과 깜빡임 프레임은 런타임 틴트로 대체
            key = body_key(img)
            chars.setdefault(key, {"front": [], "back": [], "hair": []})
            front = skin_count(img) > 25
            chars[key]["front" if front else "back"].append(k)
            if front: chars[key]["hair"].append(hair_key(img, (0, 6)))
        elif (w, h) == (32, 32):
            key = hair_key(img, (0, 6))
            if img.getbbox() and (img.getbbox()[2] - img.getbbox()[0]) >= 30 and len(np.unique(colors(img), axis=0)) <= 3: other.append(k); continue
            if key and key[0] >= 216 and key[1] >= 192 and key[2] <= 144 and key[1] >= key[0] * 0.85: cupid.append(k)   # 노란 머리 = 큐피트
            elif not is_highlight(img) and skin_count(img) > 25 and np.asarray(img)[:, :, 3].sum() / 255 < 600: heads.setdefault(key, []).append(k)
            else: other.append(k)
        else:
            other.append(k)
    # 같은 머리색의 얼굴(초상화)을 캐릭터에 연결
    # 같은 초상화를 가진 무리는 한 인물(옷 무늬 차이로 갈라진 경우)로 합친다
    merged = {}
    for key, v in chars.items():
        if not v["front"]: continue
        hk0 = max(set(v["hair"]), key=v["hair"].count) if v["hair"] else None
        mk = hk0
        if mk in merged:
            for f in ("front", "back", "hair"): merged[mk][f] += v[f]
        else: merged[mk] = {"front": list(v["front"]), "back": list(v["back"]), "hair": list(v["hair"]), "body": key}
    chars = {v["body"]: v for v in merged.values()}
    out_chars = []
    for i, (key, v) in enumerate(sorted(chars.items(), key=lambda kv: -len(kv[1]["front"]))):
        if not v["front"]: continue
        portrait = None; hk0 = max(set(v["hair"]), key=v["hair"].count) if v["hair"] else None
        best = None
        for hk, hv in heads.items():
            if hk is None or hk0 is None: continue
            d = sum(abs(a - b) for a, b in zip(hk, hk0))
            if best is None or d < best[0]: best = (d, hv[0])
        if best and best[0] <= 72: portrait = best[1]
        out_chars.append({"id": "c%d" % i, "hair": list(hk0) if hk0 else None, "body": list(key) if key else None, "front": v["front"], "back": v["back"], "portrait": portrait})
    return ui, out_chars, cupid, other


def main():
    if len(sys.argv) < 2: print(__doc__); return 2
    rom = os.path.abspath(sys.argv[1])
    out = os.path.abspath(sys.argv[2]) if len(sys.argv) > 2 else os.path.join(HERE, "..", "..", "unity", "Assets", "Resources", "LocalArt")
    work = tempfile.mkdtemp(prefix="sk_art_", dir=os.environ.get("ART_TMP"))
    exe = build_capture(work)
    print("1/4 메인 화면까지 진행…"); run(exe, rom, work, intro_script())
    print("2/4 스프라이트 수집…"); cels = collect_cels(exe, rom, work)
    print("   고유 셀", len(cels))
    print("3/4 집 파노라마…"); pano, period, perr = panorama(exe, rom, work)
    print("   순환 폭 %dpx (정합 오차 %.3f)" % (period, perr))
    print("4/4 분류·저장…")
    ui, chars, cupid, other = classify(cels)
    os.makedirs(out, exist_ok=True)
    for f in glob.glob(os.path.join(out, "*.bytes")): os.remove(f)
    def save(img, name):
        img.save(os.path.join(out, name + ".png.bytes"), format="PNG"); return name
    for k, img in cels.items(): save(img, "cel_" + k)
    save(pano, "house_day")
    manifest = {"schema": 1, "source": "local ROM extraction (do not distribute)", "scale": "GBA px",
                "house": {"file": "house_day", "rooms": find_rooms(pano), "width": pano.width, "height": pano.height, "floorY": 112, "loops": True, "note": "현관부터 한 바퀴(바깥 포함). 시간대 팔레트는 캡처 시점 기준"},
                "ui": {n: "cel_" + k for n, k in ui.items()},
                "characters": [{**c, "front": ["cel_" + x for x in c["front"]], "back": ["cel_" + x for x in c["back"]],
                                "portrait": ("cel_" + c["portrait"]) if c["portrait"] else None} for c in chars],
                "cupid": ["cel_" + k for k in cupid], "unclassified": ["cel_" + k for k in other]}
    with open(os.path.join(out, "manifest.json"), "w", encoding="utf8") as f: json.dump(manifest, f, ensure_ascii=False, indent=1)
    print("완료:", out, "| 캐릭터", len(chars), "| 큐피트 프레임", len(cupid), "| UI", len(ui), "| 미분류", len(other))
    if not os.environ.get("KEEP_WORK"): shutil.rmtree(work, ignore_errors=True)
    else: print("작업 폴더:", work)
    return 0


if __name__ == "__main__":
    sys.exit(main())
