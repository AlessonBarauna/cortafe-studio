using CortaFeStudio.Api.Models;
using CortaFeStudio.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace CortaFeStudio.Tests;

public sealed class CandidateAnalysisReportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cortafe-report", Guid.NewGuid().ToString("N"));
    [Fact]
    public void Selector_ContaCandidatosRemovidosPorSobreposicao()
    {
        var report = new CandidateAnalysisReport { RequestedClips = 3, RawCandidates = 3 };
        var pool = new List<ClipCandidate>
        {
            new() { Start = 0, End = 70, Score = 90, Transcript = "primeiro trecho sobre fé e esperança" },
            new() { Start = 5, End = 72, Score = 80, Transcript = "outro conteúdo completamente diferente" },
            new() { Start = 100, End = 170, Score = 70, Transcript = "terceiro trecho distante no vídeo" }
        };
        var result = new EditorialCandidateSelector().Select(pool, new ProjectOptions { ClipCount = 3 }, report);
        Assert.Equal(1, report.RejectedByOverlap);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Selector_ResultadoFinalPodeSerRegistradoSemInventarDescartes()
    {
        var report = new CandidateAnalysisReport { RequestedClips = 2, TranscriptSegments = 10, RawCandidates = 1, RejectedByDuration = 3 };
        report.FinalCandidates = 1;
        Assert.Equal(1, report.FinalCandidates);
        Assert.Equal(3, report.RejectedByDuration);
        Assert.Equal(0, report.RejectedByScore);
    }

    [Fact]
    public void Modelo_PersisteTodosOsContadoresDoPipeline()
    {
        var report = new CandidateAnalysisReport { RequestedClips = 10, TranscriptSegments = 34, RawCandidates = 6, RejectedByDuration = 2, RejectedByOverlap = 1, RejectedByIncompleteEnding = 1, FinalCandidates = 2 };
        Assert.Equal(10, report.RequestedClips);
        Assert.Equal(34, report.TranscriptSegments);
        Assert.Equal(6, report.RawCandidates);
        Assert.Equal(2, report.FinalCandidates);
    }

    [Fact]
    public void Analyzer_ContaRejeicaoRealPorDuracao()
    {
        var analyzer = new EditorialAnalyzer(new EditorialLearningService(new TestEnvironment(_root)), new EditorialScoringService(), new EditorialCandidateSelector());
        var transcript = Enumerable.Range(0, 4).Select(index => new TranscriptSegment { Start = index * 5, End = index * 5 + 4, Text = "Esta frase possui contexto suficiente para análise editorial." }).ToList();
        var analysis = analyzer.AnalyzeWithReport(transcript, new ProjectOptions { ClipCount = 10, MinDuration = 60, MaxDuration = 75 });
        Assert.True(analysis.Report.RejectedByDuration > 0);
        Assert.Equal(analysis.Clips.Count, analysis.Report.FinalCandidates);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private sealed class TestEnvironment(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "CortaFeStudio.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.Combine(root, "wwwroot");
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
