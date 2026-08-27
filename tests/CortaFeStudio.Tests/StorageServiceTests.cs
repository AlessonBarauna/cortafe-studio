using CortaFeStudio.Api.Models;
using CortaFeStudio.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.FileProviders;

namespace CortaFeStudio.Tests;

public sealed class StorageServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "amado-storage-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task DeleteProjectData_RemoveArquivosPesadosEMantemProjetoEEdicoes()
    {
        var environment = new TestEnvironment(_root);
        var store = new ProjectStore(environment);
        var project = await store.CreateAsync("Projeto preservado", SourceKind.YouTube, "https://youtube.com/watch?v=teste", null);
        var directory = store.ProjectDirectory(project.Id);
        project.Status = ProjectStatus.Ready;
        project.LocalMedia = "source.mp4";
        project.CompletedStages.AddRange(["media", "audio", "transcript", "analysis"]);
        var clip = new ClipCandidate { Title = "Título editado", VideoPath = "clip-teste.mp4", CoverPath = "cover-teste.jpg" };
        project.Clips.Add(clip);
        await store.SaveAsync(project);
        await File.WriteAllBytesAsync(Path.Combine(directory, "source.mp4"), new byte[2048]);
        await File.WriteAllBytesAsync(Path.Combine(directory, "clip-teste.mp4"), new byte[1024]);
        await File.WriteAllBytesAsync(Path.Combine(directory, "cover-teste.jpg"), new byte[512]);

        var freed = await new StorageService(store).DeleteProjectDataAsync(project);

        Assert.True(freed >= 3584);
        Assert.True(File.Exists(Path.Combine(directory, "project.json")));
        Assert.Null(project.LocalMedia);
        Assert.Null(clip.VideoPath);
        Assert.Null(clip.CoverPath);
        Assert.True(clip.RenderOutdated);
        Assert.Equal("Título editado", store.Get(project.Id)!.Clips.Single().Title);
        Assert.Contains("transcript", project.CompletedStages);
        Assert.DoesNotContain("media", project.CompletedStages);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private sealed class TestEnvironment(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
