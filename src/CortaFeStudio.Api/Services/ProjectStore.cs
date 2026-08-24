using System.Text.Json;
using CortaFeStudio.Api.Models;
using Microsoft.Data.Sqlite;

namespace CortaFeStudio.Api.Services;

public sealed class ProjectStore
{
    private readonly string _root;
    private readonly Dictionary<string, VideoProject> _projects = [];
    private readonly HashSet<string> _deleted = [];
    private readonly string _database;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public ProjectStore(IWebHostEnvironment env)
    {
        _root = Path.Combine(env.ContentRootPath, "storage", "projects");
        Directory.CreateDirectory(_root);
        _database = Path.Combine(env.ContentRootPath, "storage", "catalog.db");
        InitializeDatabase();
        foreach (var file in Directory.EnumerateFiles(_root, "project.json", SearchOption.AllDirectories))
            try { var p = JsonSerializer.Deserialize<VideoProject>(File.ReadAllText(file), JsonOptions); if (p is not null) _projects[p.Id] = p; } catch { }
        foreach (var project in LoadDatabase()) _projects[project.Id] = project;
        foreach (var project in _projects.Values)
        {
            foreach (var clip in project.Clips)
            {
                if (string.Equals(clip.WatermarkText, "CORTAFÉ", StringComparison.OrdinalIgnoreCase)) clip.WatermarkText = "AMADO JESUS";
                if (string.Equals(clip.BrandTheme, "cortafe", StringComparison.OrdinalIgnoreCase)) clip.BrandTheme = "amado-jesus";
            }
            UpsertDatabase(project);
        }
    }

    public IReadOnlyList<VideoProject> List() => _projects.Values.Where(p => !p.Archived).OrderByDescending(p => p.CreatedAt).ToList();
    public IReadOnlyList<VideoProject> ListAll() => _projects.Values.OrderByDescending(p => p.CreatedAt).ToList();
    public VideoProject? Get(string id) => _projects.GetValueOrDefault(id);
    public string ProjectDirectory(string id) => Path.Combine(_root, id);

    public async Task<VideoProject> CreateAsync(string? name, SourceKind kind, string source, ProjectOptions? options)
    {
        var projectOptions = options ?? new(); projectOptions.ApplyAutomaticDuration();
        var p = new VideoProject { Name = string.IsNullOrWhiteSpace(name) ? "Vídeo do YouTube" : name.Trim(), SourceKind = kind, Source = source, Options = projectOptions };
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
    public async Task SaveAsync(VideoProject p) { await _lock.WaitAsync(); try { if (_deleted.Contains(p.Id)) return; _projects[p.Id] = p; await WriteAsync(p); } finally { _lock.Release(); } }
    public async Task<bool> DeleteAsync(string id)
    {
        await _lock.WaitAsync();
        try
        {
            if (!_projects.Remove(id)) return false;
            _deleted.Add(id);
            using var connection = new SqliteConnection($"Data Source={_database}"); connection.Open();
            using var command = connection.CreateCommand(); command.CommandText = "DELETE FROM projects WHERE id=$id"; command.Parameters.AddWithValue("$id", id); command.ExecuteNonQuery();
            var directory = Path.GetFullPath(ProjectDirectory(id)); var root = Path.GetFullPath(_root) + Path.DirectorySeparatorChar;
            if (directory.StartsWith(root, StringComparison.OrdinalIgnoreCase) && Directory.Exists(directory)) Directory.Delete(directory, true);
            return true;
        }
        finally { _lock.Release(); }
    }

    public async Task<bool> DeleteClipAsync(string projectId, string clipId)
    {
        await _lock.WaitAsync();
        try
        {
            if (!_projects.TryGetValue(projectId, out var project)) return false;
            var clip = project.Clips.FirstOrDefault(item => item.Id == clipId); if (clip is null) return false;
            var directory = ProjectDirectory(projectId);
            foreach (var file in Directory.EnumerateFiles(directory).Where(file => Path.GetFileName(file).Contains(clipId, StringComparison.OrdinalIgnoreCase))) File.Delete(file);
            project.Clips.Remove(clip); await WriteAsync(project); return true;
        }
        finally { _lock.Release(); }
    }
    private async Task WriteAsync(VideoProject p)
    {
        p.UpdatedAt = DateTime.UtcNow;
        var target = Path.Combine(ProjectDirectory(p.Id), "project.json");
        var temporary = target + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(p, JsonOptions));
        File.Move(temporary, target, true);
        UpsertDatabase(p);
    }
    private void InitializeDatabase()
    {
        using var connection = new SqliteConnection($"Data Source={_database}"); connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS projects (id TEXT PRIMARY KEY, json TEXT NOT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL, archived INTEGER NOT NULL DEFAULT 0); CREATE INDEX IF NOT EXISTS ix_projects_updated ON projects(updated_at DESC);";
        command.ExecuteNonQuery();
    }
    private IEnumerable<VideoProject> LoadDatabase()
    {
        using var connection = new SqliteConnection($"Data Source={_database}"); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = "SELECT json FROM projects";
        using var reader = command.ExecuteReader();
        while (reader.Read()) { VideoProject? project = null; try { project = JsonSerializer.Deserialize<VideoProject>(reader.GetString(0), JsonOptions); } catch { } if (project is not null) yield return project; }
    }
    private void UpsertDatabase(VideoProject project)
    {
        using var connection = new SqliteConnection($"Data Source={_database}"); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = "INSERT INTO projects(id,json,created_at,updated_at,archived) VALUES($id,$json,$created,$updated,$archived) ON CONFLICT(id) DO UPDATE SET json=$json,updated_at=$updated,archived=$archived";
        command.Parameters.AddWithValue("$id", project.Id); command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(project, JsonOptions)); command.Parameters.AddWithValue("$created", project.CreatedAt.ToString("O")); command.Parameters.AddWithValue("$updated", project.UpdatedAt.ToString("O")); command.Parameters.AddWithValue("$archived", project.Archived ? 1 : 0); command.ExecuteNonQuery();
    }
    public string? ResolveAsset(string id, string path)
    {
        var root = Path.GetFullPath(ProjectDirectory(id)); var candidate = Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(candidate) ? candidate : null;
    }
}
