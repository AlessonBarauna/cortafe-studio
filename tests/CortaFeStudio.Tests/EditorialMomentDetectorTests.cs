using CortaFeStudio.Api.Models;
using CortaFeStudio.Api.Services;

namespace CortaFeStudio.Tests;

public sealed class EditorialMomentDetectorTests
{
    [Fact]
    public void Detect_IdentificaGanchoVersiculoClimaxEConclusao()
    {
        var text = "Presta atenção você precisa ouvir isso. Em Romanos está escrito que Deus transforma a mente. Mas Deus não terminou, existe uma promessa e um propósito. Por isso guarda isso no coração amém.";
        var moments = EditorialMomentDetector.Detect(text, 66);

        Assert.Contains(moments, item => item.Kind == "hook");
        Assert.Contains(moments, item => item.Kind == "scripture");
        Assert.Contains(moments, item => item.Kind == "climax");
        Assert.Contains(moments, item => item.Kind == "conclusion");
        Assert.All(moments, item => Assert.InRange(item.Strength, 0, 1));
    }

    [Fact]
    public void Detect_ComTimestampsReais_PreservaPosicaoDoMomento()
    {
        var words = new List<TranscriptWord>
        {
            Word(0, "Hoje"), Word(1, "eu"), Word(2, "quero"), Word(3, "te"), Word(4, "dizer"),
            Word(12, "em"), Word(13, "Mateus"), Word(14, "está"), Word(15, "escrito"),
            Word(30, "mas"), Word(31, "Deus"), Word(32, "tem"), Word(33, "uma"), Word(34, "promessa"),
            Word(55, "por"), Word(56, "isso"), Word(57, "guarda"), Word(58, "isso"), Word(59, "amém")
        };

        var moments = EditorialMomentDetector.Detect(words, 62);

        Assert.Contains(moments, item => item.Kind == "scripture" && item.Start >= 8 && item.Start <= 16);
        Assert.Contains(moments, item => item.Kind == "climax" && item.Start >= 25);
        Assert.Contains(moments, item => item.Kind == "conclusion" && item.Start >= 50);
    }

    [Fact]
    public void PunchInPlanner_PriorizaMomentosSemanticosSemExagerarQuantidade()
    {
        var clip = new ClipCandidate
        {
            Start = 0,
            End = 70,
            EditorialProfile = "pregacao",
            Transcript = "Presta atenção porque isso muda tudo. Agora eu quero explicar uma verdade. Em João está escrito que Deus amou o mundo. Mas Deus não parou aí, existe uma promessa e um propósito para você. Por isso guarda essa palavra no coração amém.",
            Score = 92,
            SocialScore = new SocialScoreBreakdown { Hook = 90, Retention = 82 },
            ScoreBreakdown = new EditorialScoreBreakdown { Impact = 12, Clarity = 7 }
        };

        var moments = PunchInPlanner.Plan(clip);

        Assert.NotEmpty(moments);
        Assert.InRange(moments.Count, 1, 4);
        Assert.All(moments, item => Assert.InRange(item.Scale, 1.03, 1.08));
    }

    private static TranscriptWord Word(double start, string value) => new()
    {
        Start = start,
        End = start + .7,
        Word = value
    };
}
