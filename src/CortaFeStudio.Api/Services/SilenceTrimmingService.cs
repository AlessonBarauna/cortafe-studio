using System.Globalization;
using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

public sealed class SilenceTrimmingService
{
    public const double MinimumFinalDuration = 60;
    public const double MinimumPauseToReduce = 1.2;
    public const double PreservedPause = .7;

    public SilenceTrimPlan Plan(ClipCandidate clip, IReadOnlyList<TranscriptSegment> transcript)
    {
        var original = Math.Max(0, clip.End - clip.Start);
        var manualCuts = NormalizeTranscriptCuts(clip, original);
        var plan = new SilenceTrimPlan
        {
            OriginalDuration = original,
            Cuts = manualCuts,
            RemovedDuration = Math.Round(manualCuts.Sum(item => item.Duration), 3)
        };
        plan.FinalDuration = Math.Round(Math.Max(0, original - plan.RemovedDuration), 3);

        if (!clip.SilenceTrimmingEnabled)
        {
            plan.Reason = manualCuts.Count > 0 ? $"{manualCuts.Count} cortes manuais do texto aplicados" : "Reducao de pausas desativada";
            return plan;
        }
        if (clip.EditorialProfile == "louvor")
        {
            plan.Reason = manualCuts.Count > 0 ? $"{manualCuts.Count} cortes manuais aplicados; dinamica musical preservada" : "Dinamica musical preservada";
            return plan;
        }

        var words = transcript.SelectMany(segment => segment.Words)
            .Where(word => word.End > clip.Start && word.Start < clip.End)
            .OrderBy(word => word.Start)
            .ToList();
        if (words.Count < 2)
        {
            plan.Reason = manualCuts.Count > 0 ? $"{manualCuts.Count} cortes manuais aplicados; timestamps insuficientes para reduzir pausas" : "Timestamps por palavra insuficientes";
            return plan;
        }

        var budget = Math.Min(8, Math.Max(0, plan.FinalDuration - MinimumFinalDuration));
        if (budget < .1)
        {
            plan.Reason = manualCuts.Count > 0 ? $"{manualCuts.Count} cortes manuais aplicados; duracao minima preservada para pausas" : "Duracao minima de 60s preservada";
            return plan;
        }

        var candidates = new List<SilenceCut>();
        AddCandidate(clip.Start, words[0].Start, .18);
        for (var index = 1; index < words.Count; index++) AddCandidate(words[index - 1].End, words[index].Start, PreservedPause);
        AddCandidate(words[^1].End, clip.End, .28);

        var automatic = new List<SilenceCut>();
        foreach (var candidate in candidates.OrderByDescending(item => item.Duration))
        {
            if (budget <= .05) break;
            var duration = Math.Min(candidate.Duration, budget);
            var proposed = new SilenceCut { Start = candidate.Start, End = candidate.Start + duration };
            if (manualCuts.Any(cut => Overlap(cut, proposed) > .01)) continue;
            automatic.Add(proposed);
            budget -= duration;
        }

        plan.Cuts = MergeCuts(manualCuts.Concat(automatic));
        plan.RemovedDuration = Math.Round(plan.Cuts.Sum(item => item.Duration), 3);
        plan.FinalDuration = Math.Round(Math.Max(0, original - plan.RemovedDuration), 3);
        plan.Reason = plan.Cuts.Count > 0
            ? $"{manualCuts.Count} cortes de texto + {automatic.Count} pausas reduzidas"
            : "Respiracoes e pausas naturais preservadas";
        return plan;

        void AddCandidate(double left, double right, double preserve)
        {
            var gap = right - left;
            if (gap < MinimumPauseToReduce) return;
            var start = left + preserve / 2;
            var end = right - preserve / 2;
            if (end - start > .05) candidates.Add(new SilenceCut { Start = start, End = end });
        }
    }

    public static string VideoPrefix(SilenceTrimPlan plan, double clipStart) => FilterPrefix(plan, clipStart, "select", "N/FRAME_RATE/TB");
    public static string AudioPrefix(SilenceTrimPlan plan, double clipStart) => FilterPrefix(plan, clipStart, "aselect", "N/SR/TB");

    public static List<TranscriptWord> AdjustWords(IEnumerable<TranscriptWord> source, SilenceTrimPlan plan)
    {
        return source.Select(word => new TranscriptWord
        {
            Word = word.Word,
            Start = AdjustTime(word.Start, plan),
            End = Math.Max(AdjustTime(word.Start, plan) + .01, AdjustTime(word.End, plan))
        }).ToList();
    }

    public static double AdjustTime(double sourceTime, SilenceTrimPlan plan) =>
        sourceTime - plan.Cuts.Where(cut => cut.End <= sourceTime).Sum(cut => cut.Duration);

    private static List<SilenceCut> NormalizeTranscriptCuts(ClipCandidate clip, double duration)
    {
        var source = clip.SubtitleTrack?.VideoCuts ?? [];
        var normalized = source
            .Where(cut => double.IsFinite(cut.Start) && double.IsFinite(cut.End))
            .Select(cut => new SilenceCut
            {
                Start = clip.Start + Math.Clamp(cut.Start, 0, duration),
                End = clip.Start + Math.Clamp(cut.End, 0, duration)
            })
            .Where(cut => cut.End - cut.Start >= .05)
            .ToList();
        return MergeCuts(normalized);
    }

    private static List<SilenceCut> MergeCuts(IEnumerable<SilenceCut> source)
    {
        var ordered = source.OrderBy(cut => cut.Start).ThenBy(cut => cut.End).ToList();
        if (ordered.Count == 0) return [];
        var merged = new List<SilenceCut> { new() { Start = ordered[0].Start, End = ordered[0].End } };
        foreach (var cut in ordered.Skip(1))
        {
            var last = merged[^1];
            if (cut.Start < last.End - .001) last.End = Math.Max(last.End, cut.End);
            else merged.Add(new SilenceCut { Start = cut.Start, End = cut.End });
        }
        return merged;
    }

    private static double Overlap(SilenceCut a, SilenceCut b) => Math.Max(0, Math.Min(a.End, b.End) - Math.Max(a.Start, b.Start));

    private static string FilterPrefix(SilenceTrimPlan plan, double clipStart, string filter, string timestamps)
    {
        if (!plan.Applied) return "";
        var ranges = plan.Cuts.Select(cut => $"between(t\\,{Number(cut.Start - clipStart)}\\,{Number(cut.End - clipStart)})");
        return $"{filter}='not({string.Join('+', ranges)})',setpts={timestamps},".Replace("setpts=N/SR/TB", "asetpts=N/SR/TB");
    }

    private static string Number(double value) => Math.Max(0, value).ToString("0.###", CultureInfo.InvariantCulture);
}