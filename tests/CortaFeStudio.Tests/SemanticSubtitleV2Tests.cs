using CortaFeStudio.Api.Models;
using CortaFeStudio.Api.Services;

namespace CortaFeStudio.Tests;

public sealed class SemanticSubtitleV2Tests
{
    [Fact]
    public void SemanticUnits_RespeitaPontuacaoPausaETamanhoVisual()
    {
        var words = new[] { "Voce", "precisa", "entender.", "Que", "a", "fe", "nao", "depende", "do", "que", "ve" }
            .Select((text, index) => new TranscriptWord { Word = text, Start = index * .3, End = index * .3 + .22 }).ToList();
        words[3].Start += .5;

        var units = SubtitleFormatter.SemanticUnits(words);

        Assert.All(units, unit => Assert.InRange(unit.Count, 2, 5));
        Assert.Equal("entender.", units[0][^1].Word);
        Assert.DoesNotContain(units.Take(units.Count - 1), unit => new[] { "a", "e", "de", "que" }.Contains(unit[^1].Word, StringComparer.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("impact")]
    [InlineData("clean")]
    [InlineData("podcast")]
    [InlineData("sermon")]
    [InlineData("motivational")]
    [InlineData("minimal")]
    [InlineData("worship")]
    public void Style_SuportaPerfisProfissionais(string styleName)
    {
        var style = SubtitleFormatter.Style(new ClipCandidate { SubtitleStyle = styleName }, 1080, 1920);
        Assert.StartsWith("Style: Impacto,", style);
        Assert.EndsWith(",135,135,235,1", style);
    }

    [Fact]
    public void PalavrasSemanticasLongas_RecebemDestaqueSemListaFixa()
    {
        Assert.True(SubtitleFormatter.IsEmphasisWord("transformacao"));
        Assert.True(SubtitleFormatter.IsEmphasisWord("intensamente"));
    }
}
