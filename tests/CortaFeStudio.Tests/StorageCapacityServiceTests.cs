using CortaFeStudio.Api.Models;
using CortaFeStudio.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

namespace CortaFeStudio.Tests;

public sealed class StorageCapacityServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cortafe-capacity-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(StorageOperation.Acquisition)]
    [InlineData(StorageOperation.Transcription)]
    [InlineData(StorageOperation.BatchRender)]
    public void Estimate_RetornaMargemPositivaPorOperacao(StorageOperation operation)
    {
        Assert.True(StorageCapacityService.Estimate(operation, 3600, 10) > 0);
    }

    [Fact]
    public void Evaluate_BloqueiaQuandoEstimativaInvadeReserva()
    {
        var report = StorageCapacityService.Evaluate(StorageOperation.BatchRender, 4L * 1024 * 1024 * 1024, 2L * 1024 * 1024 * 1024, 3L * 1024 * 1024 * 1024);
        Assert.False(report.Allowed);
        Assert.Contains("bloqueada", report.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_AutorizaSemConsumirReservaDeSeguranca()
    {
        var report = StorageCapacityService.Evaluate(StorageOperation.Acquisition, 10L * 1024 * 1024 * 1024, 2L * 1024 * 1024 * 1024, 3L * 1024 * 1024 * 1024);
        Assert.True(report.Allowed);
    }

    [Fact]
    public void Estimate_AquisicaoEmLote_ReservaEspacoParaCadaProjeto()
    {
        Assert.Equal(StorageCapacityService.Estimate(StorageOperation.Acquisition, 60), StorageCapacityService.Estimate(StorageOperation.Acquisition, 60, 4) / 4);
    }

    [Fact]
    public async Task CleanupTemporary_RemoveSomenteExtensoesComprovadamenteTemporarias()
    {
        Directory.CreateDirectory(_root); var environment = new TestEnvironment(_root); var store = new ProjectStore(environment); var project = await store.CreateAsync("Teste", SourceKind.Upload, "video.mp4", null); var directory = store.ProjectDirectory(project.Id);
        await File.WriteAllTextAsync(Path.Combine(directory, "download.part"), "temporario"); await File.WriteAllTextAsync(Path.Combine(directory, "render.tmp"), "temporario"); await File.WriteAllTextAsync(Path.Combine(directory, "manter.mp4"), "usuario");
        var service = new StorageCapacityService(environment, store, NullLogger<StorageCapacityService>.Instance); var removed = await service.CleanupTemporaryAsync();
        Assert.True(removed > 0); Assert.False(File.Exists(Path.Combine(directory, "download.part"))); Assert.True(File.Exists(Path.Combine(directory, "manter.mp4"))); Assert.True(File.Exists(Path.Combine(directory, "project.json"))); Assert.True(File.Exists(Path.Combine(_root, "storage", "catalog.db")));
    }

    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private sealed class TestEnvironment(string root) : IWebHostEnvironment { public string ApplicationName { get; set; } = "Tests"; public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider(); public string WebRootPath { get; set; } = root; public string EnvironmentName { get; set; } = "Test"; public string ContentRootPath { get; set; } = root; public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider(); }
}
