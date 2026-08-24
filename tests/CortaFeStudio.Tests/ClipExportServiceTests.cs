using CortaFeStudio.Api.Models;
using CortaFeStudio.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using System.IO.Compression;

namespace CortaFeStudio.Tests;

public sealed class ClipExportServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cortafe-export", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CreateZipAsync_IncluiTodosOsCortesRenderizados()
    {
        var store = new ProjectStore(new TestEnvironment(_root)); var project = await store.CreateAsync("Culto", SourceKind.Upload, "culto.mp4", null); var directory = store.ProjectDirectory(project.Id);
        await File.WriteAllBytesAsync(Path.Combine(directory, "clip-a.mp4"), [1, 2, 3]); await File.WriteAllBytesAsync(Path.Combine(directory, "clip-b.mp4"), [4, 5]);
        project.Clips = [new() { Title = "Primeiro corte", VideoPath = "clip-a.mp4" }, new() { Title = "Segundo corte", VideoPath = "clip-b.mp4" }];
        var zip = await new ClipExportService(store).CreateZipAsync(project);
        using var archive = ZipFile.OpenRead(zip); Assert.Equal(2, archive.Entries.Count); Assert.All(archive.Entries, entry => Assert.EndsWith(".mp4", entry.Name));
    }

    [Fact]
    public async Task PacoteTikTokStudio_IncluiVideosCsvTextosEGuia()
    {
        var store = new ProjectStore(new TestEnvironment(_root)); var project = await store.CreateAsync("Campanha", SourceKind.Upload, "video.mp4", null); var directory = store.ProjectDirectory(project.Id);
        await File.WriteAllBytesAsync(Path.Combine(directory, "clip-a.mp4"), [1, 2, 3]);
        project.Clips = [new() { Title = "Um título chamativo", Caption = "Assista até o final.", Hashtags = ["#fe", "#reflexao"], VideoPath = "clip-a.mp4", Approved = true, Score = 92, Start = 0, End = 65 }];

        var zip = await new ClipExportService(store).CreateTikTokStudioPackageAsync(project);

        using var archive = ZipFile.OpenRead(zip);
        Assert.Contains(archive.Entries, entry => entry.FullName.StartsWith("videos/") && entry.Name.EndsWith(".mp4"));
        Assert.Contains(archive.Entries, entry => entry.FullName.StartsWith("legendas/") && entry.Name.EndsWith(".txt"));
        Assert.Contains(archive.Entries, entry => entry.Name == "programacao-tiktok-studio.csv");
        Assert.Contains(archive.Entries, entry => entry.Name == "LEIA-ME.txt");
    }

    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private sealed class TestEnvironment(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests"; public string EnvironmentName { get; set; } = "Testing"; public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider(); public string WebRootPath { get; set; } = root; public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
