"""Turns a recording into a GBA-style chiptune loop (assets/music/<name>.s8, 8-bit signed PCM at 13379 Hz).

    python3 tools/make_bgm.py song.mp3 title

The beat grid comes from librosa's beat tracker. The lead is the strongest pitch above C4 that is
not the D drone (or its half-step leakage), held while it repeats; the bass is the low D pulse;
the noise drum follows the percussive onsets. Everything is rendered with square/noise/stepped
waves like the GBA's PSG channels, then streamed by DirectSound A (plat_bgm).
Needs librosa and soundfile (pip install librosa soundfile).
"""
import os, sys
import numpy as np, soundfile as sf, librosa

SR = 13379  # GBA timer 0 reload 65536-1254
HERE = os.path.dirname(os.path.abspath(__file__))


def analyse(path):
    y, sr = sf.read(path)
    if y.ndim > 1:
        y = y.mean(1)
    y = librosa.resample(y.astype(np.float32), orig_sr=sr, target_sr=22050)
    sr = 22050
    _, beats = librosa.beat.beat_track(y=y, sr=sr)
    bt = librosa.frames_to_time(beats, sr=sr)
    beat = float(np.median(np.diff(bt)))
    t0, step = float(bt[0] % beat), beat / 4
    yh, yp = librosa.effects.hpss(y)
    hop, lo = 256, librosa.note_to_midi("C2")
    C = librosa.amplitude_to_db(np.abs(librosa.cqt(yh, sr=sr, hop_length=hop, fmin=librosa.midi_to_hz(lo),
                                                   n_bins=72, bins_per_octave=12)), ref=np.max)
    ft = librosa.frames_to_time(np.arange(C.shape[1]), sr=sr, hop_length=hop)
    on = librosa.onset.onset_strength(y=yp, sr=sr, hop_length=hop)
    n = int((len(y) / sr - t0) / step)
    mel, bas, perc = [], [], []
    for i in range(n):
        m = (ft >= t0 + i * step) & (ft < t0 + (i + 1) * step)
        col = C[:, m].mean(1)
        v, p = max((col[k], lo + k) for k in range(24, 60) if (lo + k) % 12 != 2)
        mel.append(p if v > -40 and v > col[62 - lo] - 9 else -1)
        bas.append(lo + int(np.argmax(col[:20])))
        perc.append(float(on[m].max()) if m.any() else 0.0)
    return mel, bas, np.array(perc), t0, step


def runs(seq):
    out, i = [], 0
    while i < len(seq):
        j = i
        while j < len(seq) and seq[j] == seq[i]:
            j += 1
        if seq[i] >= 0:
            out.append((i, j - i, seq[i]))
        i = j
    return out


def render(mel, bas, perc, t0, step):
    mel = [-1 if (m >= 0 and m % 12 in (1, 3) and m < 64) else m for m in mel]  # leakage of the D drone
    for i in range(1, len(mel) - 1):  # one-step dips inside a held note
        if mel[i] >= 0 and mel[i - 1] == mel[i + 1] != mel[i]:
            mel[i] = mel[i - 1]
    for i in range(1, len(mel) - 1):  # lone one-step blips
        if mel[i] >= 0 and mel[i - 1] != mel[i] and mel[i + 1] != mel[i]:
            mel[i] = -1
    n = len(mel)
    T = int((t0 + n * step + 0.6) * SR)
    out = np.zeros(T)
    hz = lambda m: 440 * 2 ** ((m - 69) / 12)

    def env(L, a=0.004, d=0.08, sus=0.6, r=0.04):
        e = np.ones(L) * sus
        A, D, R = int(a * SR), int(d * SR), int(r * SR)
        e[:A] = np.linspace(0, 1, A)
        e[A:A + D] = np.linspace(1, sus, len(e[A:A + D]))
        e[-R:] *= np.linspace(1, 0, len(e[-R:]))
        return e

    def put(x, start):
        i = int(start * SR)
        out[i:i + len(x)] += x[:max(0, T - i)]

    for st, ln, m in runs(mel):  # lead: 25% square, vibrato on long notes
        L = int(ln * step * SR)
        t = np.arange(L) / SR
        ph = np.cumsum(hz(m + 12 if m < 60 else m) * (1 + 0.004 * np.sin(2 * np.pi * 5.5 * t) * (t > 0.25)) / SR) % 1
        put(0.30 * np.where(ph < 0.25, 1.0, -1.0) * env(L, sus=0.7), t0 + st * step)
    for st, ln, m in runs([b if 36 <= b <= 45 else -1 for b in bas]):  # bass pulse, two octaves of 50% square
        L = int(min(ln, 3) * step * SR)
        t = np.arange(L) / SR
        x = 0.5 * np.where((t * hz(m + 12)) % 1 < 0.5, 1.0, -1.0) + 0.5 * np.where((t * hz(m)) % 1 < 0.5, 1.0, -1.0)
        put(0.22 * x * env(L, d=0.12, sus=0.35), t0 + st * step)
    L = int((t0 + min(n, 400) * step) * SR)  # stepped D-A drone like the wave channel
    t = np.arange(L) / SR
    pad = np.round((np.sin(2 * np.pi * hz(50) * t) + 0.6 * np.sin(2 * np.pi * hz(57) * t)) * 4) / 4
    put(0.08 * pad * np.minimum(1, t / 1.5), 0)
    rng = np.random.default_rng(1)
    noise = np.sign(rng.standard_normal(int(0.12 * SR)))
    hit, big = np.percentile(perc, 80), np.percentile(perc, 93)
    for i, p in enumerate(perc):  # noise drum
        if p > hit:
            put(0.18 * noise * np.exp(-np.arange(len(noise)) / SR * 35) * (1 if p > big else 0.6), t0 + i * step)
    out /= np.abs(out).max() * 1.05
    return np.clip(np.round(out * 127), -128, 127).astype(np.int8)


if __name__ == "__main__":
    src, name = sys.argv[1], sys.argv[2]
    pcm = render(*analyse(src))
    d = os.path.join(HERE, "..", "assets", "music")
    os.makedirs(d, exist_ok=True)
    pcm.tofile(os.path.join(d, name + ".s8"))
    sf.write(os.path.join(d, name + "_preview.wav"), pcm.astype(np.float32) / 128, SR, subtype="PCM_16")
    print(f"{name}: {len(pcm) / SR:.1f} s")
