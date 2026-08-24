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
        if (duration <= LongVideoThreshold)
        {
            var analysis = analyzer.AnalyzeWithReport(transcript, options);
            var selected = analysis.Clips
                .OrderByDescending(clip => clip.Score)
                .ToList();

            RefineAndScore(selected, transcript, options);
            analysis.Report.FinalCandidates = selected.Count;
            return new EditorialAnalysisResult(selected, analysis.Report);
        }

        var chunks = BuildChunks(transcript, duration);
        var candidates = new List<(ClipCandidate Clip, int Chunk)>();
        var report = new CandidateAnalysisReport { RequestedClips = options.ClipCount, TranscriptSegments = transcript.Count };
        for (var index = 0; index < chunks.Count; index++)
        {
            var chunkOptions = CopyOptions(options, Math.Clamp(options.ClipCount * 2, 5, 20));
            var chunkAnalysis = analyzer.AnalyzeWithReport(chunks[index], chunkOptions);
            Merge(report, chunkAnalysis.Report);
            foreach (var clip in chunkAnalysis.Clips)
            {
                clip.Reasons = clip.Reasons.Prepend($"bloco {index + 1} de {chunks.Count} do vídeo").Distinct().Take(8).ToList();
                candidates.Add((clip, index));
            }
        }

        var unique = new List<(ClipCandidate Clip, int Chunk)>();
        foreach (var candidate in candidates.OrderByDescending(item => item.Clip.Score))
        {
            if (unique.Any(item => TemporalOverlap(item.Clip, candidate.Clip) > .45)) { report.RejectedByOverlap++; continue; }
            unique.Add(candidate);
        }

        var selectedLong = new List<(ClipCandidate Clip, int Chunk)>();
        var populatedChunks = unique.Select(item => item.Chunk).Distinct().Count();
        var perChunk = Math.Max(1, (int)Math.Ceiling(options.ClipCount / (double)Math.Max(1, populatedChunks)));
        foreach (var candidate in unique)
        {
            if (selectedLong.Count(item => item.Chunk == candidate.Chunk) >= perChunk) continue;
            selectedLong.Add(candidate); if (selectedLong.Count == options.ClipCount) break;
        }
        foreach (var candidate in unique.Where(candidate => !selectedLong.Contains(candidate)))
        {
            selectedLong.Add(candidate); if (selectedLong.Count == options.ClipCount) break;
        }

        var result = selectedLong
            .Select(item => item.Clip)
            .OrderByDescending(clip => clip.Score)
            .ToList();

        RefineAndScore(result, transcript, options);
        report.RequestedClips = options.ClipCount;
        report.TranscriptSegments = transcript.Count;
        report.FinalCandidates = result.Count;
        report.Warnings = report.Warnings.Distinct().ToList();
        if (result.Count < options.ClipCount && !report.Warnings.Any(warning => warning.Contains("Editor completo"))) report.Warnings.Add("Você pode completar a seleção pelo Editor completo.");
        return new EditorialAnalysisResult(result, report);
    }

    private static void Merge(CandidateAnalysisReport target, CandidateAnalysisReport source)
    {
        target.RawCandidates += source.RawCandidates; target.RejectedByDuration += source.RejectedByDuration;
        target.RejectedByOverlap += source.RejectedByOverlap; target.RejectedByScore += source.RejectedByScore;
        target.RejectedByContext += source.RejectedByContext; target.RejectedByIncompleteEnding += source.RejectedByIncompleteEnding;
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
        ContentType = source.ContentType, ClipCount = clipCount, MinDuration = source.MinDuration, MaxDuration = source.MaxDuration,
        WhisperModel = source.WhisperModel, Topic = source.Topic, DeleteSourceAfterProcessing = source.DeleteSourceAfterProcessing
    };

    private static double TemporalOverlap(ClipCandidate left, ClipCandidate right) =>
        Math.Max(0, Math.Min(left.End, right.End) - Math.Max(left.Start, right.Start)) / Math.Max(1, Math.Min(left.End - left.Start, right.End - right.Start));
}
