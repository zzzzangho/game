"""Converts finished audio (mp3/wav) to the GBA's DirectSound format: mono 8-bit signed PCM at 10512 Hz
(timer 0 reload 65536-1596; exactly 176 samples a frame), padded with silence so the frame-counted
stop never runs the DMA into the next clip.

    python3 tools/convert_audio.py bgm shadow  "BGM/02_Shadow_Approaches.mp3"
    python3 tools/convert_audio.py sfx pops    "SFX/05_Pop_Pop_Bursts.mp3"

Writes assets/music/<kind>_<name>.s8 (kept out of git like the pictures). The script refers to it as
@bgm <name> / @sfx <name>. Needs soundfile and librosa.
"""
import os, sys
import numpy as np, soundfile as sf, librosa

RATE = 10512
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "assets", "music")


def convert(src, peak):
    y, sr = sf.read(src, always_2d=True)
    y = librosa.resample(y.mean(1).astype(np.float32), orig_sr=sr, target_sr=RATE)
    y = y / (np.abs(y).max() + 1e-9) * peak
    pcm = np.clip(np.round(y * 127), -128, 127).astype(np.int8)
    pcm = np.concatenate([pcm, np.zeros((-len(pcm)) % 16, np.int8)])  # whole 16-byte FIFO refills
    return pcm


if __name__ == "__main__":
    kind, name, src = sys.argv[1:4]
    pcm = convert(src, 0.6 if kind == "bgm" else 1.0)  # music sits well under the effects
    os.makedirs(OUT, exist_ok=True)
    pcm.tofile(os.path.join(OUT, f"{kind}_{name}.s8"))
    print(f"{kind}_{name}: {len(pcm) / RATE:.1f} s, {len(pcm) // 1024} KB")
