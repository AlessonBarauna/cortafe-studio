using CortaFeStudio.Api.Models;
using CortaFeStudio.Api.Services;

namespace CortaFeStudio.Tests;

public sealed class ClipVariantServiceTests
{
    private readonly ClipVariantService _service = new();

    [Fact]
    public void Generate_CriaVariantesControladasESomenteUmVencedor()
    {
        var clip = Candidate(); var transcript = Transcript();
        var variants = _service.Generate(clip, transcript, new ProjectOptions(), 3);

        Assert.Equal(3, variants.Count);
        Assert.Single(variants, item => item.Winner);
        Assert.Equal(["A", "B", "C"], variants.Select(item => item.Label));
        Assert.All(variants, item => Assert.InRange(item.End - item.Start, 59.9, 60.1));
    }

    [Fact]
    public void ApplyWinner_AtualizaLimitesSemAlterarConteudoFalado()
    {
        var clip = Candidate(); var variants = _service.Generate(clip, Transcript(), new ProjectOptions(), 3);
        var winner = _service.ApplyWinner(clip, variants);

        Assert.Equal(winner.Id, clip.WinningVariantId);
        Assert.Equal(winner.Start, clip.Start);
        Assert.Equal(winner.End, clip.End);
        Assert.Equal(variants.Count, clip.Variants.Count);
        Assert.Contains("palavra", winner.HookSentence);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(20, 5)]
    public void Generate_LimitaQuantidadeDeVariantes(int requested, int expected)
    {
        Assert.Equal(expected, _service.Generate(Candidate(), Transcript(), new ProjectOptions(), requested).Count);
    }

    private static ClipCandidate Candidate() => new()
    {
        Start = 20, End = 80, HookSentence = "palavra inicial", Transcript = "palavra inicial e mensagem completa",
        Score = 82, EditorialProfile = "pregacao", ScoreBreakdown = new EditorialScoreBreakdown { Hook = 8, Structure = 7, Completion = 8, Conclusion = 7 }
    };

    private static List<TranscriptSegment> Transcript() => Enumerable.Range(0, 120).Select(index => new TranscriptSegment
    {
        Start = index, End = index + .8, Text = $"palavra {index}",
        Words = [new TranscriptWord { Start = index, End = index + .8, Word = $"palavra{index}" }]
    }).ToList();
}
