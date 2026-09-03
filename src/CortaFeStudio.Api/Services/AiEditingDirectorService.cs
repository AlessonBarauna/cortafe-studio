using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

/// <summary>
/// Transforma sinais editoriais, sociais e visuais já calculados pelo CortaFé em
/// decisões de edição. Não depende de API externa e preserva escolhas manuais
/// quando elas já representam um modo especial de layout.
/// </summary>
public sealed class AiEditingDirectorService
{
    public ClipCandidate Direct(ClipCandidate clip, ProjectOptions options)
    {
        if (string.Equals(options.ContentType, "louvor", StringComparison.OrdinalIgnoreCase))
            return DirectWorship(clip);

        var hook = clip.SocialScore.Hook;
        var retention = clip.SocialScore.Retention;
        var impact = clip.ScoreBreakdown.Impact;
        var clarity = clip.ScoreBreakdown.Clarity;
        var sceneDensity = clip.VisualDirection.SceneDensity;
        var visualScore = clip.VisualDirection.Score;
        var duration = Math.Max(1, clip.End - clip.Start);
        var intensity = EditingIntensity(clip, hook, impact, retention);

        // Ritmo: preserva naturalidade em mensagens longas e aperta um pouco cortes
        // com hook forte e fala clara. A remoção de silêncio continua protegida pelo
        // SilenceTrimmingService (mínimo de 60 s e pausas naturais preservadas).
        clip.SilenceTrimmingEnabled = clarity >= 0 && duration >= 35;
        clip.PlaybackSpeed = intensity >= .84 && duration >= 55 && clarity >= 2 ? 1.25 : 1;

        // Legendas: o estilo impact é melhor para hooks e clímax; balanced reduz
        // fadiga visual em mensagens mais didáticas/longas.
        clip.SubtitleStyle = hook >= 72 || impact >= 8 || intensity >= .82 ? "impact" : "balanced";
        if (clip.SubtitleTrack is not null && !clip.SubtitleTrack.EditedByUser)
        {
            clip.SubtitleTrack.Style = clip.SubtitleStyle;
            clip.SubtitleTrack.RecommendedStyle = clip.SubtitleStyle;
        }

        // Transições são decididas pela combinação de densidade real de cenas e
        // energia editorial, evitando efeito dinâmico em material visualmente estático.
        clip.TransitionStyle = sceneDensity >= 6 && intensity >= .7
            ? "dynamic"
            : sceneDensity >= 2.3 || intensity >= .63
                ? "editorial"
                : "smooth";

        // O FramingService pode ter escolhido split para entrevistas. Nunca desfazemos
        // essa decisão. Em baixa cobertura visual, blur é mais seguro do que crop agressivo.
        if (clip.LayoutMode == "fill" && clip.FaceTrackingAnalyzed && !clip.VisualDirection.SubjectDetected && visualScore < 38)
            clip.LayoutMode = "blur";

        var direction = intensity >= .82 ? "alta" : intensity >= .62 ? "média" : "suave";
        clip.Reasons = clip.Reasons
            .Append($"direção de edição IA: intensidade {direction}")
            .Append($"ritmo: {(clip.PlaybackSpeed > 1 ? $"{clip.PlaybackSpeed:0.##}x" : "natural")} · legenda {clip.SubtitleStyle} · transição {clip.TransitionStyle}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();

        return clip;
    }

    public static double EditingIntensity(ClipCandidate clip) => EditingIntensity(
        clip,
        clip.SocialScore.Hook,
        clip.ScoreBreakdown.Impact,
        clip.SocialScore.Retention);

    private static double EditingIntensity(ClipCandidate clip, double hook, double impact, double retention)
    {
        var semantic = Math.Clamp(clip.Score / 100d, 0, 1);
        var hook01 = NormalizeSignal(hook);
        var retention01 = NormalizeSignal(retention);
        var impact01 = Math.Clamp(impact / 14d, 0, 1);
        var emotional = EmotionalDensity(clip.Transcript);
        return Math.Round(Math.Clamp(semantic * .32 + hook01 * .26 + retention01 * .15 + impact01 * .15 + emotional * .12, 0, 1), 3);
    }

    private static ClipCandidate DirectWorship(ClipCandidate clip)
    {
        clip.SilenceTrimmingEnabled = false;
        clip.PlaybackSpeed = 1;
        clip.TransitionStyle = clip.VisualDirection.SceneDensity >= 4 ? "editorial" : "smooth";
        clip.SubtitleStyle = "balanced";
        if (clip.SubtitleTrack is not null && !clip.SubtitleTrack.EditedByUser)
        {
            clip.SubtitleTrack.Style = "balanced";
            clip.SubtitleTrack.RecommendedStyle = "balanced";
        }
        clip.Reasons = clip.Reasons
            .Append("direção de edição IA: dinâmica musical preservada")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();
        return clip;
    }

    private static double NormalizeSignal(double value) => value > 1 ? Math.Clamp(value / 100d, 0, 1) : Math.Clamp(value, 0, 1);

    private static double EmotionalDensity(string transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript)) return 0;
        var text = transcript.ToLowerInvariant();
        var terms = new[] { "deus", "jesus", "fé", "fe", "amor", "dor", "cura", "perdão", "perdao", "promessa", "milagre", "esperança", "esperanca", "propósito", "proposito", "verdade", "medo", "coragem" };
        var matches = terms.Count(text.Contains);
        return Math.Clamp(matches / 6d, 0, 1);
    }
}
