using System.Globalization;
using System.Text.Json;
using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

public sealed class ProductionBatchService
{
    private readonly ProjectStore _projects;
    private readonly ProjectQueue _queue;
    private readonly MediaPipeline _pipeline;
    private readonly SocialService _social;
    private readonly ClipVariantService _variants;
    private readonly string _file;
    private readonly Dictionary<string, ProductionBatch> _batches = [];
    private readonly SemaphoreSlim _lock = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public ProductionBatchService(IWebHostEnvironment env, ProjectStore projects, ProjectQueue queue, MediaPipeline pipeline, SocialService social, ClipVariantService variants)
    {
        _projects = projects; _queue = queue; _pipeline = pipeline; _social = social; _variants = variants;
        _file = Path.Combine(env.ContentRootPath, "storage", "production-batches.json");
        if (!File.Exists(_file)) return;
        try
        {
            foreach (var batch in JsonSerializer.Deserialize<List<ProductionBatch>>(File.ReadAllText(_file), JsonOptions) ?? [])
                _batches[batch.Id] = batch;
        }
        catch { }
    }

    public IReadOnlyList<ProductionBatch> List() => _batches.Values.OrderByDescending(item => item.CreatedAt).ToList();
    public ProductionBatch? Get(string id) => _batches.GetValueOrDefault(id);

    public async Task<ProductionBatch> CreateAsync(CreateProductionBatchRequest request)
    {
        var settings = NormalizeSettings(request.Settings);
        var options = new ProjectOptions
        {
            ContentType = request.ContentType,
            ClipCount = settings.CandidateCount,
            Topic = request.Topic,
            WhisperModel = request.WhisperModel
        };
        var project = await _projects.CreateAsync(request.Name, SourceKind.YouTube, request.Url, options);
        var batch = new ProductionBatch
        {
            Name = string.IsNullOrWhiteSpace(request.Name) ? "Producao automatizada" : request.Name.Trim(),
            SourceUrl = request.Url,
            ProjectId = project.Id,
            Settings = settings
        };
        _batches[batch.Id] = batch;
        await SaveAsync();
        await _queue.EnqueueAsync(project.Id);
        return batch;
    }

    public async Task TickAsync(CancellationToken ct)
    {
        foreach (var batch in List().Where(item => item.Status is ProductionStatus.Queued or ProductionStatus.Analyzing))
        {
            ct.ThrowIfCancellationRequested();
            var project = _projects.Get(batch.ProjectId);
            if (project is null) { await FailAsync(batch, "Projeto vinculado nao encontrado."); continue; }
            if (project.Status == ProjectStatus.Failed) { await FailAsync(batch, project.Error ?? "Falha no processamento."); continue; }
            if (project.Status == ProjectStatus.Cancelled) { batch.Status = ProductionStatus.Cancelled; batch.Stage = "Producao cancelada"; await SaveAsync(); continue; }
            if (project.Status != ProjectStatus.Ready)
            {
                batch.Status = ProductionStatus.Analyzing; batch.Progress = Math.Min(project.Progress, 74); batch.Stage = project.Stage; await SaveAsync(); continue;
            }
            await PrepareAsync(batch, project, ct);
        }
    }

    public async Task<ProductionBatch?> ApproveAsync(string id, ProductionApprovalRequest request, CancellationToken ct)
    {
        var batch = Get(id); if (batch is null) return null;
        var selected = request.ClipIds.Count == 0 ? batch.Items : batch.Items.Where(item => request.ClipIds.Contains(item.ClipId)).ToList();
        foreach (var item in selected) item.Approved = true;
        var project = _projects.Get(batch.ProjectId) ?? throw new InvalidOperationException("Projeto vinculado nao encontrado.");
        foreach (var clip in project.Clips) clip.Approved = batch.Items.Any(item => item.ClipId == clip.Id && item.Approved);
        await _projects.SaveAsync(project);
        if (request.Render) await RenderAsync(batch, project, selected, ct);
        if (request.Schedule) await ScheduleAsync(batch, project);
        batch.Status = batch.Items.Any(item => !item.Approved) ? ProductionStatus.AwaitingApproval : ProductionStatus.Ready;
        batch.Stage = "Cortes aprovados para publicacao"; batch.Progress = 100; batch.CompletedAt = DateTime.UtcNow;
        await SaveAsync(); return batch;
    }

    public async Task<bool> CancelAsync(string id)
    {
        var batch = Get(id); if (batch is null) return false;
        _queue.Cancel(batch.ProjectId); batch.Status = ProductionStatus.Cancelled; batch.Stage = "Producao cancelada"; await SaveAsync(); return true;
    }

    private async Task PrepareAsync(ProductionBatch batch, VideoProject project, CancellationToken ct)
    {
        var selected = project.Clips
            .OrderByDescending(clip => clip.SocialScore.Potential)
            .ThenByDescending(clip => clip.Score)
            .Where(clip => clip.SocialScore.Potential >= batch.Settings.MinimumSocialScore)
            .Take(batch.Settings.FinalVideoCount).ToList();
        foreach (var clip in selected)
        {
            var variants = _variants.Generate(clip, project.Transcript, project.Options, batch.Settings.VariantCount);
            _variants.ApplyWinner(clip, variants);
            clip.SocialScore = SocialScoreService.Calculate(clip, project.Options);
        }
        batch.Items = selected
            .Select(clip => new ProductionItem { ClipId = clip.Id, Title = clip.Title, SocialScore = clip.SocialScore.Potential, Approved = batch.Settings.AutoApprove })
            .ToList();
        if (batch.Items.Count == 0)
        {
            batch.Status = ProductionStatus.AwaitingApproval; batch.Progress = 100; batch.Stage = "Nenhum corte atingiu a nota minima"; await SaveAsync(); return;
        }
        foreach (var clip in project.Clips) clip.Approved = batch.Items.Any(item => item.ClipId == clip.Id && item.Approved);
        await _projects.SaveAsync(project);
        if (!batch.Settings.AutoApprove)
        {
            batch.Status = ProductionStatus.AwaitingApproval; batch.Progress = 100; batch.Stage = $"{batch.Items.Count} cortes aguardando aprovacao"; await SaveAsync(); return;
        }
        if (batch.Settings.AutoRender) await RenderAsync(batch, project, batch.Items, ct);
        if (batch.Settings.AutoSchedule && batch.Settings.AutoPublish) await ScheduleAsync(batch, project);
        batch.Status = batch.Settings.AutoSchedule && batch.Settings.AutoPublish ? ProductionStatus.Scheduled : ProductionStatus.Ready;
        batch.Progress = 100; batch.Stage = "Producao concluida"; batch.CompletedAt = DateTime.UtcNow; await SaveAsync();
    }

    private async Task RenderAsync(ProductionBatch batch, VideoProject project, IEnumerable<ProductionItem> items, CancellationToken ct)
    {
        var list = items.Where(item => item.Approved).ToList(); batch.Status = ProductionStatus.Rendering;
        for (var index = 0; index < list.Count; index++)
        {
            var item = list[index]; var clip = project.Clips.First(candidate => candidate.Id == item.ClipId);
            batch.Stage = $"Renderizando {index + 1} de {list.Count}"; batch.Progress = 75 + (int)Math.Round((index + 1d) / list.Count * 20); await SaveAsync();
            await _pipeline.RenderClipAsync(project, clip, ct); item.Rendered = true;
        }
        await _projects.SaveAsync(project);
    }

    private async Task ScheduleAsync(ProductionBatch batch, VideoProject project)
    {
        var schedule = BuildSchedule(batch.Settings, batch.Items.Count);
        for (var index = 0; index < batch.Items.Count; index++)
        {
            var item = batch.Items[index]; if (!item.Approved || !item.Rendered) continue;
            var clip = project.Clips.First(candidate => candidate.Id == item.ClipId);
            foreach (var platform in batch.Settings.Platforms.Distinct())
            {
                var record = await _social.PublishAsync(project.Id, clip.Id, new PublishRequest(platform, clip.Title, clip.Caption, "private", schedule[index]));
                item.Publications.Add(new ProductionPublication { Platform = platform, ScheduledAt = schedule[index], Status = record.Status, PublicationId = record.Id });
            }
        }
    }

    private async Task FailAsync(ProductionBatch batch, string error) { batch.Status = ProductionStatus.Failed; batch.Stage = "Producao interrompida"; batch.Error = error; await SaveAsync(); }
    private async Task SaveAsync()
    {
        await _lock.WaitAsync();
        try { Directory.CreateDirectory(Path.GetDirectoryName(_file)!); foreach (var item in _batches.Values) item.UpdatedAt = DateTime.UtcNow; await File.WriteAllTextAsync(_file, JsonSerializer.Serialize(_batches.Values, JsonOptions)); }
        finally { _lock.Release(); }
    }

    public static ProductionSettings NormalizeSettings(ProductionSettings? value)
    {
        var settings = value ?? new();
        settings.CandidateCount = Math.Clamp(settings.CandidateCount, 1, 20);
        settings.FinalVideoCount = Math.Clamp(settings.FinalVideoCount, 1, settings.CandidateCount);
        settings.VariantCount = Math.Clamp(settings.VariantCount, 1, 5);
        settings.PostsPerDay = Math.Clamp(settings.PostsPerDay, 1, 12);
        settings.MinimumSocialScore = Math.Clamp(settings.MinimumSocialScore, 0, 100);
        settings.PostingTimes = settings.PostingTimes.Where(IsValidTime).Distinct().Take(12).ToList();
        if (settings.PostingTimes.Count == 0) settings.PostingTimes = ["12:00", "19:00"];
        if (settings.Platforms.Count == 0) settings.Platforms = [SocialPlatform.YouTube];
        return settings;
    }

    public static IReadOnlyList<DateTimeOffset> BuildSchedule(ProductionSettings settings, int count)
    {
        settings = NormalizeSettings(settings); var result = new List<DateTimeOffset>(count); var date = settings.StartDate;
        while (result.Count < count)
        {
            foreach (var value in settings.PostingTimes.Take(settings.PostsPerDay))
            {
                var time = TimeOnly.ParseExact(value, "HH:mm", CultureInfo.InvariantCulture);
                result.Add(new DateTimeOffset(date.ToDateTime(time), TimeZoneInfo.Local.GetUtcOffset(date.ToDateTime(time))));
                if (result.Count == count) break;
            }
            date = date.AddDays(1);
        }
        return result;
    }

    private static bool IsValidTime(string value) => TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
}

public sealed class ProductionBatchWorker(ProductionBatchService service, ILogger<ProductionBatchWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await service.TickAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Falha ao atualizar o modo fabrica"); }
            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        }
    }
}
