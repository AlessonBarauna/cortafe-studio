using CortaFeStudio.Api.Models;
using CortaFeStudio.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace CortaFeStudio.Tests;

public sealed class PerformanceLearningServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cortafe-performance-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void PerformanceScore_ValorizaRetencaoEngajamentoEAlcance()
    {
        var strong = PerformanceLearningService.PerformanceScore(new PerformanceSnapshot { Views = 10_000, Likes = 900, Comments = 120, Shares = 180, RetentionPercent = 78 });
        var weak = PerformanceLearningService.PerformanceScore(new PerformanceSnapshot { Views = 800, Likes = 8, Comments = 1, Shares = 0, RetentionPercent = 18 });
        Assert.True(strong > weak);
        Assert.InRange(strong, 0, 100);
    }

    [Fact]
    public async Task RecordAsync_PersisteSnapshotSemDuplicarConteudo()
    {
        var service = Service(); var project = Project(); var clip = project.Clips[0]; var captured = new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);
        var request = Request(project, clip, captured, 1_000, 65);
        await service.RecordAsync(project, clip, request); request.Snapshot.Views = 1_500; await service.RecordAsync(project, clip, request);

        var item = Assert.Single(service.List());
        Assert.Single(item.Snapshots);
        Assert.Equal(1_500, item.Snapshots[0].Views);
        Assert.True(File.Exists(Path.Combine(_root, "storage", "content-performance.json")));
    }

    [Fact]
    public async Task Insights_InferePreferenciasComHeuristicaLocal()
    {
        var service = Service(); var project = Project();
        for (var index = 0; index < 4; index++)
        {
            var clip = new ClipCandidate { Id = $"clip{index}", Start = 0, End = 60 + index * 3, Title = "Mensagem", EditorialProfile = index < 3 ? "pregacao" : "podcast", SubtitleStyle = index < 3 ? "sermon" : "clean", Hashtags = [index < 3 ? "#fe" : "#podcast"], SocialScore = new SocialScoreBreakdown { Hook = 75 + index } };
            project.Clips.Add(clip); await service.RecordAsync(project, clip, Request(project, clip, DateTime.UtcNow.AddMinutes(index), 1_000 + index * 2_000, 45 + index * 10));
        }
        var insights = service.Insights();
        Assert.Equal(4, insights.Samples);
        Assert.False(string.IsNullOrWhiteSpace(insights.BestSubtitleStyle));
        Assert.NotEmpty(insights.Recommendations);
    }

    private PerformanceLearningService Service() { Directory.CreateDirectory(_root); return new PerformanceLearningService(new TestEnvironment(_root)); }
    private static VideoProject Project() => new() { Id = "project", Options = new ProjectOptions { ContentType = "pregacao" }, Clips = [new ClipCandidate { Id = "clip", Start = 0, End = 65, Title = "Promessa", EditorialProfile = "pregacao", SubtitleStyle = "sermon", Hashtags = ["#promessa"], SocialScore = new SocialScoreBreakdown { Hook = 82 } }] };
    private static RecordPerformanceRequest Request(VideoProject project, ClipCandidate clip, DateTime captured, long views, double retention) => new() { ProjectId = project.Id, ClipId = clip.Id, Platform = SocialPlatform.YouTube, PublishedAt = new DateTimeOffset(captured), Snapshot = new PerformanceSnapshot { CapturedAt = captured, Views = views, Likes = views / 10, Comments = views / 100, Shares = views / 50, RetentionPercent = retention } };
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private sealed class TestEnvironment(string root) : IWebHostEnvironment { public string ApplicationName { get; set; } = "Tests"; public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider(); public string WebRootPath { get; set; } = root; public string EnvironmentName { get; set; } = "Test"; public string ContentRootPath { get; set; } = root; public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider(); }
}
