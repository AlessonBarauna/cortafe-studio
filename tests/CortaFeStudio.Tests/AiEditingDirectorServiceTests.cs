using CortaFeStudio.Api.Models;
using CortaFeStudio.Api.Services;

namespace CortaFeStudio.Tests;

public sealed class AiEditingDirectorServiceTests
{
    private readonly AiEditingDirectorService _service = new();

    [Fact]
    public void Direct_CorteForteRecebeEdicaoMaisDinamica()
    {
        var clip = new ClipCandidate
        {
            Start = 0,
            End = 70,
            Score = 91,
            Transcript = "Você precisa entender esta verdade: Deus não esqueceu de você. Existe propósito, promessa, coragem e esperança mesmo no medo.",
            EditorialProfile = "pregacao",
            SocialScore = new SocialScoreBreakdown { Hook = 92, Retention = 84 },
            ScoreBreakdown = new EditorialScoreBreakdown { Impact = 12, Clarity = 6 },
            VisualDirection = new VisualDirectionAnalysis { Analyzed = true, SubjectDetected = true, SceneDensity = 6.8, Score = 81 }
        };

        _service.Direct(clip, new ProjectOptions { ContentType = "pregacao" });

        Assert.Equal("impact", clip.SubtitleStyle);
        Assert.Equal("dynamic", clip.TransitionStyle);
        Assert.True(clip.SilenceTrimmingEnabled);
        Assert.Equal(1.25, clip.PlaybackSpeed);
        Assert.Contains(clip.Reasons, reason => reason.Contains("direção de edição IA"));
    }

    [Fact]
    public void Direct_LouvorPreservaRitmoMusical()
    {
        var clip = new ClipCandidate
        {
            Start = 0,
            End = 80,
            EditorialProfile = "louvor",
            SilenceTrimmingEnabled = true,
            PlaybackSpeed = 1.25,
            VisualDirection = new VisualDirectionAnalysis { SceneDensity = 1 }
        };

        _service.Direct(clip, new ProjectOptions { ContentType = "louvor" });

        Assert.False(clip.SilenceTrimmingEnabled);
        Assert.Equal(1, clip.PlaybackSpeed);
        Assert.Equal("balanced", clip.SubtitleStyle);
        Assert.Equal("smooth", clip.TransitionStyle);
    }

    [Fact]
    public void PunchInPlanner_CorteFortePodeGerarMaisMomentosQueCorteSuave()
    {
        var transcript = string.Join(' ', Enumerable.Repeat("você precisa saber a verdade Deus promessa coragem milagre", 18));
        var strong = new ClipCandidate
        {
            Start = 0, End = 75, Score = 95, Transcript = transcript, EditorialProfile = "pregacao",
            SocialScore = new SocialScoreBreakdown { Hook = 95, Retention = 90 },
            ScoreBreakdown = new EditorialScoreBreakdown { Impact = 14 }
        };
        var soft = new ClipCandidate
        {
            Start = 0, End = 75, Score = 52, Transcript = transcript, EditorialProfile = "pregacao",
            SocialScore = new SocialScoreBreakdown { Hook = 35, Retention = 40 },
            ScoreBreakdown = new EditorialScoreBreakdown { Impact = 1 }
        };

        var strongMoments = PunchInPlanner.Plan(strong);
        var softMoments = PunchInPlanner.Plan(soft);

        Assert.True(strongMoments.Count >= softMoments.Count);
        Assert.True(strongMoments.Max(moment => moment.Scale) >= softMoments.Max(moment => moment.Scale));
    }
}
