"""The chapter-change eyecatch: Kindaichi's glowing silhouette, redrawn clean.

The source picture (assets/cuts/eyecatch_src.png, a screenshot, not in git) only provides the
shape: it is thresholded, smoothed at 4x and re-lit with a fresh glow, so compression noise and
the soft blur of the original are gone.
"""
import os

import numpy as np
from PIL import Image, ImageFilter

SRC = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "assets", "cuts", "eyecatch_src.png")
W, H = 240, 160


def available():
    return os.path.exists(SRC)


def render(height=138):
    src = Image.open(SRC).convert("RGB")
    lum = np.asarray(src.convert("L")).astype(np.float32)
    # the silhouette is the bright core; the halo around it is dimmer
    mask = Image.fromarray(((lum > 200) * 255).astype(np.uint8))
    bbox = mask.getbbox()
    mask = mask.crop(bbox)
    k = 4
    scale = height * k / mask.height
    big = mask.resize((max(1, int(mask.width * scale)), height * k), Image.LANCZOS)
    big = big.filter(ImageFilter.GaussianBlur(0.7 * k)).point(lambda v: 255 if v > 128 else 0)  # smooth edges
    bw, bh = big.size
    canvas_w, canvas_h = W * k, H * k
    m = Image.new("L", (canvas_w, canvas_h), 0)
    m.paste(big, ((canvas_w - bw) // 2, (canvas_h - bh) // 2))
    a = np.asarray(m).astype(np.float32) / 255
    glow1 = np.asarray(m.filter(ImageFilter.GaussianBlur(6 * k))).astype(np.float32) / 255
    glow2 = np.asarray(m.filter(ImageFilter.GaussianBlur(18 * k))).astype(np.float32) / 255
    halo = np.clip(glow1 * 1.1 + glow2 * 0.7, 0, 1)[..., None] * np.array([90, 200, 160], np.float32)
    # the figure: pale mint, a touch brighter towards the top
    yy = np.linspace(0, 1, canvas_h)[:, None, None]
    body = np.array([236, 255, 246], np.float32) * (1 - 0.12 * yy) + np.array([0, 0, 0], np.float32)
    img = halo * (1 - a[..., None]) + body * a[..., None]
    img = Image.fromarray(np.clip(img, 0, 255).astype(np.uint8), "RGB")
    return img.resize((W, H), Image.LANCZOS)
