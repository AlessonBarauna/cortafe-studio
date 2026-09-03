using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

public sealed record PunchInMoment(double Start, double End, double Scale);

public static class PunchInPlanner
{
    private static readonly string[] ImpactTerms =
    [
        "deus", "jesus", "fé", "verdade", "propósito", "promessa", "perdão",
        "nunca", "sempre", "precisa", "atenção", "cuidado", "impossível",
        "milagre", "medo", "coragem", "porque", "mas", "sabe por que",
        "a verdade é", "presta atenção", "você precisa"
    ];

    public static IReadOnlyList<PunchInMoment> Plan(ClipCandidate clip)
    {
        if (clip.EditorialProfile == "louvor") return [];
        var duration = clip.End - clip.Start;
        if (duration < 18 || string.IsNullOrWhiteSpace(clip.Transcript)) return [];

        var intensity = AiEditingDirectorService.EditingIntensity(clip);
        var maximumMoments = intensity >= .82 ? 4 : intensity >= .62 ? 3 : 2;
        var minimumDistance = intensity >= .82 ? 5.5 : intensity >= .62 ? 7.5 : 9.5;
        var baseScale = intensity >= .82 ? 1.052 : intensity >= .62 ? 1.044 : 1.034;
        var strongScale = intensity >= .82 ? 1.078 : intensity >= .62 ? 1.064 : 1.05;

        // V2: prioriza momentos semânticos do trecho. Se o detector não encontrar
        // nada confiável, mantém o algoritmo antigo por palavras-chave como fallback.
        var editorial = EditorialMomentDetector.Detect(clip.Transcript, duration)
            .OrderByDescending(moment => MomentPriority(moment.Kind) + moment.Strength)
            .ThenBy(moment => moment.Start)
            .ToList();

        var selected = new List<PunchInMoment>();
        foreach (var moment in editorial)
        {
            if (selected.Count >= maximumMoments) break;
            if (selected.Any(existing => Math.Abs(existing.Start - moment.Start) < minimumDistance)) continue;
            var scale = moment.Kind switch
            {
                "climax" => strongScale,
                "hook" => Math.Max(baseScale, strongScale - .006),
                "scripture" => baseScale,
                "conclusion" => Math.Max(1.03, baseScale - .006),
                _ => baseScale
            };
            AddMoment(selected, moment.Start, scale, duration, intensity, MomentDuration(moment.Kind, intensity));
        }

        if (selected.Count < Math.Min(2, maximumMoments))
        {
            var words = clip.Transcript.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var candidates = new List<(double Relative, int Score)>();
            for (var i = 0; i < words.Length; i++)
            {
                var window = string.Join(' ', words.Skip(Math.Max(0, i - 2)).Take(5));
                var score = ImpactScore(window);
                if (score < 5) continue;
                var relative = i / (double)Math.Max(1, words.Length - 1) * duration;
                if (relative <= duration - 2) candidates.Add((relative, score));
            }

            var opening = candidates.Where(c => c.Relative <= 8).OrderByDescending(c => c.Score).FirstOrDefault();
            if (opening.Score > 0 && selected.All(existing => Math.Abs(existing.Start - opening.Relative) >= minimumDistance))
                AddMoment(selected, opening.Relative, opening.Score >= 12 ? strongScale : baseScale, duration, intensity);

            foreach (var candidate in candidates.OrderByDescending(c => c.Score).ThenBy(c => c.Relative))
            {
                if (selected.Count >= maximumMoments) break;
                if (selected.Any(existing => Math.Abs(existing.Start - candidate.Relative) < minimumDistance)) continue;
                AddMoment(selected, candidate.Relative, candidate.Score >= 12 ? strongScale : baseScale, duration, intensity);
            }
        }

        if (selected.Count == 0 && intensity >= .72 && duration >= 28)
            AddMoment(selected, Math.Min(6, duration * .16), baseScale, duration, intensity);

        return selected.OrderBy(moment => moment.Start).ToList();
    }

    public static int ImpactScore(string text)
    {
        var value = text.Trim().ToLowerInvariant();
        if (value.Length == 0) return 0;
        var score = ImpactTerms.Count(value.Contains) * 5;
        if (value.Contains('?')) score += 4;
        if (value.Contains('!')) score += 3;
        if (value.StartsWith("não ") || value.StartsWith("você ")) score += 2;
        return score;
    }

    private static double MomentPriority(string kind) => kind switch
    {
        "climax" => 4,
        "hook" => 3,
        "scripture" => 2,
        "conclusion" => 1,
        _ => 0
    };

    private static double MomentDuration(string kind, double intensity) => kind switch
    {
        "scripture" => intensity >= .75 ? 1.8 : 2.1,
        "conclusion" => 1.7,
        "climax" => intensity >= .82 ? 1.25 : 1.4,
        "hook" => 1.35,
        _ => intensity >= .82 ? 1.35 : intensity >= .62 ? 1.5 : 1.65
    };

    private static void AddMoment(List<PunchInMoment> target, double start, double scale, double duration, double intensity, double? requestedDuration = null)
    {
        var safeStart = Math.Clamp(start + .08, .25, Math.Max(.25, duration - 1.7));
        var momentDuration = requestedDuration ?? (intensity >= .82 ? 1.35 : intensity >= .62 ? 1.5 : 1.65);
        var end = Math.Min(duration - .15, safeStart + momentDuration);
        if (end - safeStart < .8) return;
        target.Add(new PunchInMoment(safeStart, end, scale));
    }
}
