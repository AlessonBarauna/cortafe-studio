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
        var original = Math.Max(0, clip.End - clip.Start); var plan = new SilenceTrimPlan { OriginalDuration = original, FinalDuration = original };
        if (!clip.SilenceTrimmingEnabled) { plan.Reason = "Reducao de pausas desativada"; return plan; }
        if (clip.EditorialProfile == "louvor") { plan.Reason = "Dinamica musical preservada"; return plan; }
        var words = transcript.SelectMany(segment => segment.Words).Where(word => word.End > clip.Start && word.Start < clip.End).OrderBy(word => word.Start).ToList();
        if (words.Count < 2) { plan.Reason = "Timestamps por palavra insuficientes"; return plan; }
        var budget = Math.Min(8, Math.Max(0, original - MinimumFinalDuration)); if (budget < .1) { plan.Reason = "Duracao minima de 60s preservada"; return plan; }
        var candidates = new List<SilenceCut>();
        AddCandidate(clip.Start, words[0].Start, .18);
        for (var index = 1; index < words.Count; index++) AddCandidate(words[index - 1].End, words[index].Start, PreservedPause);
        AddCandidate(words[^1].End, clip.End, .28);
        foreach (var candidate in candidates.OrderByDescending(item => item.Duration))
        {
            if (budget <= .05) break; var duration = Math.Min(candidate.Duration, budget);
            plan.Cuts.Add(new SilenceCut { Start = candidate.Start, End = candidate.Start + duration }); budget -= duration;
        }
        plan.Cuts = plan.Cuts.OrderBy(item => item.Start).ToList(); plan.RemovedDuration = Math.Round(plan.Cuts.Sum(item => item.Duration), 3); plan.FinalDuration = Math.Round(original - plan.RemovedDuration, 3); plan.Reason = plan.Applied ? $"{plan.Cuts.Count} pausas longas reduzidas" : "Respiracoes e pausas naturais preservadas"; return plan;

        void AddCandidate(double left, double right, double preserve)
        {
            var gap = right - left; if (gap < MinimumPauseToReduce) return; var start = left + preserve / 2; var end = right - preserve / 2;
            if (end - start > .05) candidates.Add(new SilenceCut { Start = start, End = end });
        }
    }

    public static string VideoPrefix(SilenceTrimPlan plan, double clipStart) => FilterPrefix(plan, clipStart, "select", "N/FRAME_RATE/TB");
    public static string AudioPrefix(SilenceTrimPlan plan, double clipStart) => FilterPrefix(plan, clipStart, "aselect", "N/SR/TB");

    public static List<TranscriptWord> AdjustWords(IEnumerable<TranscriptWord> source, SilenceTrimPlan plan)
    {
        return source.Select(word => new TranscriptWord { Word = word.Word, Start = AdjustTime(word.Start, plan), End = Math.Max(AdjustTime(word.Start, plan) + .01, AdjustTime(word.End, plan)) }).ToList();
    }

    public static double AdjustTime(double sourceTime, SilenceTrimPlan plan) => sourceTime - plan.Cuts.Where(cut => cut.End <= sourceTime).Sum(cut => cut.Duration);

    private static string FilterPrefix(SilenceTrimPlan plan, double clipStart, string filter, string timestamps)
    {
        if (!plan.Applied) return "";
        var ranges = plan.Cuts.Select(cut => $"between(t\\,{Number(cut.Start - clipStart)}\\,{Number(cut.End - clipStart)})");
        return $"{filter}='not({string.Join('+', ranges)})',setpts={timestamps},".Replace("setpts=N/SR/TB", "asetpts=N/SR/TB");
    }
    private static string Number(double value) => Math.Max(0, value).ToString("0.###", CultureInfo.InvariantCulture);
}
