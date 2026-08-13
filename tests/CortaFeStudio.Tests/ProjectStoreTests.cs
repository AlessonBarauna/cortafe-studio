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
        await store.SaveAsync(project);
        var reloaded = new ProjectStore(environment).Get(project.Id);
        Assert.NotNull(reloaded);
        Assert.Contains("media", reloaded!.CompletedStages);
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
