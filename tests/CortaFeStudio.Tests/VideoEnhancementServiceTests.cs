using CortaFeStudio.Api.Models;
using CortaFeStudio.Api.Services;

namespace CortaFeStudio.Tests;

public sealed class VideoEnhancementServiceTests
{
    [Theory]
    [InlineData(45, 10, 150, 35, VideoEnhancementKind.Dark)]
    [InlineData(215, 150, 250, 30, VideoEnhancementKind.Overexposed)]
    [InlineData(175, 125, 205, 18, VideoEnhancementKind.WashedOut)]
    [InlineData(110, 75, 145, 38, VideoEnhancementKind.LowContrast)]
    [InlineData(110, 35, 205, 18, VideoEnhancementKind.LowSaturation)]
    [InlineData(110, 25, 220, 40, VideoEnhancementKind.Neutral)]
    public void Classify_DetectaCondicaoVisual(double average, double low, double high, double saturation, VideoEnhancementKind expected)
    {
        Assert.Equal(expected, VideoEnhancementService.Classify(average, low, high, saturation).Kind);
    }

    [Fact]
    public void CreateProfile_AplicaCorrecaoConservadoraParaImagemEscura()
    {
        var profile = VideoEnhancementService.CreateProfile(new VideoAnalysis { Kind = VideoEnhancementKind.Dark });
        Assert.Contains("brightness=0.035", profile.Filter);
        Assert.Contains("saturation=1.02", profile.Filter);
        Assert.DoesNotContain("hqdn3d", profile.Filter);
    }

    [Fact]
    public void CreateProfile_NeutralPreservaImagem()
    {
        var profile = VideoEnhancementService.CreateProfile(new VideoAnalysis { Kind = VideoEnhancementKind.Neutral });
        Assert.Equal("null", profile.Filter);
        Assert.False(profile.Applied);
    }

    [Fact]
    public void CreateProfile_SharpenNaoCriaHaloAgressivo()
    {
        var profile = VideoEnhancementService.CreateProfile(new VideoAnalysis { Kind = VideoEnhancementKind.LowContrast });
        Assert.Contains("unsharp=5:5:0.18", profile.Filter);
        Assert.DoesNotContain("unsharp=5:5:1", profile.Filter);
    }
}
