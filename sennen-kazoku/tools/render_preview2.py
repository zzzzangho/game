#!/usr/bin/env python3
"""새 화면 구성 미리보기 (Unity 캡처 아님). ScreenPreview JSON + LocalArt(로컬 추출 원작 그래픽)를
GameApp.cs 와 같은 배치 수식으로 합성한다. 결과물에 원작 그래픽이 들어가므로 로컬에서만 볼 것.
사용: python3 -I render_preview2.py screens.json LocalArt디렉터리 출력디렉터리"""
import json, sys, os
from PIL import Image, ImageDraw, ImageFont

F = "/usr/share/fonts/truetype/nanum/NanumGothic.ttf"; FB = "/usr/share/fonts/truetype/nanum/NanumGothicBold.ttf"
def font(s, b=False): return ImageFont.truetype(FB if b else F, max(8, int(s)))
BLUE = (0x3A, 0x6E, 0xC8); NAVY = (0x10, 0x4E, 0x6E); INK = (0x3A, 0x2E, 0x22); HUDBLUE = (0x1E, 0x46, 0x9A)

class Art:
    def __init__(self, d):
        self.d = d; self.m = json.load(open(os.path.join(d, "manifest.json"), encoding="utf8"))
    def img(self, name):
        if not name: return None
        p = os.path.join(self.d, name + ".png.bytes")
        return Image.open(p).convert("RGBA") if os.path.exists(p) else None
    def ui(self, k): return self.img(self.m["ui"].get(k))
    def char(self, cid):
        for c in self.m["characters"]:
            if c["id"] == cid: return c
    def frame(self, cid, s="front", i=0):
        c = self.char(cid); l = c[s] or c["front"]; return self.img(l[i % len(l)])
    def portrait(self, cid): return self.img(self.char(cid)["portrait"])

def paste(base, im, x, y, w, h):
    if im is None: return
    base.alpha_composite(im.resize((max(1, int(w)), max(1, int(h))), Image.NEAREST), (int(x), int(y)))

def outlined(d, xy, text, f, fill, outline, w=2):
    x, y = xy
    for dx in range(-w, w + 1):
        for dy in range(-w, w + 1):
            if dx or dy: d.text((x + dx, y + dy), text, font=f, fill=outline)
    d.text(xy, text, font=f, fill=fill)

def wrap(d, text, f, width):
    out = []
    for para in text.split("\n"):
        cur = ""
        for ch in para:
            if d.textlength(cur + ch, font=f) > width and cur: out.append(cur); cur = ch
            else: cur += ch
        out.append(cur)
    return out

def render(dev, art, mode):
    W, H = dev["w"], dev["h"]; dp = dev["dp"]; px = lambda v: v * dp; hs = dev["houseScale"]
    im = Image.new("RGBA", (W, H), (0xF8, 0xEC, 0xC0, 255)); d = ImageDraw.Draw(im)
    R = dev["rects"]; fam = dev["idle"]; members = fam["members"]
    # HUD
    x, y, w, h = R["top"]; d.rectangle([x, y, x + w, y + h], fill=(0xF6, 0xE3, 0x9A))
    t = "%d년가족   %s" % (dev["years"], fam["date"]); f = font(px(18), True)
    outlined(d, (x + w / 2 - d.textlength(t, font=f) / 2, y + h / 2 - px(11)), t, f, HUDBLUE, (255, 255, 255))
    # 집
    x, y, w, h = R["scene"]; house = art.img(art.m["house"]["file"]); HW = house.width
    rooms = art.m["house"]["rooms"]; viewW = w / hs
    sel = members[0]; homes = {0: rooms[min(3, len(rooms) - 1)], 1: rooms[min(3, len(rooms) - 1)], 2: rooms[1], 3: rooms[2]}
    pos = {}
    for i, m in enumerate(members):
        r = homes.get(i, rooms[-1]); pos[i] = r[0] + 8 + (i % 2) * 34
    scroll = pos[0] - viewW / 2 + 16
    strip = Image.new("RGBA", (int(viewW) + 2, 160))
    sx = int(scroll) % HW
    for k in range(-1, 3): strip.alpha_composite(house, (k * HW - sx, 0))
    paste(im, strip, x, y, strip.width * hs, 160 * hs)
    floorTop = art.m["house"]["floorY"] - 61
    for i, m in enumerate(members):
        dx = (pos[i] - scroll) % HW
        if dx > viewW + 32: dx -= HW
        paste(im, art.frame(m["character"], "front", i), x + dx * hs, y + floorTop * hs, 32 * hs, 64 * hs)
        if i == 2:
            paste(im, art.ui("bubble_note_white") or art.ui("bubble_note_pink"), x + dx * hs, y + (floorTop - 14) * hs, 32 * hs, 32 * hs)
    dx = (pos[0] - scroll) % HW
    paste(im, art.ui("marker_select"), x + (dx + 8) * hs, y + (floorTop + 6) * hs, 16 * hs, 16 * hs)
    cup = art.m["cupid"]; paste(im, art.img(cup[3] if len(cup) > 3 else cup[0]), x + (dx - 26) * hs, y + (floorTop - 18) * hs, 32 * hs, 32 * hs)
    # 인물 바
    x, y, w, h = R["strip"]; d.rectangle([x, y, x + w, y + h], fill=NAVY)
    ar, pad = px(30), px(6)
    for bx, t in ((x, "◀"), (x + w - ar, "▶")):
        d.rectangle([bx, y, bx + ar, y + h], fill=(0x0A, 0x38, 0x50)); f = font(px(16), True); d.text((bx + ar / 2 - px(7), y + h / 2 - px(10)), t, font=f, fill="white")
    cx = x + ar + pad; ph = h - 2 * pad
    d.rectangle([cx, y + pad, cx + ph, y + pad + ph], fill=(0xF8, 0xB0, 0x40)); paste(im, art.portrait(sel["character"]), cx, y + pad, ph, ph)
    cx += ph + pad; nw = px(110)
    d.rectangle([cx, y + pad, cx + nw, y + pad + h * 0.45], fill="white"); d.text((cx + px(6), y + pad + px(4)), sel["name"], font=font(px(15), True), fill=INK)
    hx = cx + nw + pad; hsz = h * 0.42
    for i in range(3):
        v = sel["hearts"] - i * 96; k = "heart_full" if v >= 96 else "heart_half" if v >= 48 else "heart_empty"
        paste(im, art.ui(k), hx + i * (hsz + px(2)), y + pad, hsz, hsz)
    d.text((cx, y + h * 0.55), "★" + sel["planned"], font=font(px(14)), fill="white")
    g = art.ui("gauge_mid" if sel["imm"] >= 85 else "gauge_empty"); paste(im, g, x + w - ar - px(36), y + pad, px(30) * 0.5, h - 2 * pad)
    # 대화창
    x, y, w, h = R["panel"]
    d.rounded_rectangle([x + 6, y + 6, x + w - 6, y + h - 6], radius=px(10), fill=BLUE)
    d.rounded_rectangle([x + 10, y + 10, x + w - 10, y + h - 10], radius=px(8), fill="white")
    pd = px(18)
    if mode == "event":
        ev = dev["event"]
        d.text((x + pd, y + px(12)), ev["title"] + "   [신규 · 임시 문구]", font=font(px(12), True), fill=(0x9C, 0x52, 0x20))
        d.text((x + pd, y + px(30)), ev["speaker"], font=font(px(14), True), fill=(0x1E, 0x46, 0xC8))
        ty = y + px(52)
        for ln in wrap(d, ev["text"], font(px(17)), w - 2 * pd): d.text((x + pd, ty), ln, font=font(px(17)), fill=INK); ty += px(24)
        bh, gap = px(46), px(6); total = len(ev["choices"]) * (bh + gap); by = y + h - px(14) - total
        for i, c in enumerate(ev["choices"]):
            yy = by + i * (bh + gap); d.rounded_rectangle([x + pd, yy, x + w - pd, yy + bh], radius=px(6), fill=(0xE8, 0xF0, 0xFF), outline=BLUE, width=3)
            t = "▶ " + c; f = font(px(15), True); d.text((x + w / 2 - d.textlength(t, font=f) / 2, yy + bh / 2 - px(9)), t, font=f, fill=HUDBLUE)
    else:
        f = font(px(17)); ty = y + px(52)
        d.text((x + pd, ty), sel["name"], font=f, fill=(0x1E, 0x46, 0xC8)); tl = d.textlength(sel["name"], font=f)
        d.text((x + pd + tl, ty), "(%d세)는" % sel["age"], font=f, fill=INK)
        d.text((x + pd, ty + px(26)), "이런 생각을 하는 모양이야!", font=f, fill=INK)
        d.text((x + pd, ty + px(52)), "「" + sel["planned"] + "」", font=f, fill=INK)
    # 메뉴
    x, y, w, h = R["controls"]; d.rectangle([x, y, x + w, y + h], fill=(0xE8, 0xD0, 0x90))
    pad = px(6); bw = (w - 5 * pad) / 4; bh = h - 2 * pad
    for i, t in enumerate(["활쏘기", "아이템", "큐피트", "관찰"]):
        bx = x + pad + i * (bw + pad); d.rounded_rectangle([bx, y + pad, bx + bw, y + pad + bh], radius=px(8), fill=BLUE)
        f = font(px(15), True)
        if i == 0:
            paste(im, art.ui("btn_bow"), bx + bw * 0.3, y + pad + px(4), bw * 0.4, bh * 0.55)
            d.text((bx + bw / 2 - d.textlength(t, font=f) / 2, y + pad + bh - px(24)), t, font=f, fill="white")
        else: d.text((bx + bw / 2 - d.textlength(t, font=f) / 2, y + pad + bh / 2 - px(10)), t, font=f, fill="white")
    if mode in ("detail", "arrow"): popup(im, dev, art, mode, sel)
    # 안전영역
    st = R["top"][1]
    if st: d.rectangle([0, 0, W, st], fill=(20, 20, 20))
    sb = H - (R["controls"][1] + R["controls"][3])
    if sb > 0: d.rectangle([0, H - sb, W, H], fill=(20, 20, 20))
    return im.convert("RGB")

def popup(im, dev, art, mode, sel):
    d = ImageDraw.Draw(im); dp = dev["dp"]; px = lambda v: v * dp; W, H = dev["w"], dev["h"]
    shade = Image.new("RGBA", (W, H), (0, 0, 0, 115)); im.alpha_composite(shade)
    sx, sy, sw, sh = 0, dev["rects"]["top"][1], W, dev["rects"]["controls"][1] + dev["rects"]["controls"][3] - dev["rects"]["top"][1]
    w = sw - px(20); h = min(px(560 if mode == "detail" else 420), sh - px(40)); x = sx + px(10); y = sy + (sh - h) / 2
    d.rounded_rectangle([x, y, x + w, y + h], radius=px(10), fill=BLUE); d.rounded_rectangle([x + px(4), y + px(4), x + w - px(4), y + h - px(4)], radius=px(8), fill=(0xF4, 0xF8, 0xFF))
    d.rectangle([x + px(4), y + px(4), x + w - px(4), y + px(44)], fill=(0xC8, 0xD8, 0xFF))
    title = (sel["name"] + ("  (세대주)" if sel["head"] else "")) if mode == "detail" else "화살 — " + sel["name"]
    f = font(px(17), True); d.text((x + w / 2 - d.textlength(title, font=f) / 2, y + px(14)), title, font=f, fill=HUDBLUE)
    bx, by, bw = x + px(12), y + px(50), w - px(24)
    if mode == "detail":
        paste(im, art.frame(sel["character"], "front", 0), bx, by, px(75), px(150)); d.text((bx + px(18), by + px(152)), "%d세" % sel["age"], font=font(px(16), True), fill=INK)
        cx = bx + px(106); cw = bw - px(106)
        for i, (k, v) in enumerate((("꿈", "지금은 없어…"), ("화살", "맞은 화살 없음"))):
            d.rectangle([cx, by + i * px(34), cx + cw, by + i * px(34) + px(30)], fill="white", outline=(0xB0, 0xC0, 0xE0))
            d.text((cx + px(8), by + i * px(34) + px(6)), k + "  " + v, font=font(px(14)), fill=INK)
        sw4 = cw / 4
        for i, n in enumerate(["지력", "체력", "매력", "운"]):
            f = font(px(14)); d.text((cx + i * sw4 + sw4 / 2 - d.textlength(n, font=f) / 2, by + px(72)), n, font=f, fill=INK)
            r = sel["ranks"][i]; f = font(px(24), True); outlined(d, (cx + i * sw4 + sw4 / 2 - d.textlength(r, font=f) / 2, by + px(96)), r, f, (0xE8, 0x70, 0x20), (0x80, 0x30, 0), 1)
        d.text((cx, by + px(140)), "하트", font=font(px(14)), fill=INK)
        for i in range(3):
            v = sel["hearts"] - i * 96; k = "heart_full" if v >= 96 else "heart_half" if v >= 48 else "heart_empty"
            paste(im, art.ui(k), cx + px(50) + i * px(34), by + px(134), px(30), px(30))
        d.text((cx, by + px(170)), "신님에게 감사  0개", font=font(px(14)), fill=INK)
        d.text((bx, by + px(204)), "현재  " + sel["planned"], font=font(px(16), True), fill=HUDBLUE)
        for i, ln in enumerate(["(예정 상태 설명은 원작 문구 팩에서 제공)", "", "직업 코드 %d · 몰입도 %d" % (sel["job"], sel["imm"]), "※ 직업 이름·하트 단위는 원작 확인 전 임시 표기"]):
            d.text((bx, by + px(244) + i * px(22)), ln, font=font(px(14)), fill=INK)
    else:
        for i, (n, c, desc) in enumerate((("힘내라의 화살", dev["arrows"][0], "현재 관심사에 몰두하는 마음을 응원하는 화살."), ("진정해의 화살", dev["arrows"][1], "현재 관심사에 대한 열기를 식히는 화살."),
                                           ("애정 붐의 화살", 0, "(미구현)"), ("만남의 예감의 화살", 0, "(미구현)"))):
            yy = by + i * px(64); d.rounded_rectangle([bx, yy, bx + bw, yy + px(58)], radius=px(6), fill="white" if c else (225, 225, 230), outline=(0xB0, 0xC0, 0xE0))
            d.text((bx + px(10), yy + px(8)), "%s  ×%d" % (n, c), font=font(px(15), True), fill=INK); d.text((bx + px(10), yy + px(32)), desc, font=font(px(12)), fill=INK)
    d.rounded_rectangle([x + px(10), y + h - px(56), x + w - px(10), y + h - px(8)], radius=px(6), fill=NAVY)
    f = font(px(15), True); d.text((x + w / 2 - d.textlength("닫기", font=f) / 2, y + h - px(42)), "닫기", font=f, fill="white")

def main():
    data = json.load(open(sys.argv[1])); art = Art(sys.argv[2]); out = sys.argv[3]; os.makedirs(out, exist_ok=True)
    shots = []
    for i, dev in enumerate(data):
        for mode in (("main", "event", "detail", "arrow") if (dev["w"], dev["h"]) == (1080, 2340) else ("main",)):
            im = render(dev, art, mode); n = "%dx%d_%s.png" % (dev["w"], dev["h"], mode); im.save(os.path.join(out, n)); shots.append((dev, mode, im))
    def sheet(items, name, th=1000):
        ims = [(lab, im.resize((int(im.width * th / im.height), th))) for lab, im in items]
        s = Image.new("RGB", (sum(i.width for _, i in ims) + 20 * (len(ims) + 1), th + 60), (40, 40, 40)); d = ImageDraw.Draw(s); x = 20
        for lab, im in ims: s.paste(im, (x, 46)); d.text((x, 10), lab, font=font(22, True), fill="white"); x += im.width + 20
        s.save(os.path.join(out, name))
    sheet([("%s" % m, im) for dev, m, im in shots if (dev["w"], dev["h"]) == (1080, 2340)], "sheet_screens.png")
    sheet([("%dx%d" % (dev["w"], dev["h"]), im) for dev, m, im in shots if m == "main"], "sheet_ratios.png", 800)
    print("ok", len(shots))

main()
