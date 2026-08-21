using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

public static class SocialScoreService
{
    public static SocialScoreBreakdown Calculate(ClipCandidate clip, ProjectOptions options)
    {
        if (clip.EditorialProfile == "louvor")
            return WorshipScore(clip);

        var breakdown = clip.ScoreBreakdown ?? new EditorialScoreBreakdown();
        var hookSignal = HookBoundaryRefiner.HookScore(clip.HookSentence);

        var hook = Normalize(
            45 +
            hookSignal * 1.30 +
            breakdown.Hook * 1.50 +
            breakdown.OpeningAdjustment +
            breakdown.ContextPenalty * .45);

        var duration = Math.Max(0, clip.End - clip.Start);
        var idealDuration = (options.MinDuration + options.MaxDuration) / 2d;
        var durationBonus = Math.Clamp(10 - Math.Abs(duration - idealDuration) * .8, -10, 10);

        var retention = Normalize(
            50 +
            breakdown.Structure * 1.45 +
            breakdown.Contrast * 1.30 +
            breakdown.ProfileRelevance * .75 +
            breakdown.LengthAdjustment * .90 +
            breakdown.ContextPenalty * .85 +
            breakdown.Learning * .50 +
            durationBonus);

        var conclusion = Normalize(
            48 +
            breakdown.Completion * 2.0 +
            breakdown.Conclusion * 1.8 +
            Math.Max(0, breakdown.Structure) * .70);

        var potential = Normalize(
            hook * .35 +
            retention * .35 +
            conclusion * .20 +
            Math.Clamp(clip.Score, 0, 100) * .10);

        return new SocialScoreBreakdown
        {
            Hook = hook,
            Retention = retention,
            Conclusion = conclusion,
            Potential = potential
        };
    }

    public static void Apply(IEnumerable<ClipCandidate> clips, ProjectOptions options)
    {
        foreach (var clip in clips)
            clip.SocialScore = Calculate(clip, options);
    }

    private static SocialScoreBreakdown WorshipScore(ClipCandidate clip)
    {
        var editorial = Math.Clamp(clip.Score, 0, 100);
        var hook = Normalize(editorial * .82 + 8);
        var retention = Normalize(editorial * .94 + 5);
        var conclusion = Normalize(editorial * .88 + 6);
        var potential = Normalize(hook * .25 + retention * .45 + conclusion * .20 + editorial * .10);

        return new SocialScoreBreakdown
        {
            Hook = hook,
            Retention = retention,
            Conclusion = conclusion,
            Potential = potential
        };
    }

    private static double Normalize(double value) =>
        Math.Round(Math.Clamp(value, 0, 100), 1);
}
