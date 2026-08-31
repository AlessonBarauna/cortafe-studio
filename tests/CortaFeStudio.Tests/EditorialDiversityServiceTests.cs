using CortaFeStudio.Api.Models;
using CortaFeStudio.Api.Services;

namespace CortaFeStudio.Tests;

public sealed class EditorialDiversityServiceTests
{
    [Fact]
    public void Select_EvitaCortesSemanticamenteRepetidos()
    {
        var pool = new[]
        {
            Clip(0, 70, 95, "A fé transforma o coração e muda completamente a nossa vida"),
            Clip(100, 170, 94, "A fé muda completamente nossa vida e transforma o coração"),
            Clip(300, 370, 88, "O perdão restaura relacionamentos feridos dentro da família"),
            Clip(600, 670, 86, "Propósito exige coragem para tomar decisões difíceis")
        };
        var selected = EditorialDiversityService.Select(pool, 3, 700);
        Assert.Equal(3, selected.Count);
        Assert.Single(selected, clip => clip.Transcript.Contains("fé", StringComparison.OrdinalIgnoreCase));
        Assert.All(selected, clip => Assert.False(string.IsNullOrWhiteSpace(clip.DiversityTopic)));
    }

    [Fact]
    public void Similarity_IgnoraAcentosEPalavrasDeApoio() => Assert.True(EditorialDiversityService.Similarity("Você precisa de fé no coração", "A fe transforma o coracao") > .2);
    private static ClipCandidate Clip(double start, double end, double score, string text) => new() { Start = start, End = end, Score = score, Transcript = text };
}
