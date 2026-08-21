using System.Globalization;
using System.Text;
using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

public static class HookBoundaryRefiner
{
    private static readonly string[] StrongOpeners =
    [
        "a verdade é", "sabe por que", "o problema é", "presta atenção",
        "você precisa", "você já", "imagina", "olha isso", "deixa eu te falar",
        "eu vou te dizer", "o que ninguém", "não é", "quando você"
    ];

    private static readonly string[] WeakOpeners =
    [
        "então", "bom", "bem", "agora", "gente", "né", "tá", "tipo",
        "como eu disse", "como eu falei", "vamos continuar", "voltando"
    ];

    public static void Refine(
        ClipCandidate clip,
        IReadOnlyList<TranscriptSegment> segments,
        ProjectOptions options)
    {
        if (clip.EditorialProfile == "louvor") return;

        RefineStart(clip, segments, options);
        RefineEnd(clip, segments, options);
        RefreshTranscript(clip, segments);
    }

    public static double HookScore(string text)
    {
        var value = Fold(Clean(text));
        if (string.IsNullOrWhiteSpace(value)) return 0;

        var score = 0d;
        if (value.Contains('?')) score += 8;
        if (StrongOpeners.Any(value.StartsWith)) score += 18;
        if (StrongOpeners.Any(opener => value.Contains(opener))) score += 7;
        if (WeakOpeners.Any(value.StartsWith)) score -= 12;

        var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length is >= 4 and <= 18) score += 4;
        if (value.Contains(" mas ") || value.Contains(" porque ") || value.Contains(" nunca ") || value.Contains(" sempre ")) score += 4;
        return score;
    }

    private static void RefineStart(
        ClipCandidate clip,
        IReadOnlyList<TranscriptSegment> segments,
        ProjectOptions options)
    {
        var available = segments
            .Where(segment => segment.Start >= clip.Start - .2 && segment.Start <= clip.Start + 8)
            .OrderBy(segment => segment.Start)
            .Take(6)
            .ToList();

        if (available.Count < 2) return;

        var baseline = HookScore(available[0].Text);
        var best = available[0];
        var bestScore = baseline;

        foreach (var segment in available.Skip(1))
        {
            if (segment.Start - clip.Start > 7) break;
            var durationAfterCut = clip.End - segment.Start;
            if (durationAfterCut < Math.Max(45, options.MinDuration * .88)) continue;

            var score = HookScore(segment.Text);
            if (score > bestScore)
            {
                best = segment;
                bestScore = score;
            }
        }

        if (best.Start <= clip.Start + .35 || bestScore < baseline + 8) return;

        clip.Start = Math.Max(0, best.Start - .10);
        clip.HookSentence = Clean(best.Text);
        clip.Reasons = clip.Reasons
            .Prepend("abertura ajustada para um gancho mais forte")
            .Distinct()
            .Take(5)
            .ToList();
    }

    private static void RefineEnd(
        ClipCandidate clip,
        IReadOnlyList<TranscriptSegment> segments,
        ProjectOptions options)
    {
        var current = segments
            .Where(segment =>
                segment.End >= clip.End - 1.2 &&
                segment.End <= clip.End + .3 &&
                segment.Start <= clip.End)
            .OrderBy(segment => segment.End)
            .LastOrDefault();

        if (current is null || EndsThought(current.Text)) return;

        var maxEnd = clip.Start + options.MaxDuration + 3;
        var extension = segments
            .Where(segment => segment.Start >= clip.End - .2 && segment.End <= maxEnd)
            .OrderBy(segment => segment.End)
            .FirstOrDefault(segment => EndsThought(segment.Text));

        if (extension is null || extension.End <= clip.End + .15) return;

        clip.End = extension.End + .18;
        clip.Reasons = clip.Reasons
            .Append("final preservado até concluir a ideia")
            .Distinct()
            .Take(5)
            .ToList();
    }

    private static void RefreshTranscript(
        ClipCandidate clip,
        IReadOnlyList<TranscriptSegment> segments)
    {
        var text = string.Join(
            ' ',
            segments
                .Where(segment => segment.End >= clip.Start && segment.Start <= clip.End)
                .Select(segment => Clean(segment.Text))
                .Where(value => value.Length > 0));

        if (!string.IsNullOrWhiteSpace(text)) clip.Transcript = text;
    }

    private static bool EndsThought(string value) =>
        value.TrimEnd().EndsWith('.') ||
        value.TrimEnd().EndsWith('?') ||
        value.TrimEnd().EndsWith('!');

    private static string Clean(string value) =>
        string.Join(' ', value.Replace("\n", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static string Fold(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(char.ToLowerInvariant(character));
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
