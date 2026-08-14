using CortaFeStudio.Api.Models;
using CortaFeStudio.Api.Services;

namespace CortaFeStudio.Tests;

public sealed class SubtitleFormatterTests
{
    [Fact]
    public void Karaoke_QuebraFraseLongaDentroDaAreaSeguraVertical()
    {
        var words = new[] { "outros", "não", "sentiam,", "Davi", "aprendeu" }
            .Select((word, index) => new TranscriptWord { Word = word, Start = index, End = index + .5 }).ToList();

        var result = SubtitleFormatter.Karaoke(words, new ClipCandidate { OutputPreset = "vertical" }, 1080);

        Assert.Contains("\\N", result);
        Assert.Contains("{\\kf50}", result);
    }

    [Theory]
    [InlineData("vertical", 1080, 1920, ",125,125,390,1")]
    [InlineData("portrait", 1080, 1350, ",125,125,270,1")]
    [InlineData("square", 1080, 1080, ",125,125,190,1")]
    [InlineData("landscape", 1920, 1080, ",190,190,190,1")]
    public void Style_UsaMargensSegurasPorFormato(string preset, int width, int height, string expected)
    {
        var style = SubtitleFormatter.Style(new ClipCandidate { OutputPreset = preset }, width, height);
        Assert.EndsWith(expected, style);
    }

    [Fact]
    public void Plain_QuebraTextoSemTemporizacaoPorPalavra()
    {
        var result = SubtitleFormatter.Plain("uma legenda muito longa precisa continuar sempre dentro do quadro", 1080);
        Assert.Contains("\\N", result);
    }
}
