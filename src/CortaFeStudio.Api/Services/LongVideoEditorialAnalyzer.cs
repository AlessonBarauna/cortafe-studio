using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

public sealed class LongVideoEditorialAnalyzer(EditorialAnalyzer analyzer)
{
    public const double LongVideoThreshold = 30 * 60;
    public const double ChunkDuration = 20 * 60;
    public const double ChunkOverlap = 60;

    public List<ClipCandidate> Analyze(List<TranscriptSegment> transcript, ProjectOptions options) => AnalyzeWithReport(transcript, options).Clips;

    public EditorialAnalysisResult AnalyzeWithReport(List<TranscriptSegment> transcript, ProjectOptions options)
    {
        var duration = transcript.Count == 0 ? 0 : transcript.Max(segment => segment.End);
        var intelligence = EditorialIntelligencePipeline.BuildMap(transcript, options);

        if (duration <= LongVideoThreshold)
        {
            var analysis = analyzer.AnalyzeWithReport(transcript, options);
            var selected = analysis.Clips
                .OrderByDescending(clip => clip.Score)
                .ToList();

            RefineAndScore(selected, transcript, options);
            intelligence = EditorialIntelligencePipeline.EnrichCandidates(intelligence, selected, options);
            selected = selected.OrderByDescending(clip => clip.Score).ToList();
            analysis.Report.FinalCandidates = selected.Count;
            AddIntelligenceWarning(analysis.Report, intelligence);
            EditorialIntelligencePipeline.PersistReadableReport(FilterSeriesForSelection(intelligence, selected));
            return new EditorialAnalysisResult(selected, analysis.Report);
        }

        var chunks = EditorialIntelligencePipeline.BuildTopicChunks(
            transcript,
            intelligence.Topics,
            ChunkDuration,
            ChunkOverlap);

        var candidates = new List<(ClipCandidate Clip, int Chunk)>();
        var report = new CandidateAnalysisReport { RequestedClips = options.ClipCount, TranscriptSegments = transcript.Count };
        for (var index = 0; index < chunks.Count; index++)
        {
            var chunkOptions = CopyOptions(options, Math.Clamp(options.ClipCount * 2, 5, 20));
            var chunkAnalysis = analyzer.AnalyzeWithReport(chunks[index], chunkOptions);
            Merge(report, chunkAnalysis.Report);
            var topic = TopicForChunk(chunks[index], intelligence.Topics);
            foreach (var clip in chunkAnalysis.Clips)
            {
                var blockReason = string.IsNullOrWhiteSpace(topic)
                    ? $"bloco {index + 1} de {chunks.Count} do vídeo"
                    : $"tema: {topic}";
                clip.Reasons = clip.Reasons.Prepend(blockReason).Distinct().Take(8).ToList();
                if (!string.IsNullOrWhiteSpace(topic)) clip.DiversityTopic = topic;
                candidates.Add((clip, index));
            }
        }

        var unique = new List<(ClipCandidate Clip, int Chunk)>();
        foreach (var candidate in candidates.OrderByDescending(item => item.Clip.Score))
        {
            if (unique.Any(item => TemporalOverlap(item.Clip, candidate.Clip) > .45))
            {
                report.RejectedByOverlap++;
                continue;
            }
            unique.Add(candidate);
        }

        var semanticPool = unique.Select(item => item.Clip).ToList();
        intelligence = EditorialIntelligencePipeline.EnrichCandidates(intelligence, semanticPool, options);

        var result = EditorialDiversityService.Select(semanticPool, options.ClipCount, duration, report);
        RefineAndScore(result, transcript, options);
        result = result.OrderByDescending(clip => clip.Score).ToList();

        intelligence = FilterSeriesForSelection(intelligence, result);
        EditorialIntelligencePipeline.MarkSeries(result, intelligence.Series);
        EditorialIntelligencePipeline.PersistReadableReport(intelligence);

        report.RequestedClips = options.ClipCount;
        report.TranscriptSegments = transcript.Count;
        report.FinalCandidates = result.Count;
        report.Warnings = report.Warnings.Distinct().ToList();
        AddIntelligenceWarning(report, intelligence);
        if (result.Count < options.ClipCount && !report.Warnings.Any(warning => warning.Contains("Editor completo")))
            report.Warnings.Add("Você pode completar a seleção pelo Editor completo.");
        return new EditorialAnalysisResult(result, report);
    }

    private static EditorialIntelligenceResult FilterSeriesForSelection(
        EditorialIntelligenceResult intelligence,
        IReadOnlyCollection<ClipCandidate> selected)
    {
        var ids = selected.Select(clip => clip.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        intelligence.Evaluations = intelligence.Evaluations.Where(evaluation => ids.Contains(evaluation.ClipId)).ToList();
        intelligence.Series = intelligence.Series
            .Select(series => new EditorialSeries
            {
                Id = series.Id,
                Title = series.Title,
                Summary = series.Summary,
                Score = series.Score,
                ClipIds = series.ClipIds.Where(ids.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            })
            .Where(series => series.ClipIds.Count >= 2)
            .OrderByDescending(series => series.Score)
            .ToList();
        return intelligence;
    }

    private static string? TopicForChunk(
        IReadOnlyList<TranscriptSegment> chunk,
        IReadOnlyList<EditorialTopic> topics)
    {
        if (chunk.Count == 0 || topics.Count == 0) return null;
        var start = chunk.Min(segment => segment.Start);
        var end = chunk.Max(segment => segment.End);
        return topics
            .Where(topic => topic.End >= start && topic.Start <= end)
            .OrderByDescending(topic => Math.Max(0, Math.Min(end, topic.End) - Math.Max(start, topic.Start)))
            .Select(topic => topic.Title)
            .FirstOrDefault();
    }

    private static void AddIntelligenceWarning(CandidateAnalysisReport report, EditorialIntelligenceResult intelligence)
    {
        if (intelligence.Topics.Count > 0)
            report.Warnings.Add($"Direção editorial: {intelligence.Topics.Count} temas mapeados com {intelligence.Provider}.");
        if (intelligence.Series.Count > 0)
            report.Warnings.Add($"{intelligence.Series.Count} séries temáticas sugeridas para publicação sequencial.");
    }

    private static void Merge(CandidateAnalysisReport target, CandidateAnalysisReport source)
    {
        target.RawCandidates += source.RawCandidates;
        target.RejectedByDuration += source.RejectedByDuration;
        target.RejectedByOverlap += source.RejectedByOverlap;
        target.RejectedByScore += source.RejectedByScore;
        target.RejectedByContext += source.RejectedByContext;
        target.RejectedByIncompleteEnding += source.RejectedByIncompleteEnding;
        target.Warnings.AddRange(source.Warnings.Where(warning => !warning.Contains("Editor completo")));
    }

    public static List<List<TranscriptSegment>> BuildChunks(List<TranscriptSegment> transcript, double duration)
    {
        var chunks = new List<List<TranscriptSegment>>();
        for (double start = 0; start < duration; start += ChunkDuration - ChunkOverlap)
        {
            var end = Math.Min(duration, start + ChunkDuration);
            var segments = transcript.Where(segment => segment.End >= start && segment.Start <= end).ToList();
            if (segments.Count > 0) chunks.Add(segments);
            if (end >= duration) break;
        }
        return chunks;
    }

    private static void RefineAndScore(
        IEnumerable<ClipCandidate> clips,
        IReadOnlyList<TranscriptSegment> transcript,
        ProjectOptions options)
    {
        var materialized = clips as IList<ClipCandidate> ?? clips.ToList();
        foreach (var clip in materialized)
            HookBoundaryRefiner.Refine(clip, transcript, options);

        SocialScoreService.Apply(materialized, options);
    }

    private static ProjectOptions CopyOptions(ProjectOptions source, int clipCount) => new()
    {
        ContentType = source.ContentType,
        ClipCount = clipCount,
        MinDuration = source.MinDuration,
        MaxDuration = source.MaxDuration,
        WhisperModel = source.WhisperModel,
        Topic = source.Topic,
        DeleteSourceAfterProcessing = source.DeleteSourceAfterProcessing
    };

    private static double TemporalOverlap(ClipCandidate left, ClipCandidate right) =>
        Math.Max(0, Math.Min(left.End, right.End) - Math.Max(left.Start, right.Start)) /
        Math.Max(1, Math.Min(left.End - left.Start, right.End - right.Start));
}
