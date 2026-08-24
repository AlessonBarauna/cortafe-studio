using CortaFeStudio.Api.Models;
using CortaFeStudio.Api.Services;

namespace CortaFeStudio.Tests;

public sealed class RenderStateServiceTests
{
    [Fact]
    public void Fingerprint_MudaQuandoConfiguracaoVisualMuda()
    {
        var clip = new ClipCandidate { Start = 0, End = 60, Title = "Original", VideoPath = "clip.mp4" };
        var before = RenderStateService.Fingerprint(clip);
        clip.Title = "Título editado";
        RenderStateService.MarkIfChanged(clip, before);
        Assert.True(clip.RenderOutdated);
        Assert.NotEqual(before, RenderStateService.Fingerprint(clip));
    }

    [Fact]
    public void Fingerprint_IncluiTextoEditadoDaLegenda()
    {
        var clip = new ClipCandidate { SubtitleTrack = new SubtitleTrack { Blocks = [new SubtitleBlock { Start = 0, End = 2, Text = "Texto original" }] } };
        var before = RenderStateService.Fingerprint(clip);
        clip.SubtitleTrack.Blocks[0].Text = "Texto corrigido";
        Assert.NotEqual(before, RenderStateService.Fingerprint(clip));
    }
}
