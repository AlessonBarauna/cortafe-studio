using CortaFeStudio.Api.Models;
using CortaFeStudio.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Data.Sqlite;

namespace CortaFeStudio.Tests;

public sealed class ProjectStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cortafe-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ResolveAsset_BloqueiaArquivoForaDoProjeto()
    {
        var store = new ProjectStore(new TestEnvironment(_root));
        var project = await store.CreateAsync("Teste", SourceKind.YouTube, "https://youtube.com/watch?v=teste", null);
        var outside = Path.Combine(_root, "segredo.txt");
        await File.WriteAllTextAsync(outside, "privado");
        Assert.Null(store.ResolveAsset(project.Id, "../../segredo.txt"));
    }

    [Fact]
    public async Task SaveAsync_PersisteCheckpointNoJsonESqlite()
    {
        var environment = new TestEnvironment(_root);
        var store = new ProjectStore(environment);
        var project = await store.CreateAsync("Checkpoint", SourceKind.YouTube, "https://youtube.com/watch?v=teste", null);
        project.CompletedStages.Add("media");
        project.IsRendering = true; project.RenderCompleted = 2; project.RenderTotal = 5;
        await store.SaveAsync(project);
        var reloaded = new ProjectStore(environment).Get(project.Id);
        Assert.NotNull(reloaded);
        Assert.Contains("media", reloaded!.CompletedStages);
        Assert.True(reloaded.IsRendering);
        Assert.Equal(2, reloaded.RenderCompleted);
        Assert.Equal(5, reloaded.RenderTotal);
    }

    [Fact]
    public async Task DeleteClipAsync_RemoveCorteEArquivosRelacionados()
    {
        var store = new ProjectStore(new TestEnvironment(_root));
        var project = await store.CreateAsync("Cortes", SourceKind.YouTube, "https://youtube.com/watch?v=teste", null);
        var clip = new ClipCandidate { Id = "clipteste", VideoPath = "clip-clipteste.mp4", CoverPath = "cover-clipteste.jpg" };
        project.Clips.Add(clip); await store.SaveAsync(project);
        await File.WriteAllTextAsync(Path.Combine(store.ProjectDirectory(project.Id), clip.VideoPath), "video");
        await File.WriteAllTextAsync(Path.Combine(store.ProjectDirectory(project.Id), clip.CoverPath), "capa");

        Assert.True(await store.DeleteClipAsync(project.Id, clip.Id));
        Assert.Empty(project.Clips);
        Assert.False(File.Exists(Path.Combine(store.ProjectDirectory(project.Id), clip.VideoPath)));
        Assert.False(File.Exists(Path.Combine(store.ProjectDirectory(project.Id), clip.CoverPath)));
    }

    [Fact]
    public async Task DeleteAsync_RemoveCatalogoEPastaCompleta()
    {
        var environment = new TestEnvironment(_root); var store = new ProjectStore(environment);
        var project = await store.CreateAsync("Excluir", SourceKind.YouTube, "https://youtube.com/watch?v=teste", null);
        var directory = store.ProjectDirectory(project.Id);

        Assert.True(await store.DeleteAsync(project.Id));
        Assert.Null(store.Get(project.Id));
        Assert.False(Directory.Exists(directory));
        Assert.Null(new ProjectStore(environment).Get(project.Id));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

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
