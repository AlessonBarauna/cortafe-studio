"""Sound design procedural do Cortafé FX: sem arquivos externos, sem direitos autorais."""
import numpy as np
from scipy.signal import lfilter
import wave

SR = 44100


def _env(n, attack=0.005, decay=6.0):
    t = np.arange(n) / SR
    return np.minimum(1, t / max(attack, 1e-4)) * np.exp(-t * decay)


def _noise(n, rng):
    return rng.standard_normal(n)


def _sweep_lp(x, f0, f1):
    """Passa-baixa de um polo com corte variando de f0 a f1 (Hz) ao longo do sinal."""
    out = np.zeros_like(x)
    zi = np.zeros(1)
    block = 441
    n = len(x)
    for i in range(0, n, block):
        f = f0 + (f1 - f0) * (i / max(1, n))
        a = 1 - np.exp(-2 * np.pi * max(20, f) / SR)
        seg, zi = lfilter([a], [1, -(1 - a)], x[i:i + block], zi=zi)
        out[i:i + block] = seg
    return out


def boom(rng, power=1.0):
    n = int(SR * 0.9)
    t = np.arange(n) / SR
    freq = 38 + 70 * np.exp(-t * 14)
    body = np.sin(2 * np.pi * np.cumsum(freq) / SR) * _env(n, 0.002, 4.2)
    hit = _sweep_lp(_noise(n, rng), 5000, 400) * _env(n, 0.001, 38)
    return (body * 0.9 + hit * 0.55) * power


def whoosh(rng, dur=0.55):
    n = int(SR * dur)
    x = _sweep_lp(_noise(n, rng), 300, 7000)
    t = np.linspace(0, 1, n)
    env = np.sin(np.pi * t) ** 1.6
    return x * env * 0.9


def thunder(rng, dur=2.4):
    n = int(SR * dur)
    crack = _sweep_lp(_noise(n, rng), 6000, 300) * _env(n, 0.001, 20) * 1.1
    rumble = _sweep_lp(_noise(n, rng), 420, 70)
    t = np.arange(n) / SR
    mod = 0.65 + 0.35 * np.sin(2 * np.pi * 7 * t + rng.random() * 6)
    rumble = rumble * mod * _env(n, 0.05, 1.7) * 2.2
    return crack + rumble


def chime(rng):
    n = int(SR * 1.8)
    t = np.arange(n) / SR
    out = np.zeros(n)
    for f, g, d in [(880, 1.0, 2.6), (1318.5, 0.6, 3.2), (1760, 0.45, 3.8), (2637, 0.2, 5.0)]:
        out += g * np.sin(2 * np.pi * f * t) * np.exp(-t * d)
    return out * np.minimum(1, t / 0.004) * 0.55


def gong(rng):
    n = int(SR * 2.4)
    t = np.arange(n) / SR
    out = np.zeros(n)
    for f, g, d in [(55, 1.0, 1.5), (82.4, 0.7, 1.9), (131, 0.35, 2.4), (197, 0.22, 3.0)]:
        out += g * np.sin(2 * np.pi * f * t + 0.3 * np.sin(2 * np.pi * 3 * t)) * np.exp(-t * d)
    out += _sweep_lp(_noise(n, rng), 3000, 200) * _env(n, 0.001, 18) * 0.35
    return out * 0.8


def crackle(rng, dur=2.0):
    n = int(SR * dur)
    x = np.zeros(n)
    clicks = rng.random(n) < 0.0016
    x[clicks] = rng.standard_normal(clicks.sum()) * 1.4
    x = lfilter([1, -0.9], [1, -0.2], x)
    soft = _sweep_lp(_noise(n, rng), 900, 500) * 0.25
    t = np.linspace(0, 1, n)
    return (x * 0.8 + soft) * np.sin(np.pi * t) ** 0.8


SFX = {"boom": boom, "whoosh": whoosh, "thunder": thunder, "chime": chime, "gong": gong, "crackle": crackle}


def pad(duration, rng):
    """Cama atmosférica baixa: acorde menor com batimento lento."""
    n = int(SR * duration)
    t = np.arange(n) / SR
    out = np.zeros(n)
    for f, g in [(55.0, 0.9), (110.0, 0.5), (164.8, 0.35), (261.6, 0.18), (329.6, 0.12)]:
        for det in (-0.7, 0.0, 0.8):
            out += g * np.sin(2 * np.pi * (f + det) * t + rng.random() * 6.28)
    out *= 0.6 + 0.4 * np.sin(2 * np.pi * 0.11 * t)
    out = _sweep_lp(out, 900, 900)
    fade = np.minimum(1, np.minimum(t / 1.5, (duration - t) / 1.5))
    return out / max(1e-6, np.abs(out).max()) * 0.5 * fade


def build_tracks(duration, events, seed=7):
    """events: lista de (tempo_s, nome_sfx, ganho). Retorna (sfx, pad) em float32 mono."""
    rng = np.random.default_rng(seed)
    total = int(SR * (duration + 3))
    sfx = np.zeros(total)
    for time, name, gain in events:
        s = SFX[name](rng) * gain
        a = int(max(0, time) * SR)
        b = min(total, a + len(s))
        sfx[a:b] += s[: b - a]
    peak = np.abs(sfx).max()
    if peak > 0.95:
        sfx *= 0.95 / peak
    return sfx[: int(SR * duration)].astype(np.float32), pad(duration, rng).astype(np.float32)


def write_wav(path, data):
    pcm = (np.clip(data, -1, 1) * 32767).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())
