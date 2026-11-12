"""Camada visual do Cortafé FX: legenda cinética, gancho, ilustrações temáticas, câmera e flashes (Skia)."""
import math
import os
import random

import numpy as np
import skia

W, H = 1080, 1920
GOLD = (255, 196, 46)
HOT = (255, 92, 64)
WHITE = (255, 255, 255)
FONT_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "fonts", "Poppins-Bold.ttf")


def clamp(x, a=0.0, b=1.0):
    return max(a, min(b, x))


def ease_out(x):
    return 1 - (1 - clamp(x)) ** 3


def back_out(x, s=1.9):
    x = clamp(x) - 1
    return 1 + (s + 1) * x ** 3 + s * x ** 2


def col(rgb, a=1.0):
    return skia.Color(int(rgb[0]), int(rgb[1]), int(rgb[2]), int(255 * clamp(a)))


class Painter:
    def __init__(self, words, phrase_idx, emphasis, hook, themes, duration, punches, watermark="", watermark_opacity=0.8):
        self.words, self.dur = words, duration
        self.watermark, self.watermark_opacity = watermark.strip(), watermark_opacity
        self.emph = set(emphasis)
        self.hook = hook  # (tokens, star) ou None
        self.themes = themes  # [(tempo, nome, sfx)]
        self.punches = punches  # [(tempo, amplitude_zoom, tremida)]
        self.surface = skia.Surface(W, H)
        self.tf = skia.Typeface.MakeFromFile(FONT_PATH)
        self._fonts = {}
        self.cf = skia.ColorFilters.Matrix([
            1.10, 0, 0, 0, -0.045,
            0, 1.10, 0, 0, -0.045,
            0, 0, 1.10, 0, -0.045,
            0, 0, 0, 1, 0])
        self.phrases = []
        for idxs in phrase_idx:
            t0 = words[idxs[0]]["s"]
            t1 = words[idxs[-1]]["e"]
            self.phrases.append({"idx": idxs, "t0": t0, "t1": t1})
        for k, ph in enumerate(self.phrases):
            nxt = self.phrases[k + 1]["t0"] if k + 1 < len(self.phrases) else 1e9
            ph["show0"] = ph["t0"] - 0.05
            ph["show1"] = min(ph["t1"] + 0.28, nxt - 0.02)
        self.hook_end = 3.0
        self.rng_cache = {}
        self.flash_extra = 0.0

    # ---------- texto ----------
    def font(self, size):
        size = max(8, int(round(size)))
        if size not in self._fonts:
            self._fonts[size] = skia.Font(self.tf, size)
        return self._fonts[size]

    def width(self, s, size):
        return self.font(size).measureText(s)

    def draw_text(self, c, s, cx, base, size, fill, alpha=1.0, scale=1.0, rot=0.0, glow=None, split=0.0, stroke=0.15):
        if alpha <= 0.01 or scale <= 0.01:
            return
        font = self.font(size)
        w = font.measureText(s)
        c.save()
        c.translate(cx, base)
        if rot:
            c.rotate(math.degrees(rot))
        c.scale(scale, scale)
        x = -w / 2

        def paint(color, a, style=None, sw=0, blur=0):
            p = skia.Paint(AntiAlias=True, Color=col(color, a * alpha))
            if style == "stroke":
                p.setStyle(skia.Paint.kStroke_Style)
                p.setStrokeWidth(sw)
                p.setStrokeJoin(skia.Paint.kRound_Join)
            if blur:
                p.setMaskFilter(skia.MaskFilter.MakeBlur(skia.kNormal_BlurStyle, blur))
            return p

        c.drawString(s, x + 0, 9, font, paint((0, 0, 0), 0.55, blur=7))
        if glow:
            c.drawString(s, x, 0, font, paint(glow, 0.9, blur=size * 0.22))
        if split > 0.5:
            c.drawString(s, x - split, 0, font, paint((255, 40, 70), 0.85))
            c.drawString(s, x + split, 0, font, paint((40, 230, 255), 0.85))
        c.drawString(s, x, 0, font, paint((0, 0, 0), 1, "stroke", size * stroke))
        c.drawString(s, x, 0, font, paint(fill, 1))
        c.restore()

    # ---------- câmera ----------
    def camera(self, t):
        z = 1.05 + 0.035 * t / max(1, self.dur)
        sx = sy = rot = 0.0
        for te, amp, shake in self.punches:
            dt = t - te
            if 0 <= dt < 1.4:
                z += amp * min(1, dt / 0.06) * math.exp(-dt * 5)
                if shake and dt < 0.5:
                    d = math.exp(-dt * 9)
                    sx += shake * d * math.sin(dt * 90)
                    sy += shake * 0.6 * d * math.sin(dt * 71 + 1)
                    rot += 0.009 * (shake / 16) * d * math.sin(dt * 55)
        return z, sx, sy, rot

    # ---------- camadas ----------
    def gradients(self, c, hook_env):
        bottom = skia.GradientShader.MakeLinear([(0, 1000), (0, H)], [skia.Color(0, 0, 0, 0), skia.Color(0, 0, 0, 170)])
        c.drawRect(skia.Rect(0, 1000, W, H), skia.Paint(Shader=bottom))
        top_alpha = int(70 + 130 * hook_env)
        top_h = 650 + 500 * hook_env
        top = skia.GradientShader.MakeLinear([(0, 0), (0, top_h)], [skia.Color(0, 0, 0, top_alpha), skia.Color(0, 0, 0, 0)])
        c.drawRect(skia.Rect(0, 0, W, top_h), skia.Paint(Shader=top))
        vig = skia.GradientShader.MakeRadial((W / 2, H / 2), 1250, [skia.Color(0, 0, 0, 0), skia.Color(0, 0, 0, 110)], [0.55, 1])
        c.drawRect(skia.Rect(0, 0, W, H), skia.Paint(Shader=vig))

    def rays(self, c, lt, env, cx=W / 2, cy=-80, alpha=0.40, count=9):
        sh = skia.GradientShader.MakeLinear([(cx, cy), (cx, cy + 1500)], [col(GOLD, alpha * env), col(GOLD, 0)])
        p = skia.Paint(AntiAlias=True, Shader=sh)
        for k in range(count):
            a = math.pi / 2 + (k - count // 2) * 0.17 + math.sin(lt * 0.9 + k) * 0.035
            wid = 0.045 + 0.02 * math.sin(lt * 1.3 + k * 2)
            path = skia.Path()
            path.moveTo(cx, cy)
            path.lineTo(cx + 2400 * math.cos(a - wid), cy + 2400 * math.sin(a - wid))
            path.lineTo(cx + 2400 * math.cos(a + wid), cy + 2400 * math.sin(a + wid))
            path.close()
            c.drawPath(path, p)

    def particles(self, c, lt, env, color, n, up=True, seed=1):
        rnd = random.Random(seed)
        for _ in range(n):
            x0, y0, sp, sz, ph = rnd.random() * W, rnd.random(), 80 + rnd.random() * 220, 2 + rnd.random() * 4, rnd.random() * 6
            y = (y0 * (H + 200) - lt * sp * (1 if up else -1)) % (H + 200) - 100
            x = x0 + math.sin(lt * 1.5 + ph) * 28
            a = env * (0.35 + 0.35 * math.sin(lt * 5 + ph))
            c.drawCircle(x, y, sz, skia.Paint(AntiAlias=True, Color=col(color, a)))

    def theme(self, c, name, lt, dur=2.9):
        env = clamp(lt / 0.3) * clamp((dur - lt) / 0.7)
        if env <= 0:
            return
        if name == "luz":
            self.rays(c, lt, env)
            self.particles(c, lt, env, (255, 236, 170), 46, True, 3)
        elif name == "cruz":
            self.rays(c, lt, env, W / 2, 470, 0.46, 11)
            k = 0.7 + 0.3 * ease_out(lt / 0.55)
            c.save()
            c.translate(W / 2, 520)
            c.scale(k, k)
            for blur, color, a in [(34, GOLD, 0.95), (0, (255, 244, 214), 1.0)]:
                p = skia.Paint(AntiAlias=True, Color=col(color, a * env))
                if blur:
                    p.setMaskFilter(skia.MaskFilter.MakeBlur(skia.kNormal_BlurStyle, blur))
                c.drawRoundRect(skia.Rect(-34, -190, 34, 210), 12, 12, p)
                c.drawRoundRect(skia.Rect(-125, -105, 125, -40), 12, 12, p)
            c.restore()
            self.particles(c, lt, env, (255, 230, 150), 30, True, 5)
        elif name == "tempestade":
            c.drawRect(skia.Rect(0, 0, W, H), skia.Paint(Color=col((8, 16, 34), 0.30 * env)))
            rnd = random.Random(11)
            p = skia.Paint(AntiAlias=True, Color=col((205, 225, 255), 0.42 * env), StrokeWidth=3.2)
            for _ in range(170):
                x0, y0, sp, ln = rnd.random() * (W + 400), rnd.random(), 0.8 + rnd.random() * 0.8, 70 + rnd.random() * 90
                y = ((y0 * (H + 300)) + lt * 2300 * sp) % (H + 300) - 150
                x = x0 - lt * 420 * sp
                x = x % (W + 400) - 100
                c.drawLine(x, y, x - ln * 0.2, y + ln, p)
            for t0 in (0.18, 0.7):
                if t0 <= lt < t0 + 0.55:
                    self.flash_extra = max(self.flash_extra, 0.6 * math.exp(-(lt - t0) * 10))
        elif name == "fogo":
            sh = skia.GradientShader.MakeLinear([(0, H), (0, H - 900)], [col((255, 110, 20), 0.62 * env), col((255, 110, 20), 0)])
            c.drawRect(skia.Rect(0, H - 900, W, H), skia.Paint(Shader=sh))
            rnd = random.Random(21)
            for _ in range(120):
                x0, y0, sp, sz, ph = rnd.random() * W, rnd.random(), 160 + rnd.random() * 380, 2.5 + rnd.random() * 6, rnd.random() * 6
                prog = ((y0 + lt * sp / 1500.0) % 1.0)
                y = H + 40 - prog * 1700
                x = x0 + math.sin(lt * 2.4 + ph) * 40 * prog
                a = env * (1 - prog) * (0.6 + 0.4 * math.sin(lt * 17 + ph))
                color = (255, 150 + int(90 * (1 - prog)), 40)
                c.drawCircle(x, y, sz * (1 - 0.6 * prog), skia.Paint(AntiAlias=True, Color=col(color, a)))

    def captions(self, c, t):
        for ph in self.phrases:
            if not (ph["show0"] <= t <= ph["show1"]):
                continue
            idx = ph["idx"]
            local = t - ph["show0"]
            fade = clamp((ph["show1"] - t) / 0.08)
            if fade <= 0:
                continue
            toks = []
            for i in idx:
                w = self.words[i]
                s = w["w"].rstrip(".,;:").upper()
                size = 132 if i in self.emph else 104
                toks.append({"i": i, "s": s, "size": size})
            maxw = 960
            rows, row, rw = [], [], 0
            for tk in toks:
                tw = self.width(tk["s"], tk["size"])
                gap = 0.26 * tk["size"] if row else 0
                if row and rw + gap + tw > maxw:
                    rows.append((row, rw))
                    row, rw, gap = [], 0, 0
                tk["w"] = tw
                tk["gap"] = gap
                row.append(tk)
                rw += gap + tw
            rows.append((row, rw))
            k = min(1.0, maxw / max(r[1] for r in rows))
            heights = [max(tk["size"] for tk in r[0]) * k * 1.16 for r in rows]
            block = sum(heights)
            y = 1395 - block / 2
            for (row, rw), hh in zip(rows, heights):
                y += hh
                x = (W - rw * k) / 2
                for tk in row:
                    w = self.words[tk["i"]]
                    x += tk["gap"] * k
                    cx = x + tk["w"] * k / 2
                    x += tk["w"] * k
                    dt = t - w["s"]
                    if dt < -0.03:
                        continue
                    emph = tk["i"] in self.emph
                    active = dt <= (w["e"] - w["s"]) + 0.02
                    pop = back_out(dt / (0.18 if emph else 0.12))
                    scale = (1.9 - 0.9 * pop) if emph else (1.32 - 0.32 * pop)
                    scale = max(1.0, scale) if dt > 0.3 else scale
                    scale = min(scale, 1000.0 / max(1.0, tk["w"] * k))
                    color = GOLD if (emph or active) else WHITE
                    split = 15 * math.exp(-dt * 9) if emph and dt < 0.4 else 0
                    rot = (0.05 * math.sin(dt * 60) * math.exp(-dt * 8)) if emph else 0
                    self.draw_text(c, tk["s"], cx, y, tk["size"] * k, color, alpha=fade * clamp(dt / 0.05 + 0.4),
                                   scale=scale, rot=rot, glow=HOT if emph else None, split=split)

    def hook_layer(self, c, t):
        if not self.hook:
            return 0.0
        toks, star = self.hook
        t0, t1 = 0.12, self.hook_end
        if not (t0 <= t <= t1):
            return 0.0
        env = clamp((t - t0) / 0.2) * clamp((t1 - t) / 0.4)
        size, maxw = 112, 940
        up = [s.upper() for s in toks]
        for _ in range(4):
            rows, row, rw = [], [], 0
            for i, s in enumerate(up):
                tw = self.width(s, size)
                gap = 0.26 * size if row else 0
                if row and rw + gap + tw > maxw:
                    rows.append((row, rw)); row, rw, gap = [], 0, 0
                row.append((i, s, tw, gap)); rw += gap + tw
            rows.append((row, rw))
            if len(rows) <= 3 and max(r[1] for r in rows) <= maxw:
                break
            size *= 0.88
        y = 600 - len(rows) * size * 1.18 / 2
        for row, rw in rows:
            y += size * 1.18
            x = (W - rw) / 2
            for i, s, tw, gap in row:
                x += gap
                cx = x + tw / 2
                x += tw
                dt = t - (t0 + 0.12 + 0.16 * i)
                if dt < 0:
                    continue
                pop = back_out(dt / 0.18)
                star_word = i == star
                self.draw_text(c, s, cx, y, size, GOLD if star_word else WHITE, alpha=env,
                               scale=min(1.7 - 0.7 * pop, 1000.0 / max(1.0, tw)) if dt < 0.3 else 1.0,
                               rot=0.04 * math.sin(dt * 60) * math.exp(-dt * 8), glow=HOT if star_word else None,
                               split=12 * math.exp(-dt * 10) if dt < 0.35 else 0)
        line_w = 360 * ease_out((t - t0 - 0.2 - 0.16 * len(up)) / 0.4)
        c.drawRoundRect(skia.Rect(W / 2 - line_w / 2, y + 26, W / 2 + line_w / 2, y + 34), 4, 4, skia.Paint(AntiAlias=True, Color=col(GOLD, env)))
        return env

    # ---------- quadro ----------
    def render(self, t, frame):
        self.flash_extra = 0.0
        c = self.surface.getCanvas()
        c.clear(skia.ColorBLACK)
        img = skia.Image.fromarray(frame, colorType=skia.kRGBA_8888_ColorType, alphaType=skia.kOpaque_AlphaType)
        z, sx, sy, rot = self.camera(t)
        c.save()
        c.translate(W / 2 + sx, H / 2 + sy)
        c.rotate(math.degrees(rot))
        c.scale(z, z)
        c.translate(-W / 2, -H / 2)
        c.drawImage(img, 0, 0, skia.SamplingOptions(skia.FilterMode.kLinear), skia.Paint(ColorFilter=self.cf))
        c.restore()
        hook_env = 0.0
        if self.hook and 0.12 <= t <= self.hook_end:
            hook_env = clamp((t - 0.12) / 0.2) * clamp((self.hook_end - t) / 0.4)
        self.gradients(c, hook_env)
        for te, name, _ in self.themes:
            lt = t - (te - 0.15)
            if 0 <= lt < 2.9:
                self.theme(c, name, lt)
        self.hook_layer(c, t)
        self.captions(c, t)
        if self.watermark:
            self.draw_text(c, self.watermark, W / 2, H - 64, 34, GOLD, alpha=self.watermark_opacity, stroke=0.10)
        flash = self.flash_extra
        for te, amp, shake in self.punches:
            dt = t - te
            if shake and 0 <= dt < 0.3:
                flash = max(flash, 0.30 * math.exp(-dt * 16))
        if flash > 0.01:
            c.drawRect(skia.Rect(0, 0, W, H), skia.Paint(Color=col(WHITE, flash)))
        return self.surface.makeImageSnapshot().toarray(colorType=skia.kRGBA_8888_ColorType, alphaType=skia.kUnpremul_AlphaType)
