using CortaFeStudio.Api.Models;
using CortaFeStudio.Api.Services;

namespace CortaFeStudio.Tests;

public sealed class RenderCacheTests
{
    [Fact]
    public void Fingerprint_MudaQuandoConfiguracaoVisualMuda()
    {
        var clip = new ClipCandidate { Start = 10, End = 70, CropX = .5 };
        var first = RenderStateService.Fingerprint(clip); clip.CropX = .7;
        Assert.NotEqual(first, RenderStateService.Fingerprint(clip));
    }
}
