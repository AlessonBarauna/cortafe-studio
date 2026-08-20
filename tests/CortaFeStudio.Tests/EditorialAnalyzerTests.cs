using CortaFeStudio.Api.Models;
using CortaFeStudio.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace CortaFeStudio.Tests;

public sealed class EditorialAnalyzerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cortafe-editorial", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Analyze_PrefereIdeiaCompletaEExplicaEscolha()
    {
        var analyzer = CreateAnalyzer();
        var segments = Segments("Você sabe por que a fé muda nossa caminhada?", "Muitas vezes nós olhamos somente para o problema.", "Mas Deus nos convida a confiar mesmo quando não vemos.", "Por exemplo, Abraão caminhou sem conhecer todo o percurso.", "A fé cresce porque escolhemos obedecer à palavra.", "Por isso, confie em Deus e dê hoje o próximo passo.");
        var clips = analyzer.Analyze(segments, new ProjectOptions { MinDuration = 20, MaxDuration = 40, ClipCount = 3 });
        Assert.NotEmpty(clips);
        Assert.Contains(clips, clip => clip.Reasons.Any(reason => reason.StartsWith("ideia completa")));
    }

    [Fact]
    public void Analyze_EvitaCandidatosSobrepostos()
    {
        var analyzer = CreateAnalyzer();
        var texts = Enumerable.Range(0, 18).Select(i => i % 3 == 0 ? "Presta atenção, Deus transforma o coração quando você confia." : i % 3 == 1 ? "A verdade é que a fé vence o medo porque nasce da palavra." : "Por isso siga em paz, confie em Jesus e termine bem esta caminhada.").ToArray();
        var clips = analyzer.Analyze(Segments(texts), new ProjectOptions { MinDuration = 18, MaxDuration = 35, ClipCount = 8 });
        for (var i = 0; i < clips.Count; i++) for (var j = i + 1; j < clips.Count; j++)
            Assert.True(Math.Max(0, Math.Min(clips[i].End, clips[j].End) - Math.Max(clips[i].Start, clips[j].Start)) / Math.Min(clips[i].End - clips[i].Start, clips[j].End - clips[j].Start) <= .24);
    }

    [Fact]
    public void Analyze_OrdenaPontuacaoDoMaiorParaOMenor()
    {
        var analyzer = CreateAnalyzer();
        var texts = Enumerable.Range(0, 24).Select(i => i % 4 == 0 ? "Presta atenção, você sabe por que Deus transforma o coração?" : i % 4 == 3 ? "Por isso confie em Jesus e dê o próximo passo." : "A fé cresce porque a palavra vence o medo e traz paz.").ToArray();
        var clips = analyzer.Analyze(Segments(texts), new ProjectOptions { MinDuration = 15, MaxDuration = 30, ClipCount = 8 });
        Assert.Equal(clips.OrderByDescending(clip => clip.Score).Select(clip => clip.Id), clips.Select(clip => clip.Id));
    }

    [Fact]
    public void Analyze_ExplicaPontuacaoEIdentificaGancho()
    {
        var clips = CreateAnalyzer().Analyze(Segments("Presta atenção, você sabe por que Deus transforma o coração?", "A fé cresce porque a palavra vence o medo.", "Por isso confie em Jesus e dê o próximo passo."), new ProjectOptions { MinDuration = 10, MaxDuration = 25, ClipCount = 2 });
        var clip = Assert.Single(clips);
        Assert.False(string.IsNullOrWhiteSpace(clip.HookSentence));
        Assert.True(clip.ScoreBreakdown.Hook > 0);
        Assert.Equal(clip.ScoreBreakdown.Total, clip.Score);
    }

    [Fact]
    public void Analyze_AplicaPenalidadeParaFalaDeTransicao()
    {
        var analyzer = CreateAnalyzer();

        var segments = Segments(
            "Agora vamos continuar, presta atenção no que Deus faz com o coração?",
            "A fé muda nossa caminhada porque a palavra produz confiança.",
            "Por isso confie em Jesus e dê hoje o próximo passo.");

        var clips = analyzer.Analyze(
            segments,
            new ProjectOptions
            {
                MinDuration = 10,
                MaxDuration = 25,
                ClipCount = 1
            });

        var clip = Assert.Single(clips);

        Assert.StartsWith(
            "Agora vamos continuar",
            clip.HookSentence,
            StringComparison.OrdinalIgnoreCase);

        Assert.True(
            clip.ScoreBreakdown.OpeningAdjustment < 0);

        Assert.Contains(
            clip.Reasons,
            reason => reason.Contains(
                "fala de transição",
                StringComparison.OrdinalIgnoreCase));

        Assert.Equal(
            clip.ScoreBreakdown.Total,
            clip.Score);
    }

    private EditorialAnalyzer CreateAnalyzer() =>
    new(
        new EditorialLearningService(
            new TestEnvironment(_root)),
        new EditorialScoringService());
    private static List<TranscriptSegment> Segments(params string[] texts) => texts.Select((text, index) => new TranscriptSegment { Start = index * 5, End = index * 5 + 4.8, Text = text }).ToList();
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private sealed class TestEnvironment(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests"; public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = root; public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = root; public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
