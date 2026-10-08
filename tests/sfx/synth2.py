import sys, wave
from pathlib import Path
import numpy as np

SR = 48000
OUT = Path(sys.argv[1])
OUT.mkdir(parents=True, exist_ok=True)
rng = np.random.default_rng(7)

def T(dur):
    return np.arange(int(SR * dur)) / SR

def biquad(x, kind, f, q=0.707):
    w = 2 * np.pi * f / SR
    c, s = np.cos(w), np.sin(w)
    al = s / (2 * q)
    if kind == "lp":
        b = [(1 - c) / 2, 1 - c, (1 - c) / 2]
    elif kind == "hp":
        b = [(1 + c) / 2, -(1 + c), (1 + c) / 2]
    else:  # band-pass, 0 dB peak
        b = [al, 0, -al]
    a = [1 + al, -2 * c, 1 - al]
    b = [v / a[0] for v in b]; a1, a2 = a[1] / a[0], a[2] / a[0]
    y = np.zeros_like(x); x1 = x2 = y1 = y2 = 0.0
    for i, v in enumerate(x):
        o = b[0] * v + b[1] * x1 + b[2] * x2 - a1 * y1 - a2 * y2
        x2, x1, y2, y1 = x1, v, y1, o
        y[i] = o
    return y

def soft_attack(t, ms):
    k = np.clip(t / (ms / 1000), 0, 1)
    return 0.5 - 0.5 * np.cos(np.pi * k)

def tail_fade(x, ms=6):
    n = min(len(x), int(SR * ms / 1000))
    x[-n:] *= np.linspace(1, 0, n)
    return x

def sine_sweep(f, t):
    return np.sin(2 * np.pi * np.cumsum(f) / SR)

def click(body=1400, drop=0.6, decay=0.007, noise=0.35, noise_f=4200, dur=0.035):
    # Small elastic "tock": a damped sine whose pitch settles from above, plus a tiny filtered transient.
    t = T(dur)
    f = body * (1 + drop * np.exp(-t / 0.003))
    tone = sine_sweep(f, t) * np.exp(-t / decay)
    n = biquad(rng.standard_normal(len(t)), "bp", noise_f, 1.2) * np.exp(-t / 0.0015) * noise
    return tail_fade((tone + n) * soft_attack(t, 0.6))

def drop(f0=900, rise=1.9, sweep=0.025, decay=0.03, dur=0.12):
    # Water droplet: resonance rising fast as the cavity closes.
    t = T(dur)
    f = f0 * rise ** np.clip(t / sweep, 0, 1)
    x = sine_sweep(f, t) * np.exp(-t / decay) * soft_attack(t, 1.5)
    return tail_fade(biquad(x, "lp", 7000))

def glass(f0=2400, decay=0.05, dur=0.25, bright=0.4):
    # Inharmonic partials of a small glass body; high partials damped hard so it stays "soft".
    t = T(dur)
    x = np.zeros_like(t)
    for r, a, d in [(1, 1, 1), (2.32, 0.45 * bright, 0.45), (4.25, 0.2 * bright, 0.22), (6.63, 0.08 * bright, 0.12)]:
        x += a * np.sin(2 * np.pi * f0 * r * t + rng.uniform(0, 6.28)) * np.exp(-t / (decay * d))
    return tail_fade(biquad(x * soft_attack(t, 1.2), "lp", 9000))

def air(dur=0.3, f_from=700, f_to=3500, level=0.25):
    t = T(dur)
    n = rng.standard_normal(len(t))
    out = np.zeros_like(n)
    seg = 480
    for i in range(0, len(n), seg):  # stepped sweep of a band-pass is fine at 10 ms steps
        k = i / len(n)
        out[i:i + seg] = biquad(n[max(0, i - seg):i + seg], "bp", f_from * (f_to / f_from) ** k, 1.5)[-len(n[i:i + seg]):]
    env = np.sin(np.pi * np.clip(t / dur, 0, 1)) ** 2
    return out * env * level

def mix(parts, total):
    out = np.zeros(int(SR * total))
    for start, s, g in parts:
        i = int(SR * start); n = min(len(s), len(out) - i)
        out[i:i + n] += s[:n] * g
    return out

S = {}
S["tap"] = mix([(0, click(1500, 0.5, 0.006, 0.3), 1), (0.001, glass(3200, 0.012, 0.04, 0.2), 0.25)], 0.05)
S["switch_on"] = mix([(0, click(1300, 0.4, 0.006, 0.3), 0.8), (0.012, drop(1100, 1.7, 0.02, 0.022, 0.08), 0.9)], 0.1)
S["switch_off"] = mix([(0, drop(1300, 0.65, 0.02, 0.02, 0.08), 0.85), (0.018, click(1000, 0.4, 0.006, 0.25), 0.8)], 0.1)
for i, (b, nf, d) in enumerate([(1250, 3800, 0.006), (1450, 4600, 0.005), (1100, 3400, 0.007), (1650, 5200, 0.005)], 1):
    S[f"key_{i}"] = click(b, 0.35, d, 0.55, nf, 0.03)
S["found_a"] = mix([(0, drop(1000, 1.6, 0.02, 0.035, 0.15), 0.8), (0.085, drop(1350, 1.6, 0.02, 0.05, 0.2), 0.9), (0.09, glass(2700, 0.12, 0.4, 0.3), 0.3)], 0.5)
S["found_b"] = mix([(0, glass(1976, 0.09, 0.35, 0.35), 0.8), (0.07, glass(2960, 0.14, 0.45, 0.3), 0.7)], 0.55)
S["overlay_a"] = mix([(0, air(0.28, 600, 3200, 0.2), 1), (0.2, drop(1200, 1.8, 0.025, 0.04, 0.16), 0.8), (0.21, glass(3000, 0.1, 0.3, 0.25), 0.25)], 0.55)
S["overlay_b"] = mix([(0, drop(800, 1.6, 0.02, 0.028, 0.12), 0.6), (0.06, drop(1000, 1.6, 0.02, 0.03, 0.12), 0.7), (0.12, drop(1250, 1.7, 0.02, 0.045, 0.2), 0.85)], 0.4)

def norm(x, peak_db):
    return x / (np.max(np.abs(x)) + 1e-9) * 10 ** (peak_db / 20)

def write(path, x):
    with wave.open(str(path), "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes((np.clip(x, -1, 1) * 32767).astype("<i2").tobytes())

for k, x in S.items():
    write(OUT / f"{k}.wav", norm(x, -6))

gap = lambda s: np.zeros(int(SR * s))
order = [("tap", 0.5), ("tap", 0.5), ("switch_on", 0.5), ("switch_off", 0.9)]
typing = []
for k in [1, 3, 2, 4, 1, 2, 4, 3]:
    typing += [norm(S[f"key_{k}"], -6), gap(rng.uniform(0.09, 0.16))]
preview = []
for k, g in order:
    preview += [norm(S[k], -6), gap(g)]
preview += typing + [gap(0.8)]
for k in ["found_a", "found_b", "overlay_a", "overlay_b"]:
    preview += [norm(S[k], -6), gap(0.9)]
write(OUT / "preview.wav", np.concatenate(preview))
for k, x in S.items():
    print(k, round(len(x) / SR * 1000), "ms")
