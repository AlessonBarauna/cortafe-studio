using System.Collections.Concurrent;
using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

public sealed class RenderJobService(MediaPipeline pipeline)
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _jobs = new();
    public object Status() => _jobs.Keys.ToArray();
    public async Task RunClipAsync(VideoProject project, ClipCandidate clip)
    {
        var key = Key(project.Id, clip.Id); using var cancellation = new CancellationTokenSource();
        if (!_jobs.TryAdd(key, cancellation)) throw new InvalidOperationException("Este corte já está sendo renderizado.");
        try { await pipeline.RenderClipAsync(project, clip, cancellation.Token); } finally { _jobs.TryRemove(key, out _); }
    }
    public async Task RunBatchAsync(VideoProject project)
    {
        var key = Key(project.Id, "all"); using var cancellation = new CancellationTokenSource();
        if (!_jobs.TryAdd(key, cancellation)) throw new InvalidOperationException("Este projeto já está sendo renderizado.");
        try { await pipeline.RenderAllAsync(project, cancellation.Token); } finally { _jobs.TryRemove(key, out _); }
    }
    public bool Cancel(string projectId, string clipId) => _jobs.TryGetValue(Key(projectId, clipId), out var job) && Cancel(job);
    public bool CancelBatch(string projectId) => _jobs.TryGetValue(Key(projectId, "all"), out var job) && Cancel(job);
    private static bool Cancel(CancellationTokenSource source) { source.Cancel(); return true; }
    private static string Key(string projectId, string clipId) => $"{projectId}:{clipId}";
}
