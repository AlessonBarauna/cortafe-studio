using CortaFeStudio.Api.Models;
using CortaFeStudio.Api.Services;

namespace CortaFeStudio.Tests;

public sealed class TitleGenerationTests
{
    [Fact]
    public void Louvor_UsaConteudoDoProprioCorte()
    {
        var clip = new ClipCandidate { Transcript = "Mesmo quando eu não posso ver. Eu sei que estás aqui. Tua promessa continua de pé." };
        var titles = ShortFormMetadataService.GenerateTitleSuggestions(clip, "louvor");
        Assert.NotEmpty(titles);
        Assert.Contains(titles, title => title.Contains("promessa", StringComparison.OrdinalIgnoreCase) || title.Contains("aqui", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(titles, title => title.Contains("Momento de louvor", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CortesDiferentesRecebemTitulosDiferentes()
    {
        var clips = new List<ClipCandidate>
        {
            new() { Start = 0, Title = "Momento de louvor e adoração", Transcript = "Mesmo sem ver eu continuo crendo e sei que estás aqui" },
            new() { Start = 60, Title = "Momento de louvor e adoração", Transcript = "Tu permaneces fiel em todo tempo e teu amor nunca falha" }
        };
        ShortFormMetadataService.EnsureUniqueTitles(clips, "louvor");
        Assert.NotEqual(clips[0].Title, clips[1].Title);
    }

    [Fact]
    public void TituloManualNaoEhSobrescrito()
    {
        var clip = new ClipCandidate { Title = "Meu título escolhido", TitleEditedByUser = true, Transcript = "Tu permaneces fiel" };
        ShortFormMetadataService.EnsureUniqueTitles([clip], "louvor");
        Assert.Equal("Meu título escolhido", clip.Title);
    }

    [Fact]
    public void SugestoesNaoSaoIdenticas()
    {
        var clip = new ClipCandidate { Transcript = "Mesmo quando eu não posso ver. Eu sei que estás aqui. A promessa continua de pé e o teu amor permanece para sempre." };
        var titles = ShortFormMetadataService.GenerateTitleSuggestions(clip, "louvor");
        Assert.True(titles.Count >= 2);
        Assert.Equal(titles.Count, titles.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
