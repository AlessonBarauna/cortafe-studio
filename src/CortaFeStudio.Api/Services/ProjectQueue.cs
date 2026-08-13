using System.Threading.Channels;
using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

public sealed class ProjectQueue(ProjectStore store, MediaPipeline pipeline, ILogger<ProjectQueue> logger) : BackgroundService
{
    private readonly Channel<string> _queue = Channel.CreateUnbounded<string>();
    public ValueTask EnqueueAsync(string id) => _queue.Writer.WriteAsync(id);
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var id in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            var project = store.Get(id); if (project is null) continue;
            try { await pipeline.ProcessAsync(project, stoppingToken); await store.SaveAsync(project); }
            catch (Exception ex) { logger.LogError(ex, "Falha no projeto {Id}", id); project.Status = ProjectStatus.Failed; project.Error = ex.Message; project.Stage = "Processamento interrompido"; await store.SaveAsync(project); }
        }
    }
}
