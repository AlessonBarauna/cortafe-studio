import json
import sys


def serialize_segments(segments):
    data = []
    for segment in segments:
        text = segment.text.strip()
        if not text:
            continue
        data.append({
            "start": segment.start,
            "end": segment.end,
            "text": text,
            "words": [
                {"start": word.start, "end": word.end, "word": word.word}
                for word in (segment.words or [])
                if word.word and word.word.strip()
            ]
        })
    return data


def transcribe(model, audio, use_vad):
    segments, _ = model.transcribe(
        audio,
        language="pt",
        beam_size=5,
        vad_filter=use_vad,
        word_timestamps=True
    )
    return serialize_segments(segments)


def main():
    if len(sys.argv) < 4:
        raise SystemExit("uso: transcribe.py audio.wav output.json modelo [perfil]")
    try:
        from faster_whisper import WhisperModel
    except ImportError:
        raise SystemExit("faster-whisper não está instalado. Execute scripts/instalar-windows.ps1")
    audio, output, model_name = sys.argv[1:4]
    editorial_profile = sys.argv[4].lower() if len(sys.argv) > 4 else ""
    model = WhisperModel(model_name, device="auto", compute_type="int8")
    use_vad = editorial_profile != "louvor"
    data = transcribe(model, audio, use_vad)
    if not data and use_vad:
        print("Nenhuma fala detectada com VAD; repetindo em modo contínuo.", file=sys.stderr)
        data = transcribe(model, audio, False)
    with open(output, "w", encoding="utf-8") as handle:
        json.dump(data, handle, ensure_ascii=False, indent=2)

if __name__ == "__main__":
    main()
