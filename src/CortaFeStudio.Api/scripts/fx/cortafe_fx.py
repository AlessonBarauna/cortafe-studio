#!/usr/bin/env python3
"""Cortafé FX: transforma um corte 9:16 em vídeo animado (legenda cinética, gancho, ilustrações, câmera e sons).

uso: cortafe_fx.py --video corte.mp4 --words palavras.json --out saida.mp4
palavras.json: lista de {word,start,end} ou segmentos do Faster-Whisper (com 'words'), em segundos relativos ao corte.
"""
import argparse
import json
import os
import subprocess
import sys
import tempfile

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import fx_analysis as an  # noqa: E402
import fx_audio as fa  # noqa: E402
import fx_visual as fv  # noqa: E402

FPS = 30
FFMPEG = "ffmpeg"
FFPROBE = "ffprobe"


def run(cmd, **kw):
    return subprocess.run(cmd, check=True, **kw)


def probe(video):
    out = subprocess.run([FFPROBE, "-v", "error", "-show_entries", "format=duration:stream=codec_type", "-of", "json", video],
                         capture_output=True, text=True, check=True).stdout
    data = json.loads(out)
    has_audio = any(s.get("codec_type") == "audio" for s in data.get("streams", []))
    return float(data["format"]["duration"]), has_audio


def build_plan(words, duration, args):
    hook = None
    if not args.no_hook and duration >= 8 and words:
        if args.hook:
            toks = args.hook.split()
            hook = (toks, max(range(len(toks)), key=lambda i: an.IMPACT.get(an.norm(toks[i]), 0)))
        else:
            hook = an.pick_hook(words)
    emphasis = an.pick_emphasis(words, duration)
    themes = [] if args.no_themes else [th for th in an.pick_themes(words, duration) if th[0] >= 3.1]
    punches = [(0.0, 0.12, 0)] if hook else []
    punches += [(words[i]["s"], 0.085, 16) for i in emphasis]
    events = []
    if hook:
        events += [(0.12, "whoosh", 0.8), (0.38, "boom", 1.0)]
    for i in emphasis:
        t = words[i]["s"]
        events += [(t - 0.18, "whoosh", 0.35), (t - 0.02, "boom", 0.7)]
    gain = {"thunder": 0.9, "gong": 0.9, "chime": 0.7, "crackle": 0.6}
    events += [(t, sfx, gain[sfx]) for t, _, sfx in themes]
    return hook, emphasis, themes, punches, events


def mix_audio(video, has_audio, duration, sfx_path, pad_path, out_wav, limit):
    src = "[0:a]aresample=44100,aformat=channel_layouts=stereo,asplit[v1][v2];" if has_audio else \
          f"anullsrc=r=44100:cl=stereo:d={duration}[s];[s]asplit[v1][v2];"
    graph = (src +
             "[2:a]volume=0.55,aformat=channel_layouts=stereo[bed0];"
             "[bed0][v1]sidechaincompress=threshold=0.015:ratio=12:attack=15:release=450[bed];"
             "[1:a]volume=0.5,aformat=channel_layouts=stereo[fx];"
             "[v2][fx][bed]amix=inputs=3:normalize=0:duration=first,alimiter=limit=0.94[m]")
    cmd = [FFMPEG, "-y", "-loglevel", "error", "-i", video, "-i", sfx_path, "-i", pad_path,
           "-filter_complex", graph, "-map", "[m]", "-ar", "44100", "-ac", "2"]
    if limit:
        cmd += ["-t", str(limit)]
    run(cmd + [out_wav])


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--video", required=True)
    ap.add_argument("--words", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--hook", help="texto do gancho; sem ele, a frase mais forte é escolhida")
    ap.add_argument("--no-hook", action="store_true")
    ap.add_argument("--no-themes", action="store_true")
    ap.add_argument("--no-sfx", action="store_true")
    ap.add_argument("--max-seconds", type=float, default=0)
    ap.add_argument("--snap", default="", help="tempos (s) para salvar quadros PNG, separados por vírgula")
    ap.add_argument("--snapdir", default=".")
    ap.add_argument("--plan", help="grava o plano editorial (JSON) neste caminho")
    ap.add_argument("--crf", type=int, default=21)
    ap.add_argument("--ffmpeg", default="ffmpeg")
    ap.add_argument("--ffprobe", default="ffprobe")
    ap.add_argument("--watermark", default="", help="texto fixo da marca d'água (não acompanha a câmera)")
    ap.add_argument("--watermark-opacity", type=float, default=0.8)
    ap.add_argument("--caption-x", type=float, default=50.0, help="centro horizontal da legenda (%% da largura)")
    ap.add_argument("--caption-style", default="impact", help="impact, sermon, bold, clean, minimal, podcast, motivational ou worship")
    ap.add_argument("--caption-y", type=float, default=72.7, help="centro vertical da legenda (%% da altura)")
    args = ap.parse_args()
    global FFMPEG, FFPROBE
    FFMPEG, FFPROBE = args.ffmpeg, args.ffprobe

    duration, has_audio = probe(args.video)
    if args.max_seconds:
        duration = min(duration, args.max_seconds)
    with open(args.words, encoding="utf-8") as f:
        words = an.load_words(json.load(f))
    words = [w for w in words if w["s"] < duration]
    hook, emphasis, themes, punches, events = build_plan(words, duration, args)
    phrases = an.phrases(words)

    if args.plan:
        with open(args.plan, "w", encoding="utf-8") as f:
            json.dump({"hook": hook[0] if hook else None, "emphasis": [words[i]["w"] + f"@{words[i]['s']:.1f}s" for i in emphasis],
                       "themes": [(round(t, 1), n) for t, n, _ in themes]}, f, ensure_ascii=False, indent=2)

    tmp = tempfile.mkdtemp(prefix="cortafe_fx_")
    sfx, pad = fa.build_tracks(duration, [] if args.no_sfx else events)
    if args.no_sfx:
        pad = pad * 0
    sfx_path, pad_path, mix_path = (os.path.join(tmp, n) for n in ("sfx.wav", "pad.wav", "mix.wav"))
    fa.write_wav(sfx_path, sfx)
    fa.write_wav(pad_path, pad)
    mix_audio(args.video, has_audio, duration, sfx_path, pad_path, mix_path, args.max_seconds)

    total = int(duration * FPS)
    snaps = {round(float(s) * FPS) for s in args.snap.split(",") if s.strip()}
    os.makedirs(args.snapdir, exist_ok=True)
    painter = fv.Painter(words, phrases, emphasis, hook, themes, duration, punches, args.watermark, args.watermark_opacity, args.caption_x, args.caption_y, args.caption_style)

    dec = subprocess.Popen([FFMPEG, "-loglevel", "error", "-i", args.video, "-t", str(duration),
                            "-vf", f"fps={FPS},scale={fv.W}:{fv.H}:force_original_aspect_ratio=increase,crop={fv.W}:{fv.H}",
                            "-f", "rawvideo", "-pix_fmt", "rgba", "-"], stdout=subprocess.PIPE)
    tmp_out = args.out + ".tmp.mp4"
    enc = subprocess.Popen([FFMPEG, "-y", "-loglevel", "error", "-f", "rawvideo", "-pix_fmt", "rgba", "-s", f"{fv.W}x{fv.H}",
                            "-r", str(FPS), "-i", "-", "-i", mix_path, "-map", "0:v", "-map", "1:a",
                            "-c:v", "libx264", "-preset", "veryfast", "-crf", str(args.crf), "-pix_fmt", "yuv420p",
                            "-c:a", "aac", "-b:a", "160k", "-shortest", "-movflags", "+faststart", tmp_out], stdin=subprocess.PIPE)
    size = fv.W * fv.H * 4
    n = 0
    try:
        while n < total:
            buf = dec.stdout.read(size)
            if len(buf) < size:
                break
            frame = np.frombuffer(buf, dtype=np.uint8).reshape(fv.H, fv.W, 4)
            out = painter.render(n / FPS, frame)
            enc.stdin.write(out.tobytes())
            if n in snaps:
                from PIL import Image
                Image.fromarray(out).convert("RGB").save(os.path.join(args.snapdir, f"snap_{n / FPS:05.2f}.png"))
            n += 1
            if n % 30 == 0:
                print(f"PROGRESS {n / total:.3f}", flush=True)
    finally:
        enc.stdin.close()
        code = enc.wait()
        dec.kill()
    if code != 0 or n == 0:
        raise SystemExit("falha ao codificar o vídeo")
    os.replace(tmp_out, args.out)
    print("PROGRESS 1.000", flush=True)


if __name__ == "__main__":
    main()
