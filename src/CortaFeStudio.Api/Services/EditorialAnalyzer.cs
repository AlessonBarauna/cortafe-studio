using System.Globalization;
using System.Text;
using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

public sealed class EditorialAnalyzer(
    EditorialLearningService learning,
    EditorialScoringService scoring,
    EditorialCandidateSelector selector)
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
        ProjectOptions options) => AnalyzeWithReport(source, options).Clips;

    public EditorialAnalysisResult AnalyzeWithReport(
        List<TranscriptSegment> source,
        ProjectOptions options)
    {
        var segments = Normalize(source);
        var report = new CandidateAnalysisReport { RequestedClips = options.ClipCount, TranscriptSegments = segments.Count };

        if (options.ContentType == "louvor")
        {
            var worship = AnalyzeWorship(segments, options, report);
            CompleteReport(report, worship.Count, true);
            return new EditorialAnalysisResult(worship, report);
        }

        var pool = new List<ClipCandidate>();

        for (var anchor = 0; anchor < segments.Count; anchor++)
        {
            var opening = Clean(segments[anchor].Text);

            if (opening.Length < 8)
            {
                report.RejectedByContext++;
                continue;
            }

            var startIndex =
                FindNaturalStart(segments, anchor);

            var parts =
                BuildWindow(segments, startIndex, options);

            if (parts.Count < 3)
            {
                report.RejectedByIncompleteEnding++;
                continue;
            }

            report.RawCandidates++;

            var duration =
                parts[^1].End - parts[0].Start;

            if (duration < options.MinDuration ||
                duration > options.MaxDuration + 3)
            {
                report.RejectedByDuration++;
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
                .Take(8)
                .ToList();

            if (clip.Score >= 45) pool.Add(clip);
            else report.RejectedByScore++;
        }

        var selected =
            selector.Select(pool, options, report);

        var result = RefineWordBoundaries(
            selected,
            segments,
            options);
        CompleteReport(report, result.Count, false);
        return new EditorialAnalysisResult(result, report);
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

    private List<ClipCandidate> AnalyzeWorship(
        List<TranscriptSegment> segments,
        ProjectOptions options,
        CandidateAnalysisReport report)
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

            if (parts.Count < 3)
            {
                report.RejectedByIncompleteEnding++;
                continue;
            }
            report.RawCandidates++;
            if (parts[^1].End - parts[0].Start < options.MinDuration) { report.RejectedByDuration++; continue; }

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
                    Title = ShortFormMetadataService.GenerateTitleSuggestions(new ClipCandidate { Transcript = text }, "louvor").FirstOrDefault() ?? "Adoração que permanece",
                    CoverText = ShortFormMetadataService.NormalizeCoverText(text),
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
            selector.SelectWorship(
                pool,
                options.ClipCount,
                report),
            segments,
            options);
    }

    private static void CompleteReport(CandidateAnalysisReport report, int finalCount, bool worship)
    {
        report.FinalCandidates = finalCount;
        if (report.TranscriptSegments < 3) report.Warnings.Add("Poucos segmentos foram reconhecidos na transcrição.");
        if (report.RawCandidates == 0) report.Warnings.Add(worship ? "Não houve densidade vocal suficiente para formar trechos de louvor." : "Nenhuma janela completa atingiu os critérios editoriais.");
        if (report.RejectedByDuration > 0) report.Warnings.Add("Alguns trechos não atingiram a duração automática de 60–75 segundos.");
        if (report.RejectedByOverlap > 0) report.Warnings.Add("Trechos muito sobrepostos foram removidos para evitar cortes repetidos.");
        if (finalCount < report.RequestedClips) report.Warnings.Add("Você pode completar a seleção pelo Editor completo.");
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

public sealed record EditorialAnalysisResult(List<ClipCandidate> Clips, CandidateAnalysisReport Report);
