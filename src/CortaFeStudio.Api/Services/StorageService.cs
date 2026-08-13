using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

public sealed class StorageService(ProjectStore store)
{
    public object Report() => new
    {
        totalBytes = store.ListAll().Sum(project => DirectorySize(store.ProjectDirectory(project.Id))),
        projects = store.ListAll().Select(project => new { project.Id, project.Name, project.Archived, bytes = DirectorySize(store.ProjectDirectory(project.Id)), sourceAvailable = project.LocalMedia is not null && File.Exists(Path.Combine(store.ProjectDirectory(project.Id), project.LocalMedia)) }).OrderByDescending(item => item.bytes)
    };

    public async Task<long> CleanupAsync(VideoProject project, bool deleteSource)
    {
        var directory = store.ProjectDirectory(project.Id); var before = DirectorySize(directory);
        foreach (var pattern in new[] { "source.audio.wav", "youtube-captions*", "cover-text-*.txt", "*.tmp" })
            foreach (var file in Directory.EnumerateFiles(directory, pattern)) File.Delete(file);
        if (deleteSource && project.Clips.Any(clip => !string.IsNullOrWhiteSpace(clip.VideoPath)))
        {
            var source = project.LocalMedia is null ? null : Path.Combine(directory, project.LocalMedia); if (source is not null && File.Exists(source)) File.Delete(source);
            project.LocalMedia = null; project.CompletedStages.Remove("media");
        }
        await store.SaveAsync(project); return Math.Max(0, before - DirectorySize(directory));
    }

    public async Task ArchiveAsync(VideoProject project, bool archived) { project.Archived = archived; await store.SaveAsync(project); }
    private static long DirectorySize(string path) => Directory.Exists(path) ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(file => { try { return new FileInfo(file).Length; } catch { return 0; } }) : 0;
}
