using System.Text.Json;
using CortaFeStudio.Api.Models;
using CortaFeStudio.Api.Services;

namespace CortaFeStudio.Tests;

public sealed class CortaFeFxServiceTests
{
    [Fact]
    public void WordsJson_UsaTempoRelativoAoCorteEAplicaVelocidade()
    {
        var clip = new ClipCandidate { Start = 10, End = 20 };
        var segments = new List<TranscriptSegment>
        {
            new() { Start = 10, End = 14, Text = "Deus é fiel", Words =
            [
                new TranscriptWord { Word = " Deus", Start = 10.5, End = 11 },
                new TranscriptWord { Word = " é", Start = 11, End = 11.3 },
                new TranscriptWord { Word = " fiel", Start = 11.3, End = 12 }
            ] }
        };

        using var document = JsonDocument.Parse(CortaFeFxService.WordsJson(segments, clip, 1.25));
        var words = document.RootElement.EnumerateArray().ToList();

        Assert.Equal(3, words.Count);
        Assert.Equal("Deus", words[0].GetProperty("word").GetString());
        Assert.Equal(0.4, words[0].GetProperty("start").GetDouble(), 3);
        Assert.Equal(0.8, words[0].GetProperty("end").GetDouble(), 3);
    }

    [Fact]
    public void WordsJson_DistribuiPalavrasQuandoOSegmentoNaoTemTempoPorPalavra()
    {
        var clip = new ClipCandidate { Start = 0, End = 10 };
        var segments = new List<TranscriptSegment> { new() { Start = 2, End = 6, Text = "a graça basta" } };

        using var document = JsonDocument.Parse(CortaFeFxService.WordsJson(segments, clip, 1));
        var words = document.RootElement.EnumerateArray().ToList();

        Assert.Equal(3, words.Count);
        Assert.Equal(2, words[0].GetProperty("start").GetDouble(), 3);
        Assert.Equal(6, words[^1].GetProperty("end").GetDouble(), 3);
        Assert.All(words, word => Assert.True(word.GetProperty("end").GetDouble() > word.GetProperty("start").GetDouble()));
    }
}
