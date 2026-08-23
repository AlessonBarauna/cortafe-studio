using CortaFeStudio.Api.Models;
using CortaFeStudio.Api.Services;

namespace CortaFeStudio.Tests;

public sealed class ProductionBatchServiceTests
{
    [Fact]
    public void NormalizeSettings_MantemPublicacaoAutomaticaDesligadaPorPadrao()
    {
        var settings = ProductionBatchService.NormalizeSettings(null);

        Assert.False(settings.AutoApprove);
        Assert.False(settings.AutoPublish);
        Assert.Equal(20, settings.CandidateCount);
        Assert.Equal(10, settings.FinalVideoCount);
    }

    [Fact]
    public void NormalizeSettings_LimitaValoresEIgnoraHorariosInvalidos()
    {
        var settings = ProductionBatchService.NormalizeSettings(new ProductionSettings
        {
            CandidateCount = 99,
            FinalVideoCount = 80,
            PostsPerDay = 50,
            MinimumSocialScore = 120,
            PostingTimes = ["08:30", "invalido", "08:30"]
        });

        Assert.Equal(20, settings.CandidateCount);
        Assert.Equal(20, settings.FinalVideoCount);
        Assert.Equal(12, settings.PostsPerDay);
        Assert.Equal(100, settings.MinimumSocialScore);
        Assert.Equal(["08:30"], settings.PostingTimes);
    }

    [Fact]
    public void BuildSchedule_DistribuiPublicacoesPorDiaEHorario()
    {
        var settings = new ProductionSettings
        {
            StartDate = new DateOnly(2026, 8, 24),
            PostsPerDay = 2,
            PostingTimes = ["09:00", "18:00"]
        };

        var schedule = ProductionBatchService.BuildSchedule(settings, 3);

        Assert.Equal(3, schedule.Count);
        Assert.Equal(new DateOnly(2026, 8, 24), DateOnly.FromDateTime(schedule[0].DateTime));
        Assert.Equal(new TimeOnly(9, 0), TimeOnly.FromDateTime(schedule[0].DateTime));
        Assert.Equal(new DateOnly(2026, 8, 25), DateOnly.FromDateTime(schedule[2].DateTime));
    }
}
