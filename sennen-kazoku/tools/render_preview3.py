#!/usr/bin/env python3
"""사용자 스케치 기준 새 메인 화면 미리보기 (Unity 캡처 아님). GameApp.cs 의 배치 수식을 그대로 옮겼다.
인물은 원작 ROM 파트 조합(charcompose)으로 그린다 → 결과물에 원작 그래픽이 들어가므로 로컬에서만 볼 것.
사용: python3 -I render_preview3.py <screens.json> <LocalArt> <rom.gba> <출력.png> [프리셋.json ...]"""
import json, os, sys
from PIL import Image, ImageDraw
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "gba_capture"))
from render_preview2 import Art, paste, font, outlined, wrap, BLUE, NAVY, INK, HUDBLUE
import charcompose as C, romparts as R
from export_parts import palettes
from preview_looks import palette_for, render as render_look

RANK_ORDER = [("체", 1), ("지", 0), ("매", 2), ("운", 3)]

def rank(v):
    r = ["F", "D", "C", "B", "A", "S", "SS"]; return r[6] if v >= 4800 else r[min(5, max(0, v) // 800)]

def main():
    scr, artdir, rom, out = sys.argv[1:5]
    dev = json.load(open(scr))[int(os.environ.get("PREVIEW_INDEX", "0"))]; art = Art(artdir)
    lib = C.Library(R.load(rom)); P = palettes(R.load(rom))
    presets = {}
    for f in sys.argv[5:]: presets.update(json.load(open(f)))
    W, H = dev["w"], dev["h"]; dp = dev["dp"]; px = lambda v: v * dp; hs = dev["houseScale"]
    im = Image.new("RGBA", (W, H), (0xF8, 0xEC, 0xC0, 255)); d = ImageDraw.Draw(im)
    Rr = dev["rects"]; fam = dev["idle"]; members = fam["members"]
    roles = ["father", "mother", "son18", "daughter13", "son7", "daughter7"]
    looks = []
    for i, m in enumerate(members):
        role = "father" if m["g"] == 0 and m["age"] >= 25 else "mother" if m["g"] == 1 and m["age"] >= 25 else ("son18" if m["g"] == 0 else "daughter13") if m["age"] >= 13 else ("son7" if m["g"] == 0 else "daughter7")
        ks = sorted(k for k in presets if k.startswith(role + "_")) or sorted(presets)
        L = dict(presets[ks[(i * 7 + 3) % len(ks)]]); looks.append(L)
    def sprite(i, pose=2):
        L = looks[i]; age = L.get("age", 1)
        idx = C.render(lib, dict(L, body=L["body"] % 24), age, pose, 0)
        pal = palette_for(P, L); img = Image.new("RGBA", (32, 64), (0, 0, 0, 0))
        img.putdata([((pal[v] & 31) * 255 // 31, ((pal[v] >> 5) & 31) * 255 // 31, ((pal[v] >> 10) & 31) * 255 // 31, 255) if v else (0, 0, 0, 0) for v in idx])
        return img
    # ---- HUD
    x, y, w, h = Rr["top"]; d.rectangle([x, y, x + w, y + h], fill=(0xF6, 0xE3, 0x9A))
    pad = px(6); corner = min(px(76), h * 1.6)
    paste(im, art.ui("hud_family_a"), x + pad, y + pad, h - 2 * pad, h - 2 * pad)
    tx = x + h + pad
    d.text((tx, y + px(4)), "%s  소지금 %s엔" % (fam["family"], format(fam["assets"], ",")), font=font(px(15), True), fill=HUDBLUE)
    d.text((tx, y + h * 0.52), "무드 포인트 128 (%d단계)   1년가족 · %s" % (fam["mood"], fam["date"]), font=font(px(12)), fill=INK)
    d.rounded_rectangle([x + w - corner, y, x + w + px(20), y + h], radius=px(18), fill=BLUE)
    f = font(px(14), True); d.text((x + w - corner / 2 - d.textlength("배속", font=f) / 2, y + h / 2 - px(9)), "배속", font=f, fill="white")
    # ---- 장면
    x, y, w, h = Rr["scene"]; house = art.img(art.m["house"]["file"]); HW = house.width
    rooms = art.m["house"]["rooms"]; viewW = w / hs
    pos = {i: rooms[min(len(rooms) - 1, 3)][0] + 6 + i * 30 for i in range(len(members))}
    scroll = pos[0] - viewW / 2 + 40
    strip = Image.new("RGBA", (int(viewW) + 2, 160)); sx = int(scroll) % HW
    for k in range(-1, 3): strip.alpha_composite(house, (k * HW - sx, 0))
    paste(im, strip, x, y, strip.width * hs, 160 * hs)
    floorTop = art.m["house"]["floorY"] - 61
    for i in range(len(members)):
        dx = (pos[i] - scroll) % HW
        if dx > viewW + 32: dx -= HW
        paste(im, sprite(i, 2 if i != 2 else 0), x + dx * hs, y + floorTop * hs, 32 * hs, 64 * hs)
        bx, by, bs = x + (dx + 6) * hs, y + (floorTop - 16) * hs, 20 * hs
        if i == 1:      # 사건 직전 "!"
            d.ellipse([bx, by, bx + bs, by + bs], fill=(255, 255, 255)); fb = font(14 * hs, True)
            d.text((bx + bs / 2 - d.textlength("!", font=fb) / 2, by + bs * 0.05), "!", font=fb, fill=(0xE0, 0x30, 0x30))
        if i == 0: paste(im, art.ui("bubble_note_pink"), bx, by, bs, bs)
    dx = (pos[0] - scroll) % HW
    paste(im, art.ui("marker_select"), x + (dx + 8) * hs, y + (floorTop + 6) * hs, 16 * hs, 16 * hs)
    cup = art.m["cupid"]; cx, cy = 10, 12
    paste(im, art.img(cup[0]), x + cx * hs, y + cy * hs, 32 * hs, 32 * hs)
    tw = min(w - (cx + 36) * hs - px(8), px(240)); tx0, ty0 = x + (cx + 34) * hs, y + (cy + 2) * hs
    d.rounded_rectangle([tx0, ty0, tx0 + tw, ty0 + px(40)], radius=px(14), fill=(255, 255, 255))
    t = "가족을 누르면 자세히 볼 수 있어!"; f = font(px(13)); d.text((tx0 + tw / 2 - d.textlength(t, font=f) / 2, ty0 + px(12)), t, font=f, fill=INK)
    r = min(px(84), h * 0.42)
    d.ellipse([x - r, y + h - r, x + r, y + h + r], fill=(0xF0, 0xA0, 0x40))
    d.ellipse([x + w - r, y + h - r, x + w + r, y + h + r], fill=(0xE8, 0x50, 0x70))
    f = font(px(14), True)
    outlined(d, (x + r * 0.2, y + h - r * 0.6), "아이템", f, (255, 255, 255), (120, 60, 0), 1)
    paste(im, art.ui("btn_bow"), x + w - r * 0.7, y + h - r * 0.95, r * 0.5, r * 0.3)
    outlined(d, (x + w - r * 0.55, y + h - r * 0.6), "활", f, (255, 255, 255), (120, 0, 40), 1)
    # ---- 인물 패널 (strip + panel)
    sx_, sy_, sw_, sh_ = Rr["strip"]; _, py_, _, ph_ = Rr["panel"]
    x, y, w, H2 = sx_, sy_, sw_, sh_ + ph_
    d.rectangle([x, y, x + w, y + H2], fill=NAVY)
    ar, pad, effH = px(30), px(6), px(30)
    for bx, t in ((x, "◀"), (x + w - ar, "▶")):
        d.rectangle([bx, y, bx + ar, y + H2 - effH], fill=(0x0A, 0x38, 0x50)); fb = font(px(18), True)
        d.text((bx + ar / 2 - px(8), y + (H2 - effH) / 2 - px(12)), t, font=fb, fill="white")
    sel = members[0]; leftW = min(px(96), w * 0.26); inner = w - 2 * ar; ph = min(leftW, (H2 - effH) * 0.55)
    bx0 = x + ar
    d.rectangle([bx0 + pad, y + pad, bx0 + leftW, y + pad + ph], fill=(0xF8, 0xB0, 0x40))
    paste(im, sprite(0), bx0 + pad + (leftW - pad - ph / 2) / 2, y + pad, ph / 2, ph)
    d.rectangle([bx0 + pad, y + pad + ph + px(2), bx0 + leftW, y + pad + ph + px(28)], fill="white")
    f = font(px(14), True); d.text((bx0 + pad + (leftW - pad) / 2 - d.textlength(sel["name"], font=f) / 2, y + pad + ph + px(6)), sel["name"], font=f, fill=INK)
    hsz = min(px(22), (leftW - pad) / 3 - px(2)); hy = y + pad + ph + px(32)
    for i in range(3):
        v = sel["hearts"] - i * 96; k = "heart_full" if v >= 96 else "heart_half" if v >= 48 else "heart_empty"
        paste(im, art.ui(k), bx0 + pad + i * (hsz + px(2)), hy, hsz, hsz)
    rx = bx0 + leftW + pad * 2; rw = inner - leftW - pad * 3
    tx = rx; f = font(px(20), True)
    stats = {"C": 1800, "B": 2500, "A": 3300, "S": 4100, "F": 300, "D": 1000}
    for lab, si in RANK_ORDER:
        rk = sel["ranks"][si]
        d.text((tx, y + pad), lab, font=f, fill="white"); tx += d.textlength(lab, font=f)
        d.text((tx, y + pad), rk, font=f, fill=(0xFF, 0xC0, 0x40)); tx += d.textlength(rk + "  ", font=f)
    imm = 220   # 미리보기: 힘내라의 화살을 맞은 상태
    d.text((rx, y + pad + px(32)), "열중", font=font(px(15), True), fill=(0xFF, 0xC0, 0x40))
    gx, gy, gw, gh = rx + px(44), y + pad + px(34), rw - px(44), px(20)
    d.rectangle([gx, gy, gx + gw, gy + gh], fill=(0x06, 0x26, 0x38)); d.rectangle([gx, gy, gx + gw * imm / 255, gy + gh], fill=(0xF0, 0x50, 0x30))
    t = "%d / 255" % imm; f = font(px(12), True); d.text((gx + gw / 2 - d.textlength(t, font=f) / 2, gy + px(2)), t, font=f, fill="white")
    d.text((rx, y + pad + px(58)), "%d세 · 힘내라의 화살 효과 중" % sel["age"], font=font(px(12)), fill="white")
    dx0, dy0, dw, dh = rx, y + pad + px(80), rw, H2 - effH - px(80) - pad * 2
    d.rounded_rectangle([dx0, dy0, dx0 + dw, dy0 + dh], radius=px(8), fill=BLUE)
    d.rounded_rectangle([dx0 + px(2), dy0 + px(2), dx0 + dw - px(2), dy0 + dh - px(2)], radius=px(6), fill="white")
    ev = dev.get("event")
    if not ev:
        f = font(px(15)); ty = dy0 + px(14)
        for ln in wrap(d, sel["name"] + "는 이런 생각을 하는 모양이야\n「" + sel["planned"] + "」", f, dw - px(20)):
            d.text((dx0 + px(10), ty), ln, font=f, fill=INK); ty += px(22)
        d.text((dx0 + px(10), ty + px(4)), "푹 빠져 있어!", font=font(px(15), True), fill=(0xC0, 0x50, 0x20))
    if ev:
        d.text((dx0 + px(10), dy0 + px(6)), ev["title"] + "  [신규 · 임시 문구]", font=font(px(11), True), fill=(0x9C, 0x52, 0x20))
        d.text((dx0 + px(10), dy0 + px(22)), ev["speaker"], font=font(px(13), True), fill=(0x1E, 0x46, 0xC8))
        ty = dy0 + px(40)
        for ln in wrap(d, ev["text"], font(px(15)), dw - px(20))[:3]: d.text((dx0 + px(10), ty), ln, font=font(px(15)), fill=INK); ty += px(20)
        n = len(ev["choices"]); gap = px(4); bh = max(px(34), min(px(44), (dh - px(70)) / n - gap)); by = dy0 + dh - px(6) - n * (bh + gap)
        for i, c in enumerate(ev["choices"]):
            yy = by + i * (bh + gap); d.rounded_rectangle([dx0 + px(8), yy, dx0 + dw - px(8), yy + bh], radius=px(6), fill=(0xE8, 0xF0, 0xFF), outline=BLUE, width=2)
            t = "▶ " + c; f = font(px(14), True); d.text((dx0 + dw / 2 - d.textlength(t, font=f) / 2, yy + bh / 2 - px(8)), t, font=f, fill=HUDBLUE)
    d.text((x + ar + inner / 2 - px(110), y + H2 - effH + px(4)), "지난 일:  무드", font=font(px(16), True), fill="white")
    d.text((x + ar + inner / 2 + px(22), y + H2 - effH + px(4)), "↑", font=font(px(16), True), fill=(0xFF, 0x90, 0x90))
    d.text((x + ar + inner / 2 + px(42), y + H2 - effH + px(4)), "하트", font=font(px(16), True), fill="white")
    d.text((x + ar + inner / 2 + px(82), y + H2 - effH + px(4)), "↓", font=font(px(16), True), fill=(0x90, 0xB8, 0xFF))
    # ---- 탭
    x, y, w, h = Rr["controls"]; d.rectangle([x, y, x + w, y + h], fill=(0xE8, 0xD0, 0x90))
    pad = px(4); bw = (w - 5 * pad) / 4; bh = h - 2 * pad
    for i, t in enumerate(["상세", "가계도", "사건", "설정"]):
        bx = x + pad + i * (bw + pad); d.rounded_rectangle([bx, y + pad, bx + bw, y + pad + bh], radius=px(8), fill=BLUE)
        f = font(px(16), True); d.text((bx + bw / 2 - d.textlength(t, font=f) / 2, y + pad + bh / 2 - px(10)), t, font=f, fill="white")
    im.convert("RGB").save(out)

if __name__ == "__main__":
    main()
