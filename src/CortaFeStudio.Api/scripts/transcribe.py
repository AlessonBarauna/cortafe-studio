import json
import sys

def main():
    if len(sys.argv) < 4:
        raise SystemExit("uso: transcribe.py audio.wav output.json modelo")
    try:
        from faster_whisper import WhisperModel
    except ImportError:
        raise SystemExit("faster-whisper não está instalado. Execute scripts/instalar-windows.ps1")
    audio, output, model_name = sys.argv[1:4]
    model = WhisperModel(model_name, device="auto", compute_type="int8")
    segments, _ = model.transcribe(audio, language="pt", beam_size=5, vad_filter=True, word_timestamps=True)
    data = []
    for segment in segments:
        data.append({
            "start": segment.start,
            "end": segment.end,
            "text": segment.text.strip(),
            "words": [{"start": w.start, "end": w.end, "word": w.word} for w in (segment.words or [])]
        })
    with open(output, "w", encoding="utf-8") as handle:
        json.dump(data, handle, ensure_ascii=False, indent=2)

if __name__ == "__main__":
    main()
