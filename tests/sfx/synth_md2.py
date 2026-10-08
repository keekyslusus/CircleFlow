import sys, wave
from pathlib import Path
import numpy as np

SR = 48000
OUT = Path(sys.argv[1])
OUT.mkdir(parents=True, exist_ok=True)
rng = np.random.default_rng(11)

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
    else:
        b = [al, 0, -al]
    a = [1 + al, -2 * c, 1 - al]
    b = [v / a[0] for v in b]; a1, a2 = a[1] / a[0], a[2] / a[0]
    y = np.zeros_like(x); x1 = x2 = y1 = y2 = 0.0
    for i, v in enumerate(x):
        o = b[0] * v + b[1] * x1 + b[2] * x2 - a1 * y1 - a2 * y2
        x2, x1, y2, y1 = x1, v, y1, o
        y[i] = o
    return y

def env(t, attack_ms, decay_s):
    a = np.clip(t / (attack_ms / 1000), 0, 1)
    return (0.5 - 0.5 * np.cos(np.pi * a)) * np.exp(-t / decay_s)

def modes(t, freqs, amps, decays, detune=0.0):
    x = np.zeros_like(t)
    for f, a, d in zip(freqs, amps, decays):
        f = f * (1 + rng.uniform(-detune, detune))
        x += a * np.sin(2 * np.pi * f * t + rng.uniform(0, 6.28)) * np.exp(-t / d)
    return x

def noise_burst(t, f, q, decay, attack_ms=0.3):
    return biquad(rng.standard_normal(len(t)), "bp", f, q) * env(t, attack_ms, decay)

def room(x, size=0.06, level=0.12, tone=3500):
    # A tiny desk-sized space: makes contact sounds read as happening somewhere real.
    t = T(size)
    ir = rng.standard_normal(len(t)) * np.exp(-t / (size / 5))
    ir = biquad(ir, "lp", tone)
    ir[0] = 0
    wet = np.convolve(x, ir)
    wet *= level * np.max(np.abs(x)) / (np.max(np.abs(wet)) + 1e-9)
    dry = np.concatenate([x, np.zeros(len(wet) - len(x))])
    return fade(dry + wet)

def fade(x, ms=8):
    n = min(len(x), int(SR * ms / 1000))
    x[-n:] *= np.linspace(1, 0, n)
    return x

def mix(parts, total):
    out = np.zeros(int(SR * total))
    for start, s, g in parts:
        i = int(SR * start); n = min(len(s), len(out) - i)
        out[i:i + n] += s[:n] * g
    return out

def finger_tap(body=190, panel=(760, 1490, 2650), bright=1.0, dur=0.07):
    # Fingertip on glass over a phone body: soft flesh contact, a low thump, short panel ring.
    t = T(dur)
    thump = np.sin(2 * np.pi * body * t * (1 + 0.25 * np.exp(-t / 0.004))) * env(t, 1.2, 0.012)
    contact = noise_burst(t, 1600, 0.8, 0.0025, 0.6) * 0.5
    ring = modes(t, panel, [0.35, 0.18 * bright, 0.08 * bright], [0.009, 0.006, 0.004], 0.02) * env(t, 0.5, 1)
    return fade(biquad(thump + contact + ring, "lp", 6000))

def plastic_click(f=2300, body=420, q=1.5, decay=0.004, level_body=0.6, dur=0.04):
    t = T(dur)
    click = noise_burst(t, f, q, decay * 0.5, 0.15)
    tone = modes(t, [f * 0.92, f * 1.63], [0.5, 0.25], [decay, decay * 0.6], 0.03) * env(t, 0.3, 1)
    low = np.sin(2 * np.pi * body * t) * env(t, 0.8, decay * 2.5) * level_body
    return fade(click + tone + low)

def keystroke(seed, dur=0.09):
    r = np.random.default_rng(seed)
    t = T(dur)
    top = r.uniform(2400, 3400)
    bottom = r.uniform(260, 380)
    travel = r.uniform(0.007, 0.011)
    contact = noise_burst(t, top, 1.2, 0.0018) * 0.35
    thock = np.zeros_like(t)
    i = int(SR * travel)
    tt = t[: len(t) - i]
    thock[i:] = (np.sin(2 * np.pi * bottom * tt * (1 + 0.3 * np.exp(-tt / 0.003))) * env(tt, 0.6, 0.014)
                 + biquad(r.standard_normal(len(tt)), "bp", r.uniform(1100, 1600), 1.0) * env(tt, 0.3, 0.004) * 0.6
                 + modes(tt, [bottom * 3.1, bottom * 5.4], [0.25, 0.12], [0.008, 0.005]))
    return fade(biquad(contact + thock, "lp", 7500))

def wood_knock(f=520, dur=0.25, decay=0.06):
    # Small wooden block struck with a fingertip: strong fundamental, quickly damped overtones.
    t = T(dur)
    x = modes(t, [f, f * 2.57, f * 4.2], [1, 0.35, 0.12], [decay, decay * 0.35, decay * 0.18]) * env(t, 1.0, 1)
    x += noise_burst(t, 1800, 0.9, 0.002) * 0.25
    return fade(biquad(x, "lp", 5000))

def bell(f=1320, dur=0.7, decay=0.22):
    # Small hand bell: near-harmonic partials with slow beating, soft felt mallet.
    t = T(dur)
    x = np.zeros_like(t)
    for r, a, d in [(1, 1, 1), (2.0, 0.3, 0.55), (2.7, 0.18, 0.35), (4.1, 0.07, 0.2)]:
        x += a * np.sin(2 * np.pi * f * r * t) * (1 + 0.08 * np.sin(2 * np.pi * 3.5 * t)) * np.exp(-t / (decay * d))
    return fade(biquad(x * env(t, 2.5, 10), "lp", 6500))

def paper_slide(dur=0.24, f_from=900, f_to=2600, level=1.0):
    t = T(dur)
    grain = rng.standard_normal(len(t)) * (0.6 + 0.4 * (rng.random(len(t)) > 0.97))
    out = np.zeros_like(grain); seg = 480
    for i in range(0, len(grain), seg):
        k = i / len(grain)
        chunk = biquad(grain[max(0, i - seg):i + seg], "bp", f_from * (f_to / f_from) ** k, 0.9)
        out[i:i + seg] = chunk[-len(grain[i:i + seg]):]
    shape = np.sin(np.pi * np.clip(t / dur, 0, 1)) ** 1.5
    return biquad(out * shape * level, "lp", 4000)

S = {}
S["tap"] = room(finger_tap())
S["switch_on"] = room(mix([(0, plastic_click(2100, 380, 1.4, 0.004, 0.5), 0.55), (0.022, plastic_click(2600, 460, 1.6, 0.005, 0.8), 1)], 0.075))
S["switch_off"] = room(mix([(0, plastic_click(2600, 460, 1.6, 0.004, 0.5), 0.55), (0.026, plastic_click(1900, 330, 1.4, 0.005, 0.8), 1)], 0.075))
for i, seed in enumerate([3, 8, 21, 34], 1):
    S[f"key_{i}"] = room(keystroke(seed))
S["found_a"] = room(mix([(0, wood_knock(523), 0.8), (0.11, wood_knock(784, 0.3, 0.075), 1)], 0.45), 0.08, 0.2)
S["found_b"] = room(mix([(0, bell(1319), 0.8), (0.09, bell(1976, 0.75, 0.25), 0.7)], 0.85), 0.1, 0.25)
S["overlay_a"] = room(mix([(0, paper_slide(), 0.5), (0.2, finger_tap(170, (680, 1350, 2400), 0.6), 0.9)], 0.3))
S["overlay_b"] = room(mix([(0, finger_tap(150, (600, 1200, 2200), 0.4), 0.6), (0.06, wood_knock(660, 0.22, 0.05), 0.8)], 0.3))

def norm(x, peak_db=-6):
    return x / (np.max(np.abs(x)) + 1e-9) * 10 ** (peak_db / 20)

def write(path, x):
    with wave.open(str(path), "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes((np.clip(x, -1, 1) * 32767).astype("<i2").tobytes())

for k, x in S.items():
    write(OUT / f"{k}.wav", norm(x))

gap = lambda s: np.zeros(int(SR * s))
preview = []
for k, g in [("tap", 0.45), ("tap", 0.45), ("tap", 0.8), ("switch_on", 0.5), ("switch_off", 0.9)]:
    preview += [norm(S[k]), gap(g)]
for k in [1, 3, 2, 4, 1, 2, 4, 3, 1, 4]:
    preview += [norm(S[f"key_{k}"]), gap(rng.uniform(0.08, 0.15))]
preview += [gap(0.7)]
for k in ["found_a", "found_b", "overlay_a", "overlay_b"]:
    preview += [norm(S[k]), gap(0.9)]
write(OUT / "preview.wav", np.concatenate(preview))
for k, x in S.items():
    print(k, round(len(x) / SR * 1000), "ms")
