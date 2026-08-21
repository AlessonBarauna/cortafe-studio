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

    public static IReadOnlyList<PunchInMoment> Plan(
        ClipCandidate clip,
        IReadOnlyList<TranscriptSegment> transcript)
    {
        if (clip.EditorialProfile == "louvor") return [];

        var duration = clip.End - clip.Start;
        if (duration < 18) return [];

        var candidates = transcript
            .Where(segment => segment.End >= clip.Start && segment.Start <= clip.End)
            .Select(segment => new
            {
                Segment = segment,
                Relative = Math.Max(0, segment.Start - clip.Start),
                Score = ImpactScore(segment.Text)
            })
            .Where(item => item.Score >= 5 && item.Relative <= duration - 2)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Relative)
            .ToList();

        var selected = new List<PunchInMoment>();

        var opening = candidates
            .Where(item => item.Relative <= 8)
            .OrderByDescending(item => item.Score)
            .FirstOrDefault();

        if (opening is not null)
            AddMoment(selected, opening.Relative, opening.Score >= 12 ? 1.065 : 1.05, duration);

        foreach (var candidate in candidates)
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
