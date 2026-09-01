using System.Text.Json;
using CortaFeStudio.Api.Models;
using CortaFeStudio.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

namespace CortaFeStudio.Tests;

public sealed class PipelineEndToEndTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cortafe-e2e-" + Guid.NewGuid().ToString("N"));

    [Fact(Timeout = 120_000)]
    public async Task RenderCompleto_GeraMp4ProfissionalEPassaNoQualityGate()
    {
        Directory.CreateDirectory(_root); var apiRoot = FindApiRoot(); var toolEnvironment = new TestEnvironment(apiRoot); var dataEnvironment = new TestEnvironment(_root);
        var tools = new ToolService(toolEnvironment);
        Assert.Contains("ffmpeg version", await tools.CaptureAsync(tools.Find("ffmpeg"), ["-version"], _root), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ffprobe version", await tools.CaptureAsync(tools.Find("ffprobe"), ["-version"], _root), StringComparison.OrdinalIgnoreCase);
        var source = Path.Combine(_root, "synthetic-source.mp4");
        await tools.RunAsync(tools.Find("ffmpeg"), ["-y", "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "testsrc2=size=640x360:rate=30:duration=6", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=6", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", source], _root);

        var store = new ProjectStore(dataEnvironment); var project = await store.CreateAsync("E2E sintetico", SourceKind.Upload, "synthetic-source.mp4", new ProjectOptions { ContentType = "podcast" }); var directory = store.ProjectDirectory(project.Id); var projectSource = Path.Combine(directory, "source.mp4"); File.Copy(source, projectSource); project.LocalMedia = "source.mp4"; project.Duration = 6; project.Status = ProjectStatus.Ready;
        project.Transcript = Transcript(); var clip = new ClipCandidate { Start = 0, End = 6, Title = "Teste completo do pipeline", Caption = "Conteudo sintetico para validacao.", CoverText = "TESTE COMPLETO", Hashtags = ["#teste", "#pipeline"], EditorialProfile = "podcast", SubtitleStyle = "podcast", OutputPreset = "vertical", Approved = true, SocialScore = new SocialScoreBreakdown { Hook = 80, Potential = 85 } };
        clip.CoverPath = $"cover-{clip.Id}.jpg"; await File.WriteAllTextAsync(Path.Combine(directory, clip.CoverPath), "capa sintetica"); project.Clips = [clip]; await store.SaveAsync(project);

        var quality = new QualityGateService(tools, store, NullLogger<QualityGateService>.Instance);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ProductionConcurrency:Render"] = "1", ["ProductionConcurrency:Metadata"] = "1" }).Build();
        var learning = new EditorialLearningService(dataEnvironment); var editorial = new LongVideoEditorialAnalyzer(new EditorialAnalyzer(learning, new EditorialScoringService(), new EditorialCandidateSelector()));
        var pipeline = new MediaPipeline(store, tools, new TestHttpClientFactory(), editorial, new AudioAnalyzer(tools, NullLogger<AudioAnalyzer>.Instance), new VideoEnhancementService(tools, NullLogger<VideoEnhancementService>.Instance), new HardwareEncoderDetector(tools, NullLogger<HardwareEncoderDetector>.Instance), quality, new ProductionWorkLimiter(configuration), new StorageCapacityService(dataEnvironment, store, NullLogger<StorageCapacityService>.Instance), new SilenceTrimmingService(), new FramingService(store, tools), NullLogger<MediaPipeline>.Instance);

        await pipeline.RenderClipAsync(project, clip);

        var output = store.ResolveAsset(project.Id, clip.VideoPath!); Assert.NotNull(output); Assert.True(new FileInfo(output!).Length > 20_000);
        var probe = await tools.CaptureAsync(tools.Find("ffprobe"), ["-v", "error", "-show_streams", "-show_format", "-of", "json", output!], directory);
        using var document = JsonDocument.Parse(probe); var streams = document.RootElement.GetProperty("streams").EnumerateArray().ToList();
        var video = streams.Single(stream => stream.GetProperty("codec_type").GetString() == "video"); var audio = streams.Single(stream => stream.GetProperty("codec_type").GetString() == "audio");
        Assert.Equal("h264", video.GetProperty("codec_name").GetString()); Assert.Equal(1080, video.GetProperty("width").GetInt32()); Assert.Equal(1920, video.GetProperty("height").GetInt32()); Assert.Equal("aac", audio.GetProperty("codec_name").GetString());
        Assert.NotNull(clip.QualityReport); var blocked = string.Join(" | ", clip.QualityReport!.Checks.Where(check => check.Status == QualityStatus.Blocked).Select(check => $"{check.Code}:{check.Detail}")); Assert.True(clip.QualityReport.Status != QualityStatus.Blocked, blocked); Assert.True(clip.QualityReport.Score >= 80);
    }

    private static List<TranscriptSegment> Transcript() => Enumerable.Range(0, 12).Select(index => new TranscriptSegment { Start = index * .5, End = index * .5 + .42, Text = $"palavra {index}", Words = [new TranscriptWord { Start = index * .5, End = index * .5 + .42, Word = $"palavra{index}" }] }).ToList();
    private static string FindApiRoot() { var current = new DirectoryInfo(Directory.GetCurrentDirectory()); while (current is not null) { var candidate = Path.Combine(current.FullName, "src", "CortaFeStudio.Api"); if (File.Exists(Path.Combine(candidate, "CortaFeStudio.Api.csproj"))) return candidate; current = current.Parent; } throw new DirectoryNotFoundException("Raiz da API nao encontrada."); }
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private sealed class TestHttpClientFactory : IHttpClientFactory { public HttpClient CreateClient(string name) => new(new HttpClientHandler()) { Timeout = TimeSpan.FromMilliseconds(100) }; }
    private sealed class TestEnvironment(string root) : IWebHostEnvironment { public string ApplicationName { get; set; } = "Tests"; public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider(); public string WebRootPath { get; set; } = root; public string EnvironmentName { get; set; } = "Test"; public string ContentRootPath { get; set; } = root; public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider(); }
}
