using CortaFeStudio.Api.Models;
using CortaFeStudio.Api.Services;

namespace CortaFeStudio.Tests;

public sealed class SocialScoreServiceTests
{
    [Fact]
    public void Calculate_CorteForteSuperaTrechoDependenteDeContexto()
    {
        var options = new ProjectOptions { ContentType = "pregacao", MinDuration = 60, MaxDuration = 75 };

        var strong = new ClipCandidate
        {
            Start = 0,
            End = 66,
            Score = 91,
            HookSentence = "Sabe por que você continua com medo mesmo tendo fé?",
            ScoreBreakdown = new EditorialScoreBreakdown
            {
                Hook = 14,
                OpeningAdjustment = 11,
                Completion = 10,
                Contrast = 10,
                Conclusion = 8,
                Structure = 14,
                ProfileRelevance = 10,
                LengthAdjustment = 7
            }
        };

        var weak = new ClipCandidate
        {
            Start = 0,
            End = 66,
            Score = 54,
            HookSentence = "Como eu estava dizendo anteriormente",
            ScoreBreakdown = new EditorialScoreBreakdown
            {
                OpeningAdjustment = -18,
                Completion = -10,
                Structure = -7,
                ContextPenalty = -18,
                LengthAdjustment = 7
            }
        };

        var strongScore = SocialScoreService.Calculate(strong, options);
        var weakScore = SocialScoreService.Calculate(weak, options);

        Assert.True(strongScore.Hook > weakScore.Hook);
        Assert.True(strongScore.Retention > weakScore.Retention);
        Assert.True(strongScore.Conclusion > weakScore.Conclusion);
        Assert.True(strongScore.Potential > weakScore.Potential);
    }

    [Fact]
    public void Calculate_MantemTodasAsNotasEntreZeroECem()
    {
        var score = SocialScoreService.Calculate(
            new ClipCandidate
            {
                Start = 0,
                End = 70,
                Score = 99,
                HookSentence = "Presta atenção! A verdade é que você precisa entender isso.",
                ScoreBreakdown = new EditorialScoreBreakdown
                {
                    Hook = 100,
                    OpeningAdjustment = 100,
                    Completion = 100,
                    Contrast = 100,
                    Conclusion = 100,
                    Structure = 100,
                    ProfileRelevance = 100,
                    LengthAdjustment = 100,
                    Learning = 100
                }
            },
            new ProjectOptions());

        Assert.InRange(score.Hook, 0, 100);
        Assert.InRange(score.Retention, 0, 100);
        Assert.InRange(score.Conclusion, 0, 100);
        Assert.InRange(score.Potential, 0, 100);
    }

    [Fact]
    public void Apply_PreencheSocialScoreNoClip()
    {
        var clip = new ClipCandidate
        {
            Start = 0,
            End = 65,
            Score = 82,
            HookSentence = "A verdade é que você precisa continuar.",
            ScoreBreakdown = new EditorialScoreBreakdown
            {
                Hook = 7,
                OpeningAdjustment = 11,
                Completion = 10,
                Structure = 6
            }
        };

        SocialScoreService.Apply([clip], new ProjectOptions());

        Assert.True(clip.SocialScore.Potential > 0);
    }
}
