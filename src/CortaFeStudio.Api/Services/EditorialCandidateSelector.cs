using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

public sealed class EditorialCandidateSelector
{
    public List<ClipCandidate> Select(
        List<ClipCandidate> pool,
        ProjectOptions options,
        CandidateAnalysisReport? report = null)
    {
        var candidates = FilterByTopic(pool, options, report);

        var duration = candidates.Count == 0 ? 0 : candidates.Max(clip => clip.End);
        return EditorialDiversityService.Select(candidates, options.ClipCount, duration, report);
    }

    public List<ClipCandidate> SelectWorship(
        List<ClipCandidate> pool,
        int count,
        CandidateAnalysisReport? report = null)
    {
        var result = new List<ClipCandidate>();

        foreach (var candidate in pool
                     .OrderByDescending(clip => clip.Score))
        {
            if (result.Any(existing =>
                    Overlap(existing, candidate) > .22))
            {
                if (report is not null) report.RejectedByOverlap++;
                continue;
            }

            result.Add(candidate);

            if (result.Count == count)
                break;
        }

        return result
            .OrderByDescending(clip => clip.Score)
            .ToList();
    }

    private static List<ClipCandidate> FilterByTopic(
        List<ClipCandidate> pool,
        ProjectOptions options,
        CandidateAnalysisReport? report)
    {
        if (string.IsNullOrWhiteSpace(options.Topic))
            return pool;

        var focused = pool
            .Where(clip =>
                clip.Reasons.Any(reason =>
                    reason.StartsWith(
                        "relacionado ao tema",
                        StringComparison.Ordinal)))
            .ToList();

        if (focused.Count == 0) return pool;
        if (report is not null) report.RejectedByContext += pool.Count - focused.Count;
        return focused;
    }

    private static double Overlap(
        ClipCandidate first,
        ClipCandidate second)
    {
        return Math.Max(
                   0,
                   Math.Min(first.End, second.End) -
                   Math.Max(first.Start, second.Start)) /
               Math.Min(
                   first.End - first.Start,
                   second.End - second.Start);
    }

}
