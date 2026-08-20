using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

public sealed class EditorialCandidateSelector
{
    public List<ClipCandidate> Select(
        List<ClipCandidate> pool,
        ProjectOptions options)
    {
        var candidates = FilterByTopic(pool, options);

        var targetPool = Math.Clamp(
            options.ClipCount * 4,
            options.ClipCount,
            40);

        var diverse = new List<ClipCandidate>();

        foreach (var candidate in candidates
                     .OrderByDescending(clip => clip.Score)
                     .Take(targetPool * 3))
        {
            var conflicts = diverse.Any(existing =>
                Overlap(existing, candidate) > .24 ||
                Similar(
                    existing.Transcript,
                    candidate.Transcript) > .72);

            if (conflicts)
                continue;

            diverse.Add(candidate);

            if (diverse.Count >= targetPool)
                break;
        }

        return diverse
            .Take(options.ClipCount)
            .OrderByDescending(clip => clip.Score)
            .ToList();
    }

    public List<ClipCandidate> SelectWorship(
        List<ClipCandidate> pool,
        int count)
    {
        var result = new List<ClipCandidate>();

        foreach (var candidate in pool
                     .OrderByDescending(clip => clip.Score))
        {
            if (result.Any(existing =>
                    Overlap(existing, candidate) > .22))
            {
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
        ProjectOptions options)
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

        return focused.Count > 0
            ? focused
            : pool;
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

    private static double Similar(
        string first,
        string second)
    {
        var firstTokens =
            Tokenize(first).ToHashSet();

        var secondTokens =
            Tokenize(second).ToHashSet();

        if (firstTokens.Count == 0 ||
            secondTokens.Count == 0)
        {
            return 0;
        }

        return firstTokens
                   .Intersect(secondTokens)
                   .Count() /
               (double)firstTokens
                   .Union(secondTokens)
                   .Count();
    }

    private static List<string> Tokenize(string value)
    {
        return value
            .ToLowerInvariant()
            .Split(
                [' ', ',', '.', '?', '!', ':', ';', '—', '-'],
                StringSplitOptions.RemoveEmptyEntries)
            .ToList();
    }
}