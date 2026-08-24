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

    [Theory]
    [InlineData(1.25, "atempo=1.25", "st=47.82")]
    [InlineData(1.5, "atempo=1.5", "st=39.82")]
    public void Create_AceleraAudioEMantemFadeNoFinalReal(double speed, string tempo, string fade)
    {
        var profile = AudioFilterFactory.Create(new AudioAnalysis { Profile = AudioProfile.VoiceClean }, 60, speed);
        Assert.Contains(tempo, profile.Filter);
        Assert.Contains(fade, profile.Filter);
        Assert.True(profile.Filter.IndexOf("atempo", StringComparison.Ordinal) < profile.Filter.IndexOf("afade", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(AudioProfile.VoiceClean)]
    [InlineData(AudioProfile.VoiceNoisy)]
    [InlineData(AudioProfile.VoiceWithMusic)]
    [InlineData(AudioProfile.Worship)]
    [InlineData(AudioProfile.Music)]
    [InlineData(AudioProfile.Podcast)]
    [InlineData(AudioProfile.LowVolume)]
    [InlineData(AudioProfile.Clipped)]
    public void Create_TodosOsPerfisRespeitamLimiteMakeupDoFfmpeg(AudioProfile audioProfile)
    {
        var filter = AudioFilterFactory.Create(new AudioAnalysis { Profile = audioProfile }, 60).Filter;
        var marker = "makeup="; var start = filter.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = filter.IndexOf(',', start); var value = double.Parse(filter[start..end], System.Globalization.CultureInfo.InvariantCulture);

        Assert.InRange(value, 1, 64);
    }
}
