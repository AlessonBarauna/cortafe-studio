using System.Globalization;
using System.Text;
using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

public sealed class EditorialAnalyzer(
    EditorialLearningService learning,
    EditorialScoringService scoring)
{
    private static readonly string[] Spiritual =
    [
        "deus",
        "jesus",
        "fé",
        "graça",
        "coração",
        "reino",
        "justiça",
        "perdão",
        "amor",
        "cruz",
        "palavra",
        "espírito"
    ];

    public List<ClipCandidate> Analyze(
        List<TranscriptSegment> source,
        ProjectOptions options)
    {
        var segments = Normalize(source);

        if (options.ContentType == "louvor")
            return AnalyzeWorship(segments, options);

        var pool = new List<ClipCandidate>();

        for (var anchor = 0; anchor < segments.Count; anchor++)
        {
            var opening = Clean(segments[anchor].Text);

            if (opening.Length < 8)
                continue;

            var startIndex =
                FindNaturalStart(segments, anchor);

            var parts =
                BuildWindow(segments, startIndex, options);

            if (parts.Count < 3)
                continue;

            var duration =
                parts[^1].End - parts[0].Start;

            if (duration < options.MinDuration ||
                duration > options.MaxDuration + 3)
            {
                continue;
            }

            var text = string.Join(
                    " ",
                    parts.Select(segment =>
                        Clean(segment.Text)))
                .Trim();

            var clip =
                scoring.Score(parts, text, options);

            var learningScore =
                learning.Adjustment(
                    options.ContentType,
                    text,
                    duration,
                    out var learningReasons);

            clip.ScoreBreakdown.Learning =
                learningScore;

            clip.Score =
                clip.ScoreBreakdown.Total;

            clip.Reasons.AddRange(
                learningReasons);

            clip.Reasons = clip.Reasons
                .Distinct()
                .Take(5)
                .ToList();

            if (clip.Score >= 45)
                pool.Add(clip);
        }

        if (!string.IsNullOrWhiteSpace(options.Topic))
        {
            var focused = pool
                .Where(clip =>
                    clip.Reasons.Any(reason =>
                        reason.StartsWith(
                            "relacionado ao tema")))
                .ToList();

            if (focused.Count > 0)
                pool = focused;
        }

        var targetPool =
            Math.Clamp(
                options.ClipCount * 4,
                options.ClipCount,
                40);

        var diverse =
            new List<ClipCandidate>();

        foreach (var clip in pool
                     .OrderByDescending(candidate =>
                         candidate.Score)
                     .Take(targetPool * 3))
        {
            if (diverse.Any(candidate =>
                    Overlap(candidate, clip) > .24 ||
                    Similar(
                        candidate.Transcript,
                        clip.Transcript) > .72))
            {
                continue;
            }

            diverse.Add(clip);

            if (diverse.Count >= targetPool)
                break;
        }

        return RefineWordBoundaries(
            diverse
                .Take(options.ClipCount)
                .OrderByDescending(clip =>
                    clip.Score)
                .ToList(),
            segments,
            options);
    }

    private List<TranscriptSegment> BuildWindow(
        List<TranscriptSegment> segments,
        int startIndex,
        ProjectOptions options)
    {
        var parts =
            new List<TranscriptSegment>();

        var start =
            segments[startIndex].Start;

        for (var index = startIndex;
             index < segments.Count;
             index++)
        {
            if (segments[index].End - start >
                options.MaxDuration + 3)
            {
                break;
            }

            parts.Add(segments[index]);

            var elapsed =
                parts[^1].End - start;

            if (elapsed >= options.MinDuration &&
                EndsThought(parts[^1].Text) &&
                scoring.HasResolution(parts))
            {
                break;
            }
        }

        return parts;
    }

    private int FindNaturalStart(
        List<TranscriptSegment> segments,
        int anchor)
    {
        var text =
            Clean(segments[anchor].Text)
                .ToLowerInvariant();

        if (anchor > 0 &&
            (scoring.IsIncompleteOpening(text) ||
             scoring.DependsOnContext(text)))
        {
            var previous =
                Clean(segments[anchor - 1].Text)
                    .ToLowerInvariant();

            if (!scoring.IsTransitionOpening(previous) &&
                segments[anchor].Start -
                segments[anchor - 1].End < 2.5)
            {
                return anchor - 1;
            }
        }

        return anchor;
    }

    private static List<ClipCandidate> AnalyzeWorship(
        List<TranscriptSegment> segments,
        ProjectOptions options)
    {
        var usable = segments
            .Where(segment =>
                Clean(segment.Text)
                    .Replace(
                        "[música]",
                        "",
                        StringComparison.OrdinalIgnoreCase)
                    .Length > 2)
            .ToList();

        var pool =
            new List<ClipCandidate>();

        for (var index = 0;
             index < usable.Count;
             index += 2)
        {
            var parts = usable
                .Skip(index)
                .TakeWhile(segment =>
                    segment.End -
                    usable[index].Start <=
                    options.MaxDuration)
                .ToList();

            if (parts.Count < 3 ||
                parts[^1].End -
                parts[0].Start <
                options.MinDuration)
            {
                continue;
            }

            var text = string.Join(
                " ",
                parts.Select(segment =>
                    Clean(segment.Text)
                        .Replace(
                            "[música]",
                            "",
                            StringComparison.OrdinalIgnoreCase)));

            var repeated = Tokenize(text)
                .GroupBy(word => word)
                .Count(group =>
                    group.Count() >= 3);

            double score =
                55 +
                Math.Min(22, repeated * 3) +
                Spiritual.Count(
                    text.ToLowerInvariant().Contains) * 2;

            pool.Add(
                new ClipCandidate
                {
                    Start = parts[0].Start,
                    End = parts[^1].End,
                    Score = Math.Round(
                        Math.Min(96, score),
                        1),
                    Transcript = text,
                    Title = "Momento de louvor e adoração",
                    CoverText = "UM MOMENTO DE ADORAÇÃO",
                    Caption =
                        "Uma canção para renovar a fé. 🎶✨",
                    EditorialProfile = "louvor",
                    Reasons =
                    [
                        "trecho lírico contínuo",
                        repeated > 0
                            ? "possível refrão ou repetição"
                            : "boa densidade de letra"
                    ]
                });
        }

        return RefineWordBoundaries(
            SelectDiverse(
                pool,
                options.ClipCount),
            segments,
            options);
    }

    private static List<ClipCandidate> RefineWordBoundaries(
        List<ClipCandidate> clips,
        List<TranscriptSegment> segments,
        ProjectOptions options)
    {
        foreach (var clip in clips)
        {
            var words = segments
                .SelectMany(segment =>
                    segment.Words)
                .Where(word =>
                    word.End >= clip.Start &&
                    word.Start <= clip.End)
                .OrderBy(word =>
                    word.Start)
                .ToList();

            if (words.Count < 8)
                continue;

            var first = 0;

            while (
                first <
                Math.Min(4, words.Count - 1) &&
                IsOpeningFiller(words[first].Word) &&
                words[first + 1].Start -
                words[0].Start <= 1.8)
            {
                first++;
            }

            var refinedStart =
                words[first].Start;

            var refinedEnd =
                words.Last().End;

            if (refinedEnd - refinedStart <
                options.MinDuration * .85)
            {
                continue;
            }

            clip.Start =
                Math.Max(
                    0,
                    refinedStart - .12);

            clip.End =
                refinedEnd + .22;

            clip.Transcript =
                string.Join(
                    ' ',
                    words
                        .Skip(first)
                        .Select(word =>
                            word.Word.Trim())
                        .Where(word =>
                            word.Length > 0));

            clip.Reasons = clip.Reasons
                .Append(
                    "limites ajustados palavra por palavra")
                .Distinct()
                .Take(4)
                .ToList();
        }

        return clips;
    }

    private static bool IsOpeningFiller(string word)
    {
        var value = Fold(word)
            .Trim(
                ' ',
                ',',
                '.',
                '?',
                '!',
                ':',
                ';',
                '-');

        return value is
            "e" or
            "ai" or
            "entao" or
            "bom" or
            "bem" or
            "ne" or
            "ta" or
            "gente";
    }

    private static List<ClipCandidate> SelectDiverse(
        List<ClipCandidate> pool,
        int count)
    {
        var result =
            new List<ClipCandidate>();

        foreach (var candidate in pool
                     .OrderByDescending(
                         clip => clip.Score))
        {
            if (result.Any(existing =>
                    Overlap(
                        existing,
                        candidate) > .22))
            {
                continue;
            }

            result.Add(candidate);

            if (result.Count == count)
                break;
        }

        return result
            .OrderByDescending(
                candidate =>
                    candidate.Score)
            .ToList();
    }

    private static List<TranscriptSegment> Normalize(
        List<TranscriptSegment> source) =>
        source
            .Where(segment =>
                segment.End > segment.Start &&
                !string.IsNullOrWhiteSpace(
                    segment.Text))
            .OrderBy(segment =>
                segment.Start)
            .ToList();

    private static string Clean(string value) =>
        string.Join(
            ' ',
            value
                .Replace("\n", " ")
                .Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries));

    private static bool EndsThought(string value) =>
        value.TrimEnd().EndsWith('.') ||
        value.TrimEnd().EndsWith('?') ||
        value.TrimEnd().EndsWith('!');

    private static double Overlap(
        ClipCandidate first,
        ClipCandidate second) =>
        Math.Max(
            0,
            Math.Min(first.End, second.End) -
            Math.Max(first.Start, second.Start)) /
        Math.Min(
            first.End - first.Start,
            second.End - second.Start);

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

    private static List<string> Tokenize(string value) =>
        value
            .ToLower(
                CultureInfo.GetCultureInfo("pt-BR"))
            .Split(
                [' ', ',', '.', '?', '!', ':', ';', '—', '-'],
                StringSplitOptions.RemoveEmptyEntries)
            .ToList();

    private static string Fold(string value)
    {
        var normalized =
            value.Normalize(
                NormalizationForm.FormD);

        var builder =
            new StringBuilder();

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(
                    character) !=
                UnicodeCategory.NonSpacingMark)
            {
                builder.Append(
                    char.ToLowerInvariant(character));
            }
        }

        return builder
            .ToString()
            .Normalize(
                NormalizationForm.FormC);
    }
}