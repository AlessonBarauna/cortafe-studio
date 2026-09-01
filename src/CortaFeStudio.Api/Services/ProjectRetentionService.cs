using System.Text.Json;
using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

public sealed class ProjectRetentionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly ProjectStore _projects;
    private readonly StorageService _storage;
    private readonly ILogger<ProjectRetentionService> _logger;
    private readonly string _settingsFile;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private RetentionPolicy _policy;

    public ProjectRetentionService(IWebHostEnvironment environment, ProjectStore projects, StorageService storage, ILogger<ProjectRetentionService> logger)
    {
        _projects = projects; _storage = storage; _logger = logger;
        var root = Path.Combine(environment.ContentRootPath, "storage"); Directory.CreateDirectory(root);
        _settingsFile = Path.Combine(root, "retention-policy.json"); _policy = Load();
    }

    public RetentionPolicy GetPolicy() => Copy(_policy);
    public async Task<RetentionPolicy> UpdateAsync(RetentionPolicyUpdate update, CancellationToken ct = default)
    {
        if (update.RetentionDays is < 1 or > 365) throw new ArgumentOutOfRangeException(nameof(update.RetentionDays), "A retenção deve ficar entre 1 e 365 dias.");
        await _gate.WaitAsync(ct);
        try
        {
            _policy.Enabled = update.Enabled; _policy.RetentionDays = update.RetentionDays; _policy.Mode = update.Mode;
            _policy.ProtectFavorites = update.ProtectFavorites; _policy.ProtectPinned = update.ProtectPinned;
            _policy.NextRunAt = update.Enabled ? DateTime.UtcNow.AddHours(1) : null;
            await SaveAsync(ct); return Copy(_policy);
        }
        finally { _gate.Release(); }
    }

    public RetentionPreview Preview(DateTime? now = null)
    {
        var policy = GetPolicy(); var cutoff = (now ?? DateTime.UtcNow).AddDays(-policy.RetentionDays);
        var candidates = _projects.ListAll().Where(project => Eligible(project, policy, cutoff))
            .Select(project => new RetentionCandidate(project.Id, project.Name, ReferenceDate(project), DirectorySize(_projects.ProjectDirectory(project.Id)), policy.Mode == RetentionCleanupMode.FullProject))
            .OrderBy(item => item.ReferenceDate).ToList();
        return new RetentionPreview(policy, cutoff, candidates, candidates.Sum(item => item.EstimatedBytes));
    }

    public async Task<RetentionExecution> ExecuteAsync(bool force = false, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!_policy.Enabled && !force) return new RetentionExecution(0, 0, [], DateTime.UtcNow);
            var preview = Preview(); long freed = 0; var processed = new List<string>();
            foreach (var candidate in preview.Candidates)
            {
                ct.ThrowIfCancellationRequested();
                var project = _projects.Get(candidate.ProjectId);
                if (project is null || !Eligible(project, _policy, preview.Cutoff)) continue;
                var before = DirectorySize(_projects.ProjectDirectory(project.Id));
                if (_policy.Mode == RetentionCleanupMode.FullProject)
                {
                    if (!await _projects.DeleteAsync(project.Id)) continue;
                    freed += before;
                }
                else
                {
                    freed += await _storage.DeleteProjectDataAsync(project);
                    project.DataPurgedAt = DateTime.UtcNow; await _projects.SaveAsync(project);
                }
                processed.Add(candidate.ProjectId);
            }
            var completedAt = DateTime.UtcNow; _policy.LastRunAt = completedAt; _policy.NextRunAt = _policy.Enabled ? completedAt.AddDays(1) : null;
            await SaveAsync(ct); _logger.LogInformation("Retencao concluida: {Count} projetos, {Bytes} bytes liberados", processed.Count, freed);
            return new RetentionExecution(processed.Count, freed, processed, completedAt);
        }
        finally { _gate.Release(); }
    }

    public static bool Eligible(VideoProject project, RetentionPolicy policy, DateTime cutoff)
    {
        if (project.Status is not (ProjectStatus.Ready or ProjectStatus.Failed or ProjectStatus.Cancelled)) return false;
        if (policy.ProtectFavorites && project.Favorite || policy.ProtectPinned && project.Pinned) return false;
        if (policy.Mode == RetentionCleanupMode.ProjectData && project.DataPurgedAt is not null) return false;
        return ReferenceDate(project) <= cutoff;
    }

    private static DateTime ReferenceDate(VideoProject project) => project.CompletedAt ?? project.UpdatedAt;
    private RetentionPolicy Load() { try { return File.Exists(_settingsFile) ? JsonSerializer.Deserialize<RetentionPolicy>(File.ReadAllText(_settingsFile), JsonOptions) ?? new() : new(); } catch (Exception ex) { _logger.LogWarning(ex, "Politica de retencao invalida; usando configuracao segura"); return new(); } }
    private async Task SaveAsync(CancellationToken ct) { var temporary = _settingsFile + ".tmp"; await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(_policy, JsonOptions), ct); File.Move(temporary, _settingsFile, true); }
    private static RetentionPolicy Copy(RetentionPolicy p) => new() { Enabled = p.Enabled, RetentionDays = p.RetentionDays, Mode = p.Mode, ProtectFavorites = p.ProtectFavorites, ProtectPinned = p.ProtectPinned, LastRunAt = p.LastRunAt, NextRunAt = p.NextRunAt };
    private static long DirectorySize(string path) => Directory.Exists(path) ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(file => { try { return new FileInfo(file).Length; } catch { return 0; } }) : 0;
}

public sealed class ProjectRetentionWorker(ProjectRetentionService retention, ILogger<ProjectRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (!stoppingToken.IsCancellationRequested)
        {
            try { var policy = retention.GetPolicy(); if (policy.Enabled && (policy.NextRunAt is null || policy.NextRunAt <= DateTime.UtcNow)) await retention.ExecuteAsync(ct: stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex) { logger.LogError(ex, "Falha na rotina de retencao"); }
            if (!await timer.WaitForNextTickAsync(stoppingToken)) break;
        }
    }
}
