using System.Text.Json;
using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

public sealed class ProjectStore
{
    private readonly string _root;
    private readonly Dictionary<string, VideoProject> _projects = [];
    private readonly SemaphoreSlim _lock = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public ProjectStore(IWebHostEnvironment env)
    {
        _root = Path.Combine(env.ContentRootPath, "storage", "projects");
        Directory.CreateDirectory(_root);
        foreach (var file in Directory.EnumerateFiles(_root, "project.json", SearchOption.AllDirectories))
            try { var p = JsonSerializer.Deserialize<VideoProject>(File.ReadAllText(file), JsonOptions); if (p is not null) _projects[p.Id] = p; } catch { }
    }

    public IReadOnlyList<VideoProject> List() => _projects.Values.OrderByDescending(p => p.CreatedAt).ToList();
    public VideoProject? Get(string id) => _projects.GetValueOrDefault(id);
    public string ProjectDirectory(string id) => Path.Combine(_root, id);

    public async Task<VideoProject> CreateAsync(string? name, SourceKind kind, string source, ProjectOptions? options)
    {
        var p = new VideoProject { Name = string.IsNullOrWhiteSpace(name) ? "Vídeo do YouTube" : name.Trim(), SourceKind = kind, Source = source, Options = options ?? new() };
        _projects[p.Id] = p; Directory.CreateDirectory(ProjectDirectory(p.Id)); await SaveAsync(p); return p;
    }

    public async Task<VideoProject> CreateFromUploadAsync(string? name, IFormFile file, ProjectOptions options)
    {
        var p = await CreateAsync(string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(file.FileName) : name, SourceKind.Upload, file.FileName, options);
        var safeExt = new[] { ".mp4", ".mov", ".mkv", ".webm", ".mp3", ".wav", ".m4a" }.Contains(Path.GetExtension(file.FileName).ToLowerInvariant()) ? Path.GetExtension(file.FileName) : ".bin";
        var target = Path.Combine(ProjectDirectory(p.Id), "source" + safeExt);
        await using var output = File.Create(target); await file.CopyToAsync(output); p.LocalMedia = Path.GetFileName(target); await SaveAsync(p); return p;
    }

    public async Task<VideoProject?> UpdateAsync(string id, Action<VideoProject> action)
    { if (!_projects.TryGetValue(id, out var p)) return null; await _lock.WaitAsync(); try { action(p); await WriteAsync(p); return p; } finally { _lock.Release(); } }
    public async Task SaveAsync(VideoProject p) { await _lock.WaitAsync(); try { _projects[p.Id] = p; await WriteAsync(p); } finally { _lock.Release(); } }
    private async Task WriteAsync(VideoProject p) => await File.WriteAllTextAsync(Path.Combine(ProjectDirectory(p.Id), "project.json"), JsonSerializer.Serialize(p, JsonOptions));
    public string? ResolveAsset(string id, string path)
    {
        var root = Path.GetFullPath(ProjectDirectory(id)); var candidate = Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(candidate) ? candidate : null;
    }
}
