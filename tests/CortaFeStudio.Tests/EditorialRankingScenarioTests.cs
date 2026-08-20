using CortaFeStudio.Api.Models;
using CortaFeStudio.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace CortaFeStudio.Tests;

public sealed class EditorialRankingScenarioTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "cortafe-editorial-scenarios",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Analyze_PriorizaGanchoForteSobreAberturaGenerica()
    {
        var strong = AnalyzeSingle(
            [
                "Presta atenção, você sabe por que Deus pode transformar completamente o seu coração?",
                "Muitas pessoas vivem dominadas pelo medo, mas a fé cresce quando escolhem confiar na palavra mesmo sem enxergar todo o caminho.",
                "Por isso, confie em Jesus hoje e dê o próximo passo com coragem e propósito."
            ]);

        var generic = AnalyzeSingle(
            [
                "Amém, um dia eu percebi que Deus trabalha profundamente no coração de quem decide confiar.",
                "Muitas pessoas sentem medo, mas a fé cresce porque a palavra nos ensina a caminhar mesmo sem controlar todas as respostas.",
                "Por isso, confie em Jesus hoje e continue avançando com coragem e propósito."
            ]);

        Assert.True(
            strong.Score > generic.Score,
            $"Esperava que o gancho forte ({strong.Score}) superasse a abertura genérica ({generic.Score}).");

        Assert.True(
            strong.ScoreBreakdown.OpeningAdjustment >
            generic.ScoreBreakdown.OpeningAdjustment);

        Assert.Contains(
            generic.Reasons,
            reason => reason.Contains(
                "abertura genérica",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_PenalizaTrechoDependenteDoContextoAnterior()
    {
        var clip = AnalyzeSingle(
            [
                "Como eu disse, presta atenção: Deus transforma o coração quando aprendemos a confiar na palavra.",
                "Muitas vezes queremos controlar tudo, mas a fé cresce porque seguimos mesmo quando não vemos todo o caminho.",
                "Por isso, confie em Jesus hoje e dê o próximo passo com coragem e propósito."
            ]);

        Assert.True(
            clip.ScoreBreakdown.ContextPenalty < 0);

        Assert.Contains(
            clip.Reasons,
            reason => reason.Contains(
                "ideia completa: gancho,",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_AumentaPontuacaoQuandoTrechoCorrespondeAoTema()
    {
        var transcript = new[]
        {
            "Você sabe por que a coragem muda nossas decisões?",
            "Quando o medo aparece, precisamos escolher um caminho e agir mesmo sem ter todas as respostas.",
            "Por isso, a fé nos ajuda a seguir e terminar aquilo que começamos."
        };

        var matchingTopic = AnalyzeSingle(
            transcript,
            new ProjectOptions
            {
                MinDuration = 10,
                MaxDuration = 25,
                ClipCount = 1,
                Topic = "fé coragem"
            });

        var unrelatedTopic = AnalyzeSingle(
            transcript,
            new ProjectOptions
            {
                MinDuration = 10,
                MaxDuration = 25,
                ClipCount = 1,
                Topic = "finanças investimentos"
            });

        Assert.True(
            matchingTopic.Score > unrelatedTopic.Score,
            $"Tema relacionado: {matchingTopic.Score}; tema não relacionado: {unrelatedTopic.Score}.");

        Assert.True(
            matchingTopic.ScoreBreakdown.TopicRelevance > 0);

        Assert.True(
            unrelatedTopic.ScoreBreakdown.TopicRelevance < 0);

        Assert.Contains(
            matchingTopic.Reasons,
            reason => reason.Contains(
                "relacionado ao tema",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_PriorizaConclusaoCompletaSobreCorteAbrupto()
    {
        var complete = AnalyzeSingle(
            [
                "Presta atenção, Deus transforma o coração quando aprendemos a confiar na palavra.",
                "Nós sentimos medo, mas a fé cresce porque continuamos caminhando mesmo sem controlar tudo.",
                "Por isso, confie em Jesus e dê hoje o próximo passo."
            ]);

        var abrupt = AnalyzeSingle(
            [
                "Presta atenção, Deus transforma o coração quando aprendemos a confiar na palavra.",
                "Nós sentimos medo, mas a fé cresce porque continuamos caminhando mesmo sem controlar tudo.",
                "E a gente continua pensando sobre todas essas coisas"
            ]);

        Assert.True(
            complete.Score > abrupt.Score,
            $"Conclusão completa: {complete.Score}; corte abrupto: {abrupt.Score}.");

        Assert.True(
            complete.ScoreBreakdown.Completion >
            abrupt.ScoreBreakdown.Completion);

        Assert.True(
            complete.ScoreBreakdown.Structure >
            abrupt.ScoreBreakdown.Structure);
    }

    [Fact]
    public void Analyze_ReconheceRelevanciaDoPerfilDeNegocios()
    {
        var clip = AnalyzeSingle(
            [
                "Você sabe por que uma estratégia simples pode mudar o resultado de uma empresa?",
                "Quando entendemos o cliente, as vendas melhoram porque o mercado responde melhor a uma proposta clara.",
                "Por isso, uma boa liderança acompanha resultado, lucro e execução com disciplina."
            ],
            new ProjectOptions
            {
                ContentType = "negocios",
                MinDuration = 10,
                MaxDuration = 25,
                ClipCount = 1
            });

        Assert.True(
            clip.ScoreBreakdown.ProfileRelevance > 0);

        Assert.Equal(
            "negocios",
            clip.EditorialProfile);

        Assert.Contains(
            "#negócios",
            clip.Hashtags);
    }

    private ClipCandidate AnalyzeSingle(
        string[] texts,
        ProjectOptions? options = null)
    {
        options ??= new ProjectOptions
        {
            MinDuration = 10,
            MaxDuration = 25,
            ClipCount = 1
        };

        var clips = CreateAnalyzer().Analyze(
            Segments(texts),
            options);

        return Assert.Single(clips);
    }

    private EditorialAnalyzer CreateAnalyzer() =>
        new(
            new EditorialLearningService(
                new TestEnvironment(_root)),
            new EditorialScoringService());

    private static List<TranscriptSegment> Segments(
        params string[] texts) =>
        texts
            .Select((text, index) =>
                new TranscriptSegment
                {
                    Start = index * 5,
                    End = index * 5 + 4.8,
                    Text = text
                })
            .ToList();

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    private sealed class TestEnvironment(string root)
        : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests";
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
        public string WebRootPath { get; set; } = root;
        public IFileProvider WebRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}