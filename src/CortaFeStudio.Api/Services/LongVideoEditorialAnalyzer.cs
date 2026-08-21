using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

public sealed class LongVideoEditorialAnalyzer(EditorialAnalyzer analyzer)
{
    public const double LongVideoThreshold = 30 * 60;
    public const double ChunkDuration = 20 * 60;
    public const double ChunkOverlap = 60;

    public List<ClipCandidate> Analyze(List<TranscriptSegment> transcript, ProjectOptions options)
    {
        var duration = transcript.Count == 0 ? 0 : transcript.Max(segment => segment.End);
        if (duration <= LongVideoThreshold)
        {
            var selected = analyzer.Analyze(transcript, options)
                .OrderByDescending(clip => clip.Score)
                .ToList();

            RefineHooks(selected, transcript, options);
            return selected;
        }

        var chunks = BuildChunks(transcript, duration);
        var candidates = new List<(ClipCandidate Clip, int Chunk)>();
        for (var index = 0; index < chunks.Count; index++)
        {
            var chunkOptions = CopyOptions(options, Math.Clamp(options.ClipCount * 2, 5, 20));
            foreach (var clip in analyzer.Analyze(chunks[index], chunkOptions))
            {
                clip.Reasons = clip.Reasons.Prepend($"bloco {index + 1} de {chunks.Count} do vídeo").Distinct().Take(5).ToList();
                candidates.Add((clip, index));
            }
        }

        var unique = new List<(ClipCandidate Clip, int Chunk)>();
        foreach (var candidate in candidates.OrderByDescending(item => item.Clip.Score))
        {
            if (unique.Any(item => TemporalOverlap(item.Clip, candidate.Clip) > .45)) continue;
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

        RefineHooks(result, transcript, options);
        return result;
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

    private static void RefineHooks(
        IEnumerable<ClipCandidate> clips,
        IReadOnlyList<TranscriptSegment> transcript,
        ProjectOptions options)
    {
        foreach (var clip in clips)
            HookBoundaryRefiner.Refine(clip, transcript, options);
    }

    private static ProjectOptions CopyOptions(ProjectOptions source, int clipCount) => new()
    {
        ContentType = source.ContentType, ClipCount = clipCount, MinDuration = source.MinDuration, MaxDuration = source.MaxDuration,
        WhisperModel = source.WhisperModel, Topic = source.Topic, DeleteSourceAfterProcessing = source.DeleteSourceAfterProcessing
    };

    private static double TemporalOverlap(ClipCandidate left, ClipCandidate right) =>
        Math.Max(0, Math.Min(left.End, right.End) - Math.Max(left.Start, right.Start)) / Math.Max(1, Math.Min(left.End - left.Start, right.End - right.Start));
}
