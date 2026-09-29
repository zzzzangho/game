#!/usr/bin/env python3
"""Import hand-drawn character busts (white background) as transparent portraits.

    python3 tools/import_charart.py <folder with 김전일.png, 미유키.png, ...> [--clean]

Transparent PNGs are used as drawn (just cropped); --clean also repairs leftover background
(white slivers by the hair, stray outline bits, the hand-marked HOLES).

The white background is flood-filled from the top edge and the upper part of the side
edges only, so white clothing that runs off the bottom of the picture is kept (the line
art separates it from the background). Output goes to assets/portraits/<key>.png, which
stays out of git like the other personal images.
"""
import collections
import os
import sys

from PIL import Image, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "assets", "portraits")

# file name (without .png) -> portrait key
NAMES = {
    "김전일": "kin", "미유키": "miyuki", "사키": "saki", "켄모치": "kenmochi", "아케치": "akechi",
    "야마가미": "yamagami", "유미": "yumi", "사콘지": "sakonji", "유라마": "yurama",
    "사쿠라바": "sakuraba", "사쿠라바_무대분장": "sakuraba_stage", "타카토": "takato",
    "사토미": "satomi", "나가사키": "nagasaki", "트네 마리오": "mario",
    "젠틀 야마가미": "yamagami", "지배인 코지로": "nagasaki", "치카미야 레이코": "reiko", "트네": "mario",
    "켄모치_놀람": "kenmochi_surprised", "타카토_걱정": "takato_worried",
}


def cut_out(img, tol=8, side_frac=0.45):
    img = img.convert("RGBA")
    w, h = img.size
    px = img.load()

    def bg(c):
        return c[0] >= 255 - tol and c[1] >= 255 - tol and c[2] >= 255 - tol

    seeds = [(x, 0) for x in range(w)]
    seeds += [(0, y) for y in range(int(h * side_frac))] + [(w - 1, y) for y in range(int(h * side_frac))]
    seen = bytearray(w * h)
    q = collections.deque()
    for x, y in seeds:
        if not seen[y * w + x] and bg(px[x, y]):
            seen[y * w + x] = 1
            q.append((x, y))
    while q:
        x, y = q.popleft()
        for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
            if 0 <= nx < w and 0 <= ny < h and not seen[ny * w + nx] and bg(px[nx, ny]):
                seen[ny * w + nx] = 1
                q.append((nx, ny))
    mask = Image.frombytes("L", (w, h), bytes(0 if s else 255 for s in seen))
    # pull the edge in by a pixel so no white fringe survives the downscale
    mask = mask.filter(ImageFilter.MinFilter(3))
    img.putalpha(mask)
    return img.crop(mask.getbbox())


def defringe(img, depth=10, light=222, grey=22):
    """Remove leftover background around an already transparent cut-out: near-white, low
    saturation pixels reachable from the transparent area within `depth` px (slivers between
    hair strands and the old background), plus semi-transparent light edge pixels."""
    img = img.convert("RGBA")
    w, h = img.size
    px = img.load()

    def whitish(c):
        return min(c[:3]) >= light and max(c[:3]) - min(c[:3]) <= grey

    dist = {}
    q = collections.deque()
    for y in range(h):
        for x in range(w):
            if px[x, y][3] == 0:
                continue
            for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
                if 0 <= nx < w and 0 <= ny < h and px[nx, ny][3] == 0:
                    if whitish(px[x, y]) or px[x, y][3] < 200:
                        dist[(x, y)] = 1
                        q.append((x, y))
                    break
    while q:
        x, y = q.popleft()
        d = dist[(x, y)]
        if d >= depth:
            continue
        for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
            if 0 <= nx < w and 0 <= ny < h and (nx, ny) not in dist:
                c = px[nx, ny]
                if c[3] and whitish(c):
                    dist[(nx, ny)] = d + 1
                    q.append((nx, ny))
    for (x, y) in dist:
        px[x, y] = (0, 0, 0, 0)
    return img, len(dist)


def fill_edge_slivers(img, reach=7, light=140, grey=26):
    """White slivers of old background trapped between the artist's outline and the hair:
    whitish connected patches lying entirely within `reach` px of the transparent area are
    repainted with the colour of the nearest non-white pixel (so no hole opens up)."""
    import numpy as np
    from scipy import ndimage
    a = np.array(img.convert("RGBA"))
    rgb, alpha = a[..., :3].astype(int), a[..., 3]
    white = (rgb.min(axis=2) >= light) & (rgb.max(axis=2) - rgb.min(axis=2) <= grey) & (alpha > 0)
    dist = ndimage.distance_transform_edt(alpha > 0)
    lab, n = ndimage.label(white)
    if not n:
        return img, 0
    far = ndimage.maximum(dist, lab, index=np.arange(1, n + 1))
    kill = np.zeros(n + 1, bool)
    kill[1:] = far <= reach
    mask = kill[lab]
    if not mask.any():
        return img, 0
    donor = (alpha > 0) & ~white
    _, (iy, ix) = ndimage.distance_transform_edt(~donor, return_indices=True)
    a[mask, :3] = a[iy[mask], ix[mask], :3]
    a[mask, 3] = 255
    return Image.fromarray(a, "RGBA"), int(mask.sum())


def drop_stray_lines(img, keep_near=2):
    """Remove thin bits of outline left floating beside the figure (1-2 px wide lines that
    were separated from the body by the background removal)."""
    import numpy as np
    from scipy import ndimage
    a = np.array(img.convert("RGBA"))
    solid = a[..., 3] > 0
    core = ndimage.binary_opening(solid, structure=np.ones((3, 3)), iterations=1)
    lab, n = ndimage.label(core)
    if n:
        sizes = ndimage.sum(core, lab, index=np.arange(1, n + 1))
        big = np.zeros(n + 1, bool)
        big[1:] = sizes >= sizes.max() * 0.01
        core = big[lab]
    keep = solid & ndimage.binary_dilation(core, iterations=keep_near)
    removed = int((solid & ~keep).sum())
    a[~keep, 3] = 0
    return Image.fromarray(a, "RGBA"), removed


# Background left inside closed shapes (not reachable from the outside), marked by hand as
# fractions of the picture size: file name -> [(x, y), ...]
HOLES = {
    "사키": [(0.352, 0.530), (0.403, 0.460)],   # between camera, cheek and hand
}


def clear_holes(img, seeds, light=225):
    import numpy as np
    from scipy import ndimage
    a = np.array(img.convert("RGBA"))
    rgb = a[..., :3].astype(int)
    white = (rgb.min(axis=2) >= light) & (a[..., 3] > 0)
    lab, _ = ndimage.label(white)
    h, w = white.shape
    cleared = 0
    for fx, fy in seeds:
        x, y = int(fx * w), int(fy * h)
        win = lab[max(0, y - 4):y + 5, max(0, x - 4):x + 5]
        ids = set(win[win > 0].tolist())
        for i in ids:
            m = lab == i
            m = ndimage.binary_dilation(m, iterations=1) & (a[..., 3] > 0) & (rgb.min(axis=2) >= 150)
            a[m, 3] = 0
            cleared += int(m.sum())
    return Image.fromarray(a, "RGBA"), cleared


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    clean = "--clean" in sys.argv  # repair leftover background in transparent art (off: use it as drawn)
    src = args[0] if args else "."
    os.makedirs(OUT, exist_ok=True)
    for fn in sorted(os.listdir(src)):
        stem, ext = os.path.splitext(fn)
        if ext.lower() not in (".png", ".jpg", ".jpeg", ".webp") or stem not in NAMES:
            if ext.lower() in (".png", ".jpg", ".jpeg", ".webp"):
                print(f"skip {fn} (unknown name)")
            continue
        img = Image.open(os.path.join(src, fn))
        transparent = img.mode in ("RGBA", "LA") and img.getextrema()[-1][0] < 255
        if transparent:
            img = img.convert("RGBA")
            if clean:
                img, n = defringe(img)
                print(f"  {fn}: removed {n} leftover background pixels")
            img = img.crop(img.getchannel("A").getbbox())
        else:
            img = cut_out(img)
        # keep it reasonably small; art.py fits it into the bust area
        img.thumbnail((512, 640), Image.LANCZOS)
        if transparent and not clean:
            img.save(os.path.join(OUT, NAMES[stem] + ".png"))
            print(f"{fn} -> portraits/{NAMES[stem]}.png {img.size} (as drawn)")
            continue
        if stem in HOLES:
            img, c = clear_holes(img, HOLES[stem])
            print(f"  {fn}: cleared {c} px of enclosed background")
        img, n = fill_edge_slivers(img)
        img, m = drop_stray_lines(img)
        print(f"  {fn}: filled {n} white edge pixels, dropped {m} stray line pixels")
        img.save(os.path.join(OUT, NAMES[stem] + ".png"))
        print(f"{fn} -> portraits/{NAMES[stem]}.png {img.size}")


if __name__ == "__main__":
    main()
