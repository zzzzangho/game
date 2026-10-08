#!/usr/bin/env python3
"""원작 캐릭터 선택 갤러리 캡처(gallery.py 의 <역할>_NN.idx)를 ROM 파트 조합으로 분해해 프리셋 json 을 만든다 (로컬 전용).
사용: python3 -I gallery_fit.py <rom.gba> <idx 디렉터리> <역할> <출력.json>
좌표 하강: 각 분류의 모든 번호를 규칙 위치에 넣어 보고 차이가 가장 작은 것을 고른다(몸통 먼저/머리 먼저 두 출발점, 얼굴·몸통은 함께도 탐색, 각 3회)."""
import json, os, struct, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import romparts as R, charcompose as C

def load_idx(f):
    b = open(f, "rb").read(); w, h = struct.unpack_from("<HH", b); return w, h, b[4:4 + w * h]

def preset_palette(idx_path, img, w):
    """갤러리 미리보기 PNG 에서 색 번호별 실제 색을 읽어 BGR555 16칸으로 (없는 칸 -1)."""
    png = idx_path[:-4] + ".png"
    pal = [-1] * 16
    if not os.path.exists(png): return pal
    from PIL import Image
    im = Image.open(png).convert("RGB")
    for i, v in enumerate(img):
        if v and pal[v] < 0:
            r, g, b = im.getpixel((i % w, i // w)); pal[v] = (r >> 3) | (g >> 3) << 5 | (b >> 3) << 10
    return pal

def fit(lib, img, age=1):
    diff = lambda l: sum(1 for a, b in zip(C.compose(lib, l, age=age), img) if a != b)
    cats = {"hair": [(i, 0) for i in range(len(lib.cat["hairfront"]))], "face": [(i, 0) for i in range(len(lib.cat["face"]))],
            "body": [(i, 0) for i in range(48, 72)],
            "facebody": [((fc, b), 0) for fc in range(len(lib.cat["face"])) for b in range(48, 72)],
            "hairface": [((h, fc), 0) for h in range(len(lib.cat["hairfront"])) for fc in range(len(lib.cat["face"]))],
            "eyes": [(i, f) for i in range(len(lib.cat["eyes"])) for f in (0, 1)],
            "nose": [(i, f) for i in range(len(lib.cat["nose"])) for f in (0, 1)],
            "mouth": [(i, f) for i in range(len(lib.cat["mouth"])) for f in (0, 1)]}
    best_all = None
    # 국소 최솟값을 피하려고 출발점 셋: 몸통 먼저 / 머리 먼저 / (그래도 10px 넘게 다르면) 머리·얼굴 동시 탐색
    for order in (("body", "face", "hair", "facebody"), ("hair", "facebody"), ("hairface", "facebody")):
        if order[0] == "hairface" and best_all[0] <= 10: break
        look = {"body": 48, "face": 0, "hair": 0, "eyes": 0, "nose": 0, "mouth": 0}
        for _ in range(3):
            for k in order + ("eyes", "nose", "mouth"):
                best = None
                for i, f in cats[k]:
                    l = dict(look)
                    if k == "facebody": l["face"], l["body"] = i
                    elif k == "hairface": l["hair"], l["face"] = i
                    else: l[k] = i
                    if k in ("eyes", "nose", "mouth"): l[k + "Flip"] = f
                    d = diff(l)
                    if best is None or d < best[0]: best = (d, l)
                look = best[1]
            if best[0] == 0: break
        if best_all is None or best[0] < best_all[0]: best_all = best
        if best_all[0] == 0: break
    return best_all[1], best_all[0]

if __name__ == "__main__":
    rom, d, role, out = sys.argv[1:5]
    lib = C.Library(R.load(rom)); res = {}
    # 연령 칸(파트 묶음 안 순번)은 목록마다 하나 — 앞 3명으로 고른다.
    # 1) 모든 분류 같은 칸으로 맞춰 보고 2) 그 결과에서 분류별(몸통·얼굴·머리·눈코입) 칸 조합 256가지를 비교한다.
    probe = [load_idx(os.path.join(d, "%s_%02d.idx" % (role, n)))[2] for n in range(3) if os.path.exists(os.path.join(d, "%s_%02d.idx" % (role, n)))]
    base = min(range(4), key=lambda a: sum(fit(lib, im, a)[1] for im in probe))
    looks = [fit(lib, im, base)[0] for im in probe]
    import itertools
    def total(a):
        A = dict(zip(C.AGE_KEYS, a))
        return sum(sum(1 for x, y in zip(C.compose(lib, l, age=A), im) if x != y) for l, im in zip(looks, probe))
    age = dict(zip(C.AGE_KEYS, min(itertools.product(range(4), repeat=4), key=total)))
    if os.environ.get("FIT_AGE"): age = json.loads(os.environ["FIT_AGE"])     # 수동 지정 (예: 노인 {"body":2,"face":1,"hair":1,"feat":2})
    print(role, "연령 칸", age, flush=True)
    for n in range(84):
        f = os.path.join(d, "%s_%02d.idx" % (role, n))
        if not os.path.exists(f): continue
        w, h, img = load_idx(f)
        look, dd = fit(lib, img, age)
        res["%s_%02d" % (role, n)] = dict(look, role=role, slot=n, age=age, diffPixels=dd, palette=preset_palette(f, img, w))
        print(role, n, dd, flush=True)
    json.dump(res, open(out, "w"), ensure_ascii=False, indent=0)
