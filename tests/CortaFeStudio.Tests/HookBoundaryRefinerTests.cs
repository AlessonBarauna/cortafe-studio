using CortaFeStudio.Api.Models;
using CortaFeStudio.Api.Services;

namespace CortaFeStudio.Tests;

public sealed class HookBoundaryRefinerTests
{
    [Fact]
    public void HookScore_PriorizaPerguntaEContraste()
    {
        var weak = HookBoundaryRefiner.HookScore("Então gente, vamos continuar aqui.");
        var strong = HookBoundaryRefiner.HookScore("Sabe por que você continua com medo mesmo tendo fé?");

        Assert.True(strong >= weak + 8);
    }

    [Fact]
    public void Refine_AvancaParaGanchoMaisForteSemEncurtarDemais()
    {
        var clip = new ClipCandidate
        {
            Start = 0,
            End = 65,
            Transcript = "Então gente, vamos continuar. Sabe por que você ainda tem medo?",
            EditorialProfile = "pregacao"
        };
        var segments = new List<TranscriptSegment>
        {
            new() { Start = 0, End = 3, Text = "Então gente, vamos continuar aqui." },
            new() { Start = 3.2, End = 7, Text = "Sabe por que você ainda tem medo mesmo tendo fé?" },
            new() { Start = 7, End = 65, Text = "Porque fé não significa ausência de luta." }
        };
        var options = new ProjectOptions { MinDuration = 60, MaxDuration = 75 };

        HookBoundaryRefiner.Refine(clip, segments, options);

        Assert.True(clip.Start >= 3);
        Assert.Contains("Sabe por que", clip.HookSentence);
        Assert.Contains("gancho mais forte", string.Join(' ', clip.Reasons));
    }

    [Fact]
    public void Refine_EstendeFinalAteConclusaoQuandoCouber()
    {
        var clip = new ClipCandidate
        {
            Start = 0,
            End = 60,
            Transcript = "Uma ideia incompleta",
            EditorialProfile = "pregacao"
        };
        var segments = new List<TranscriptSegment>
        {
            new() { Start = 0, End = 58, Text = "A mensagem começa aqui e segue" },
            new() { Start = 58, End = 60, Text = "mas ainda não terminou" },
            new() { Start = 60, End = 64, Text = "até entendermos que Deus continua presente." }
        };
        var options = new ProjectOptions { MinDuration = 60, MaxDuration = 75 };

        HookBoundaryRefiner.Refine(clip, segments, options);

        Assert.True(clip.End > 63.5);
        Assert.Contains("concluir a ideia", string.Join(' ', clip.Reasons));
    }

    [Fact]
    public void Refine_NaoAlteraLouvor()
    {
        var clip = new ClipCandidate
        {
            Start = 0,
            End = 65,
            Transcript = "Louvor",
            EditorialProfile = "louvor"
        };
        var segments = new List<TranscriptSegment>
        {
            new() { Start = 0, End = 4, Text = "Então" },
            new() { Start = 4, End = 65, Text = "Sabe por que Deus é fiel?" }
        };
        var options = new ProjectOptions();

        HookBoundaryRefiner.Refine(clip, segments, options);

        Assert.Equal(0, clip.Start);
        Assert.Equal(65, clip.End);
    }
}
