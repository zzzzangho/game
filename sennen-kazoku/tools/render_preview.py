#!/usr/bin/env python3
"""레이아웃 미리보기 PNG 생성 (Unity 캡처가 아님).
tools/ScreenPreview 가 코어로 만든 영역·텍스트 JSON 을 GameApp.cs 와 같은 배치 수식으로 그린다.
사용: python3 -I render_preview.py screens.json 출력디렉터리"""
import json, sys, os
from PIL import Image, ImageDraw, ImageFont

FONT = "/usr/share/fonts/truetype/nanum/NanumGothic.ttf"
FONTB = "/usr/share/fonts/truetype/nanum/NanumGothicBold.ttf"
C = dict(bg=(0xFB,0xF3,0xE0), panel=(0xFF,0xFC,0xF2), ink=(0x3A,0x2E,0x22), accent=(0xD9,0x7B,0x3C), dark=(0x9C,0x52,0x20), soft=(0xEA,0xDC,0xBE))

def f(size, bold=False): return ImageFont.truetype(FONTB if bold else FONT, max(8, int(size)))

def wrap(d, text, font, width):
    lines = []
    for para in text.split("\n"):
        cur = ""
        for ch in para:
            if d.textlength(cur + ch, font=font) > width and cur: lines.append(cur); cur = ch
            else: cur += ch
        lines.append(cur)
    return lines

def render(dev, state):
    W, H, dp = dev["w"], dev["h"], dev["dp"]
    px = lambda v: int(round(v * dp))
    im = Image.new("RGB", (W, H), C["bg"]); d = ImageDraw.Draw(im)
    R = dev["rects"]; data = dev["idle"] if state == "idle" else dev["idle"]
    x, y, w, h = R["top"]
    d.rectangle([x, y, x+w, y+h], fill=C["accent"])
    d.text((x+px(12), y+h*0.05), data["date"], font=f(px(18), True), fill="white")
    d.text((x+px(12), y+h*0.55), "가족 무드 %d/5" % data["mood"], font=f(px(13)), fill="white")
    s = "자산 {:,}엔 · {}".format(data["assets"], data["family"]); fo = f(px(13))
    d.text((x+w-px(12)-d.textlength(s, font=fo), y+h*0.5), s, font=fo, fill="white")
    # 가족 띠
    x, y, w, h = R["strip"]; d.rectangle([x, y, x+w, y+h], fill=C["soft"])
    cx, pad, cw = x+px(8), px(8), px(78)
    for m in data["members"]:
        col = (0x8F,0xBC,0xE3) if m["g"] == 0 else (0xE8,0xA0,0xB5)
        d.rounded_rectangle([cx, y+pad, cx+cw, y+h-pad], radius=px(4), fill=col)
        fo = f(px(12), True)
        d.text((cx+px(6), y+pad+px(8)), m["name"], font=fo, fill=C["ink"])
        d.text((cx+px(6), y+pad+px(26)), "%d세 · 지%s ♥" % (m["age"], m["rank"]), font=f(px(11)), fill=C["ink"])
        cx += cw + pad
    # 장면
    x, y, w, h = R["scene"]
    d.rectangle([x, y, x+w, y+h], fill=(0xBF,0xE3,0xF2))
    d.rectangle([x, y+h*0.62, x+w, y+h], fill=(0xA8,0xD5,0x8E))
    d.rectangle([x+w*0.1, y+h*0.18, x+w*0.9, y+h*0.68], fill=(0xE8,0xC9,0x9A))
    d.rectangle([x+w*0.06, y+h*0.08, x+w*0.94, y+h*0.20], fill=(0xB5,0x5A,0x3C))
    n = len(data["members"]); fw = min(px(64), w/max(3, n+1)); step = w*0.8/max(1, n)
    for i, m in enumerate(data["members"]):
        size = fw * (0.6 if m["age"] < 6 else 0.8 if m["age"] < 15 else 1.0)
        fx = x + w*0.1 + step*i + (step-size)/2; fy = y + min(h*0.55 - size*0.2, h - px(36) - size)
        d.ellipse([fx, fy, fx+size, fy+size], fill=(0x5B,0x8F,0xC9) if m["g"] == 0 else (0xD9,0x6A,0x8A))
        fo = f(px(11), True); d.text((fx+size/2-d.textlength(m["name"], font=fo)/2, fy+size/2-px(7)), m["name"], font=fo, fill="white")
    cap = "※ 임시 그래픽"; fo = f(px(13)); d.text((x+w/2-d.textlength(cap, font=fo)/2, y+h-px(30)), cap, font=fo, fill=C["ink"])
    # 이벤트 패널
    x, y, w, h = R["panel"]; pd = px(14)
    if state == "event":                       # GameApp.RefreshEvent 와 같은 확장 규칙
        total = len(dev["event"]["choices"]) * (px(48) + px(6))
        need = px(64) + px(66) + total + px(6) * 2
        if need > h: y -= need - h; h = need
    d.rectangle([x, y, x+w, y+h], fill=C["panel"])
    if state == "idle":
        d.text((x+pd, y+px(20)), "가족의 하루", font=f(px(16), True), fill=C["ink"])
        d.text((x+pd, y+px(64)), data["text"], font=f(px(16)), fill=C["ink"])
    else:
        ev = dev["event"]
        d.text((x+pd, y+px(4)), ev["badge"], font=f(px(11), True), fill=C["dark"])
        d.text((x+pd, y+px(20)), ev["title"], font=f(px(16), True), fill=C["ink"])
        d.text((x+pd, y+px(46)), ev["speaker"], font=f(px(13), True), fill=C["dark"])
        bh, gap = px(48), px(6); total = len(ev["choices"]) * (bh+gap)
        fo = f(px(16)); ty = y+px(64)
        for ln in wrap(d, ev["text"], fo, w-2*pd)[:3]:
            d.text((x+pd, ty), ln, font=fo, fill=C["ink"]); ty += px(22)
        by = y + h - total - gap
        for i, c in enumerate(ev["choices"]):
            yy = by + i*(bh+gap)
            d.rounded_rectangle([x+pd, yy, x+w-pd, yy+bh], radius=px(6), fill=C["accent"])
            fo2 = f(px(15), True); d.text((x+w/2-d.textlength(c, font=fo2)/2, yy+bh/2-px(9)), c, font=fo2, fill="white")
    # 조작 바
    x, y, w, h = R["controls"]; d.rectangle([x, y, x+w, y+h], fill=C["soft"])
    pad = px(8); bh = max(px(48), h-2*pad); bw = (w-4*pad)/3
    for i, (t, col) in enumerate((("정지", C["dark"]), ("▶ ×1", C["accent"]), ("메뉴", C["dark"]))):
        bx = x + pad*(i+1) + bw*i; by = y + (h-bh)/2
        d.rounded_rectangle([bx, by, bx+bw, by+bh], radius=px(6), fill=col)
        fo = f(px(16), True); d.text((bx+bw/2-d.textlength(t, font=fo)/2, by+bh/2-px(10)), t, font=fo, fill="white")
    # 안전영역 표시(노치/제스처 바)
    if dev["safeTop"]: d.rectangle([0, 0, W, dev["safeTop"]], fill=(30, 30, 30))
    if dev["safeBottom"]: d.rectangle([0, H-dev["safeBottom"], W, H], fill=(30, 30, 30))
    return im

def main():
    data = json.load(open(sys.argv[1])); out = sys.argv[2]; os.makedirs(out, exist_ok=True)
    if len(sys.argv) > 3 and sys.argv[3] == "--stress":          # 선택지 3개 최악 조건
        for dev in data: dev["event"]["choices"].append("일찍 돌아와서 낮잠을 잔다")
    tiles = []
    for i, dev in enumerate(data):
        for st in ("event",) + (("idle",) if i in (0, 3) else ()):
            im = render(dev, st); name = "%dx%d_%s.png" % (dev["w"], dev["h"], st)
            im.save(os.path.join(out, name)); tiles.append((dev, st, im))
    # 한 장짜리 비교 시트: 높이를 맞춰 가로로 나열
    th = 900; ims = []
    for dev, st, im in tiles:
        if st != "event": continue
        ims.append((dev, im.resize((int(im.width*th/im.height), th))))
    sheet = Image.new("RGB", (sum(i.width for _, i in ims) + 20*(len(ims)+1), th+70), (40, 40, 40)); d = ImageDraw.Draw(sheet); x = 20
    for dev, im in ims:
        sheet.paste(im, (x, 50)); d.text((x, 12), "%dx%d  (%.2f:1)" % (dev["w"], dev["h"], dev["h"]/dev["w"]), font=f(22, True), fill="white"); x += im.width + 20
    sheet.save(os.path.join(out, "sheet.png")); print("ok", len(tiles))

main()
