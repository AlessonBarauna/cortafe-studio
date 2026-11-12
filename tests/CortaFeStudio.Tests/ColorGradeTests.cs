using CortaFeStudio.Api.Models;
using CortaFeStudio.Api.Services;

namespace CortaFeStudio.Tests;

public sealed class ColorGradeTests
{
    [Fact]
    public void FiltroDeCor_EntraNoVisualDoCorte()
    {
        var clip = new ClipCandidate { Start = 0, End = 30, ColorFilter = "bw" };
        Assert.Contains("hue=s=0", RenderFilterFactory.CreativeLook(clip));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("filtro-inexistente")]
    [InlineData(null)]
    public void SemFiltroOuDesconhecido_NaoAlteraOVisual(string? name)
    {
        var padrao = new ClipCandidate { Start = 0, End = 30 };
        var clip = new ClipCandidate { Start = 0, End = 30, ColorFilter = name! };
        Assert.Equal(RenderFilterFactory.CreativeLook(padrao), RenderFilterFactory.CreativeLook(clip));
    }

    [Fact]
    public void TodosOsFiltrosTemNomeValidoParaOEditor()
    {
        foreach (var name in new[] { "none", "vivid", "cinema", "bw", "warm", "cool", "drama", "soft" })
            Assert.True(RenderFilterFactory.ColorGrades.ContainsKey(name));
    }

    [Fact]
    public void MudarFiltro_MarcaORenderComoDesatualizado()
    {
        var clip = new ClipCandidate { Start = 0, End = 30, VideoPath = "clip.mp4" };
        var before = RenderStateService.Fingerprint(clip);
        clip.ColorFilter = "cinema";
        RenderStateService.MarkIfChanged(clip, before);
        Assert.True(clip.RenderOutdated);
    }
}
