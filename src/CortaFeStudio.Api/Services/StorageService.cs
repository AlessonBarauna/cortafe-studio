using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

public sealed class StorageService(ProjectStore store)
{
    public object Report() => new
    {
        totalBytes = store.ListAll().Sum(project => DirectorySize(store.ProjectDirectory(project.Id))),
        projects = store.ListAll().Select(project =>
        {
            var directory = store.ProjectDirectory(project.Id);
            var source = project.LocalMedia is null ? null : Path.Combine(directory, project.LocalMedia);
            var sourceAvailable = source is not null && File.Exists(source);

            return new
            {
                project.Id,
                project.Name,
                project.Archived,
                project.Source,
                project.SourceKind,
                project.Status,
                project.CreatedAt,
                project.CompletedAt,
                project.UpdatedAt,
                contentType = project.Options.ContentType,
                clipCount = project.Clips.Count,
                bytes = DirectorySize(directory),
                sourceAvailable,
                sourceBytes = sourceAvailable ? new FileInfo(source!).Length : 0,
                mediaRemoved = !sourceAvailable
            };
        }).OrderByDescending(item => item.UpdatedAt)
    };

    public async Task<long> CleanupAsync(VideoProject project, bool deleteSource)
    {
        var directory = store.ProjectDirectory(project.Id);
        var before = DirectorySize(directory);

        foreach (var pattern in new[]
                 {
                     "source.audio.wav",
                     "youtube-captions*",
                     "cover-text-*.txt",
                     "*.tmp",
                     "*.part",
                     "*.ytdl"
                 })
        {
            foreach (var file in Directory.EnumerateFiles(directory, pattern))
                File.Delete(file);
        }

        if (deleteSource)
        {
            if (project.Status is not (ProjectStatus.Ready or ProjectStatus.Failed or ProjectStatus.Cancelled))
                throw new InvalidOperationException("Cancele ou finalize o processamento antes de remover o vídeo original.");

            var source = project.LocalMedia is null
                ? null
                : Path.Combine(directory, project.LocalMedia);

            if (source is not null && File.Exists(source))
                File.Delete(source);

            project.LocalMedia = null;
            project.CompletedStages.Remove("media");
        }

        await store.SaveAsync(project);
        return Math.Max(0, before - DirectorySize(directory));
    }

    public async Task ArchiveAsync(VideoProject project, bool archived)
    {
        project.Archived = archived;
        await store.SaveAsync(project);
    }

    public async Task<long> DeleteProjectDataAsync(VideoProject project)
    {
        if (project.Status is not (ProjectStatus.Ready or ProjectStatus.Failed or ProjectStatus.Cancelled))
            throw new InvalidOperationException("Cancele ou finalize o processamento antes de excluir os arquivos.");

        var directory = store.ProjectDirectory(project.Id);
        var before = DirectorySize(directory);
        var metadataFile = Path.GetFullPath(Path.Combine(directory, "project.json"));
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            if (Path.GetFullPath(file).Equals(metadataFile, StringComparison.OrdinalIgnoreCase)) continue;
            File.Delete(file);
        }
        foreach (var child in Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories).OrderByDescending(path => path.Length))
            if (!Directory.EnumerateFileSystemEntries(child).Any()) Directory.Delete(child);

        project.LocalMedia = null;
        project.CompletedStages.RemoveAll(stage => stage is "media" or "audio");
        foreach (var clip in project.Clips)
        {
            clip.VideoPath = null;
            clip.CoverPath = null;
            clip.RenderOutdated = true;
            clip.LastRenderFingerprint = null;
        }
        project.Stage = "Arquivos removidos; projeto e edições preservados";
        await store.SaveAsync(project);
        return Math.Max(0, before - DirectorySize(directory));
    }

    private static long DirectorySize(string path) =>
        Directory.Exists(path)
            ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(file =>
            {
                try { return new FileInfo(file).Length; }
                catch { return 0; }
            })
            : 0;
}
