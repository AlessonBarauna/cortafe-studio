using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

public sealed class ClipVariantService
{
    private static readonly double[] Offsets = [0, 1.2, -1.2, 2.2, -2.2];

    public IReadOnlyList<ClipVariant> Generate(ClipCandidate clip, IReadOnlyList<TranscriptSegment> transcript, ProjectOptions options, int count)
    {
        var duration = clip.End - clip.Start; var maximumEnd = transcript.Count == 0 ? clip.End + 5 : transcript.Max(item => item.End);
        var variants = Offsets.Take(Math.Clamp(count, 1, 5)).Select((offset, index) =>
        {
            var start = Math.Clamp(clip.Start + offset, 0, Math.Max(0, maximumEnd - duration));
            var end = Math.Min(maximumEnd, start + duration);
            var hook = HookAt(transcript, start, clip.HookSentence);
            var candidate = CopyForScore(clip, start, end, hook);
            var social = SocialScoreService.Calculate(candidate, options).Potential;
            var semanticOpening = HookBoundaryRefiner.HookScore(hook);
            var shiftPenalty = Math.Abs(offset) * 1.2;
            var score = Math.Round(Math.Clamp(social * .82 + semanticOpening * .18 - shiftPenalty, 0, 100), 1);
            return new ClipVariant { Label = ((char)('A' + index)).ToString(), Start = Math.Round(start, 2), End = Math.Round(end, 2), HookSentence = hook, PunchInIntensity = index % 2 == 0 ? 1 : 1.04, SubtitleDensity = index % 3 == 2 ? "compact" : "balanced", SocialScore = social, VariantScore = score };
        }).ToList();
        var winner = variants.OrderByDescending(item => item.VariantScore).ThenBy(item => Math.Abs(item.Start - clip.Start)).First(); winner.Winner = true;
        return variants;
    }

    public ClipVariant ApplyWinner(ClipCandidate clip, IReadOnlyList<ClipVariant> variants)
    {
        var winner = variants.FirstOrDefault(item => item.Winner) ?? variants.OrderByDescending(item => item.VariantScore).First();
        clip.Start = winner.Start; clip.End = winner.End; clip.HookSentence = winner.HookSentence;
        clip.Variants = variants.ToList(); clip.WinningVariantId = winner.Id; return winner;
    }

    private static string HookAt(IReadOnlyList<TranscriptSegment> transcript, double start, string fallback)
    {
        var words = transcript.SelectMany(segment => segment.Words.Count > 0 ? segment.Words : [new TranscriptWord { Start = segment.Start, End = segment.End, Word = segment.Text }])
            .Where(word => word.End >= start && word.Start <= start + 4).OrderBy(word => word.Start).Select(word => word.Word.Trim()).Where(word => word.Length > 0).Take(10);
        var hook = string.Join(' ', words); return string.IsNullOrWhiteSpace(hook) ? fallback : hook;
    }

    private static ClipCandidate CopyForScore(ClipCandidate source, double start, double end, string hook) => new()
    {
        Start = start, End = end, HookSentence = hook, Score = source.Score, ScoreBreakdown = source.ScoreBreakdown,
        EditorialProfile = source.EditorialProfile, Transcript = source.Transcript
    };
}
