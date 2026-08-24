using CortaFeStudio.Api.Services;

namespace CortaFeStudio.Tests;

public sealed class WaveformServiceTests
{
    [Fact]
    public void Downsample_ReduzENormalizaEnvelopeReal()
    {
        var samples = new float[] { 0, .2f, -.8f, .1f, .4f, -1f, .3f, .2f };
        var result = WaveformService.Downsample(samples, 4);
        Assert.Equal(4, result.Count);
        Assert.Equal(.8, result[1]);
        Assert.Equal(1, result[2]);
        Assert.All(result, value => Assert.InRange(value, 0, 1));
    }

    [Fact]
    public void Downsample_LimitaQuantidadeDePontos()
    {
        var result = WaveformService.Downsample(Enumerable.Repeat(.5f, 5000).ToArray(), 1200);
        Assert.Equal(1200, result.Count);
    }
}
