using System.Globalization;
using System.Text;
using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

public static class EditorialDiversityService
{
    private static readonly HashSet<string> StopWords = ["para", "com", "que", "uma", "por", "dos", "das", "isso", "voce", "mais", "como", "nao", "sim", "ele", "ela", "seu", "sua", "aqui", "agora", "entao", "porque", "tambem", "muito", "gente", "sobre", "quando"];

    public static List<ClipCandidate> Select(IEnumerable<ClipCandidate> source, int count, double totalDuration, CandidateAnalysisReport? report = null)
    {
        var remaining = source.OrderByDescending(clip => clip.Score).ToList();
        var selected = new List<ClipCandidate>();
        while (remaining.Count > 0 && selected.Count < count)
        {
            var ranked = remaining.Select(candidate =>
            {
                var semanticConflict = selected.Count == 0 ? 0 : selected.Max(existing => Similarity(existing.Transcript, candidate.Transcript));
                var overlap = selected.Count == 0 ? 0 : selected.Max(existing => Overlap(existing, candidate));
                var nearest = selected.Count == 0 ? totalDuration : selected.Min(existing => Math.Abs(Middle(existing) - Middle(candidate)));
                var coverageBonus = totalDuration <= 0 ? 0 : Math.Min(8, nearest / totalDuration * 32);
                var diversity = Math.Clamp(100 - semanticConflict * 100 - overlap * 100, 0, 100);
                return new { Candidate = candidate, SemanticConflict = semanticConflict, Overlap = overlap, Diversity = diversity, Rank = candidate.Score + coverageBonus - semanticConflict * 38 - overlap * 90 };
            }).OrderByDescending(item => item.Rank).ToList();

            var best = ranked.FirstOrDefault(item => item.Overlap <= .24 && item.SemanticConflict <= .70) ?? ranked.FirstOrDefault(item => item.Overlap <= .24);
            if (best is null) break;
            best.Candidate.DiversityScore = Math.Round(best.Diversity, 1);
            best.Candidate.DiversityTopic = Topic(best.Candidate.Transcript);
            best.Candidate.Reasons = best.Candidate.Reasons.Prepend(selected.Count == 0 ? "melhor momento geral" : "amplia a diversidade de temas").Distinct().Take(8).ToList();
            selected.Add(best.Candidate);
            foreach (var rejected in ranked.Where(item => item.Candidate != best.Candidate && (item.Overlap > .24 || item.SemanticConflict > .70)).ToList())
            {
                remaining.Remove(rejected.Candidate);
                if (report is not null) { if (rejected.Overlap > .24) report.RejectedByOverlap++; else report.RejectedByContext++; }
            }
            remaining.Remove(best.Candidate);
        }
        return selected.OrderByDescending(clip => clip.Score).ToList();
    }

    public static double Similarity(string first, string second)
    {
        var a = Tokens(first); var b = Tokens(second);
        if (a.Count == 0 || b.Count == 0) return 0;
        var intersection = a.Intersect(b).Count();
        return intersection / (double)a.Union(b).Count();
    }

    public static string Topic(string text) => string.Join(" · ", Tokens(text).GroupBy(token => token).OrderByDescending(group => group.Count()).ThenByDescending(group => group.Key.Length).Take(3).Select(group => group.Key));
    private static double Middle(ClipCandidate clip) => (clip.Start + clip.End) / 2;
    private static double Overlap(ClipCandidate a, ClipCandidate b) => Math.Max(0, Math.Min(a.End, b.End) - Math.Max(a.Start, b.Start)) / Math.Max(1, Math.Min(a.End - a.Start, b.End - b.Start));
    private static List<string> Tokens(string value) => Fold(value).Split([' ', ',', '.', '?', '!', ':', ';', '—', '-', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Select(token => token.Trim()).Where(token => token.Length > 3 && !StopWords.Contains(token)).ToList();
    private static string Fold(string value) { var normalized = value.Normalize(NormalizationForm.FormD); var builder = new StringBuilder(); foreach (var character in normalized) if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark) builder.Append(char.ToLowerInvariant(character)); return builder.ToString().Normalize(NormalizationForm.FormC); }
}
