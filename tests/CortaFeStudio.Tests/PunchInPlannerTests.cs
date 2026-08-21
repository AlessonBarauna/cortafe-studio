using CortaFeStudio.Api.Models;
using CortaFeStudio.Api.Services;

namespace CortaFeStudio.Tests;

public sealed class PunchInPlannerTests
{
    [Fact]
    public void Plan_CriaPoucosMomentosSeparados()
    {
        var clip = new ClipCandidate
        {
            Start = 100,
            End = 165,
            EditorialProfile = "pregacao",
            Transcript = "presta atenção porque Deus não perdeu o controle da sua história e você precisa continuar com fé mesmo quando o medo aparece porque a verdade é que existe propósito e promessa para quem permanece"
        };

        var moments = PunchInPlanner.Plan(clip);

        Assert.NotEmpty(moments);
        Assert.True(moments.Count <= 3);
        Assert.All(moments, moment => Assert.InRange(moment.Scale, 1.02, 1.08));
        Assert.All(moments, moment => Assert.True(moment.End > moment.Start));
        for (var i = 1; i < moments.Count; i++)
            Assert.True(moments[i].Start - moments[i - 1].Start >= 7.5);
    }

    [Fact]
    public void Plan_LouvorNaoRecebePunchInAutomatico()
    {
        var clip = new ClipCandidate
        {
            Start = 0,
            End = 70,
            EditorialProfile = "louvor",
            Transcript = "Deus promessa fé milagre coragem verdade propósito"
        };

        Assert.Empty(PunchInPlanner.Plan(clip));
    }

    [Fact]
    public void PunchIn_GeraCurvaSuaveECropNoPreset()
    {
        var filter = RenderFilterFactory.PunchIn(
            [new PunchInMoment(2, 3.5, 1.06)],
            1080,
            1920);

        Assert.Contains("sin(PI*(t-2)/1.5)", filter);
        Assert.Contains("eval=frame", filter);
        Assert.Contains("crop=1080:1920", filter);
        Assert.Contains("between(t\\,2\\,3.5)", filter);
    }
}
