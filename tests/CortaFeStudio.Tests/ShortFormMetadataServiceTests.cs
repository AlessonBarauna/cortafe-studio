using CortaFeStudio.Api.Services;

namespace CortaFeStudio.Tests;

public sealed class ShortFormMetadataServiceTests
{
    [Fact]
    public void Titulo_LongosSaoLimitadosSemCortarPalavraNoMeio()
    {
        var title = ShortFormMetadataService.NormalizeTitle(
            "Essa é uma explicação muito importante sobre como a fé transforma decisões em momentos difíceis da vida cristã");

        Assert.True(title.Length <= 76);
        Assert.EndsWith("…", title);
    }

    [Fact]
    public void Capa_UsaNoMaximoSeisPalavras()
    {
        var cover = ShortFormMetadataService.NormalizeCoverText(
            "Você precisa entender por que a fé muda completamente suas decisões");

        Assert.True(cover.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 6);
        Assert.Equal(cover.ToUpperInvariant(), cover);
    }

    [Fact]
    public void Hashtags_RemovemSpamECompletamONicho()
    {
        var tags = ShortFormMetadataService.NormalizeHashtags(
            ["#fyp", "viral", "#graca", "#graca", "#Jesus"],
            "pregacao");

        Assert.DoesNotContain(tags, tag => tag.Equals("#fyp", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(tags, tag => tag.Equals("#viral", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(tags, tag => tag.Equals("#graca", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(tags, tag => tag.Equals("#pregacao", StringComparison.OrdinalIgnoreCase));
        Assert.InRange(tags.Count, 5, 7);
    }

    [Fact]
    public void Legenda_LimitaTamanhoEExcessoDeQuebras()
    {
        var caption = ShortFormMetadataService.NormalizeCaption(
            new string('a', 800) + "\n\n\n\ncontinuação");

        Assert.True(caption.Length <= 700);
        Assert.DoesNotContain("\n\n\n", caption);
    }
}
