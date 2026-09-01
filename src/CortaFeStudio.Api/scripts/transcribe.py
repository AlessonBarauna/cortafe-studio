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
                {"start": word.start, "end": word.end, "word": word.word, "probability": getattr(word, "probability", None)}
                for word in (segment.words or [])
                if word.word and word.word.strip()
            ]
        })
    return data


def transcribe(model, audio, use_vad, profile):
    prompts = {
        "louvor": "Letra de louvor cristão em português. Deus, Jesus, Espírito Santo, graça, adoração, presença.",
        "pregacao": "Pregação cristã em português. Deus, Jesus, Bíblia, fé, graça, propósito, igreja.",
        "podcast": "Conversa e entrevista em português brasileiro. Preserve literalmente as palavras faladas."
    }
    segments, _ = model.transcribe(
        audio,
        language="pt",
        beam_size=5,
        vad_filter=use_vad,
        vad_parameters={"min_silence_duration_ms": 420, "speech_pad_ms": 220} if use_vad else None,
        word_timestamps=True,
        condition_on_previous_text=True,
        initial_prompt=prompts.get(profile, "Fala em português brasileiro. Preserve nomes e o texto literalmente."),
        no_speech_threshold=.55,
        hallucination_silence_threshold=1.2
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
    data = transcribe(model, audio, use_vad, editorial_profile)
    if not data and use_vad:
        print("Nenhuma fala detectada com VAD; repetindo em modo contínuo.", file=sys.stderr)
        data = transcribe(model, audio, False, editorial_profile)
    with open(output, "w", encoding="utf-8") as handle:
        json.dump(data, handle, ensure_ascii=False, indent=2)

if __name__ == "__main__":
    main()
