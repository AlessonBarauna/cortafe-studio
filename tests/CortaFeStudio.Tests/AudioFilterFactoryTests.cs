using CortaFeStudio.Api.Models;
using CortaFeStudio.Api.Services;

namespace CortaFeStudio.Tests;

public sealed class AudioFilterFactoryTests
{
    [Theory]
    [InlineData("louvor", -18, -2, AudioProfile.Worship)]
    [InlineData("podcast", -19, -2, AudioProfile.Podcast)]
    [InlineData("pregacao", -31, -4, AudioProfile.LowVolume)]
    [InlineData("pregacao", -15, -.1, AudioProfile.Clipped)]
    [InlineData("pregacao", -18, -3, AudioProfile.VoiceClean)]
    public void Classify_SelecionaPerfilConformeConteudoEMedicoes(string contentType, double mean, double peak, AudioProfile expected)
    {
        var analysis = AudioAnalyzer.Classify(contentType, mean, peak, .02);
        Assert.Equal(expected, analysis.Profile);
    }

    [Fact]
    public void Create_VozRuidosaUsaReducaoLeveCompressaoNormalizacaoELimiter()
    {
        var profile = AudioFilterFactory.Create(new AudioAnalysis { Profile = AudioProfile.VoiceNoisy }, 60);

        Assert.Contains("afftdn=nf=-32", profile.Filter);
        Assert.Contains("acompressor", profile.Filter);
        Assert.Contains("loudnorm=I=-16", profile.Filter);
        Assert.Contains("alimiter", profile.Filter);
        Assert.EndsWith("afade=t=out:st=59.82:d=0.18", profile.Filter);
    }

    [Fact]
    public void Create_LouvorPreservaDinamicaESemReducaoDeRuidoAgressiva()
    {
        var profile = AudioFilterFactory.Create(new AudioAnalysis { Profile = AudioProfile.Worship }, 75);

        Assert.DoesNotContain("afftdn", profile.Filter);
        Assert.Contains("ratio=1.45", profile.Filter);
        Assert.Contains("loudnorm=I=-14:LRA=12", profile.Filter);
        Assert.Equal("-14 LUFS", profile.TargetLoudness);
    }
}
