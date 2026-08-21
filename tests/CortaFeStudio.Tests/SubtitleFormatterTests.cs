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

        var result = SubtitleFormatter.Karaoke(words, new ClipCandidate { OutputPreset = "vertical", SubtitleStyle = "bold" }, 1080);

        Assert.Contains("\\N", result);
        Assert.Contains("{\\kf50}", result);
    }

    [Theory]
    [InlineData("vertical", 1080, 1920, ",135,135,430,1")]
    [InlineData("portrait", 1080, 1350, ",135,135,310,1")]
    [InlineData("square", 1080, 1080, ",135,135,220,1")]
    [InlineData("landscape", 1920, 1080, ",210,210,220,1")]
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

    [Theory]
    [InlineData("fé")]
    [InlineData("propósito")]
    [InlineData("Atenção!")]
    [InlineData("JESUS")]
    public void PalavrasDeImpacto_SaoReconhecidas(string word)
    {
        Assert.True(SubtitleFormatter.IsEmphasisWord(word));
    }

    [Theory]
    [InlineData("e")]
    [InlineData("de")]
    [InlineData("uma")]
    [InlineData("casa")]
    public void PalavrasComuns_NaoRecebemDestaque(string word)
    {
        Assert.False(SubtitleFormatter.IsEmphasisWord(word));
    }

    [Fact]
    public void Karaoke_DestacaPalavraDeImpacto()
    {
        var words = new List<TranscriptWord>
        {
            new() { Start = 0, End = .4, Word = "Você" },
            new() { Start = .4, End = .8, Word = "precisa" },
            new() { Start = .8, End = 1.2, Word = "ter" },
            new() { Start = 1.2, End = 1.6, Word = "fé" }
        };
        var clip = new ClipCandidate { SubtitleStyle = "bold" };

        var result = SubtitleFormatter.Karaoke(words, clip, 1080);

        Assert.Contains("\\1c&H0000B7FF&", result);
        Assert.Contains("fé", result);
    }
}
