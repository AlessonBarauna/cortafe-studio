using System.Threading.Channels;
using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

public sealed class ProjectQueue(ProjectStore store, MediaPipeline pipeline, ILogger<ProjectQueue> logger) : BackgroundService
{
    private readonly Channel<string> _queue = Channel.CreateBounded<string>(new BoundedChannelOptions(100) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly HashSet<string> _scheduled = [];
    private readonly object _sync = new();
    private string? _active;
    private CancellationTokenSource? _activeCancellation;

    public async ValueTask EnqueueAsync(string id)
    {
        lock (_sync) if (!_scheduled.Add(id)) return;
        await _queue.Writer.WriteAsync(id);
    }

    public object Status()
    {
        lock (_sync) return new { activeProjectId = _active, waiting = _scheduled.Count(id => id != _active), scheduledProjectIds = _scheduled.ToArray() };
    }
    public bool Cancel(string id)
    {
        lock (_sync)
        {
            if (_active == id) { _activeCancellation?.Cancel(); return true; }
            return _scheduled.Remove(id);
        }
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (var project in store.List().Where(p => p.Status is ProjectStatus.Queued or ProjectStatus.Acquiring or ProjectStatus.Transcribing or ProjectStatus.Analyzing))
        {
            project.Status = ProjectStatus.Queued; project.Stage = "Retomado após reinicialização";
            await store.SaveAsync(project); await EnqueueAsync(project.Id);
        }
        await foreach (var id in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            lock (_sync) if (!_scheduled.Contains(id)) continue;
            var project = store.Get(id); if (project is null) continue;
            lock (_sync) _active = id;
            using var projectCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            lock (_sync) _activeCancellation = projectCancellation;
            try
            {
                project.Attempt++; project.StartedAt = DateTime.UtcNow; project.Error = null;
                await pipeline.ProcessAsync(project, projectCancellation.Token); await store.SaveAsync(project);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { project.Status = ProjectStatus.Queued; project.Stage = "Aguardando retomada"; await store.SaveAsync(project); }
            catch (OperationCanceledException) { project.Status = ProjectStatus.Cancelled; project.Stage = "Processamento cancelado"; project.Error = null; await store.SaveAsync(project); }
            catch (Exception ex) { logger.LogError(ex, "Falha no projeto {Id}", id); project.Status = ProjectStatus.Failed; project.Error = ex.Message; project.Stage = "Processamento interrompido"; await store.SaveAsync(project); }
            finally { lock (_sync) { _scheduled.Remove(id); _active = null; _activeCancellation = null; } }
        }
    }
}
