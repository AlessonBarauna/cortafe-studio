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

        var words = clip.Transcript.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 12) return [];

        var candidates = new List<(double Relative, int Score)>();
        for (var i = 0; i < words.Length; i++)
        {
            var window = string.Join(' ', words.Skip(Math.Max(0, i - 2)).Take(5));
            var score = ImpactScore(window);
            if (score < 5) continue;
            var relative = i / (double)Math.Max(1, words.Length - 1) * duration;
            if (relative <= duration - 2) candidates.Add((relative, score));
        }

        var selected = new List<PunchInMoment>();
        var opening = candidates.Where(c => c.Relative <= 8).OrderByDescending(c => c.Score).FirstOrDefault();
        if (opening.Score > 0) AddMoment(selected, opening.Relative, opening.Score >= 12 ? 1.065 : 1.05, duration);

        foreach (var candidate in candidates.OrderByDescending(c => c.Score).ThenBy(c => c.Relative))
        {
            if (selected.Count >= 3) break;
            if (selected.Any(existing => Math.Abs(existing.Start - candidate.Relative) < 8)) continue;
            AddMoment(selected, candidate.Relative, candidate.Score >= 12 ? 1.06 : 1.045, duration);
        }

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

    private static void AddMoment(List<PunchInMoment> target, double start, double scale, double duration)
    {
        var safeStart = Math.Clamp(start + .08, .25, Math.Max(.25, duration - 1.7));
        var end = Math.Min(duration - .15, safeStart + 1.55);
        if (end - safeStart < .8) return;
        target.Add(new PunchInMoment(safeStart, end, scale));
    }
}
